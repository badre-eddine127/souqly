using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Thème de connexion « souqly » (la lampe) : le realm est importé une seule fois dans le volume persistant de
/// Keycloak, donc on applique le thème au realm existant par l'API d'administration, à chaque démarrage (idempotent).
/// </summary>
internal static class KeycloakTheme
{
    public static async Task ApplyAsync(KeycloakResource keycloak, string keycloakUrl, ILogger logger, CancellationToken ct)
    {
        try
        {
            var user = keycloak.AdminUserNameParameter is { } u ? await u.GetValueAsync(ct) : "admin";
            var password = await keycloak.AdminPasswordParameter.GetValueAsync(ct);
            using var http = new HttpClient { BaseAddress = new Uri(keycloakUrl) };

            using var tokenResponse = await http.PostAsync(new Uri("/realms/master/protocol/openid-connect/token", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = user ?? "admin",
                ["password"] = password ?? string.Empty,
            }), ct);
            tokenResponse.EnsureSuccessStatusCode();
            var token = (await tokenResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>(ct))!["access_token"].ToString();

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var update = await http.PutAsJsonAsync(new Uri("/admin/realms/souqly", UriKind.Relative), new { loginTheme = "souqly" }, ct);
            update.EnsureSuccessStatusCode();
            logger.LogInformation("Thème de connexion « souqly » appliqué au realm.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            // La connexion fonctionne quand même avec le thème Keycloak standard.
            logger.LogWarning(ex, "Impossible d'appliquer le thème de connexion Souqly.");
        }
    }
}
