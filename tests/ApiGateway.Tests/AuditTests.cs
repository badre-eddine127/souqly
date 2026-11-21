using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Souqly.ApiGateway;
using Souqly.Chassis.Paging;
using Souqly.Testing;

namespace ApiGateway.Tests;

/// <summary>US-18 : la passerelle trace les actions sensibles ; seul l'administrateur consulte le journal.</summary>
public class AuditTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient ClientAs(string user, params string[] roles) => factory.CreateClient().WithToken(TestJwt.Token(user, roles));

    private async Task<IReadOnlyList<AuditEntry>> JournalAsync(string query)
    {
        var page = await ClientAs("audit-admin", "ADMIN")
            .GetFromJsonAsync<PageResponse<AuditEntry>>($"/api/v1/admin/audit?{query}", Json);
        return page!.Content;
    }

    [Fact]
    public async Task Une_ecriture_authentifiee_est_tracee_qui_quoi_quand()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await ClientAs("audit-writer", "VENDOR").PostAsync("/api/v1/products?secret=1", null);

        var entry = (await JournalAsync("user=audit-writer")).ShouldHaveSingleItem();
        entry.UserId.ShouldBe("audit-writer");
        entry.Username.ShouldBe("audit-writer");
        entry.Roles.ShouldBe(["VENDOR"]);
        entry.Method.ShouldBe("POST");
        entry.Path.ShouldBe("/api/v1/products");   // jamais la chaîne de requête
        entry.Status.ShouldBe(200);
        entry.At.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task Les_lectures_et_les_requetes_anonymes_ne_sont_pas_tracees()
    {
        await ClientAs("audit-reader", "CUSTOMER").GetAsync("/api/v1/products");
        await factory.CreateClient().PostAsync("/api/v1/products", null);

        (await JournalAsync("user=audit-reader")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_tentative_refusee_403_est_tracee()
    {
        await ClientAs("audit-curious", "CUSTOMER").GetAsync("/api/v1/admin/audit");   // lecture : non tracée
        await ClientAs("audit-curious", "CUSTOMER").PostAsync("/api/v1/vendors/42/approval", null);

        var entry = (await JournalAsync("user=audit-curious")).ShouldHaveSingleItem();
        entry.Path.ShouldBe("/api/v1/vendors/42/approval");
        entry.Status.ShouldBe(403);
    }

    [Fact]
    public async Task Seul_l_administrateur_consulte_le_journal()
    {
        (await ClientAs("audit-vendor", "VENDOR").GetAsync("/api/v1/admin/audit")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/api/v1/admin/audit")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Le_journal_est_du_plus_recent_au_plus_ancien_et_filtrable_par_verbe()
    {
        var writer = ClientAs("audit-sequence", "VENDOR");
        await writer.PostAsync("/api/v1/products", null);
        await writer.PutAsync("/api/v1/products/1", null);

        var entries = await JournalAsync("user=audit-sequence");
        entries.Select(e => e.Method).ShouldBe(["PUT", "POST"]);
        (await JournalAsync("user=audit-sequence&method=POST")).ShouldHaveSingleItem().Method.ShouldBe("POST");
    }
}
