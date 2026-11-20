using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Souqly.Messaging;

/// <summary>Réglages de la messagerie (section « Messaging » + chaîne de connexion « Kafka »).</summary>
public sealed class MessagingOptions
{
    /// <summary>« Kafka » (par défaut) ou « InMemory » (tests).</summary>
    public string Transport { get; set; } = "Kafka";

    /// <summary>Adresse du broker (ConnectionStrings:Kafka, fournie par Aspire ou Compose).</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Groupe de consommateurs = nom du service : chaque service reçoit tous les événements une fois.</summary>
    public string ConsumerGroup { get; set; } = "souqly";

    public int MaxAttempts { get; set; } = 3;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    public TimeSpan OutboxPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    public int Partitions { get; set; } = 3;

    public bool UseInMemory => string.Equals(Transport, "InMemory", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Déclaration des gestionnaires d'événements d'un service.</summary>
public sealed class MessagingBuilder(IServiceCollection services, HandlerRegistry registry)
{
    public MessagingBuilder Consume<TMessage, THandler>(string topic)
        where THandler : class, IMessageHandler<TMessage>
    {
        services.TryAddScoped<THandler>();
        registry.Add(new HandlerRegistration(topic, typeof(TMessage).Name, typeof(TMessage),
            (sp, message, context, ct) => sp.GetRequiredService<THandler>().HandleAsync((TMessage)message, context, ct)));
        return this;
    }
}

public static class MessagingSetup
{
    /// <summary>
    /// Ajoute l'outbox, l'inbox, le publieur et les consommateurs pour <typeparamref name="TDbContext"/>.
    /// Le DbContext doit appeler <c>modelBuilder.AddMessagingTables()</c>.
    /// </summary>
    public static IHostApplicationBuilder AddSouqlyMessaging<TDbContext>(this IHostApplicationBuilder builder, Action<MessagingBuilder>? configure = null)
        where TDbContext : DbContext
    {
        var options = new MessagingOptions { ConsumerGroup = builder.Environment.ApplicationName.Replace("Souqly.", string.Empty, StringComparison.Ordinal).Replace(".Api", string.Empty, StringComparison.Ordinal).ToLowerInvariant() };
        builder.Configuration.GetSection("Messaging").Bind(options);
        options.BootstrapServers = builder.Configuration.GetConnectionString("Kafka") ?? options.BootstrapServers;

        var registry = new HandlerRegistry();
        configure?.Invoke(new MessagingBuilder(builder.Services, registry));

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(registry);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IEventPublisher, OutboxEventPublisher<TDbContext>>();
        builder.Services.AddSingleton<IMessageDispatcher, MessageDispatcher<TDbContext>>();
        builder.Services.AddHostedService<OutboxPublisher<TDbContext>>();

        if (options.UseInMemory)
        {
            builder.Services.TryAddSingleton<InMemoryBroker>();
            builder.Services.AddSingleton<IMessageTransport, InMemoryTransport>();
            builder.Services.AddHostedService<InMemoryConsumerHost>();
        }
        else
        {
            builder.Services.AddSingleton<IMessageTransport, KafkaTransport>();
            builder.Services.AddHostedService<KafkaConsumerHost>();
        }

        return builder;
    }
}
