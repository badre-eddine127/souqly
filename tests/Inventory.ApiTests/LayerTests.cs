using NetArchTest.Rules;
using Souqly.Inventory.Application;
using Souqly.Inventory.Domain;

namespace Inventory.ApiTests;

public class LayerTests
{
    [Fact]
    public void Le_domaine_Inventory_ne_depend_d_aucune_technologie()
    {
        var result = Types.InAssembly(typeof(StockItem).Assembly).ShouldNot()
            .HaveDependencyOnAny("Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql", "Souqly.Inventory.Application", "Souqly.Inventory.Infrastructure")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void L_application_Inventory_ne_depend_pas_d_EF_Core()
    {
        var result = Types.InAssembly(typeof(ReservationService).Assembly).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "Souqly.Inventory.Infrastructure")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}
