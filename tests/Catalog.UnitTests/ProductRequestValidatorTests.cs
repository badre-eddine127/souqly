using Souqly.Catalog.Application.Products;

namespace Catalog.UnitTests;

public class ProductRequestValidatorTests
{
    private readonly ProductRequestValidator _validator = new();

    private static ProductRequest Valid() => new(2, "TAPIS-120", "Tapis berbère", "Laine", new MoneyDto("1200.00", "MAD"));

    [Fact]
    public void Une_requete_complete_est_valide() => _validator.Validate(Valid()).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("1200.555")]
    [InlineData("12,50")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("")]
    public void Un_montant_mal_forme_est_refuse(string amount)
    {
        var result = _validator.Validate(Valid() with { Price = new MoneyDto(amount, "MAD") });
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "Price.Amount");
    }

    [Fact]
    public void Une_autre_devise_que_MAD_est_refusee() =>
        _validator.Validate(Valid() with { Price = new MoneyDto("10.00", "EUR") }).IsValid.ShouldBeFalse();

    [Fact]
    public void Un_SKU_avec_caracteres_speciaux_est_refuse() =>
        _validator.Validate(Valid() with { Sku = "TAPIS 120'" }).IsValid.ShouldBeFalse();
}
