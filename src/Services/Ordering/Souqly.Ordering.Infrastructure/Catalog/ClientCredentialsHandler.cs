using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Souqly.Ordering.Infrastructure.Catalog;

/// <summary>Identifiants du compte technique Keycloak du service (client credentials).</summary>
public sealed class ServiceAccountOptions
{
    public const string Section = "ServiceAccount";

    /// <summary>Point de jeton Keycloak, ex. http://localhost:8180/realms/souqly/protocol/openid-connect/token.</summary>
    public string TokenEndpoint { get; set; } = "http://localhost:8180/realms/souqly/protocol/openid-connect/token";

    public string ClientId { get; set; } = "ordering-service";

    /// <summary>Secret fourni par l'environnement (jamais versionné).</summary>
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>
/// Ajoute « Authorization: Bearer » aux appels vers les autres services, avec un jeton client credentials
/// mis en cache jusqu'à 30 s avant son expiration (un seul appel à Keycloak à la fois).
/// </summary>
internal sealed class ClientCredentialsHandler(IHttpClientFactory factory, IOptions<ServiceAccountOptions> options, TimeProvider clock)
    : DelegatingHandler
{
    public const string TokenClientName = "keycloak-token";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _cachedToken;
    private static DateTimeOffset _expiresAt;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && clock.GetUtcNow() < _expiresAt)
        {
            return _cachedToken;
        }

        await Gate.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && clock.GetUtcNow() < _expiresAt)
            {
                return _cachedToken;
            }

            var settings = options.Value;
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret,
            });
            using var response = await factory.CreateClient(TokenClientName).PostAsync(new Uri(settings.TokenEndpoint), form, ct);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                        ?? throw new HttpRequestException("Réponse de jeton vide.");
            _cachedToken = token.AccessToken;
            _expiresAt = clock.GetUtcNow().AddSeconds(Math.Max(token.ExpiresIn - 30, 5));
            return _cachedToken;
        }
        finally
        {
            Gate.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
