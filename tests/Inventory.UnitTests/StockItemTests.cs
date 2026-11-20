using Souqly.Inventory.Application;
using Souqly.Inventory.Domain;

namespace Inventory.UnitTests;

public class StockItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Une_declaration_de_stock_fixe_le_physique_sans_reservation()
    {
        var item = StockItem.Declare(42, "vendor-1", 3, Now);
        item.OnHand.ShouldBe(3);
        item.Reserved.ShouldBe(0);
        item.Available.ShouldBe(3);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void Un_stock_hors_bornes_est_refuse(int onHand) =>
        Should.Throw<StockRuleException>(() => StockItem.Declare(42, "v", onHand, Now)).Code.ShouldBe("invalid-quantity");

    [Fact]
    public void Un_ajustement_negatif_ne_peut_pas_rendre_le_stock_negatif()
    {
        var item = StockItem.Declare(42, "v", 2, Now);
        Should.Throw<StockRuleException>(() => item.Adjust(-3, Now));
        item.OnHand.ShouldBe(2);
    }

    [Fact]
    public void Un_ajustement_nul_est_refuse() =>
        Should.Throw<StockRuleException>(() => StockItem.Declare(42, "v", 2, Now).Adjust(0, Now)).Code.ShouldBe("invalid-delta");

    [Fact]
    public void La_propriete_appartient_au_vendeur_declarant()
    {
        var item = StockItem.Declare(42, "vendor-1", 1, Now);
        item.IsOwnedBy("vendor-1").ShouldBeTrue();
        item.IsOwnedBy("vendor-2").ShouldBeFalse();
    }
}

public class ReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RG03_une_reservation_expire_15_minutes_apres_sa_creation()
    {
        var reservation = Reservation.Active(Guid.NewGuid(), 42, 1, Now);
        reservation.ExpiresAt.ShouldBe(Now.AddMinutes(15));
        reservation.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Une_reservation_fermee_ne_peut_pas_l_etre_une_seconde_fois()
    {
        var reservation = Reservation.Active(Guid.NewGuid(), 42, 1, Now);
        reservation.MarkCommitted();
        Should.Throw<InvalidOperationException>(reservation.MarkReleased);
        reservation.Status.ShouldBe(ReservationStatus.Committed);
    }
}

public class ReserveStockRequestValidatorTests
{
    private readonly ReserveStockRequestValidator _validator = new();

    [Fact]
    public void RG04_la_quantite_par_ligne_va_de_1_a_10()
    {
        _validator.Validate(new ReserveStockRequest(Guid.NewGuid(), [new ReserveLine(1, 10)])).IsValid.ShouldBeTrue();
        _validator.Validate(new ReserveStockRequest(Guid.NewGuid(), [new ReserveLine(1, 11)])).IsValid.ShouldBeFalse();
        _validator.Validate(new ReserveStockRequest(Guid.NewGuid(), [new ReserveLine(1, 0)])).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void RG04_une_commande_contient_au_plus_20_lignes()
    {
        var lines = Enumerable.Range(1, 21).Select(i => new ReserveLine(i, 1)).ToList();
        _validator.Validate(new ReserveStockRequest(Guid.NewGuid(), lines)).IsValid.ShouldBeFalse();
    }
}
