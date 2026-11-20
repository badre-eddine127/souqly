using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Souqly.Chassis.Security;

public static class Roles
{
    public const string Customer = "CUSTOMER";
    public const string Vendor = "VENDOR";
    public const string Admin = "ADMIN";
}

/// <summary>Utilisateur courant, lu dans le jeton : "sub" Keycloak et rôles.</summary>
public sealed record CurrentUser(string Id, string? Email, IReadOnlySet<string> Roles)
{
    public bool IsInRole(string role) => Roles.Contains(role);

    public static CurrentUser From(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Jeton sans identifiant (sub)");
        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        return new CurrentUser(id, principal.FindFirstValue("email"), roles);
    }
}

public static class CurrentUserExtensions
{
    public static CurrentUser CurrentUser(this HttpContext context) => Security.CurrentUser.From(context.User);
}
