using System.Diagnostics.Metrics;
using Souqly.Ordering.Domain;

namespace Souqly.Ordering.Application;

/// <summary>
/// Métriques métier des commandes (exportées vers Grafana sous les noms Prometheus indiqués) :
/// - souqly_orders_placed_total : commandes acceptées (202) ;
/// - souqly_orders_completed_total{outcome, reason} : issues de la saga (confirmed, rejected, cancelled) ;
/// - souqly_orders_failed_total{reason} : échecs techniques (dépendance indisponible) — numérateur de l'alerte US-20 ;
/// - souqly_order_saga_duration_seconds : durée entre la commande et son issue (SLO : p95 &lt; 5 s).
/// </summary>
public static class OrderMetrics
{
    public const string MeterName = "Souqly.Ordering";

    private static readonly Meter Meter = new(MeterName, "1.0");

    private static readonly Counter<long> Placed = Meter.CreateCounter<long>(
        "souqly.orders.placed", "{order}", "Commandes acceptées (202)");

    private static readonly Counter<long> Completed = Meter.CreateCounter<long>(
        "souqly.orders.completed", "{order}", "Commandes arrivées à une issue de saga");

    private static readonly Counter<long> Failed = Meter.CreateCounter<long>(
        "souqly.orders.failed", "{order}", "Commandes refusées pour une raison technique (5xx)");

    private static readonly Histogram<double> SagaDuration = Meter.CreateHistogram<double>(
        "souqly.order.saga.duration", "s", "Durée entre la commande et son issue",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.25, 0.5, 1, 2, 5, 10, 30, 60, 300] });

    public static void RecordPlaced() => Placed.Add(1);

    public static void RecordFailed(string reason) => Failed.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public static void RecordOutcome(Order order, DateTimeOffset now)
    {
        var outcome = new KeyValuePair<string, object?>("outcome", order.Status.ToString().ToLowerInvariant());
        var reason = new KeyValuePair<string, object?>("reason", order.StatusReason ?? "none");
        Completed.Add(1, outcome, reason);
        SagaDuration.Record(Math.Max(0, (now - order.CreatedAt).TotalSeconds), outcome);
    }
}
