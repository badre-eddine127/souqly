using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Souqly.Catalog.Application.Vendors;
using Souqly.Catalog.Infrastructure.Identity;

namespace Catalog.ApiTests;

/// <summary>US-16 : dialogue avec l'API d'administration Keycloak (Keycloak simulé par un faux serveur HTTP).</summary>
public class KeycloakVendorRoleGranterTests
{
    private sealed class FakeKeycloak(bool alreadyVendor) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add($"{request.Method} {path}");
            var body = path switch
            {
                "/realms/souqly/protocol/openid-connect/token" => """{"access_token":"jeton-de-service"}""",
                "/admin/realms/souqly/users/u-1/role-mappings/realm/available" =>
                    alreadyVendor ? """[{"id":"r-admin","name":"ADMIN"}]""" : """[{"id":"r-vendor","name":"VENDOR"},{"id":"r-admin","name":"ADMIN"}]""",
                "/admin/realms/souqly/users/u-1/role-mappings/realm" when request.Method == HttpMethod.Get =>
                    """[{"id":"r-vendor","name":"VENDOR"}]""",
                "/admin/realms/souqly/users/u-1/role-mappings/realm" => "",
                _ => null,
            };
            if (body is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));   // tout autre appel : droit manquant
            }

            var status = request.Method == HttpMethod.Post && path.EndsWith("/realm", StringComparison.Ordinal) ? HttpStatusCode.NoContent : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static IVendorRoleGranter Granter(FakeKeycloak keycloak)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new KeycloakAdminOptions { ClientSecret = "secret" }));
        services.AddHttpClient<IVendorRoleGranter, KeycloakVendorRoleGranter>(c => c.BaseAddress = new Uri("http://keycloak.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => keycloak);
        return services.BuildServiceProvider().GetRequiredService<IVendorRoleGranter>();
    }

    [Fact]
    public async Task Le_role_VENDOR_est_attribue_sans_lire_la_configuration_du_realm()
    {
        var keycloak = new FakeKeycloak(alreadyVendor: false);

        await Granter(keycloak).GrantVendorRoleAsync("u-1", CancellationToken.None);

        keycloak.Calls.ShouldBe([
            "POST /realms/souqly/protocol/openid-connect/token",
            "GET /admin/realms/souqly/users/u-1/role-mappings/realm/available",
            "POST /admin/realms/souqly/users/u-1/role-mappings/realm",
        ]);
    }

    [Fact]
    public async Task Un_utilisateur_deja_vendeur_ne_provoque_pas_d_erreur()
    {
        var keycloak = new FakeKeycloak(alreadyVendor: true);

        await Granter(keycloak).GrantVendorRoleAsync("u-1", CancellationToken.None);

        keycloak.Calls.ShouldNotContain(c => c.StartsWith("POST /admin", StringComparison.Ordinal));
    }
}
