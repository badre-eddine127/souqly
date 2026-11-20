using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Souqly.Chassis.Security;

/// <summary>
/// Authentification de développement et de test : lit X-User-Id, X-User-Roles et X-User-Email.
/// Activée uniquement quand Authentication:Mode = "DevHeaders" (jamais en production).
/// Elle sera remplacée par JwtBearer + Keycloak au sprint 2 sans changer les endpoints,
/// car ceux-ci ne dépendent que des claims "sub" et des rôles.
/// </summary>
public sealed class DevHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevHeaders";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers["X-User-Id"].ToString();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", userId) };
        var email = Request.Headers["X-User-Email"].ToString();
        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim("email", email));
        }

        foreach (var role in Request.Headers["X-User-Roles"].ToString()
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(ClaimTypes.Role, role.ToUpperInvariant()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, "sub", ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
