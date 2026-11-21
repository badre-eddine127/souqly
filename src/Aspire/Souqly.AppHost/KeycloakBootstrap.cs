using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Le realm « souqly » n'est importé qu'une fois dans le volume persistant de Keycloak : ce qui a été ajouté depuis
/// est appliqué au realm existant par l'API d'administration, à chaque démarrage (opérations idempotentes) :
/// <list type="bullet">
/// <item>le thème de connexion « souqly » (la lampe et la lanterne) ;</item>
/// <item>le compte technique catalog-service (US-16), autorisé à attribuer le rôle VENDOR (view-users, manage-users).</item>
/// </list>
/// </summary>
internal static class KeycloakBootstrap
{
    private const string Realm = "/admin/realms/souqly";

    public static async Task ApplyAsync(KeycloakResource keycloak, string keycloakUrl, string catalogSecret, ILogger logger, CancellationToken ct)
    {
        try
        {
            using var http = await AdminClientAsync(keycloak, keycloakUrl, ct);

            using (var update = await http.PutAsJsonAsync(Relative(Realm), new { loginTheme = "souqly" }, ct))
            {
                update.EnsureSuccessStatusCode();
            }

            logger.LogInformation("Thème de connexion « souqly » appliqué au realm.");

            await EnsureCatalogServiceAsync(http, catalogSecret, ct);
            logger.LogInformation("Compte technique catalog-service prêt (attribution du rôle VENDOR).");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or JsonException)
        {
            // La connexion fonctionne quand même ; seule la validation des vendeurs (US-16) en dépend.
            logger.LogWarning(ex, "Configuration complémentaire de Keycloak impossible.");
        }
    }

    private static async Task<HttpClient> AdminClientAsync(KeycloakResource keycloak, string keycloakUrl, CancellationToken ct)
    {
        var user = keycloak.AdminUserNameParameter is { } u ? await u.GetValueAsync(ct) : "admin";
        var password = await keycloak.AdminPasswordParameter.GetValueAsync(ct);
        var http = new HttpClient { BaseAddress = new Uri(keycloakUrl) };

        using var tokenResponse = await http.PostAsync(Relative("/realms/master/protocol/openid-connect/token"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = user ?? "admin",
            ["password"] = password ?? string.Empty,
        }), ct);
        tokenResponse.EnsureSuccessStatusCode();
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task EnsureCatalogServiceAsync(HttpClient http, string secret, CancellationToken ct)
    {
        var id = await FindClientIdAsync(http, "catalog-service", ct);
        if (id is null)
        {
            using var create = await http.PostAsJsonAsync(Relative($"{Realm}/clients"), new
            {
                clientId = "catalog-service",
                name = "Service Catalog (compte technique)",
                description = "Client confidentiel en client credentials : attribue le rôle VENDOR après validation d'une boutique (US-16).",
                enabled = true,
                publicClient = false,
                clientAuthenticatorType = "client-secret",
                secret,
                protocol = "openid-connect",
                standardFlowEnabled = false,
                directAccessGrantsEnabled = false,
                serviceAccountsEnabled = true,
            }, ct);
            create.EnsureSuccessStatusCode();
            id = await FindClientIdAsync(http, "catalog-service", ct) ?? throw new InvalidOperationException("Client catalog-service non créé.");
        }
        else
        {
            // Le secret suit le paramètre de l'AppHost (secrets utilisateur).
            using var update = await http.PutAsJsonAsync(Relative($"{Realm}/clients/{id}"), new { secret }, ct);
            update.EnsureSuccessStatusCode();
        }

        var serviceAccount = await http.GetFromJsonAsync<JsonElement>(Relative($"{Realm}/clients/{id}/service-account-user"), ct);
        var serviceAccountId = serviceAccount.GetProperty("id").GetString();

        var realmManagement = await FindClientIdAsync(http, "realm-management", ct)
                              ?? throw new InvalidOperationException("Client realm-management introuvable.");
        var roles = new List<JsonElement>();
        foreach (var role in new[] { "view-users", "manage-users" })
        {
            roles.Add(await http.GetFromJsonAsync<JsonElement>(Relative($"{Realm}/clients/{realmManagement}/roles/{role}"), ct));
        }

        using var grant = await http.PostAsJsonAsync(
            Relative($"{Realm}/users/{serviceAccountId}/role-mappings/clients/{realmManagement}"), roles, ct);
        grant.EnsureSuccessStatusCode();
    }

    private static async Task<string?> FindClientIdAsync(HttpClient http, string clientId, CancellationToken ct)
    {
        var clients = await http.GetFromJsonAsync<JsonElement>(Relative($"{Realm}/clients?clientId={Uri.EscapeDataString(clientId)}"), ct);
        return clients.GetArrayLength() > 0 ? clients[0].GetProperty("id").GetString() : null;
    }

    private static Uri Relative(string path) => new(path, UriKind.Relative);
}
