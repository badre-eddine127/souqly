namespace Souqly.Ordering.Domain;

/// <summary>
/// Commande d'un client. L'agrégat protège ses règles :
/// RG-02 prix figés à la création, RG-04 limites (1 à 20 lignes, 1 à 10 unités, 50 000 MAD max),
/// RG-06 annulation impossible dès qu'un colis est parti, et une machine à états stricte :
/// PENDING → STOCK_RESERVED → CONFIRMED → SHIPPED → DELIVERED, avec PENDING → REJECTED
/// et PENDING / STOCK_RESERVED / CONFIRMED → CANCELLED. Toute autre transition est refusée (409).
/// Expédition (US-13/14) : chaque vendeur expédie SON colis (ses lignes) ; la commande passe à SHIPPED quand
/// tous les colis sont partis, puis à DELIVERED quand tous sont livrés.
/// </summary>
public sealed class Order
{
    public const int MaxLines = 20;
    public const int MaxQuantity = 10;
    public const decimal MaxTotal = 50_000m;
    public const string Currency = "MAD";

    /// <summary>Transporteurs acceptés (Maroc).</summary>
    public static readonly IReadOnlySet<string> Carriers = new HashSet<string>(StringComparer.Ordinal) { "AMANA", "CTM", "ARAMEX", "DHL" };

    private readonly List<OrderLine> _lines = [];
    private readonly List<Shipment> _shipments = [];

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

    /// <summary>Colis : un par vendeur, créé quand le vendeur expédie.</summary>
    public IReadOnlyCollection<Shipment> Shipments => _shipments;

    /// <summary>Vendeurs ayant au moins une ligne dans la commande.</summary>
    public IReadOnlyList<string> Vendors => _lines.Select(l => l.VendorId).Distinct().Order(StringComparer.Ordinal).ToList();

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

    /// <summary>
    /// US-13 : le vendeur expédie ses lignes avec un transporteur et un numéro de suivi.
    /// Renvoie vrai quand ce colis était le dernier : la commande passe alors à SHIPPED.
    /// </summary>
    public bool ShipFor(string vendorId, string carrier, string trackingNumber, DateTimeOffset now)
    {
        if (Status != OrderStatus.Confirmed)
        {
            throw new IllegalOrderTransitionException(Id, Status, OrderStatus.Shipped);
        }

        if (!_lines.Any(l => l.VendorId == vendorId))
        {
            throw new OrderRuleException("not-your-order", "Cette commande ne contient aucun de vos produits.");
        }

        if (_shipments.Any(s => s.VendorId == vendorId))
        {
            throw new OrderRuleException("already-shipped", "Votre colis pour cette commande est déjà expédié.");
        }

        _shipments.Add(new Shipment(vendorId, carrier, trackingNumber, now));
        UpdatedAt = now;
        if (Vendors.All(v => _shipments.Any(s => s.VendorId == v)))
        {
            Move(OrderStatus.Shipped, null, now, OrderStatus.Confirmed);
            return true;
        }

        return false;
    }

    /// <summary>Le colis du vendeur est livré ; quand tous le sont, la commande passe à DELIVERED (renvoie vrai).</summary>
    public bool MarkDelivered(string vendorId, DateTimeOffset now)
    {
        var shipment = _shipments.FirstOrDefault(s => s.VendorId == vendorId)
                       ?? throw new OrderRuleException("not-shipped", "Ce colis n'a pas encore été expédié.");
        if (shipment.DeliveredAt is not null)
        {
            return false;   // déjà livré : rien à faire (idempotent)
        }

        shipment.MarkDelivered(now);
        UpdatedAt = now;
        if (Status == OrderStatus.Shipped && _shipments.All(s => s.DeliveredAt is not null))
        {
            Move(OrderStatus.Delivered, null, now, OrderStatus.Shipped);
            return true;
        }

        return false;
    }

    /// <summary>US-08 / RG-06 : annulation possible tant qu'aucun colis n'est parti.</summary>
    public void Cancel(string reason, DateTimeOffset now)
    {
        if (_shipments.Count > 0)
        {
            throw new IllegalOrderTransitionException(Id, OrderStatus.Shipped, OrderStatus.Cancelled);
        }

        Move(OrderStatus.Cancelled, reason, now, OrderStatus.Pending, OrderStatus.StockReserved, OrderStatus.Confirmed);
    }

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

/// <summary>Colis d'un vendeur pour une commande (US-13, US-14).</summary>
public sealed class Shipment
{
    private Shipment()
    {
        VendorId = Carrier = TrackingNumber = string.Empty;
    }

    internal Shipment(string vendorId, string carrier, string trackingNumber, DateTimeOffset now)
    {
        if (!Order.Carriers.Contains(carrier))
        {
            throw new OrderRuleException("unknown-carrier", $"Transporteur inconnu : {carrier}. Acceptés : {string.Join(", ", Order.Carriers.Order(StringComparer.Ordinal))}.");
        }

        if (trackingNumber.Length is < 6 or > 30 || !trackingNumber.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '-'))
        {
            throw new OrderRuleException("invalid-tracking", "Le numéro de suivi contient de 6 à 30 caractères : majuscules, chiffres ou tirets.");
        }

        VendorId = vendorId;
        Carrier = carrier;
        TrackingNumber = trackingNumber;
        ShippedAt = now;
    }

    public string VendorId { get; private set; }
    public string Carrier { get; private set; }
    public string TrackingNumber { get; private set; }
    public DateTimeOffset ShippedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    internal void MarkDelivered(DateTimeOffset now) => DeliveredAt = now;
}
