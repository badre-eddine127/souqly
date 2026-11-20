// Souqly — orchestration locale avec Aspire.
// Visual Studio : définir Souqly.AppHost comme projet de démarrage, puis F5.
// Ligne de commande : dotnet run --project src/Aspire/Souqly.AppHost
//
// Par défaut, PostgreSQL 16, Redis 7 et Keycloak 26 tournent dans des conteneurs (Docker Desktop requis).
// Sans Docker, renseigner ConnectionStrings:CatalogDb, InventoryDb, OrderDb et Redis (appsettings.Development.json
// ou secrets utilisateur de l'AppHost) : Aspire utilisera alors ces serveurs existants (Keycloak sur localhost:8180).

var builder = DistributedApplication.CreateBuilder(args);

var useExistingInfrastructure = !string.IsNullOrWhiteSpace(builder.Configuration["ConnectionStrings:CatalogDb"]);

IResourceBuilder<IResourceWithConnectionString> catalogDb;
IResourceBuilder<IResourceWithConnectionString> inventoryDb;
IResourceBuilder<IResourceWithConnectionString> orderDb;
IResourceBuilder<IResourceWithConnectionString> paymentDb;
IResourceBuilder<IResourceWithConnectionString> notificationDb;
IResourceBuilder<IResourceWithConnectionString> redis;
IResourceBuilder<IResourceWithConnectionString> kafka;

// Secret du compte technique d'Ordering : généré une fois puis conservé dans les secrets utilisateur de l'AppHost.
var orderingSecret = builder.AddParameter("ordering-client-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

if (useExistingInfrastructure)
{
    catalogDb = builder.AddConnectionString("CatalogDb");
    inventoryDb = builder.AddConnectionString("InventoryDb");
    orderDb = builder.AddConnectionString("OrderDb");
    paymentDb = builder.AddConnectionString("PaymentDb");
    notificationDb = builder.AddConnectionString("NotificationDb");
    kafka = builder.AddConnectionString("Kafka");
    redis = builder.AddConnectionString("Redis");
}
else
{
    // Une base par service, comme en production (aucun service ne lit la base d'un autre).
    var postgres = builder.AddPostgres("postgres")
        .WithImageTag("16-alpine")
        .WithDataVolume("souqly-postgres-data")
        .WithLifetime(ContainerLifetime.Persistent);   // le conteneur survit entre deux F5 : démarrage rapide
    catalogDb = postgres.AddDatabase("CatalogDb", databaseName: "catalog_db");
    inventoryDb = postgres.AddDatabase("InventoryDb", databaseName: "inventory_db");
    orderDb = postgres.AddDatabase("OrderDb", databaseName: "ordering_db");
    paymentDb = postgres.AddDatabase("PaymentDb", databaseName: "payment_db");
    notificationDb = postgres.AddDatabase("NotificationDb", databaseName: "notification_db");

    // Kafka en mode KRaft (sans ZooKeeper) + Kafka UI pour voir passer les événements de la saga.
    kafka = builder.AddKafka("Kafka")
        .WithKafkaUI(ui => ui.WithHostPort(8085))
        .WithDataVolume("souqly-kafka-data")
        .WithLifetime(ContainerLifetime.Persistent);

    redis = builder.AddRedis("Redis")
        .WithImageTag("7-alpine")
        .WithDataVolume("souqly-redis-data")
        .WithLifetime(ContainerLifetime.Persistent);
}

// Fournisseur d'identité (ADR-003) : le realm « souqly » (rôles, clients, comptes de démonstration) est importé
// depuis infra/keycloak au premier démarrage. Port fixe 8180 : c'est l'émetteur (iss) attendu dans les jetons.
var keycloak = useExistingInfrastructure ? null : builder.AddKeycloak("keycloak", port: 8180)
    .WithImageTag("26.6")
    .WithRealmImport("../../../infra/keycloak")
    .WithEnvironment("ORDERING_CLIENT_SECRET", orderingSecret)
    .WithDataVolume("souqly-keycloak-data")
    .WithLifetime(ContainerLifetime.Persistent);

const string Authority = "http://localhost:8180/realms/souqly";

// Observabilité (sprint 4, US-19/20) : Grafana + Prometheus + Tempo + Loki dans une seule image.
// Tableaux de bord et alertes chargés depuis infra/grafana (versionnés, non modifiables dans l'interface).
var grafana = useExistingInfrastructure ? null : builder.AddContainer("grafana", "grafana/otel-lgtm", "0.41.1")
    .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "http")
    .WithEndpoint(targetPort: 4317, name: "otlp", scheme: "http")
    .WithBindMount("../../../infra/grafana/dashboards", "/otel-lgtm/grafana/conf/provisioning/dashboards/souqly", isReadOnly: true)
    .WithBindMount("../../../infra/grafana/provisioning/dashboards.yaml", "/otel-lgtm/grafana/conf/provisioning/dashboards/souqly.yaml", isReadOnly: true)
    .WithBindMount("../../../infra/grafana/provisioning/alerting", "/otel-lgtm/grafana/conf/provisioning/alerting", isReadOnly: true)
    .WithEnvironment("GF_DASHBOARDS_DEFAULT_HOME_DASHBOARD_PATH", "/otel-lgtm/grafana/conf/provisioning/dashboards/souqly/souqly-overview.json")
    .WithVolume("souqly-grafana-data", "/data")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithUrlForEndpoint("http", u => u.DisplayText = "Grafana (tableaux de bord)");

