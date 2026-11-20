using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Souqly.Chassis.Security;

/// <summary>
/// Paramètres de la section "Authentication" (appsettings, variables d'environnement Authentication__…).
/// </summary>
public sealed class SouqlyAuthOptions
{
    /// <summary>Émetteur des jetons, tel que le voit le navigateur : http://localhost:8180/realms/souqly.</summary>
    public string Authority { get; set; } = "http://localhost:8180/realms/souqly";

    /// <summary>Adresse interne du document de découverte si elle diffère (conteneurs : http://keycloak:8080/…).</summary>
    public string? MetadataAddress { get; set; }

    /// <summary>Audience attendue dans "aud" : seuls les jetons destinés aux API Souqly sont acceptés.</summary>
    public string Audience { get; set; } = "souqly-api";

    /// <summary>Client public utilisé par Scalar et le frontend (Authorization Code + PKCE).</summary>
    public string PublicClientId { get; set; } = "souqly-web";
}

public static class JwtSetup
{
    /// <summary>Chaque service revalide lui-même le jeton (défense en profondeur) : signature, émetteur, audience, expiration.</summary>
    public static IServiceCollection AddSouqlyJwt(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection("Authentication").Get<SouqlyAuthOptions>() ?? new SouqlyAuthOptions();
        services.AddSingleton(options);
        services.AddTransient<IClaimsTransformation, KeycloakRolesTransformation>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority;
                jwt.MetadataAddress = string.IsNullOrWhiteSpace(options.MetadataAddress)
                    ? $"{options.Authority.TrimEnd('/')}/.well-known/openid-configuration"
                    : options.MetadataAddress;
                jwt.Audience = options.Audience;
                jwt.RequireHttpsMetadata = !environment.IsDevelopment() && options.Authority.StartsWith("https", StringComparison.Ordinal);
                jwt.MapInboundClaims = false;   // garde "sub", "email", "realm_access" tels quels
                jwt.TokenValidationParameters.ValidIssuer = options.Authority.TrimEnd('/');
                jwt.TokenValidationParameters.NameClaimType = "preferred_username";
                jwt.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
                jwt.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            });
        return services;
    }
}
