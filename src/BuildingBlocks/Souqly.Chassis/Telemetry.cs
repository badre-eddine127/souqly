using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Souqly.Chassis;

/// <summary>
/// Traces, métriques et logs OpenTelemetry (US-19 : une commande suivie à travers tous les services ; US-20 : alerte).
/// Deux destinations possibles, cumulables :
/// - OTEL_EXPORTER_OTLP_ENDPOINT : tableau de bord Aspire (fourni automatiquement par F5) ;
/// - Telemetry:GrafanaEndpoint : Grafana (image otel-lgtm : Prometheus, Tempo, Loki), où vivent les tableaux de bord
///   et les alertes versionnés dans infra/grafana.
/// Sans destination, rien n'est exporté (tests, exécution isolée).
/// </summary>
public static class Telemetry
{
    /// <summary>Compteurs métier de Souqly (Souqly.Ordering, Souqly.Messaging…).</summary>
    public const string MeterPrefix = "Souqly.*";

    private const string GrafanaExporter = "grafana";

    public static IHostApplicationBuilder AddSouqlyTelemetry(this IHostApplicationBuilder builder)
    {
        var aspire = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var grafana = builder.Configuration["Telemetry:GrafanaEndpoint"];
        var toAspire = !string.IsNullOrWhiteSpace(aspire);
        var toGrafana = Uri.TryCreate(grafana, UriKind.Absolute, out var grafanaUri);
        if (!toAspire && !toGrafana)
        {
            return builder;
        }

        void Grafana(OtlpExporterOptions o)
        {
            o.Endpoint = grafanaUri!;
            o.Protocol = OtlpExportProtocol.Grpc;
        }

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
            if (toAspire)
            {
                o.AddOtlpExporter();   // lit OTEL_EXPORTER_OTLP_* (adresse, protocole, clé fournis par Aspire)
            }

            if (toGrafana)
            {
                o.AddOtlpExporter(GrafanaExporter, Grafana);
            }
        });

        builder.Services.AddOpenTelemetry()
            .WithTracing(t =>
            {
                t.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation()
                    .AddSource("Npgsql")   // Npgsql émet ses propres traces SQL
                    .AddSource("Souqly.Messaging");   // envoi et traitement des messages Kafka
                if (toAspire)
                {
                    t.AddOtlpExporter();
                }

                if (toGrafana)
                {
                    t.AddOtlpExporter(GrafanaExporter, Grafana);
                }
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(MeterPrefix);
                if (toAspire)
                {
                    m.AddOtlpExporter();
                }

                if (toGrafana)
                {
                    m.AddOtlpExporter(GrafanaExporter, (exporter, reader) =>
                    {
                        Grafana(exporter);
                        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000;
                    });
                }
            });

        return builder;
    }
}
