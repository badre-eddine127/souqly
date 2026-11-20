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
IResourceBuilder<IResourceWithConnectionString> redis;

// Secret du compte technique d'Ordering : généré une fois puis conservé dans les secrets utilisateur de l'AppHost.
var orderingSecret = builder.AddParameter("ordering-client-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

if (useExistingInfrastructure)
{
    catalogDb = builder.AddConnectionString("CatalogDb");
    inventoryDb = builder.AddConnectionString("InventoryDb");
    orderDb = builder.AddConnectionString("OrderDb");
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

var catalog = builder.AddProject<Projects.Souqly_Catalog_Api>("catalog")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithHttpHealthCheck("/health/ready");

var inventory = builder.AddProject<Projects.Souqly_Inventory_Api>("inventory")
    .WithReference(inventoryDb)
    .WaitFor(inventoryDb)
    .WithHttpHealthCheck("/health/ready");

var cart = builder.AddProject<Projects.Souqly_Cart_Api>("cart")
    .WithReference(redis)
    .WaitFor(redis)
    .WithHttpHealthCheck("/health/ready");

var ordering = builder.AddProject<Projects.Souqly_Ordering_Api>("ordering")
    .WithReference(orderDb)
    .WaitFor(orderDb)
    .WaitFor(catalog)
    .WithEnvironment("Catalog__BaseAddress", catalog.GetEndpoint("http"))
    .WithEnvironment("ServiceAccount__TokenEndpoint", Authority + "/protocol/openid-connect/token")
    .WithEnvironment("ServiceAccount__ClientSecret", orderingSecret)
    .WithHttpHealthCheck("/health/ready");

// Les services valident les jetons Keycloak : ils démarrent une fois Keycloak prêt.
if (keycloak is not null)
{
    foreach (var service in new[] { catalog, inventory, cart, ordering })
    {
        service.WaitFor(keycloak);
    }
}

// La passerelle est le seul point d'entrée : elle attend que les quatre services soient sains.
builder.AddProject<Projects.Souqly_ApiGateway>("gateway")
    .WithEnvironment("ReverseProxy__Clusters__catalog__Destinations__d1__Address", catalog.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__inventory__Destinations__d1__Address", inventory.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__cart__Destinations__d1__Address", cart.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__ordering__Destinations__d1__Address", ordering.GetEndpoint("http"))
    .WaitFor(catalog)
    .WaitFor(inventory)
    .WaitFor(cart)
    .WaitFor(ordering)
    .WithHttpHealthCheck("/health/ready")
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = url.Url.TrimEnd('/') + "/scalar";
        url.DisplayText = "Documentation de l'API (Scalar)";
    });

await builder.Build().RunAsync();
