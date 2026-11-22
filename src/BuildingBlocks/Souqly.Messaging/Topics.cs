namespace Souqly.Messaging;

/// <summary>
/// Topics Kafka de Souqly : un topic d'événements par service producteur, versionné (contrat dans
/// contracts/asyncapi/souqly.yaml). La clé de message est l'identifiant de l'agrégat (commande, produit) :
/// tous ses événements vont dans la même partition, donc sont lus dans l'ordre.
/// </summary>
public static class Topics
{
    public const string OrderingEvents = "souqly.ordering.order-events.v1";
    public const string InventoryEvents = "souqly.inventory.stock-events.v1";
    public const string PaymentEvents = "souqly.payment.payment-events.v1";

    /// <summary>Cycle de vie des produits (US-04 : modèle de lecture du service Search).</summary>
    public const string CatalogEvents = "souqly.catalog.product-events.v1";

    /// <summary>Suffixe des topics de messages en échec après tous les essais (dead letter queue).</summary>
    public const string DeadLetterSuffix = ".dlq";

    public static IReadOnlyList<string> All { get; } = [OrderingEvents, InventoryEvents, PaymentEvents, CatalogEvents];

    public static string DeadLetter(string topic) => topic + DeadLetterSuffix;
}
