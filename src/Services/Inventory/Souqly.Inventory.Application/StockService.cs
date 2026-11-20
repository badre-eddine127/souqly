using Souqly.Chassis.Errors;
using Souqly.Chassis.Security;
using Souqly.Inventory.Domain;

namespace Souqly.Inventory.Application;

/// <summary>Cas d'usage du vendeur sur son stock (US-10).</summary>
public sealed class StockService(IStockStore store, TimeProvider clock)
{
    public async Task<AvailabilityResponse> AvailabilityAsync(long productId, CancellationToken ct)
    {
        var item = await store.FindAsync(productId, ct);
        var available = item?.Available ?? 0;
        return new AvailabilityResponse(productId, available, available > 0);
    }

    public async Task<StockItemResponse> GetForVendorAsync(long productId, CurrentUser vendor, CancellationToken ct) =>
        StockItemResponse.From(await FindOwnedAsync(productId, vendor, ct));

    /// <summary>Fixe le stock physique ; crée la ligne de stock à la première déclaration.</summary>
    public async Task<StockItemResponse> SetAsync(long productId, SetStockRequest request, CurrentUser vendor, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var item = await store.FindAsync(productId, ct);
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

        await store.SaveChangesAsync(ct);
        return StockItemResponse.From(item);
    }

    public async Task<StockItemResponse> AdjustAsync(long productId, AdjustStockRequest request, CurrentUser vendor, CancellationToken ct)
    {
        var item = await FindOwnedAsync(productId, vendor, ct);
        Translate(() => { item.Adjust(request.Delta, clock.GetUtcNow()); return item; });
        await store.SaveChangesAsync(ct);
        return StockItemResponse.From(item);
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
