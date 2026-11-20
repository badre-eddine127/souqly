using System.Globalization;
using Souqly.Messaging;
using Souqly.Notification.Api.Infrastructure;

namespace Souqly.Notification.Api.Application;

// Événements lus (copies locales du contrat, ADR-009).
public sealed record OrderLineEvent(long ProductId, string VendorId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record OrderConfirmed(Guid OrderId, string CustomerId, Guid PaymentId, IReadOnlyList<OrderLineEvent> Lines, decimal Total, string Currency);

public sealed record OrderRejected(Guid OrderId, string CustomerId, string Reason);

public sealed record OrderCancelled(Guid OrderId, string CustomerId, string Reason);

public sealed record PaymentRefunded(Guid OrderId, string CustomerId, Guid PaymentId, decimal Amount, string Currency);

public sealed record OrderShipped(Guid OrderId, string CustomerId, string VendorId, string Carrier, string TrackingNumber, bool AllShipped);

public sealed record OrderDelivered(Guid OrderId, string CustomerId, string VendorId, bool AllDelivered);

/// <summary>
/// Transforme les événements de la saga en messages lisibles. Le client est prévenu de chaque issue ;
/// chaque vendeur reçoit seulement ses propres lignes d'une commande confirmée (RG-05).
/// L'envoi d'e-mail est simulé par un journal (pas de serveur SMTP dans le projet).
/// </summary>
public sealed partial class NotificationHandlers(NotificationDbContext db, TimeProvider clock, ILogger<NotificationHandlers> logger) :
    IMessageHandler<OrderConfirmed>,
    IMessageHandler<OrderRejected>,
    IMessageHandler<OrderCancelled>,
    IMessageHandler<PaymentRefunded>,
    IMessageHandler<OrderShipped>,
    IMessageHandler<OrderDelivered>
{
    private static readonly Dictionary<string, string> CarrierNames = new(StringComparer.Ordinal)
    {
        ["AMANA"] = "Amana",
        ["CTM"] = "CTM Messagerie",
        ["ARAMEX"] = "Aramex",
        ["DHL"] = "DHL",
    };

    // Format français écrit à la main : les images Docker .NET tournent sans données de culture (mode invariant).
    private static readonly NumberFormatInfo French = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "\u202F" };

    public Task HandleAsync(OrderConfirmed message, MessageContext context, CancellationToken ct)
    {
        Notify(message.CustomerId, "ORDER_CONFIRMED", message.OrderId, "Commande confirmée",
            $"Votre commande {Short(message.OrderId)} de {Money(message.Total, message.Currency)} est confirmée et payée.");

        foreach (var vendor in message.Lines.GroupBy(l => l.VendorId))
        {
            var items = string.Join(", ", vendor.Select(l => $"{l.Quantity} × {l.ProductName}"));
            Notify(vendor.Key, "NEW_ORDER", message.OrderId, "Nouvelle commande à préparer",
                $"Commande {Short(message.OrderId)} : {items} ({Money(vendor.Sum(l => l.UnitPrice * l.Quantity), message.Currency)}).");
        }

        return db.SaveChangesAsync(ct);
    }

    public Task HandleAsync(OrderRejected message, MessageContext context, CancellationToken ct)
    {
        var reason = message.Reason switch
        {
            "OUT_OF_STOCK" => "un des produits n'est plus disponible en quantité suffisante",
            "PAYMENT_DECLINED" => "le paiement a été refusé",
            _ => "elle n'a pas pu être traitée",
        };
        Notify(message.CustomerId, "ORDER_REJECTED", message.OrderId, "Commande refusée",
            $"Votre commande {Short(message.OrderId)} n'a pas abouti : {reason}. Aucun montant n'a été débité.");
        return db.SaveChangesAsync(ct);
    }

    public Task HandleAsync(OrderCancelled message, MessageContext context, CancellationToken ct)
    {
        Notify(message.CustomerId, "ORDER_CANCELLED", message.OrderId, "Commande annulée",
            $"Votre commande {Short(message.OrderId)} est annulée. Si elle avait été payée, le remboursement suit.");
        return db.SaveChangesAsync(ct);
    }

    public Task HandleAsync(PaymentRefunded message, MessageContext context, CancellationToken ct)
    {
        Notify(message.CustomerId, "PAYMENT_REFUNDED", message.OrderId, "Remboursement effectué",
            $"{Money(message.Amount, message.Currency)} ont été remboursés pour la commande {Short(message.OrderId)}.");
        return db.SaveChangesAsync(ct);
    }

    public Task HandleAsync(OrderShipped message, MessageContext context, CancellationToken ct)
    {
        var carrier = CarrierNames.GetValueOrDefault(message.Carrier, message.Carrier);
        var rest = message.AllShipped ? "Tous vos colis sont en route." : "Les autres colis de la commande suivront.";
        Notify(message.CustomerId, "ORDER_SHIPPED", message.OrderId, "Colis expédié",
            $"Un colis de votre commande {Short(message.OrderId)} est parti avec {carrier}, n° de suivi {message.TrackingNumber}. {rest}");
        return db.SaveChangesAsync(ct);
    }

    public Task HandleAsync(OrderDelivered message, MessageContext context, CancellationToken ct)
    {
        var body = message.AllDelivered
            ? $"Votre commande {Short(message.OrderId)} est entièrement livrée. Merci pour votre achat sur Souqly !"
            : $"Un colis de votre commande {Short(message.OrderId)} est livré ; les autres sont encore en route.";
        Notify(message.CustomerId, message.AllDelivered ? "ORDER_DELIVERED" : "PARCEL_DELIVERED", message.OrderId, "Colis livré", body);
        return db.SaveChangesAsync(ct);
    }

    private void Notify(string recipientId, string kind, Guid orderId, string title, string body)
    {
        db.Notifications.Add(Domain.Notification.Create(recipientId, kind, orderId, title, body, clock.GetUtcNow()));
        LogEmail(logger, recipientId, title);
    }

    private static string Short(Guid orderId) => orderId.ToString()[..8].ToUpperInvariant();

    private static string Money(decimal amount, string currency) => amount.ToString("N2", French) + " " + currency;

    [LoggerMessage(Level = LogLevel.Information, Message = "E-mail simulé pour {RecipientId} : {Title}")]
    private static partial void LogEmail(ILogger logger, string recipientId, string title);
}
