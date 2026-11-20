using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace Souqly.Chassis.Security;

/// <summary>
/// Keycloak place les rôles du realm dans un objet JSON : "realm_access": { "roles": ["VENDOR", …] }.
/// ASP.NET Core attend un claim par rôle : cette transformation fait la conversion, une seule fois par requête.
/// </summary>
public sealed class KeycloakRolesTransformation : IClaimsTransformation
{
    private const string Marker = "souqly:roles-mapped";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity || identity.HasClaim(c => c.Type == Marker))
        {
            return Task.FromResult(principal);
        }

        var realmAccess = identity.FindFirst("realm_access")?.Value;
        if (!string.IsNullOrEmpty(realmAccess))
        {
            using var document = JsonDocument.Parse(realmAccess);
            if (document.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in roles.EnumerateArray().Select(r => r.GetString()).Where(r => !string.IsNullOrEmpty(r)))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role!));
                }
            }
        }

        identity.AddClaim(new Claim(Marker, "1"));
        return Task.FromResult(principal);
    }
}
