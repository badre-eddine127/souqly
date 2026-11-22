using Souqly.Chassis.Errors;
using Souqly.Chassis.Security;
using Souqly.Inventory.Domain;
using Souqly.Messaging;

namespace Souqly.Inventory.Application;

/// <summary>Cas d'usage du vendeur sur son stock (US-10).</summary>
public sealed class StockService(IStockStore store, IEventPublisher events, TimeProvider clock)
{
    public async Task<AvailabilityResponse> AvailabilityAsync(long productId, CancellationToken ct)
    {
        var item = await store.FindAsync(productId, ct);
        var available = item?.Available ?? 0;
        return new AvailabilityResponse(productId, available, available > 0);
    }

    public async Task<StockItemResponse> GetForVendorAsync(long productId, CurrentUser vendor, CancellationToken ct) =>
        StockItemResponse.From(await FindOwnedAsync(productId, vendor, ct));

    /// <summary>
    /// US-17 : ruptures (disponible 0) et stocks bas du vendeur, les plus urgents d'abord.
    /// Par défaut, le seuil d'alerte de chaque produit (US-12) ; <paramref name="threshold"/> l'impose pour tous.
    /// </summary>
    public async Task<IReadOnlyList<StockItemResponse>> AlertsAsync(CurrentUser vendor, int? threshold, CancellationToken ct)
    {
        int? limit = threshold is { } t ? Math.Clamp(t, 0, StockItem.MaxLowStockThreshold) : null;
        return (await store.ListLowForVendorAsync(vendor.Id, limit, ct)).Select(StockItemResponse.From).ToList();
    }

    /// <summary>Fixe le stock physique ; crée la ligne de stock à la première déclaration.</summary>
    public async Task<StockItemResponse> SetAsync(long productId, SetStockRequest request, CurrentUser vendor, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var item = await store.FindAsync(productId, ct);
        int? before = item?.Available;
        if (item is null)
        {
            item = Translate(() => StockItem.Declare(productId, vendor.Id, request.OnHand, now));
            store.Add(item);
        }
        else
        {
            EnsureOwner(item, vendor);
            Translate(() => { item.SetOnHand(request.OnHand, now); return item; });
        }

        if (request.LowStockThreshold is { } threshold)
        {
            Translate(() => { item.SetLowStockThreshold(threshold, now); return item; });
        }

        AlertIfCrossed(item, before);
        await store.SaveChangesAsync(ct);   // stock et alerte (outbox) enregistrés ensemble
        return StockItemResponse.From(item);
    }

    public async Task<StockItemResponse> AdjustAsync(long productId, AdjustStockRequest request, CurrentUser vendor, CancellationToken ct)
    {
        var item = await FindOwnedAsync(productId, vendor, ct);
        var before = item.Available;
        Translate(() => { item.Adjust(request.Delta, clock.GetUtcNow()); return item; });
        AlertIfCrossed(item, before);
        await store.SaveChangesAsync(ct);
        return StockItemResponse.From(item);
    }

    /// <summary>US-12 : une première déclaration n'alerte pas (rien n'a « baissé ») ; un passage sous le seuil, si.</summary>
    private void AlertIfCrossed(StockItem item, int? availableBefore)
    {
        if (availableBefore is { } before && item.CrossedLowStock(before))
        {
            events.Publish(Topics.InventoryEvents, item.ProductId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new LowStock(item.ProductId, item.VendorId, null, item.Available, item.LowStockThreshold));
        }
    }

    private async Task<StockItem> FindOwnedAsync(long productId, CurrentUser vendor, CancellationToken ct)
    {
        var item = await store.FindAsync(productId, ct) ?? throw new NotFoundException("Stock du produit", productId);
        EnsureOwner(item, vendor);
        return item;
    }

    private static void EnsureOwner(StockItem item, CurrentUser vendor)
    {
        // RG-07 : 404 plutôt que 403 pour ne pas révéler le stock d'un autre vendeur.
        if (!item.IsOwnedBy(vendor.Id))
        {
            throw new NotFoundException("Stock du produit", item.ProductId);
        }
    }

    private static T Translate<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (StockRuleException e)
        {
            throw new BusinessRuleException(e.Code, e.Message);
        }
    }
}
