using Souqly.Catalog.Domain.Products;

namespace Catalog.UnitTests;

public class ProductTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static Product NewRug(decimal price = 1200.00m) =>
        Product.Create("vendor-1", 2, "tapis-120x80", "  Tapis berbère 120x80 ", "Laine naturelle", price, Now);

    [Fact]
    public void Un_nouveau_produit_est_un_brouillon_normalise()
    {
        var product = NewRug();

        product.Status.ShouldBe(ProductStatus.Draft);
        product.Name.ShouldBe("Tapis berbère 120x80");
        product.Sku.ShouldBe("TAPIS-120X80");
        product.Price.ShouldBe(1200.00m);
        product.IsPubliclyVisible.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(50_000.01)]
    [InlineData(10.555)]
    public void Un_prix_invalide_est_refuse(decimal price)
    {
        var error = Should.Throw<ProductRuleException>(() => NewRug(price));
        error.Code.ShouldBe("invalid-price");
    }

    [Fact]
    public void Le_prix_maximum_de_50000_MAD_est_accepte()
    {
        NewRug(50_000m).Price.ShouldBe(50_000m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Le_nom_est_obligatoire(string name)
    {
        Should.Throw<ProductRuleException>(() => Product.Create("v", 1, "SKU", name, "", 10m, Now))
            .Code.ShouldBe("invalid-name");
    }

    [Fact]
    public void Un_produit_publie_devient_visible()
    {
        var product = NewRug();
        product.Publish(Now);
        product.Status.ShouldBe(ProductStatus.Active);
        product.IsPubliclyVisible.ShouldBeTrue();
    }

    [Fact]
    public void Un_produit_archive_ne_peut_plus_etre_modifie_ni_publie()
    {
        var product = NewRug();
        product.Archive(Now);

        Should.Throw<ProductStateException>(() => product.Update(2, "X", "Y", "", 10m, Now));
        Should.Throw<ProductStateException>(() => product.Publish(Now));
        product.IsPubliclyVisible.ShouldBeFalse();
    }

    [Fact]
    public void Archiver_deux_fois_est_sans_effet()
    {
        var product = NewRug();
        product.Archive(Now);
        Should.NotThrow(() => product.Archive(Now.AddMinutes(1)));
        product.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Seul_le_vendeur_createur_est_proprietaire()
    {
        var product = NewRug();
        product.IsOwnedBy("vendor-1").ShouldBeTrue();
        product.IsOwnedBy("vendor-2").ShouldBeFalse();
    }
}
