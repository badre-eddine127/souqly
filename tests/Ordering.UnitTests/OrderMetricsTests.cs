using System.Diagnostics.Metrics;
using Souqly.Ordering.Application;
using Souqly.Ordering.Domain;

namespace Ordering.UnitTests;

/// <summary>Les compteurs lus par le tableau de bord et l'alerte US-20 (infra/grafana) sont bien émis.</summary>
public class OrderMetricsTests
{
    [Fact]
    public void Issue_et_duree_de_saga_et_echec_technique_sont_mesures()
    {
        var measures = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OrderMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Add(i.Name, v, tags));
        listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Add(i.Name, v, tags));
        listener.Start();

        var placedAt = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var order = Order.Place("sara", [new OrderLine(1, "vendor-1", "Tapis", 100m, 1)], placedAt);
        order.MarkStockReserved(placedAt);
        order.Reject("PAYMENT_DECLINED", placedAt);

        OrderMetrics.RecordPlaced();
        OrderMetrics.RecordOutcome(order, placedAt.AddSeconds(1.5));
        OrderMetrics.RecordFailed("catalog_unavailable");

        lock (measures)
        {
            measures.ShouldContain(m => m.Name == "souqly.orders.placed" && m.Value == 1);
            measures.ShouldContain(m => m.Name == "souqly.orders.completed"
                                         && (string?)m.Tags["outcome"] == "rejected" && (string?)m.Tags["reason"] == "PAYMENT_DECLINED");
            measures.ShouldContain(m => m.Name == "souqly.order.saga.duration" && m.Value == 1.5);
            measures.ShouldContain(m => m.Name == "souqly.orders.failed" && (string?)m.Tags["reason"] == "catalog_unavailable");
        }

        void Add(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            lock (measures)
            {
                measures.Add((name, value, copy));
            }
        }
    }
}
