extern alias cart;
extern alias catalog;
extern alias gateway;
extern alias inventory;
extern alias notification;
extern alias ordering;
extern alias payment;
extern alias search;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Souqly.Catalog.Application.Vendors;
using Souqly.Messaging;
using Souqly.Testing;
using Yarp.ReverseProxy.Forwarder;

namespace Acceptance.Tests.Support;

/// <summary>
/// La plateforme Souqly complète pour les scénarios Gherkin : Catalog, Cart, Ordering, Inventory, Payment et
/// Notification, chacun avec sa propre base (PostgreSQL ou Redis), reliés par un broker en mémoire.
/// Ordering appelle le vrai Catalog (prix figés, RG-02) ; seul Keycloak est remplacé par des jetons de test
/// (et par un enregistreur pour l'attribution du rôle VENDOR, US-16). La passerelle est hébergée aussi : elle route
/// vers les services en mémoire et tient le journal d'audit (US-18).
/// Démarrée une fois pour toute la campagne : chaque scénario crée ses propres utilisateurs et produits.
/// </summary>
public sealed class Platform : IAsyncDisposable
{
    public const string CatalogHost = "catalog.acceptance";

    private readonly PostgresFixture _postgres = new();
    private readonly RedisFixture _redis = new();
    private readonly List<IAsyncDisposable> _hosts = [];

    // Écoute toutes les sources de traces, comme lorsque OpenTelemetry est branché (US-19).
    private static readonly System.Diagnostics.ActivityListener Tracing = new()
    {
        ShouldListenTo = _ => true,
        Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllData,
    };

    public InMemoryBroker Broker { get; } = new();

    internal Host<catalog::Program> Catalog { get; private set; } = null!;
    internal Host<cart::Program> Cart { get; private set; } = null!;
    internal Host<ordering::Program> Ordering { get; private set; } = null!;
    internal Host<inventory::Program> Inventory { get; private set; } = null!;
    internal Host<payment::Program> Payment { get; private set; } = null!;
    internal Host<notification::Program> Notification { get; private set; } = null!;
    internal Host<gateway::Program> Gateway { get; private set; } = null!;
    internal Host<search::Program> Search { get; private set; } = null!;

    /// <summary>Utilisateurs à qui Catalog a fait attribuer le rôle VENDOR (à la place de l'API d'administration Keycloak).</summary>
    public ConcurrentBag<string> GrantedVendors { get; } = [];

    public static async Task<Platform> StartAsync()
    {
        var platform = new Platform();
        await platform.InitializeAsync();
        return platform;
    }

    private async Task InitializeAsync()
    {
        System.Diagnostics.ActivitySource.AddActivityListener(Tracing);
        await _postgres.InitializeAsync();
        await _redis.InitializeAsync();

        Catalog = Track(new Host<catalog::Program>(this, s =>
        {
            s["ConnectionStrings:CatalogDb"] = _postgres.ConnectionString;
        })
        {
            ExtraServices = services => services.AddSingleton<IVendorRoleGranter>(new RecordingRoleGranter(GrantedVendors)),
        });
        Cart = Track(new Host<cart::Program>(this, s => s["ConnectionStrings:Redis"] = _redis.ConnectionString));
        Inventory = Track(new Host<inventory::Program>(this, async s => s["ConnectionStrings:InventoryDb"] = await _postgres.CreateDatabaseAsync("inventory")));
        _paymentDb = await _postgres.CreateDatabaseAsync("payment");
        Payment = Track(NewPaymentHost());
        Notification = Track(new Host<notification::Program>(this, async s => s["ConnectionStrings:NotificationDb"] = await _postgres.CreateDatabaseAsync("notification")));
        Ordering = Track(new Host<ordering::Program>(this, async s =>
        {
            s["ConnectionStrings:OrderDb"] = await _postgres.CreateDatabaseAsync("ordering");
            s["Catalog:BaseAddress"] = $"http://{CatalogHost}";
            s["ServiceAccount:TokenEndpoint"] = $"http://{CatalogHost}/token";
            s["ServiceAccount:ClientSecret"] = "acceptance-secret";
        }, routeToCatalog: true));

        // US-04 : alimenté par les événements de Catalog (broker en mémoire), réconcilié au démarrage avec la liste de Catalog.
        Search = Track(new Host<search::Program>(this, async s =>
        {
            s["ConnectionStrings:SearchDb"] = await _postgres.CreateDatabaseAsync("search");
            s["Catalog:BaseAddress"] = $"http://{CatalogHost}";
        }, routeToCatalog: true));

        Gateway = Track(new Host<gateway::Program>(this, s =>
        {
            foreach (var cluster in GatewayRouter.Clusters)
            {
                s[$"ReverseProxy:Clusters:{cluster}:Destinations:d1:Address"] = $"http://{cluster}.gateway/";
            }

            s["RateLimiting:PermitsPerSecond"] = "1000";
            s["RateLimiting:Burst"] = "1000";
        })
        {
            ExtraServices = services => services.AddSingleton<IForwarderHttpClientFactory>(new GatewayRouter(this)),
        });

        // Le premier client démarre chaque hôte (migrations, consommateurs, publieur d'outbox).
        foreach (var host in new IHost[] { Catalog, Cart, Inventory, Payment, Notification, Ordering, Search, Gateway })
        {
            await host.StartAsync();
        }
    }

