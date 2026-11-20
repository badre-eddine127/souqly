// Souqly — orchestration locale avec Aspire.
// Visual Studio : définir Souqly.AppHost comme projet de démarrage, puis F5.
// Ligne de commande : dotnet run --project src/Aspire/Souqly.AppHost
//
// Par défaut, PostgreSQL 16 et Redis 7 tournent dans des conteneurs (Docker Desktop requis).
// Sans Docker, renseigner ConnectionStrings:CatalogDb, InventoryDb et Redis (appsettings.Development.json
// ou secrets utilisateur de l'AppHost) : Aspire utilisera alors ces serveurs existants.

var builder = DistributedApplication.CreateBuilder(args);

var useExistingInfrastructure = !string.IsNullOrWhiteSpace(builder.Configuration["ConnectionStrings:CatalogDb"]);

IResourceBuilder<IResourceWithConnectionString> catalogDb;
IResourceBuilder<IResourceWithConnectionString> inventoryDb;
IResourceBuilder<IResourceWithConnectionString> redis;

if (useExistingInfrastructure)
{
    catalogDb = builder.AddConnectionString("CatalogDb");
    inventoryDb = builder.AddConnectionString("InventoryDb");
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

    redis = builder.AddRedis("Redis")
        .WithImageTag("7-alpine")
        .WithDataVolume("souqly-redis-data")
        .WithLifetime(ContainerLifetime.Persistent);
}

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

// La passerelle est le seul point d'entrée : elle attend que les trois services soient sains.
builder.AddProject<Projects.Souqly_ApiGateway>("gateway")
    .WithEnvironment("ReverseProxy__Clusters__catalog__Destinations__d1__Address", catalog.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__inventory__Destinations__d1__Address", inventory.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__cart__Destinations__d1__Address", cart.GetEndpoint("http"))
    .WaitFor(catalog)
    .WaitFor(inventory)
    .WaitFor(cart)
    .WithHttpHealthCheck("/health/ready")
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = url.Url.TrimEnd('/') + "/scalar";
        url.DisplayText = "Documentation de l'API (Scalar)";
    });

await builder.Build().RunAsync();
