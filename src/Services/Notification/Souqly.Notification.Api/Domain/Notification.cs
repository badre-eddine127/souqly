namespace Souqly.Notification.Api.Domain;

/// <summary>Message destiné à un client ou à un vendeur (affiché dans l'application ; envoi d'e-mail simulé).</summary>
public sealed class Notification
{
    private Notification()
    {
        RecipientId = string.Empty;
        Kind = string.Empty;
        Title = string.Empty;
        Body = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Identifiant Keycloak (sub) du destinataire. Ni e-mail ni nom stockés ici (minimisation, loi 09-08).</summary>
    public string RecipientId { get; private set; }

    public string Kind { get; private set; }
    /// <summary>Commande concernée (étapes de commande) ; absente pour une alerte de stock.</summary>
    public Guid? OrderId { get; private set; }

    /// <summary>Produit concerné (US-12 : alerte de stock bas) ; absent pour une étape de commande.</summary>
    public long? ProductId { get; private set; }
    public string Title { get; private set; }
    public string Body { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public static Notification Create(string recipientId, string kind, Guid? orderId, string title, string body, DateTimeOffset now, long? productId = null) => new()
    {
        Id = Guid.CreateVersion7(now),
        RecipientId = recipientId,
        Kind = kind,
        OrderId = orderId,
        ProductId = productId,
        Title = title,
        Body = body,
        CreatedAt = now,
    };

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