    private string _paymentDb = string.Empty;

    private Host<payment::Program> NewPaymentHost() => new(this, s =>
    {
        s["ConnectionStrings:PaymentDb"] = _paymentDb;
        s["Payment:DeclineAbove"] = "10000";
    });

    /// <summary>Panne simulée de Payment : ses messages s'accumulent dans le broker.</summary>
    public async Task StopPaymentAsync()
    {
        _hosts.Remove(Payment);
        await Payment.DisposeAsync();
    }

    /// <summary>Redémarre Payment sur la même base : il reprend les messages en attente.</summary>
    public async Task StartPaymentAsync()
    {
        Payment = Track(NewPaymentHost());
        await Payment.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in Enumerable.Reverse(_hosts))
        {
            await host.DisposeAsync();
        }

        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private T Track<T>(T host)
        where T : IAsyncDisposable
    {
        _hosts.Add(host);
        return host;
    }

    internal interface IHost
    {
        Task StartAsync();
    }

    /// <summary>Un service Souqly hébergé en mémoire avec sa configuration de test.</summary>
    internal sealed class Host<TProgram>(Platform platform, Func<Dictionary<string, string>, Task> settings, bool routeToCatalog = false)
        : WebApplicationFactory<TProgram>, IHost
        where TProgram : class
    {
        private readonly Dictionary<string, string> _values = new()
        {
            ["Database:MigrateOnStartup"] = "true",
            ["Messaging:Transport"] = "InMemory",
            ["Messaging:OutboxPollInterval"] = "00:00:00.100",
            ["Messaging:RetryBaseDelay"] = "00:00:00.050",
            ["Inventory:ExpiryWorkerEnabled"] = "false",
        };

        public Host(Platform platform, Action<Dictionary<string, string>> settings, bool routeToCatalog = false)
            : this(platform, s =>
            {
                settings(s);
                return Task.CompletedTask;
            }, routeToCatalog)
        {
        }

        public async Task StartAsync()
        {
            await settings(_values);
            using var _ = CreateClient();
        }

        /// <summary>Services supplémentaires propres à cet hôte (remplacements de dépendances externes).</summary>
        public Action<IServiceCollection>? ExtraServices { get; init; }

        public HttpClient As(string userId, params string[] roles) => CreateClient().WithToken(TestJwt.Token(userId, roles));

        public HttpClient Anonymous() => CreateClient();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in _values)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.UseTestJwt();
                services.RemoveAll<InMemoryBroker>();
                services.AddSingleton(platform.Broker);   // un seul broker pour tous les services
                ExtraServices?.Invoke(services);
                if (routeToCatalog)
                {
                    // Les appels sortants d'Ordering vers Catalog (et vers le « Keycloak » qui délivre son jeton de service)
                    // sont routés vers le vrai Catalog hébergé en mémoire.
                    services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => new CatalogRouter { InnerHandler = platform.Catalog.Server.CreateHandler() }));
                }
            });
        }
    }

    /// <summary>Remplace le réseau pour Ordering : /token rend un jeton de service, le reste va au Catalog en mémoire.</summary>
    private sealed class CatalogRouter : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                var token = TestJwt.Token("service-account-ordering", "SERVICE");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"access_token":"{{token}}","expires_in":300,"token_type":"Bearer"}""", Encoding.UTF8, "application/json"),
                };
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>US-16 : remplace l'API d'administration Keycloak ; on vérifie seulement à qui le rôle a été attribué.</summary>
    private sealed class RecordingRoleGranter(ConcurrentBag<string> granted) : IVendorRoleGranter
    {
        public Task GrantVendorRoleAsync(string userId, CancellationToken ct)
        {
            granted.Add(userId);
            return Task.CompletedTask;
        }
    }

    /// <summary>Le « réseau » de la passerelle : chaque cluster YARP est relié au service hébergé en mémoire.</summary>
    private sealed class GatewayRouter(Platform platform) : IForwarderHttpClientFactory
    {
        public static readonly string[] Clusters = ["catalog", "inventory", "cart", "ordering", "payment", "notification", "search"];

        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(new Router(platform), disposeHandler: true);

        private sealed class Router(Platform platform) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpMessageHandler target = request.RequestUri!.Host switch
                {
                    "catalog.gateway" => platform.Catalog.Server.CreateHandler(),
                    "inventory.gateway" => platform.Inventory.Server.CreateHandler(),
                    "cart.gateway" => platform.Cart.Server.CreateHandler(),
                    "ordering.gateway" => platform.Ordering.Server.CreateHandler(),
                    "payment.gateway" => platform.Payment.Server.CreateHandler(),
                    "notification.gateway" => platform.Notification.Server.CreateHandler(),
                    "search.gateway" => platform.Search.Server.CreateHandler(),
                    var host => throw new InvalidOperationException($"Destination inconnue : {host}"),
                };
                return new HttpMessageInvoker(target).SendAsync(request, cancellationToken);
            }
        }
    }
}
