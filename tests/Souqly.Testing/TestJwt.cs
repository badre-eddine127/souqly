using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Souqly.Testing;

/// <summary>
/// Jetons JWT de test, au format Keycloak (sub, realm_access.roles, aud souqly-api), signés RS256 avec une clé de test.
/// Les services les valident avec le vrai pipeline JwtBearer : seuls la clé et l'émetteur changent.
/// </summary>
public static class TestJwt
{
    public const string Issuer = "https://keycloak.test/realms/souqly";
    public const string Audience = "souqly-api";

    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "souqly-test" };
    private static readonly RsaSecurityKey OtherKey = new(RSA.Create(2048)) { KeyId = "intruder" };
    private static readonly JsonWebTokenHandler Handler = new();

    public static string Token(string userId, params string[] roles) =>
        Create(userId, roles, Key, Issuer, Audience, DateTime.UtcNow.AddMinutes(5));

    /// <summary>Jeton signé par une autre clé : doit être refusé (401).</summary>
    public static string ForgedToken(string userId, params string[] roles) =>
        Create(userId, roles, OtherKey, Issuer, Audience, DateTime.UtcNow.AddMinutes(5));

    /// <summary>Jeton expiré : doit être refusé (401).</summary>
    public static string ExpiredToken(string userId, params string[] roles) =>
        Create(userId, roles, Key, Issuer, Audience, DateTime.UtcNow.AddMinutes(-10));

    /// <summary>Jeton destiné à une autre application (aud = account) : doit être refusé (401).</summary>
    public static string WrongAudienceToken(string userId, params string[] roles) =>
        Create(userId, roles, Key, Issuer, "account", DateTime.UtcNow.AddMinutes(5));

    /// <summary>Remplace la découverte Keycloak par la clé de test ; tout le reste de la validation est inchangé.</summary>
    public static void UseTestJwt(this IServiceCollection services) =>
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            o.Authority = null;
            o.MetadataAddress = null!;
            o.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration { Issuer = Issuer };
            o.Configuration.SigningKeys.Add(Key);
            o.TokenValidationParameters.ValidIssuer = Issuer;
            o.TokenValidationParameters.ValidAudience = Audience;
            o.TokenValidationParameters.IssuerSigningKey = Key;
        });

    private static string Create(string userId, string[] roles, SecurityKey key, string issuer, string audience, DateTime expires)
    {
        var notBefore = expires.AddMinutes(-15);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore,
            IssuedAt = notBefore,
            Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = userId,
                ["preferred_username"] = userId,
                ["email"] = $"{userId}@example.test",
                ["realm_access"] = JsonSerializer.SerializeToElement(new { roles }),
            },
        };
        return Handler.CreateToken(descriptor);
    }
}

public static class TestClients
{
    public static HttpClient WithToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());
}
