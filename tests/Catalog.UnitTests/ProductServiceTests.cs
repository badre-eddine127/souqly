using NSubstitute;
using Souqly.Catalog.Application.Products;
using Souqly.Catalog.Domain.Products;
using Souqly.Chassis.Errors;
using Souqly.Chassis.Security;
using Souqly.Messaging;

namespace Catalog.UnitTests;

public class ProductServiceTests
{
    private readonly IProductRepository _repository = Substitute.For<IProductRepository>();
    private readonly IEventPublisher _events = Substitute.For<IEventPublisher>();
    private readonly ProductService _service;
    private static readonly CurrentUser Vendor = new("vendor-1", null, new HashSet<string> { Roles.Vendor });
    private static readonly CurrentUser OtherVendor = new("vendor-2", null, new HashSet<string> { Roles.Vendor });

    public ProductServiceTests()
    {
        _service = new ProductService(_repository, _events, TimeProvider.System);
        _repository.CategoryExistsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);
        _repository.InTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Func<Task>>()());
    }

    private static ProductRequest Request(string amount = "1200.00", bool publish = false) =>
        new(2, "TAPIS-1", "Tapis berbère", "Laine", new MoneyDto(amount, "MAD"), publish);

    [Fact]
    public async Task Creer_un_produit_le_rattache_au_vendeur_appelant()
    {
        var created = await _service.CreateAsync(Request(publish: true), Vendor, CancellationToken.None);

        created.VendorId.ShouldBe("vendor-1");
        created.Status.ShouldBe("ACTIVE");
        created.Price.ShouldBe(new MoneyDto("1200.00", "MAD"));
        _repository.Received(1).Add(Arg.Is<Product>(p => p.VendorId == "vendor-1"));
        await _repository.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());   // produit, puis événement (même transaction)
        _events.Received(1).Publish(Topics.CatalogEvents, Arg.Any<string>(),
            Arg.Is<ProductCreated>(e => e.Product.VendorId == "vendor-1" && e.Product.Status == "ACTIVE" && e.Product.Price == 1200m));
    }

    [Fact]
    public async Task Modifier_puis_archiver_publie_l_etat_complet_du_produit()
    {
        var product = Product.Create("vendor-1", 2, "TAPIS-1", "Tapis berbère", "Laine", 1200m, DateTimeOffset.UtcNow);
        _repository.FindAsync(5, Arg.Any<CancellationToken>()).Returns(product);

        await _service.UpdateAsync(5, Request("950.00", publish: true), Vendor, CancellationToken.None);
        await _service.ArchiveAsync(5, Vendor, CancellationToken.None);

        _events.Received(1).Publish(Topics.CatalogEvents, Arg.Any<string>(), Arg.Is<ProductUpdated>(e => e.Product.Price == 950m && e.Product.Status == "ACTIVE"));
        _events.Received(1).Publish(Topics.CatalogEvents, Arg.Any<string>(), Arg.Is<ProductArchived>(e => e.Product.Status == "ARCHIVED"));
    }

    [Fact]
    public async Task Une_categorie_inconnue_donne_une_erreur_metier()
    {
        _repository.CategoryExistsAsync(99, Arg.Any<CancellationToken>()).Returns(false);
        var request = Request() with { CategoryId = 99 };

        var error = await Should.ThrowAsync<BusinessRuleException>(() => _service.CreateAsync(request, Vendor, CancellationToken.None));
        error.Code.ShouldBe("unknown-category");
    }

    [Fact]
    public async Task Un_SKU_deja_utilise_par_le_vendeur_est_un_conflit()
    {
        _repository.SkuExistsForVendorAsync("vendor-1", "TAPIS-1", null, Arg.Any<CancellationToken>()).Returns(true);

        await Should.ThrowAsync<ConflictException>(() => _service.CreateAsync(Request(), Vendor, CancellationToken.None));
    }

    [Fact]
    public async Task Un_vendeur_ne_peut_pas_modifier_le_produit_d_un_autre_404()
    {
        var product = Product.Create("vendor-1", 2, "TAPIS-1", "Tapis", "", 100m, DateTimeOffset.UtcNow);
        _repository.FindAsync(7, Arg.Any<CancellationToken>()).Returns(product);

        await Should.ThrowAsync<NotFoundException>(() => _service.UpdateAsync(7, Request(), OtherVendor, CancellationToken.None));
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_brouillon_est_invisible_pour_le_public()
    {
        var draft = Product.Create("vendor-1", 2, "TAPIS-1", "Tapis", "", 100m, DateTimeOffset.UtcNow);
        _repository.FindAsync(7, Arg.Any<CancellationToken>()).Returns(draft);

        await Should.ThrowAsync<NotFoundException>(() => _service.GetAsync(7, null, CancellationToken.None));
        (await _service.GetAsync(7, Vendor, CancellationToken.None)).Status.ShouldBe("DRAFT");
    }

    [Theory]
    [InlineData(null, ProductSort.Newest)]
    [InlineData("price,asc", ProductSort.PriceAsc)]
    [InlineData("price, desc", ProductSort.PriceDesc)]
    [InlineData("NAME", ProductSort.NameAsc)]
    public void Le_tri_est_lu_au_format_champ_sens(string? sort, ProductSort expected) =>
        ProductService.ParseSort(sort).ShouldBe(expected);

    [Fact]
    public void Un_tri_inconnu_est_refuse() =>
        Should.Throw<BusinessRuleException>(() => ProductService.ParseSort("id; DROP TABLE products"));

    [Fact]
    public async Task L_instantane_de_prix_refuse_plus_de_20_produits()
    {
        var ids = Enumerable.Range(1, 21).Select(i => (long)i).ToList();
        await Should.ThrowAsync<BusinessRuleException>(() => _service.PriceSnapshotAsync(ids, CancellationToken.None));
    }
}
