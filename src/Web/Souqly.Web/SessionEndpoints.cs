using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Souqly.Web;

/// <summary>Connexion, déconnexion et identité de l'utilisateur pour les pages JavaScript.</summary>
public static class SessionEndpoints
{
    /// <summary>En-tête exigé sur les appels modifiants : une autre origine ne peut pas l'envoyer sans CORS (anti-CSRF).</summary>
    public const string CsrfHeader = "X-Souqly";

    public static void MapSessionEndpoints(this WebApplication app)
    {
        var session = app.MapGroup("/session");

        session.MapGet("/login", (string? returnUrl) =>
            TypedResults.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }));

        // Formulaire POST (navigation) : le cookie SameSite=Lax n'est pas envoyé par un formulaire d'un autre site.
        session.MapPost("/logout", () => Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

        // US-16 : après la validation de sa boutique, le jeton est renouvelé pour porter le rôle VENDOR (sans se reconnecter).
        session.MapPost("/refresh", async (HttpContext http, TokenRefresher tokens) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }

            var token = await tokens.GetAccessTokenAsync(http, force: true);
            return token is null ? Results.Unauthorized() : Results.Json(new { roles = RealmRoles(token) });
        });

        // Noms des ateliers des vendeurs (le catalogue ne stocke que leur identifiant Keycloak).
        session.MapGet("/vendors", (VendorDirectory directory) => Results.Json(directory.All));

        // Qui suis-je ? Nom et rôles lus dans le jeton d'accès (affichage seulement : chaque service revérifie).
        session.MapGet("/user", async (HttpContext http) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
            {
                return Results.Json(new UserInfo(false, null, null, []));
            }

            var token = await http.GetTokenAsync("access_token");
            return Results.Json(new UserInfo(
                true,
                http.User.FindFirst("preferred_username")?.Value,
                http.User.FindFirst("given_name")?.Value ?? http.User.FindFirst("name")?.Value,
                RealmRoles(token)));
        });

        // Page admin « Gherkin » : rapport de la dernière exécution des tests d'acceptation (scénarios et étapes, OK/KO).
        session.MapGet("/gherkin-report", async (HttpContext http, IConfiguration configuration, IWebHostEnvironment environment) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }

            if (!RealmRoles(await http.GetTokenAsync("access_token")).Contains("ADMIN"))
            {
                return Results.Problem(statusCode: 403, title: "Accès réservé aux administrateurs");
            }

            var path = GherkinReportPath(configuration, environment.ContentRootPath);
            return path is not null && File.Exists(path)
                ? Results.File(path, "application/json")
                : Results.Problem(statusCode: 404, title: "Pas encore de rapport",
                    detail: "Lancez les tests d'acceptation (dotnet test tests/Acceptance.Tests ou l'Explorateur de tests) : ils écrivent artifacts/gherkin-report.json.");
        });
    }

    /// <summary>GherkinReport:Path, sinon artifacts/gherkin-report.json à la racine du dépôt (dossier qui contient Souqly.slnx).</summary>
    internal static string? GherkinReportPath(IConfiguration configuration, string contentRoot)
    {
        if (configuration["GherkinReport:Path"] is { Length: > 0 } configured)
        {
            return configured;
        }

        for (var dir = new DirectoryInfo(contentRoot); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Souqly.slnx")))
            {
                return Path.Combine(dir.FullName, "artifacts", "gherkin-report.json");
            }
        }

        return null;
    }

    /// <summary>N'accepte qu'un chemin local : pas de redirection ouverte vers un autre site.</summary>
    internal static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";

    internal static IReadOnlyList<string> RealmRoles(string? accessToken)
    {
        using var json = Payload(accessToken);
        return json is not null && json.RootElement.TryGetProperty("realm_access", out var access) && access.TryGetProperty("roles", out var roles)
            ? [.. roles.EnumerateArray().Select(r => r.GetString()!).Where(r => r is "CUSTOMER" or "VENDOR" or "ADMIN")]
            : [];
    }

    internal static string? Claim(string? accessToken, string name)
    {
        using var json = Payload(accessToken);
        return json is not null && json.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    /// <summary>Contenu (non vérifié) d'un jeton : le jeton vient de notre propre session, l'API le revérifie.</summary>
    private static JsonDocument? Payload(string? accessToken)
    {
        var parts = accessToken?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            return JsonDocument.Parse(Convert.FromBase64String(payload));
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public sealed record UserInfo(bool Authenticated, string? Username, string? Name, IReadOnlyList<string> Roles);
}