var catalog = builder.AddProject<Projects.Souqly_Catalog_Api>("catalog")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithHttpHealthCheck("/health/ready");

var inventory = builder.AddProject<Projects.Souqly_Inventory_Api>("inventory")
    .WithReference(inventoryDb)
    .WaitFor(inventoryDb)
    .WithReference(kafka)
    .WaitFor(kafka)
    .WithHttpHealthCheck("/health/ready");

var cart = builder.AddProject<Projects.Souqly_Cart_Api>("cart")
    .WithReference(redis)
    .WaitFor(redis)
    .WithHttpHealthCheck("/health/ready");

var ordering = builder.AddProject<Projects.Souqly_Ordering_Api>("ordering")
    .WithReference(orderDb)
    .WaitFor(orderDb)
    .WithReference(kafka)
    .WaitFor(kafka)
    .WaitFor(catalog)
    .WithEnvironment("Catalog__BaseAddress", catalog.GetEndpoint("http"))
    .WithEnvironment("ServiceAccount__TokenEndpoint", Authority + "/protocol/openid-connect/token")
    .WithEnvironment("ServiceAccount__ClientSecret", orderingSecret)
    .WithHttpHealthCheck("/health/ready");

// Saga (sprint 3) : Payment encaisse, Notification prévient client et vendeurs ; tout passe par Kafka.
var payment = builder.AddProject<Projects.Souqly_Payment_Api>("payment")
    .WithReference(paymentDb)
    .WaitFor(paymentDb)
    .WithReference(kafka)
    .WaitFor(kafka)
    .WithHttpHealthCheck("/health/ready");

var notification = builder.AddProject<Projects.Souqly_Notification_Api>("notification")
    .WithReference(notificationDb)
    .WaitFor(notificationDb)
    .WithReference(kafka)
    .WaitFor(kafka)
    .WithHttpHealthCheck("/health/ready");

// Chaque service envoie aussi ses traces, métriques et logs à Grafana (en plus du tableau de bord Aspire).
if (grafana is not null)
{
    foreach (var service in new[] { catalog, inventory, cart, ordering, payment, notification })
    {
        service.WithEnvironment("Telemetry__GrafanaEndpoint", grafana.GetEndpoint("otlp"));
    }
}

// Les services valident les jetons Keycloak : ils démarrent une fois Keycloak prêt.
if (keycloak is not null)
{
    foreach (var service in new[] { catalog, inventory, cart, ordering, payment, notification })
    {
        service.WaitFor(keycloak);
    }
}

// La passerelle est le seul point d'entrée : elle attend que les six services soient sains.
var gateway = builder.AddProject<Projects.Souqly_ApiGateway>("gateway")
    .WithEnvironment("ReverseProxy__Clusters__catalog__Destinations__d1__Address", catalog.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__inventory__Destinations__d1__Address", inventory.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__cart__Destinations__d1__Address", cart.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__ordering__Destinations__d1__Address", ordering.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__payment__Destinations__d1__Address", payment.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__notification__Destinations__d1__Address", notification.GetEndpoint("http"))
    .WaitFor(catalog)
    .WaitFor(inventory)
    .WaitFor(cart)
    .WaitFor(ordering)
    .WaitFor(payment)
    .WaitFor(notification)
    .WithHttpHealthCheck("/health/ready")
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", url =>
    {
        // launchSettings (launchUrl) peut déjà avoir ajouté /scalar : ne pas le doubler (sinon /scalar/scalar).
        var root = url.Url.TrimEnd('/');
        url.Url = root.EndsWith("/scalar", StringComparison.OrdinalIgnoreCase) ? root : root + "/scalar";
        url.DisplayText = "Documentation de l'API (Scalar)";
    });

if (grafana is not null)
{
    gateway.WithEnvironment("Telemetry__GrafanaEndpoint", grafana.GetEndpoint("otlp"));
}

await builder.Build().RunAsync();
