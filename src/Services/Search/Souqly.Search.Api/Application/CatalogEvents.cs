using Microsoft.EntityFrameworkCore;
using Souqly.Messaging;
using Souqly.Search.Api.Domain;
using Souqly.Search.Api.Infrastructure;

namespace Souqly.Search.Api.Application;

// Contrats consommés (topic souqly.catalog.product-events.v1) : copie locale, comme dans chaque service consommateur.
public sealed record ProductSnapshot(
    long ProductId, string VendorId, long CategoryId, string Sku, string Name, string Description,
    decimal Price, string Currency, string Status, DateTimeOffset UpdatedAt);

public sealed record ProductCreated(ProductSnapshot Product);

public sealed record ProductUpdated(ProductSnapshot Product);

public sealed record ProductArchived(ProductSnapshot Product);

/// <summary>
/// Tient l'index à jour. Les trois événements portent l'état complet du produit : un produit ACTIVE est indexé
/// (ou mis à jour), un brouillon ou un produit archivé est retiré. Idempotent (inbox) et insensible au désordre
/// (une version plus ancienne n'écrase jamais une plus récente).
/// </summary>
public sealed class CatalogEventHandlers(SearchDbContext db) :
    IMessageHandler<ProductCreated>, IMessageHandler<ProductUpdated>, IMessageHandler<ProductArchived>
{
    public Task HandleAsync(ProductCreated message, MessageContext context, CancellationToken ct) => ApplyAsync(message.Product, ct);

    public Task HandleAsync(ProductUpdated message, MessageContext context, CancellationToken ct) => ApplyAsync(message.Product, ct);

    public Task HandleAsync(ProductArchived message, MessageContext context, CancellationToken ct) => ApplyAsync(message.Product, ct);

    internal async Task ApplyAsync(ProductSnapshot p, CancellationToken ct)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.ProductId == p.ProductId, ct);
        if (document is not null && document.UpdatedAt > p.UpdatedAt)
        {
            return;   // événement en retard sur ce qui est déjà indexé
        }

        if (!string.Equals(p.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            if (document is not null)
            {
                db.Documents.Remove(document);
            }

            return;   // le SaveChanges est fait par le dispatcher, avec l'inbox
        }

        if (document is null)
        {
            document = SearchDocument.Create(p.ProductId);
            db.Documents.Add(document);
        }

        document.Apply(p.VendorId, p.CategoryId, p.Sku, p.Name, p.Description, p.Price, p.Currency, p.UpdatedAt);
    }
}
