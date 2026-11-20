using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Souqly.Chassis;

/// <summary>
/// Traces, métriques et logs OpenTelemetry (US-19 : une commande suivie à travers tous les services).
/// Activé seulement si un collecteur est configuré (OTEL_EXPORTER_OTLP_ENDPOINT) : le tableau de bord Aspire
/// en local, le collecteur Grafana en Compose/Kubernetes (Sprint 4). Sans collecteur, rien n'est exporté.
/// </summary>
public static class Telemetry
{
    public static IHostApplicationBuilder AddSouqlyTelemetry(this IHostApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            return builder;
        }

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithTracing(t => t
                .AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddSource("Npgsql"))   // Npgsql émet ses propres traces SQL
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .UseOtlpExporter();

        return builder;
    }
}
