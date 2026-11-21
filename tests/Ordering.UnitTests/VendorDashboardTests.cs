using NSubstitute;
using Souqly.Chassis.Security;
using Souqly.Messaging;
using Souqly.Ordering.Application;
using Souqly.Ordering.Domain;

namespace Ordering.UnitTests;

/// <summary>US-17 : chiffres du tableau de bord vendeur.</summary>
public class VendorDashboardTests
{
    // 21/11/2026 à 14 h UTC : l'après-midi au Maroc.
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 14, 0, 0, TimeSpan.Zero);
    private static readonly CurrentUser Fatima = new("fatima", null, new HashSet<string> { Roles.Vendor });

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Order PaidOrder(DateTimeOffset at, decimal price, int quantity, string otherVendorPrice = "0")
    {
        var lines = new List<OrderLine> { new(1, "fatima", "Tapis", price, quantity) };
        if (otherVendorPrice != "0")
        {
            lines.Add(new OrderLine(2, "karim", "Huile d'argan", decimal.Parse(otherVendorPrice, System.Globalization.CultureInfo.InvariantCulture), 1));
        }

        var order = Order.Place("sara", lines, at);
        order.MarkStockReserved(at);
        order.Confirm(Guid.NewGuid(), at);
        return order;
    }

    private Task<VendorDashboardResponse> DashboardAsync(params Order[] orders)
    {
        _orders.ListForVendorDashboardAsync("fatima", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(orders);
        var service = new OrderService(_orders, Substitute.For<ICatalogPort>(), Substitute.For<IEventPublisher>(), new FixedClock(Now));
        return service.DashboardAsync(Fatima, CancellationToken.None);
    }

    [Fact]
    public async Task Le_jour_commence_a_minuit_heure_du_Maroc()
    {
        await DashboardAsync();

        // Minuit du jour courant à Casablanca (selon la base des fuseaux de la machine : UTC+1, UTC+0 pendant le ramadan).
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById("Africa/Casablanca", out var z) ? z : TimeZoneInfo.CreateCustomTimeZone("Morocco", TimeSpan.FromHours(1), "Maroc", "Maroc");
        await _orders.Received(1).ListForVendorDashboardAsync("fatima",
            Arg.Is<DateTimeOffset>(d => TimeZoneInfo.ConvertTime(d, zone).TimeOfDay == TimeSpan.Zero && d <= Now && Now - d < TimeSpan.FromDays(1)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Les_ventes_du_jour_ne_comptent_que_les_lignes_du_vendeur_des_commandes_payees()
    {
        var paid = PaidOrder(Now.AddHours(-2), 1200m, 1, otherVendorPrice: "300");
        var unpaid = Order.Place("sara", [new OrderLine(1, "fatima", "Tapis", 999m, 1)], Now.AddHours(-1));

        var dashboard = await DashboardAsync(paid, unpaid);

        dashboard.Day.ShouldBe(new DateOnly(2026, 11, 21));
        dashboard.OrdersToday.ShouldBe(1);
        dashboard.UnitsSoldToday.ShouldBe(1);
        dashboard.SalesToday.Amount.ShouldBe("1200.00");
        dashboard.AwaitingPaymentToday.ShouldBe(1);
    }

    [Fact]
    public async Task A_preparer_les_commandes_confirmees_sans_colis_du_vendeur_les_plus_anciennes_d_abord()
    {
        var older = PaidOrder(Now.AddDays(-2), 100m, 1);
        var recent = PaidOrder(Now.AddHours(-1), 200m, 2);
        var shipped = PaidOrder(Now.AddHours(-3), 300m, 1);
        shipped.ShipFor("fatima", "AMANA", "AM123456789MA", Now);

        var dashboard = await DashboardAsync(recent, shipped, older);

        dashboard.ToPrepareCount.ShouldBe(2);
        dashboard.ToPrepare.Select(o => o.OrderId).ShouldBe([older.Id, recent.Id]);
        dashboard.ToPrepare[1].VendorTotal.Amount.ShouldBe("400.00");
    }
}
