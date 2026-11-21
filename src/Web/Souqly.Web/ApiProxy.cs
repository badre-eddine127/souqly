using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace Souqly.Web;

/// <summary>
/// Relais /api/** vers la passerelle Souqly : le navigateur appelle la même origine avec son cookie,
/// le serveur ajoute le jeton d'accès Keycloak (renouvelé si besoin). Aucun jeton n'arrive dans le JavaScript.
/// </summary>
public static class ApiProxy
{
    public static IServiceCollection AddSouqlyApiProxy(this IServiceCollection services, IConfiguration configuration)
    {
        var gateway = configuration["Gateway:Address"] ?? "http://localhost:8080";
        services.AddReverseProxy()
            .LoadFromMemory(
                [new RouteConfig { RouteId = "api", ClusterId = "gateway", Match = new RouteMatch { Path = "/api/{**rest}" } }],
                [new ClusterConfig { ClusterId = "gateway", Destinations = new Dictionary<string, DestinationConfig> { ["d1"] = new() { Address = gateway } } }])
            .AddTransforms(t => t.AddRequestTransform(async context =>
            {
                var http = context.HttpContext;
                context.ProxyRequest.Headers.Remove("Cookie");   // le cookie de session ne quitte pas le site web
                if (http.User.Identity?.IsAuthenticated == true)
                {
                    var token = await http.RequestServices.GetRequiredService<TokenRefresher>().GetAccessTokenAsync(http);
                    if (token is not null)
                    {
                        context.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                }
            }));
        return services;
    }

    /// <summary>Anti-CSRF : un appel modifiant doit porter l'en-tête ajouté par notre JavaScript (une autre origine ne le peut pas).</summary>
    public static void MapSouqlyApiProxy(this WebApplication app) =>
        app.MapReverseProxy(pipeline => pipeline.Use(async (http, next) =>
        {
            if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && !http.Request.Headers.ContainsKey(SessionEndpoints.CsrfHeader))
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next();
        }));
}

/// <summary>Renouvelle le jeton d'accès (5 minutes chez Keycloak) avec le refresh token, et met à jour le cookie.</summary>
public sealed class TokenRefresher(
    IHttpClientFactory httpClients,
    IConfiguration configuration,
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
    TimeProvider clock,
    ILogger<TokenRefresher> logger)
{
    public const string HttpClientName = "keycloak-token";

    public async Task<string?> GetAccessTokenAsync(HttpContext http)
    {
        var result = await http.AuthenticateAsync();
        if (!result.Succeeded || result.Properties is null)
        {
            return null;
        }

        var properties = result.Properties;
        var accessToken = properties.GetTokenValue("access_token");
        var expiresAt = properties.GetTokenValue("expires_at");
        if (accessToken is not null && DateTimeOffset.TryParse(expiresAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var expiry)
            && expiry > clock.GetUtcNow().AddSeconds(30))
        {
            return accessToken;
        }

        var refreshToken = properties.GetTokenValue("refresh_token");
        if (refreshToken is null)
        {
            return accessToken;
        }

        // Adresse lue dans la découverte OIDC (réseau interne en conteneur, localhost:8180 en développement).
        var discovery = await oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme).ConfigurationManager!.GetConfigurationAsync(http.RequestAborted);
        var tokenEndpoint = discovery.TokenEndpoint;
        using var response = await httpClients.CreateClient(HttpClientName).PostAsync(tokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = configuration["Oidc:ClientId"]!,
            ["refresh_token"] = refreshToken,
        }), http.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            // Session Keycloak expirée : l'utilisateur devra se reconnecter (l'API répondra 401).
            logger.LogInformation("Renouvellement du jeton refusé ({Status}) : session terminée.", (int)response.StatusCode);
            await http.SignOutAsync();
            return null;
        }

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(http.RequestAborted);
        properties.UpdateTokenValue("access_token", tokens!.AccessToken);
        properties.UpdateTokenValue("refresh_token", tokens.RefreshToken ?? refreshToken);
        properties.UpdateTokenValue("expires_at", clock.GetUtcNow().AddSeconds(tokens.ExpiresIn).ToString("o", System.Globalization.CultureInfo.InvariantCulture));
        await http.SignInAsync(result.Principal!, properties);
        return tokens.AccessToken;
    }

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);
}
