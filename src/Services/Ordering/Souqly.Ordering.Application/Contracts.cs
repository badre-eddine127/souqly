using System.Globalization;
using FluentValidation;
using Souqly.Ordering.Domain;

namespace Souqly.Ordering.Application;

public sealed record MoneyDto(string Amount, string Currency)
{
    public static MoneyDto Of(decimal amount) => new(amount.ToString("0.00", CultureInfo.InvariantCulture), Order.Currency);
}

public sealed record OrderLineRequest(long ProductId, int Quantity);

/// <summary>
/// Corps de POST /api/v1/orders. Il ne contient AUCUN prix : le serveur le lit dans le catalogue (RG-02).
/// </summary>
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineRequest> Lines);

public sealed record OrderLineResponse(long ProductId, string VendorId, string ProductName, MoneyDto UnitPrice, int Quantity, MoneyDto LineTotal)
{
    public static OrderLineResponse From(OrderLine l) =>
        new(l.ProductId, l.VendorId, l.ProductName, MoneyDto.Of(l.UnitPrice), l.Quantity, MoneyDto.Of(l.LineTotal));
}

public sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    string Status,
    string? StatusReason,
    IReadOnlyList<OrderLineResponse> Lines,
    MoneyDto Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static OrderResponse From(Order o) => new(
        o.Id, o.CustomerId, StatusName(o.Status), o.StatusReason,
        o.Lines.OrderBy(l => l.ProductId).Select(OrderLineResponse.From).ToList(),
        MoneyDto.Of(o.Total), o.CreatedAt, o.UpdatedAt);

    public static string StatusName(OrderStatus status) => status switch
    {
        OrderStatus.StockReserved => "STOCK_RESERVED",
        _ => status.ToString().ToUpperInvariant(),
    };
}

/// <summary>Vue d'une commande par un vendeur : uniquement SES lignes (RG-05), jamais l'identité complète du client.</summary>
public sealed record VendorOrderResponse(Guid OrderId, string Status, IReadOnlyList<OrderLineResponse> Lines, MoneyDto VendorTotal, DateTimeOffset CreatedAt);

public sealed record PlaceOrderResult(OrderResponse Order, bool Replayed);

/// <summary>Prix figé lu dans le catalogue au moment de la commande.</summary>
public sealed record CatalogPrice(long ProductId, string VendorId, string Name, decimal Price, bool Purchasable);

/// <summary>Port vers le service Catalog (seul appel synchrone du parcours de commande).</summary>
public interface ICatalogPort
{
    /// <exception cref="Souqly.Chassis.Errors.DependencyUnavailableException">Catalogue injoignable, lent ou disjoncteur ouvert.</exception>
    Task<IReadOnlyList<CatalogPrice>> GetPricesAsync(IReadOnlyCollection<long> productIds, CancellationToken ct);
}

public sealed class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(r => r.Lines).NotEmpty().WithMessage("La commande doit contenir au moins une ligne.")
            .Must(l => l is null || l.Count <= Order.MaxLines).WithMessage($"Une commande contient au plus {Order.MaxLines} lignes.");
        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).GreaterThan(0);
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, Order.MaxQuantity);
        });
    }
}

public sealed record OrderPage(
    IReadOnlyList<OrderResponse> Content, int Page, int Size, long TotalElements, int TotalPages);
