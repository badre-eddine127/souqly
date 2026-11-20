using NetArchTest.Rules;
using Souqly.Ordering.Application;
using Souqly.Ordering.Domain;

namespace Ordering.UnitTests;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static OrderLine Line(long productId, decimal price = 100m, int quantity = 1, string vendor = "vendor-1") =>
        new(productId, vendor, $"Produit {productId}", price, quantity);

    private static Order NewOrder() => Order.Place("sara", [Line(1, 1200m), Line(2, 50m, 2, "vendor-2")], Now);

    [Fact]
    public void Une_nouvelle_commande_est_en_attente_avec_le_total_des_prix_figes()
    {
        var order = NewOrder();
        order.Status.ShouldBe(OrderStatus.Pending);
        order.Total.ShouldBe(1300m);
        order.Lines.Count.ShouldBe(2);
    }

    [Fact]
    public void RG04_une_commande_vide_ou_de_plus_de_20_lignes_est_refusee()
    {
        Should.Throw<OrderRuleException>(() => Order.Place("sara", [], Now)).Code.ShouldBe("invalid-lines");
        var lines = Enumerable.Range(1, 21).Select(i => Line(i)).ToList();
        Should.Throw<OrderRuleException>(() => Order.Place("sara", lines, Now)).Code.ShouldBe("invalid-lines");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void RG04_la_quantite_par_ligne_va_de_1_a_10(int quantity) =>
        Should.Throw<OrderRuleException>(() => Order.Place("sara", [Line(1, quantity: quantity)], Now)).Code.ShouldBe("invalid-quantity");

    [Fact]
    public void RG04_le_total_ne_depasse_pas_50000_MAD()
    {
        Should.NotThrow(() => Order.Place("sara", [Line(1, 5000m, 10)], Now));
        Should.Throw<OrderRuleException>(() => Order.Place("sara", [Line(1, 5000m, 10), Line(2, 0.01m)], Now)).Code.ShouldBe("total-too-high");
    }

    [Fact]
    public void Un_meme_produit_ne_peut_pas_apparaitre_deux_fois() =>
        Should.Throw<OrderRuleException>(() => Order.Place("sara", [Line(1), Line(1)], Now)).Code.ShouldBe("duplicate-product");

    [Fact]
    public void Le_parcours_nominal_va_de_PENDING_a_DELIVERED()
    {
        var order = NewOrder();
        order.MarkStockReserved(Now);
        order.Confirm(Now);
        order.Ship(Now);
        order.Deliver(Now);
        order.Status.ShouldBe(OrderStatus.Delivered);
    }

    [Fact]
    public void Stock_insuffisant_la_commande_PENDING_est_rejetee()
    {
        var order = NewOrder();
        order.Reject("OUT_OF_STOCK", Now);
        order.Status.ShouldBe(OrderStatus.Rejected);
        order.StatusReason.ShouldBe("OUT_OF_STOCK");
    }

    [Fact]
    public void RG06_une_commande_confirmee_peut_etre_annulee_mais_pas_une_commande_expediee()
    {
        var confirmed = NewOrder();
        confirmed.MarkStockReserved(Now);
        confirmed.Confirm(Now);
        Should.NotThrow(() => confirmed.Cancel("CUSTOMER_REQUEST", Now));

        var shipped = NewOrder();
        shipped.MarkStockReserved(Now);
        shipped.Confirm(Now);
        shipped.Ship(Now);
        Should.Throw<IllegalOrderTransitionException>(() => shipped.Cancel("CUSTOMER_REQUEST", Now));
        shipped.Status.ShouldBe(OrderStatus.Shipped);
    }

    [Fact]
    public void Une_transition_illegale_est_refusee()
    {
        var order = NewOrder();
        Should.Throw<IllegalOrderTransitionException>(() => order.Confirm(Now));   // il faut d'abord le stock
        Should.Throw<IllegalOrderTransitionException>(() => order.Ship(Now));
        var rejected = NewOrder();
        rejected.Reject("OUT_OF_STOCK", Now);
        Should.Throw<IllegalOrderTransitionException>(() => rejected.Cancel("x", Now));
    }

    [Fact]
    public void RG05_un_vendeur_ne_voit_que_ses_lignes()
    {
        var order = NewOrder();
        order.LinesOf("vendor-2").Select(l => l.ProductId).ShouldBe([2L]);
    }

    [Fact]
    public void Un_prix_nul_ou_negatif_est_refuse() =>
        Should.Throw<OrderRuleException>(() => Line(1, 0m));

    [Fact]
    public void Le_domaine_Ordering_ne_depend_d_aucune_technologie()
    {
        var result = Types.InAssembly(typeof(Order).Assembly).ShouldNot()
            .HaveDependencyOnAny("Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql", "Souqly.Ordering.Application")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue();
        Types.InAssembly(typeof(OrderService).Assembly).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Souqly.Ordering.Infrastructure").GetResult().IsSuccessful.ShouldBeTrue();
    }
}
