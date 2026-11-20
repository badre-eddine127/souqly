# ADR-014 : Observabilité avec OpenTelemetry et Grafana (image otel-lgtm)

- **Statut** : accepté
- **Date** : 2026-11-20
- **Preuves** : `OrderMetricsTests`, `US19_une_seule_trace_…`, `Payment_arrete_…_au_redemarrage_sans_perte` ; requêtes PromQL validées

## Contexte

US-19 (une commande suivie dans une seule trace) et US-20 (alerte si plus de 2 % des commandes échouent) demandent des traces, des métriques et des alertes **versionnées**. Le tableau de bord Aspire montre les traces en direct, mais ne garde pas d'historique, n'a ni tableaux de bord personnalisés ni alertes.

## Options étudiées

1. **Prometheus + Grafana + Tempo + Loki séparés** : proche de la production, mais quatre conteneurs et leur configuration à maintenir pour un environnement de développement.
2. **Grafana Cloud** : rien à héberger, mais compte externe et données envoyées hors du poste (registre 09-08).
3. **`grafana/otel-lgtm`** : les mêmes briques (collecteur OpenTelemetry, Prometheus, Tempo, Loki, Grafana) dans une image, recevant directement l'OTLP des services.

## Décision

Option 3 en développement (Aspire et Compose, profil `observability`) ; en Kubernetes, les mêmes tableaux de bord et alertes iront dans la pile Grafana du cluster.

- Les services exportent vers **deux destinations** : le tableau de bord Aspire (`OTEL_EXPORTER_OTLP_ENDPOINT`) et Grafana (`Telemetry:GrafanaEndpoint`).
- **Métriques métier** (`System.Diagnostics.Metrics`) : `souqly_orders_placed_total`, `souqly_orders_completed_total{outcome,reason}`, `souqly_orders_failed_total`, `souqly_order_saga_duration_seconds`, `souqly_messaging_processed_total`, `souqly_messaging_dead_letters_total`.
- **Tableau de bord** `infra/grafana/dashboards/souqly-overview.json` et **alertes** `infra/grafana/provisioning/alerting/` chargés au démarrage ; modification uniquement par Git (`allowUiUpdates: false`).
- **Définition d'un échec (US-20)** : 5xx sur `POST /orders` ou message de saga en DLQ, rapporté aux commandes tentées. Une rupture de stock ou un paiement refusé est un refus métier, pas un échec technique : ils n'alertent pas.
- Chaque alerte renvoie au **runbook** (`docs/operations/runbook.md`).

## Conséquences

- (+) Mêmes définitions (tableaux de bord, alertes) en développement et plus tard en cluster : ce qui est démontré est ce qui est déployé.
- (+) Corrélation trace ↔ logs ↔ métriques dans Grafana (Tempo, Loki, Prometheus préconfigurés).
- (−) Image de 1 à 1,5 Go de mémoire en développement.
- (−) Pas de notification sortante configurée (e-mail, Slack) : les alertes s'affichent dans Grafana. Point de contact à ajouter pour la démo.
