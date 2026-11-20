using Souqly.Cart.Domain;

namespace Cart.Tests;

public class ShoppingCartTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fixer_la_quantite_remplace_la_precedente()
    {
        var cart = new ShoppingCart("sara");
        cart.SetQuantity(42, 1, Now);
        cart.SetQuantity(42, 3, Now);
        cart.Lines[42].ShouldBe(3);
        cart.TotalUnits.ShouldBe(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void RG04_quantite_hors_1_a_10_refusee(int quantity) =>
        Should.Throw<CartRuleException>(() => new ShoppingCart("sara").SetQuantity(42, quantity, Now)).Code.ShouldBe("invalid-quantity");

    [Fact]
    public void RG04_pas_plus_de_20_produits_differents()
    {
        var cart = new ShoppingCart("sara");
        for (var id = 1; id <= 20; id++)
        {
            cart.SetQuantity(id, 1, Now);
        }

        Should.Throw<CartRuleException>(() => cart.SetQuantity(21, 1, Now)).Code.ShouldBe("too-many-lines");
        Should.NotThrow(() => cart.SetQuantity(5, 2, Now));   // modifier une ligne existante reste permis
    }

    [Fact]
    public void Retirer_un_produit_absent_est_sans_effet()
    {
        var cart = new ShoppingCart("sara");
        cart.Remove(99, Now);
        cart.Lines.ShouldBeEmpty();
    }
}
