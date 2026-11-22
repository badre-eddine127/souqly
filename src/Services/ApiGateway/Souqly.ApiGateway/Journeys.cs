using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Souqly.Chassis;
using StackExchange.Redis;

namespace Souqly.ApiGateway;

/// <summary>
/// Une action réelle d'un utilisateur, écrite comme une étape Gherkin (« Quand le client "sara" ajoute 2 × {produit:12} au panier »)
/// avec son résultat (OK si le service a répondu 2xx/3xx, KO sinon). {produit:12} est remplacé par le nom du produit dans la page admin.
/// Aucune donnée personnelle : identifiant Keycloak, nom de connexion, et seulement quelques champs du corps (quantités, transporteur…).
/// </summary>
public sealed record JourneyStep(
    DateTimeOffset At,
    string UserId,
    string? Username,
    string Actor,
    string Text,
    string Method,
    string Path,
    int Status,
    bool Ok);

/// <summary>Un « scénario » vécu : les actions successives d'un utilisateur, sans pause de plus de 30 minutes.</summary>
public sealed record JourneyScenario(
    string UserId,
    string? Username,
    string Actor,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    bool Ok,
    int Passed,
    int Failed,
    IReadOnlyList<GherkinLine> Lines);

/// <summary>Une ligne Gherkin affichée : mot-clé (Étant donné que, Quand, Et, Alors), texte, et résultat pour les lignes « Alors ».</summary>
public sealed record GherkinLine(string Keyword, string Text, bool? Ok, int? Status, DateTimeOffset? At);

public sealed class JourneyOptions
{
    public const string Section = "Journeys";

    public int Capacity { get; set; } = 5_000;

    /// <summary>Au-delà de cette pause, les actions suivantes forment un nouveau scénario.</summary>
    public TimeSpan SessionGap { get; set; } = TimeSpan.FromMinutes(30);
}

public interface IJourneyStore
{
    Task AppendAsync(JourneyStep step, CancellationToken ct);

    /// <summary>Les étapes, de la plus récente à la plus ancienne.</summary>
    Task<IReadOnlyList<JourneyStep>> ReadAllAsync(CancellationToken ct);
}

internal sealed class RedisJourneyStore(IConnectionMultiplexer redis, IOptions<JourneyOptions> options) : IJourneyStore
{
    private const string Key = "souqly:journeys";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync(JourneyStep step, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        await db.ListLeftPushAsync(Key, JsonSerializer.Serialize(step, Json));
        await db.ListTrimAsync(Key, 0, options.Value.Capacity - 1);
    }

    public async Task<IReadOnlyList<JourneyStep>> ReadAllAsync(CancellationToken ct) =>
        (await redis.GetDatabase().ListRangeAsync(Key, 0, -1))
            .Select(v => JsonSerializer.Deserialize<JourneyStep>(v.ToString(), Json))
            .OfType<JourneyStep>()
            .ToList();
}

internal sealed class InMemoryJourneyStore(IOptions<JourneyOptions> options) : IJourneyStore
{
    private readonly ConcurrentQueue<JourneyStep> _steps = new();

