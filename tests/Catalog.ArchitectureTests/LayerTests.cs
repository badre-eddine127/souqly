using NetArchTest.Rules;
using Souqly.Catalog.Application.Products;
using Souqly.Catalog.Domain.Products;

namespace Catalog.ArchitectureTests;

/// <summary>L'architecture devient un test : les dépendances pointent vers le domaine.</summary>
public class LayerTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(Product).Assembly;
    private static readonly System.Reflection.Assembly Application = typeof(ProductService).Assembly;

    [Fact]
    public void Le_domaine_ne_depend_ni_d_ASP_NET_ni_d_EF_Core_ni_des_autres_couches()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Souqly.Catalog.Application",
                "Souqly.Catalog.Infrastructure",
                "Souqly.Catalog.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void L_application_ne_depend_ni_d_EF_Core_ni_de_l_infrastructure()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "Souqly.Catalog.Infrastructure", "Souqly.Catalog.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}
