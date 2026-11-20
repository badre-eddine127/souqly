using System.Text.Json.Serialization;
using FluentValidation;
using Souqly.Inventory.Domain;

namespace Souqly.Inventory.Application;

/// <summary>Vue publique : seule la disponibilité est exposée (pas le stock physique ni les réservations).</summary>
public sealed record AvailabilityResponse(long ProductId, int Available, bool InStock);

/// <summary>Vue du vendeur propriétaire.</summary>
public sealed record StockItemResponse(long ProductId, string VendorId, int OnHand, int Reserved, int Available, DateTimeOffset UpdatedAt)
{
    public static StockItemResponse From(StockItem s) => new(s.ProductId, s.VendorId, s.OnHand, s.Reserved, s.Available, s.UpdatedAt);
}

public sealed record SetStockRequest(int OnHand);

public sealed record AdjustStockRequest(int Delta, string Reason);

public sealed record ReserveLine(long ProductId, int Quantity);

/// <summary>Commande ReserveStock (appelée par Ordering ; transportée par Kafka au sprint 3).</summary>
public sealed record ReserveStockRequest(Guid OrderId, IReadOnlyList<ReserveLine> Lines);

public sealed record ReservationResult(Guid OrderId, string Status, long? FailedProductId)
{
    public const string Reserved = "RESERVED";
    public const string OutOfStock = "OUT_OF_STOCK";

    [JsonIgnore]
    public bool Succeeded => Status == Reserved;
}

public sealed class SetStockRequestValidator : AbstractValidator<SetStockRequest>
{
    public SetStockRequestValidator() => RuleFor(r => r.OnHand).InclusiveBetween(0, StockItem.MaxOnHand);
}

public sealed class AdjustStockRequestValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustStockRequestValidator()
    {
        RuleFor(r => r.Delta).NotEqual(0).InclusiveBetween(-StockItem.MaxOnHand, StockItem.MaxOnHand);
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(200);
    }
}

public sealed class ReserveStockRequestValidator : AbstractValidator<ReserveStockRequest>
{
    public ReserveStockRequestValidator()
    {
        RuleFor(r => r.OrderId).NotEmpty();
        // RG-04 : 1 à 20 lignes, 1 à 10 unités par ligne.
        RuleFor(r => r.Lines).NotEmpty().Must(l => l.Count <= 20).WithMessage("Une commande contient au plus 20 lignes.");
        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).GreaterThan(0);
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, 10);
        });
    }
}