    public Task AppendAsync(JourneyStep step, CancellationToken ct)
    {
        _steps.Enqueue(step);
        while (_steps.Count > options.Value.Capacity && _steps.TryDequeue(out _))
        {
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JourneyStep>> ReadAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<JourneyStep>>(_steps.Reverse().ToList());
}

/// <summary>Traduction d'une requête en phrase Gherkin. Seules les actions « métier » connues sont retenues.</summary>
internal static partial class GherkinPhrases
{
    private sealed record Rule(string Method, Regex Path, Func<Match, IReadOnlyDictionary<string, string>, string> Phrase);

    private static readonly Rule[] Rules =
    [
        new("GET", PathRegex(@"^/api/v1/search$"), (_, f) => $"recherche « {f.GetValueOrDefault("q", "")} »"),
        new("PUT", PathRegex(@"^/api/v1/carts/me/items/(?<id>\d+)$"), (m, f) => $"met {f.GetValueOrDefault("quantity", "?")} × {{produit:{m.Groups["id"].Value}}} dans son panier"),
        new("DELETE", PathRegex(@"^/api/v1/carts/me/items/(?<id>\d+)$"), (m, _) => $"retire {{produit:{m.Groups["id"].Value}}} de son panier"),
        new("DELETE", PathRegex(@"^/api/v1/carts/me/?$"), (_, _) => "vide son panier"),
        new("POST", PathRegex(@"^/api/v1/orders/?$"), (_, f) => $"valide son panier ({f.GetValueOrDefault("lines", "?")} ligne(s)) et passe commande"),
        new("POST", PathRegex(@"^/api/v1/orders/(?<id>[0-9a-f-]{36})/cancellation$"), (m, _) => $"annule la commande {Short(m)}"),
        new("POST", PathRegex(@"^/api/v1/products/?$"), (_, _) => "crée un produit"),
        new("PUT", PathRegex(@"^/api/v1/products/(?<id>\d+)$"), (m, _) => $"modifie {{produit:{m.Groups["id"].Value}}}"),
        new("DELETE", PathRegex(@"^/api/v1/products/(?<id>\d+)$"), (m, _) => $"archive {{produit:{m.Groups["id"].Value}}}"),
        new("PUT", PathRegex(@"^/api/v1/stock-items/(?<id>\d+)$"), (m, f) => $"déclare {f.GetValueOrDefault("onHand", "?")} unités en stock pour {{produit:{m.Groups["id"].Value}}}"
            + (f.TryGetValue("lowStockThreshold", out var t) ? $" (alerte à {t})" : string.Empty)),
        new("POST", PathRegex(@"^/api/v1/stock-items/(?<id>\d+)/adjustments$"), (m, f) => $"ajuste le stock de {{produit:{m.Groups["id"].Value}}} de {Signed(f.GetValueOrDefault("delta", "?"))}"),
        new("POST", PathRegex(@"^/api/v1/vendor/orders/(?<id>[0-9a-f-]{36})/shipment$"), (m, f) => $"expédie sa part de la commande {Short(m)} avec {f.GetValueOrDefault("carrier", "?")}"),
        new("POST", PathRegex(@"^/api/v1/vendor/orders/(?<id>[0-9a-f-]{36})/delivery$"), (m, _) => $"confirme la livraison de la commande {Short(m)}"),
        new("POST", PathRegex(@"^/api/v1/vendors/?$"), (_, _) => "demande l'ouverture d'une boutique"),
        new("POST", PathRegex(@"^/api/v1/vendors/(?<id>\d+)/approval$"), (m, f) =>
            $"{(f.GetValueOrDefault("approved") == "true" ? "valide" : "refuse")} la demande de boutique n° {m.Groups["id"].Value}"),
        new("POST", PathRegex(@"^/api/v1/notifications/read-all$"), (_, _) => "ouvre ses notifications"),
    ];

    /// <summary>Champs du corps JSON gardés pour écrire la phrase (jamais d'adresse, de texte libre ou de donnée personnelle).</summary>
    public static readonly string[] BodyFields = ["quantity", "onHand", "delta", "approved", "carrier", "lowStockThreshold"];

    public static bool IsTracked(string method, string path) => Rules.Any(r => r.Method == method && r.Path.IsMatch(path));

    public static string? Phrase(string method, string path, IReadOnlyDictionary<string, string> fields)
    {
        foreach (var rule in Rules)
        {
            if (rule.Method == method && rule.Path.Match(path) is { Success: true } match)
            {
                return rule.Phrase(match, fields);
            }
        }

        return null;
    }

    public static string Actor(IReadOnlyCollection<string> roles, string name) =>
        roles.Contains("ADMIN") ? $"l'administrateur \"{name}\""
        : roles.Contains("VENDOR") ? $"le vendeur \"{name}\""
        : $"le client \"{name}\"";

    public static string Outcome(int status) => status switch
    {
        < 400 => $"la réponse est OK ({status})",
        400 => "la réponse est KO (400 · demande invalide)",
        401 => "la réponse est KO (401 · non connecté)",
        403 => "la réponse est KO (403 · accès refusé)",
        404 => "la réponse est KO (404 · introuvable)",
        409 => "la réponse est KO (409 · conflit)",
        422 => "la réponse est KO (422 · règle métier non respectée)",
        429 => "la réponse est KO (429 · trop de requêtes)",
        503 => "la réponse est KO (503 · service indisponible)",
        _ => $"la réponse est KO ({status})",
    };

    private static string Short(Match m) => m.Groups["id"].Value[..8].ToUpperInvariant();

    private static string Signed(string delta) => delta.StartsWith('-') ? delta : "+" + delta;

    private static Regex PathRegex(string pattern) => new(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
}

/// <summary>
/// Enregistre les actions métier des utilisateurs connectés (après l'authentification, avant l'autorisation : les refus sont gardés).
/// Lit seulement quelques champs du corps JSON (petit corps, mis en mémoire tampon pour que le service le reçoive intact).
/// Un échec d'enregistrement n'affecte jamais la requête.
/// </summary>
internal sealed class JourneyMiddleware(RequestDelegate next, IJourneyStore store, TimeProvider clock, ILogger<JourneyMiddleware> logger)
{
    private const int MaxBody = 8 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value ?? "/";
        if (context.User.Identity?.IsAuthenticated != true || !GherkinPhrases.IsTracked(request.Method, path))
        {
            await next(context);
            return;
        }

        var fields = await ReadFieldsAsync(request);
        if (HttpMethods.IsGet(request.Method) && request.Query.TryGetValue("q", out var q))
        {
            var text = q.ToString().Trim();
            fields["q"] = text.Length > 60 ? text[..60] + "…" : text;
        }

        await next(context);

        try
        {
            var user = context.User;
            var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet();
            var id = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "inconnu";
            var name = user.FindFirstValue("preferred_username");
            var status = context.Response.StatusCode;
            await store.AppendAsync(new JourneyStep(clock.GetUtcNow(), id, name, GherkinPhrases.Actor(roles, name ?? id[..Math.Min(8, id.Length)]),
                GherkinPhrases.Phrase(request.Method, path, fields) ?? path, request.Method, path, status, status < 400), CancellationToken.None);
        }
        catch (Exception e) when (e is RedisException or TimeoutException or ObjectDisposedException)
        {
            logger.LogWarning(e, "Étape de parcours non enregistrée.");
        }
    }

    private static async Task<Dictionary<string, string>> ReadFieldsAsync(HttpRequest request)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.ContentLength is not > 0 and <= MaxBody || request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return fields;
        }

        request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (GherkinPhrases.BodyFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        fields[property.Name] = property.Value.ValueKind switch
                        {
                            JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            _ => property.Value.GetRawText(),
                        };
                    }
                    else if (property.NameEquals("lines") && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        fields["lines"] = property.Value.GetArrayLength().ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Corps illisible : la phrase sera écrite sans détails, le service répondra 400.
        }
        finally
        {
            request.Body.Position = 0;
        }

        return fields;
    }
}

