using Souqly.Ordering.Domain;

namespace Souqly.Ordering.Application;

// Événements d'intégration du service Ordering (contrat : contracts/asyncapi/souqly.yaml).
// Copies locales : aucun service ne partage ses classes avec un autre (ADR-009) ; seul le JSON fait foi.

/// <summary>Ligne transmise aux autres services : prix figé, vendeur, quantité.</summary>
public sealed record OrderLineEvent(long ProductId, string VendorId, string ProductName, decimal UnitPrice, int Quantity);

// ----- Publiés sur souqly.ordering.order-events.v1 -----

/// <summary>Nouvelle commande : Inventory doit réserver le stock (étape 1 de la saga).</summary>
public sealed record OrderPlaced(Guid OrderId, string CustomerId, IReadOnlyList<OrderLineEvent> Lines, decimal Total, string Currency);

/// <summary>Stock réservé : Payment doit encaisser le montant (étape 2).</summary>
public sealed record PaymentRequested(Guid OrderId, string CustomerId, decimal Amount, string Currency);

/// <summary>Commande confirmée : Inventory sort le stock, Notification prévient le client et les vendeurs.</summary>
public sealed record OrderConfirmed(Guid OrderId, string CustomerId, Guid PaymentId, IReadOnlyList<OrderLineEvent> Lines, decimal Total, string Currency);

/// <summary>Commande rejetée (OUT_OF_STOCK, PAYMENT_DECLINED) : Inventory libère ce qui avait été réservé.</summary>
public sealed record OrderRejected(Guid OrderId, string CustomerId, string Reason);

/// <summary>Commande annulée par le client : Inventory rend le stock, Payment rembourse si besoin.</summary>
public sealed record OrderCancelled(Guid OrderId, string CustomerId, string Reason);

// ----- Consommés -----

/// <summary>Inventory : toutes les lignes sont réservées (15 min).</summary>
public sealed record StockReserved(Guid OrderId);

/// <summary>Inventory : au moins une ligne n'est pas disponible ; rien n'a été réservé.</summary>
public sealed record StockReservationFailed(Guid OrderId, long ProductId);

/// <summary>Payment : montant encaissé.</summary>
public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId, decimal Amount);

/// <summary>Payment : paiement refusé.</summary>
public sealed record PaymentFailed(Guid OrderId, string Reason);

internal static class OrderEvents
{
    public static IReadOnlyList<OrderLineEvent> Lines(Order order) =>
        order.Lines.OrderBy(l => l.ProductId).Select(l => new OrderLineEvent(l.ProductId, l.VendorId, l.ProductName, l.UnitPrice, l.Quantity)).ToList();

    public static OrderPlaced Placed(Order order) => new(order.Id, order.CustomerId, Lines(order), order.Total, Order.Currency);
}
