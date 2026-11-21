using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Souqly.Chassis;
using Souqly.Chassis.Paging;
using StackExchange.Redis;

namespace Souqly.ApiGateway;

/// <summary>
/// Une action sensible (US-18) : qui (identifiant Keycloak et nom de connexion, jamais l'e-mail ni le jeton),
/// quoi (verbe, chemin sans la chaîne de requête, code de réponse) et quand. L'identifiant de trace relie l'entrée
/// à la trace complète dans Grafana (US-19).
/// </summary>
public sealed record AuditEntry(
    DateTimeOffset At,
    string UserId,
    string? Username,
    IReadOnlyList<string> Roles,
    string Method,
    string Path,
    int Status,
    long DurationMs,
    string? TraceId);

public sealed class AuditOptions
{
    public const string Section = "Audit";

    /// <summary>Nombre d'entrées conservées (les plus anciennes sont supprimées).</summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>Préfixes jamais audités : opérations courantes et non sensibles (panier, notifications lues).</summary>
    public string[] ExcludedPrefixes { get; set; } = ["/api/v1/carts", "/api/v1/notifications"];
}

/// <summary>Stockage du journal d'audit, du plus récent au plus ancien.</summary>
public interface IAuditStore
{
    Task AppendAsync(AuditEntry entry, CancellationToken ct);

    Task<(IReadOnlyList<AuditEntry> Items, long Total)> ReadAsync(AuditQuery query, int skip, int take, CancellationToken ct);
}

/// <summary>Filtres de GET /api/v1/admin/audit.</summary>
public sealed record AuditQuery(string? User, string? Method, string? Path, int? Page, int? Size)
{
    public bool Matches(AuditEntry e) =>
        (string.IsNullOrWhiteSpace(User)
         || e.UserId.Equals(User, StringComparison.OrdinalIgnoreCase)
         || (e.Username?.Contains(User, StringComparison.OrdinalIgnoreCase) ?? false))
        && (string.IsNullOrWhiteSpace(Method) || e.Method.Equals(Method, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(Path) || e.Path.Contains(Path, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Journal dans une liste Redis plafonnée (LPUSH + LTRIM) : partagé par toutes les instances de la passerelle.</summary>
internal sealed class RedisAuditStore(IConnectionMultiplexer redis, Microsoft.Extensions.Options.IOptions<AuditOptions> options) : IAuditStore
{
    private const string Key = "souqly:audit";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        await db.ListLeftPushAsync(Key, JsonSerializer.Serialize(entry, Json));
        await db.ListTrimAsync(Key, 0, options.Value.Capacity - 1);
    }

    public async Task<(IReadOnlyList<AuditEntry> Items, long Total)> ReadAsync(AuditQuery query, int skip, int take, CancellationToken ct)
    {
        var values = await redis.GetDatabase().ListRangeAsync(Key, 0, -1);
        var entries = values
            .Select(v => JsonSerializer.Deserialize<AuditEntry>(v.ToString(), Json))
            .OfType<AuditEntry>()
            .Where(query.Matches)
            .ToList();
        return (entries.Skip(skip).Take(take).ToList(), entries.Count);
    }
}

/// <summary>Journal en mémoire (une seule instance, développement et tests).</summary>
internal sealed class InMemoryAuditStore(Microsoft.Extensions.Options.IOptions<AuditOptions> options) : IAuditStore
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > options.Value.Capacity && _entries.TryDequeue(out _))
        {
        }

        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<AuditEntry> Items, long Total)> ReadAsync(AuditQuery query, int skip, int take, CancellationToken ct)
    {
        var entries = _entries.Reverse().Where(query.Matches).ToList();
        return Task.FromResult<(IReadOnlyList<AuditEntry>, long)>((entries.Skip(skip).Take(take).ToList(), entries.Count));
    }
}

/// <summary>
/// Enregistre chaque requête d'écriture authentifiée qui traverse la passerelle (y compris les refus 403),
/// dans le journal consultable par l'administrateur et dans les logs (Loki, catégorie Souqly.Audit).
/// Un échec d'écriture du journal n'interrompt jamais la requête de l'utilisateur.
/// </summary>
internal sealed class AuditMiddleware(RequestDelegate next, IAuditStore store, Microsoft.Extensions.Options.IOptions<AuditOptions> options,
    TimeProvider clock, ILoggerFactory loggers)
{
    private readonly ILogger _audit = loggers.CreateLogger("Souqly.Audit");

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsAudited(context))
        {
            await next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            await RecordAsync(context, Stopwatch.GetElapsedTime(started));
        }
    }

    private bool IsAudited(HttpContext context)
    {
        var request = context.Request;
        return context.User.Identity?.IsAuthenticated == true
               && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method)
               && request.Path.StartsWithSegments("/api")
               && !options.Value.ExcludedPrefixes.Any(p => request.Path.StartsWithSegments(p));
    }

    private async Task RecordAsync(HttpContext context, TimeSpan elapsed)
    {
        var user = context.User;
        var entry = new AuditEntry(
            clock.GetUtcNow(),
            user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "inconnu",
            user.FindFirstValue("preferred_username"),
            user.FindAll(ClaimTypes.Role).Select(c => c.Value).Where(r => r is "CUSTOMER" or "VENDOR" or "ADMIN").Distinct().Order().ToList(),
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Response.StatusCode,
            (long)elapsed.TotalMilliseconds,
            Activity.Current?.TraceId.ToString());

        _audit.LogInformation("Audit {Method} {Path} par {UserId} ({Username}) : {Status} en {DurationMs} ms",
            entry.Method, entry.Path, entry.UserId, entry.Username, entry.Status, entry.DurationMs);
        try
        {
            await store.AppendAsync(entry, CancellationToken.None);
        }
        catch (Exception e) when (e is RedisException or TimeoutException or ObjectDisposedException)
        {
            _audit.LogWarning(e, "Entrée d'audit non enregistrée dans le journal.");
        }
    }
}

internal static class AuditSetup
{
    public static IServiceCollection AddSouqlyAudit(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.Section));
        services.AddSingleton(TimeProvider.System);
        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            services.AddSingleton<IAuditStore, InMemoryAuditStore>();
        }
        else
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var config = ConfigurationOptions.Parse(redis);
                config.AbortOnConnectFail = false;
                return ConnectionMultiplexer.Connect(config);
            });
            services.AddSingleton<IAuditStore, RedisAuditStore>();
        }

        return services;
    }

    /// <summary>US-18 : GET /api/v1/admin/audit, servi par la passerelle elle-même (administrateur uniquement).</summary>
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/audit", ReadAsync)
            .WithTags("Administration")
            .WithSummary("Journal d'audit des actions sensibles (qui, quoi, quand)")
            .RequireAuthorization(Policies.Admin);
        return app;
    }

    private static async Task<Ok<PageResponse<AuditEntry>>> ReadAsync([AsParameters] AuditQuery query, IAuditStore store, CancellationToken ct)
    {
        var page = PageRequest.Of(query.Page, query.Size);
        var (items, total) = await store.ReadAsync(query, page.Skip, page.Size, ct);
        return TypedResults.Ok(PageResponse<AuditEntry>.From(items, page, total));
    }
}
