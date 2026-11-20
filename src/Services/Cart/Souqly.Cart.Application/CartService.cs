using FluentValidation;
using Souqly.Cart.Domain;
using Souqly.Chassis.Errors;
using Souqly.Chassis.Security;

namespace Souqly.Cart.Application;

public sealed record CartLineResponse(long ProductId, int Quantity);

public sealed record CartResponse(string CustomerId, IReadOnlyList<CartLineResponse> Items, int TotalUnits, DateTimeOffset? UpdatedAt)
{
    public static CartResponse From(ShoppingCart cart) => new(
        cart.CustomerId,
        cart.Lines.OrderBy(l => l.Key).Select(l => new CartLineResponse(l.Key, l.Value)).ToList(),
        cart.TotalUnits,
        cart.UpdatedAt == DateTimeOffset.MinValue ? null : cart.UpdatedAt);
}

public sealed record SetQuantityRequest(int Quantity);

public sealed class SetQuantityRequestValidator : AbstractValidator<SetQuantityRequest>
{
    public SetQuantityRequestValidator() => RuleFor(r => r.Quantity).InclusiveBetween(1, ShoppingCart.MaxQuantity);
}

/// <summary>Cas d'usage du panier (US-05). Le client ne voit et ne modifie que son propre panier (clé = "sub" du jeton).</summary>
public sealed class CartService(ICartStore store, TimeProvider clock)
{
    public async Task<CartResponse> GetAsync(CurrentUser customer, CancellationToken ct) =>
        CartResponse.From(await store.FindAsync(customer.Id, ct) ?? new ShoppingCart(customer.Id));

    public async Task<CartResponse> SetQuantityAsync(CurrentUser customer, long productId, int quantity, CancellationToken ct)
    {
        var cart = await store.FindAsync(customer.Id, ct) ?? new ShoppingCart(customer.Id);
        try
        {
            cart.SetQuantity(productId, quantity, clock.GetUtcNow());
        }
        catch (CartRuleException e)
        {
            throw new BusinessRuleException(e.Code, e.Message);
        }

        await store.SaveAsync(cart, ct);
        return CartResponse.From(cart);
    }

    public async Task<CartResponse> RemoveAsync(CurrentUser customer, long productId, CancellationToken ct)
    {
        var cart = await store.FindAsync(customer.Id, ct) ?? new ShoppingCart(customer.Id);
        cart.Remove(productId, clock.GetUtcNow());
        await store.SaveAsync(cart, ct);
        return CartResponse.From(cart);
    }

    /// <summary>Vide le panier (appelé aussi à la réception de OrderConfirmed au sprint 3).</summary>
    public Task ClearAsync(string customerId, CancellationToken ct) => store.DeleteAsync(customerId, ct);
}
