namespace Souqly.Ordering.Domain;

/// <summary>
/// Commande d'un client. L'agrégat protège ses règles :
/// RG-02 prix figés à la création, RG-04 limites (1 à 20 lignes, 1 à 10 unités, 50 000 MAD max),
/// RG-06 annulation impossible après expédition, et une machine à états stricte :
/// PENDING → STOCK_RESERVED → CONFIRMED → SHIPPED → DELIVERED, avec PENDING → REJECTED
/// et PENDING / STOCK_RESERVED / CONFIRMED → CANCELLED. Toute autre transition est refusée (409).
/// </summary>
public sealed class Order
{
    public const int MaxLines = 20;
    public const int MaxQuantity = 10;
    public const decimal MaxTotal = 50_000m;
    public const string Currency = "MAD";

    private readonly List<OrderLine> _lines = [];

    private Order()
    {
        CustomerId = string.Empty;
    }

    public Guid Id { get; private set; }
    public string CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal Total { get; private set; }
    public string? StatusReason { get; private set; }

    /// <summary>Référence du paiement accepté (renseignée à la confirmation).</summary>
    public Guid? PaymentId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public IReadOnlyCollection<OrderLine> Lines => _lines;

    /// <summary>Crée une commande en attente à partir de l'instantané de prix du catalogue (jamais du prix envoyé par le client).</summary>
    public static Order Place(string customerId, IReadOnlyList<OrderLine> lines, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new OrderRuleException("customer-required", "Une commande appartient toujours à un client.");
        }

        if (lines.Count is 0 or > MaxLines)
        {
            throw new OrderRuleException("invalid-lines", $"Une commande contient de 1 à {MaxLines} lignes.");
        }

        if (lines.GroupBy(l => l.ProductId).Any(g => g.Count() > 1))
        {
            throw new OrderRuleException("duplicate-product", "Un même produit ne peut apparaître qu'une fois par commande.");
        }

        if (lines.Any(l => l.Quantity is < 1 or > MaxQuantity))
        {
            throw new OrderRuleException("invalid-quantity", $"La quantité par ligne va de 1 à {MaxQuantity}.");
        }

        var total = lines.Sum(l => l.LineTotal);
        if (total > MaxTotal)
        {
            throw new OrderRuleException("total-too-high", $"Le total d'une commande ne peut pas dépasser {MaxTotal} MAD.");
        }

        var order = new Order
        {
            Id = Guid.CreateVersion7(now),
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            Total = total,
            CreatedAt = now,
            UpdatedAt = now,
        };
        order._lines.AddRange(lines);
        return order;
    }

    public bool IsOwnedBy(string customerId) => string.Equals(CustomerId, customerId, StringComparison.Ordinal);

    public IEnumerable<OrderLine> LinesOf(string vendorId) => _lines.Where(l => l.VendorId == vendorId);

    public void MarkStockReserved(DateTimeOffset now) => Move(OrderStatus.StockReserved, null, now, OrderStatus.Pending);

    /// <summary>Paiement accepté : la commande est confirmée et garde la référence du paiement.</summary>
    public void Confirm(Guid paymentId, DateTimeOffset now)
    {
        Move(OrderStatus.Confirmed, null, now, OrderStatus.StockReserved);
        PaymentId = paymentId;
    }

    /// <summary>Stock insuffisant (depuis PENDING) ou paiement refusé (depuis STOCK_RESERVED).</summary>
    public void Reject(string reason, DateTimeOffset now) => Move(OrderStatus.Rejected, reason, now, OrderStatus.Pending, OrderStatus.StockReserved);

    public void Ship(DateTimeOffset now) => Move(OrderStatus.Shipped, null, now, OrderStatus.Confirmed);

    public void Deliver(DateTimeOffset now) => Move(OrderStatus.Delivered, null, now, OrderStatus.Shipped);

    /// <summary>US-08 / RG-06 : annulation possible jusqu'à l'expédition (exclue).</summary>
    public void Cancel(string reason, DateTimeOffset now) =>
        Move(OrderStatus.Cancelled, reason, now, OrderStatus.Pending, OrderStatus.StockReserved, OrderStatus.Confirmed);

    private void Move(OrderStatus target, string? reason, DateTimeOffset now, params OrderStatus[] allowedFrom)
    {
        if (!allowedFrom.Contains(Status))
        {
            throw new IllegalOrderTransitionException(Id, Status, target);
        }

        Status = target;
        StatusReason = reason;
        UpdatedAt = now;
    }
}

/// <summary>Ligne de commande : copie figée du produit et de son prix au moment de la commande (RG-02).</summary>
public sealed class OrderLine
{
    private OrderLine()
    {
        VendorId = ProductName = string.Empty;
    }

    public OrderLine(long productId, string vendorId, string productName, decimal unitPrice, int quantity)
    {
        if (unitPrice <= 0)
        {
            throw new OrderRuleException("invalid-price", "Le prix unitaire doit être positif.");
        }

        ProductId = productId;
        VendorId = vendorId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public long ProductId { get; private set; }
    public string VendorId { get; private set; }
    public string ProductName { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public enum OrderStatus
{
    Pending,
    StockReserved,
    Confirmed,
    Shipped,
    Delivered,
    Rejected,
    Cancelled,
}

public sealed class OrderRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class IllegalOrderTransitionException(Guid orderId, OrderStatus from, OrderStatus to)
    : Exception($"La commande {orderId} ne peut pas passer de {from} à {to}.");