public static class JourneySetup
{
    internal static IServiceCollection AddSouqlyJourneys(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JourneyOptions>(configuration.GetSection(JourneyOptions.Section));
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis")))
        {
            services.AddSingleton<IJourneyStore, InMemoryJourneyStore>();
        }
        else
        {
            services.AddSingleton<IJourneyStore, RedisJourneyStore>();   // le multiplexeur Redis est enregistré par AddSouqlyAudit
        }

        return services;
    }

    /// <summary>GET /api/v1/admin/journeys : les parcours réels, en Gherkin, du plus récent au plus ancien (administrateur).</summary>
    internal static IEndpointRouteBuilder MapJourneyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/journeys", ReadAsync)
            .WithTags("Administration")
            .WithSummary("Parcours réels des utilisateurs écrits en Gherkin, avec OK/KO par action")
            .RequireAuthorization(Policies.Admin);
        return app;
    }

    private static async Task<Ok<JourneyPage>> ReadAsync(string? user, bool? failedOnly, int? page, int? size,
        IJourneyStore store, IOptions<JourneyOptions> options, CancellationToken ct)
    {
        var steps = await store.ReadAllAsync(ct);
        var scenarios = Group(steps, options.Value.SessionGap)
            .Where(s => string.IsNullOrWhiteSpace(user)
                        || s.UserId.Equals(user, StringComparison.OrdinalIgnoreCase)
                        || (s.Username?.Contains(user, StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(s => failedOnly != true || !s.Ok)
            .ToList();
        var take = Math.Clamp(size ?? 10, 1, 50);
        var number = Math.Max(0, page ?? 0);
        return TypedResults.Ok(new JourneyPage(
            scenarios.Skip(number * take).Take(take).ToList(),
            number,
            take,
            scenarios.Count,
            (int)Math.Ceiling(scenarios.Count / (double)take),
            steps.Count(s => s.Ok),
            steps.Count(s => !s.Ok)));
    }

    /// <summary>Regroupe les étapes (plus récentes d'abord) en scénarios par utilisateur, coupés par une pause trop longue.</summary>
    public static IEnumerable<JourneyScenario> Group(IReadOnlyList<JourneyStep> newestFirst, TimeSpan gap)
    {
        var scenarios = new List<List<JourneyStep>>();
        foreach (var userSteps in newestFirst.OrderBy(s => s.At).GroupBy(s => s.UserId))
        {
            List<JourneyStep>? current = null;
            foreach (var step in userSteps)
            {
                if (current is null || step.At - current[^1].At > gap)
                {
                    current = [];
                    scenarios.Add(current);
                }

                current.Add(step);
            }
        }

        return scenarios
            .Select(ToScenario)
            .OrderByDescending(s => s.EndedAt);
    }

    private static JourneyScenario ToScenario(List<JourneyStep> steps)
    {
        var first = steps[0];
        var lines = new List<GherkinLine> { new("Étant donné que", $"{first.Actor} est connecté", null, null, first.At) };
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            lines.Add(new GherkinLine(i == 0 ? "Quand" : "Et", $"{step.Actor} {step.Text}", null, null, step.At));
            lines.Add(new GherkinLine("Alors", GherkinPhrases.Outcome(step.Status), step.Ok, step.Status, step.At));
        }

        var failed = steps.Count(s => !s.Ok);
        return new JourneyScenario(first.UserId, first.Username, first.Actor, first.At, steps[^1].At, failed == 0,
            steps.Count - failed, failed, lines);
    }
}

public sealed record JourneyPage(
    IReadOnlyList<JourneyScenario> Content,
    int Page,
    int Size,
    long TotalElements,
    int TotalPages,
    int PassedSteps,
    int FailedSteps);
