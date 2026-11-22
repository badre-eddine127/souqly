using Souqly.Inventory.Domain;

namespace Inventory.UnitTests;

/// <summary>US-12 : franchissement du seuil d'alerte (une alerte par passage, pas à chaque vente).</summary>
public class LowStockTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Le_seuil_par_defaut_est_de_3_unites()
    {
        StockItem.Declare(1, "fatima", 10, Now).LowStockThreshold.ShouldBe(3);
    }

    [Theory]
    [InlineData(5, 3, true)]    // 5 → 3 : on passe au seuil
    [InlineData(4, 1, true)]    // 4 → 1
    [InlineData(3, 2, false)]   // déjà au seuil : pas de nouvelle alerte
    [InlineData(10, 4, false)]  // encore au-dessus
    public void Seul_le_passage_au_seuil_declenche_une_alerte(int before, int after, bool alert)
    {
        var item = StockItem.Declare(1, "fatima", after, Now);

        item.CrossedLowStock(before).ShouldBe(alert);
    }

    [Fact]
    public void Un_seuil_a_zero_n_alerte_qu_a_la_rupture()
    {
        var item = StockItem.Declare(1, "fatima", 1, Now);
        item.SetLowStockThreshold(0, Now);
        item.CrossedLowStock(2).ShouldBeFalse();

        item.Adjust(-1, Now);
        item.CrossedLowStock(1).ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Un_seuil_hors_limites_est_refuse(int threshold)
    {
        var item = StockItem.Declare(1, "fatima", 5, Now);

        Should.Throw<StockRuleException>(() => item.SetLowStockThreshold(threshold, Now)).Code.ShouldBe("invalid-threshold");
    }
}
