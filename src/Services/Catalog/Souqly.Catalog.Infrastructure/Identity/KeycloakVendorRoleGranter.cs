using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Souqly.Catalog.Application.Vendors;

namespace Souqly.Catalog.Infrastructure.Identity;

/// <summary>Compte technique catalog-service (client credentials) et emplacement de l'API d'administration Keycloak.</summary>
public sealed class KeycloakAdminOptions
{
    public const string Section = "KeycloakAdmin";

    /// <summary>Adresse de Keycloak, ex. http://localhost:8180.</summary>
    public string BaseAddress { get; set; } = "http://localhost:8180";

    public string Realm { get; set; } = "souqly";

    public string ClientId { get; set; } = "catalog-service";

    /// <summary>Secret fourni par l'environnement (jamais versionné).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    public string VendorRole { get; set; } = "VENDOR";
}

/// <summary>
/// Attribue le rôle de realm VENDOR via l'API d'administration Keycloak. Le compte de service de catalog-service
/// n'a que les droits view-users et manage-users du client realm-management (moindre privilège).
/// L'opération est idempotente : attribuer un rôle déjà présent ne change rien.
/// </summary>
internal sealed class KeycloakVendorRoleGranter(HttpClient http, IOptions<KeycloakAdminOptions> options) : IVendorRoleGranter
{
    public async Task GrantVendorRoleAsync(string userId, CancellationToken ct)
    {
        var settings = options.Value;
        var token = await GetTokenAsync(settings, ct);
        var admin = $"admin/realms/{Uri.EscapeDataString(settings.Realm)}";

        var user = $"{admin}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm";

        // Le rôle est cherché parmi les rôles attribuables à cet utilisateur : view-users suffit
        // (lire /roles/VENDOR exigerait view-realm, un droit sur toute la configuration du realm).
        var available = await GetRolesAsync($"{user}/available", token, ct);
        var role = available.FirstOrDefault(r => r.Name == settings.VendorRole);
        if (role is null)
        {
            // Absent des rôles attribuables : soit l'utilisateur l'a déjà (opération idempotente), soit il n'existe pas.
            var assigned = await GetRolesAsync(user, token, ct);
            if (assigned.Any(r => r.Name == settings.VendorRole))
            {
                return;
            }

            throw new HttpRequestException($"Rôle {settings.VendorRole} introuvable dans Keycloak.");
        }

        using var grant = new HttpRequestMessage(HttpMethod.Post, user)
        {
            Content = JsonContent.Create(new[] { role }),
        };
        grant.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var grantResponse = await http.SendAsync(grant, ct);
        if (grantResponse.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Utilisateur {userId} inconnu de Keycloak.");
        }

        await EnsureSuccessAsync(grantResponse, "l'attribution du rôle", ct);
    }

    private async Task<IReadOnlyList<RoleRepresentation>> GetRolesAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("Utilisateur inconnu de Keycloak.");
        }

        await EnsureSuccessAsync(response, path, ct);
        return await response.Content.ReadFromJsonAsync<List<RoleRepresentation>>(ct) ?? [];
    }

    /// <summary>Comme EnsureSuccessStatusCode, mais le message dit quel appel a échoué et pourquoi (visible dans les logs).</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Keycloak a répondu {(int)response.StatusCode} à {what} : {body[..Math.Min(body.Length, 300)]}", null, response.StatusCode);
        }
    }

    private async Task<string> GetTokenAsync(KeycloakAdminOptions settings, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
        });
        using var response = await http.PostAsync(
            $"realms/{Uri.EscapeDataString(settings.Realm)}/protocol/openid-connect/token", form, ct);
        await EnsureSuccessAsync(response, "la demande de jeton du compte catalog-service", ct);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                    ?? throw new HttpRequestException("Réponse de jeton vide.");
        return token.AccessToken;
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record RoleRepresentation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);
}
