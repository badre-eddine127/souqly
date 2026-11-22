# Déployer Souqly sur Kubernetes (Helm)

Le chart [`souqly`](souqly) installe les **9 services** de Souqly : passerelle, site web, Catalog, Inventory, Cart, Ordering, Payment, Notification et Search. Choix et compromis : [ADR-016](../../docs/adr/ADR-016-deploiement-kubernetes-helm.md).

PostgreSQL, Redis, Kafka et Keycloak **ne sont pas dans ce chart**. En production, ce sont des services gérés (base managée, Kafka managé…) ou leurs opérateurs (CloudNativePG, Strimzi, opérateur Keycloak). Le chart reçoit seulement leurs adresses.

## Ce que crée le chart

| Objet | Pour quoi | Services |
| --- | --- | --- |
| `Deployment` + `Service` | Un par microservice, port 80 → 8080 | tous |
| `HorizontalPodAutoscaler` | Ajoute des pods au-delà de 70 % de CPU, en retire lentement (5 min) | gateway (2→6), catalog (2→8), search (2→6), cart (2→6), ordering (2→6) |
| `PodDisruptionBudget` | Garde au moins 1 pod pendant la maintenance d'un nœud | services à 2 pods ou plus |
| `NetworkPolicy` | Ingress → web et gateway ; web → gateway ; gateway → services ; ordering et search → catalog | tous |
| `Ingress` | `souqly.ma` → site, `api.souqly.ma` → passerelle, TLS par cert-manager | web, gateway |
| `Secret` (option) | Mots de passe des bases et secrets Keycloak | `secrets.create=true` en démo seulement |
| Pod de test | `helm test` : la passerelle est prête et sert le catalogue | — |

Chaque pod : utilisateur non root, système de fichiers en lecture seule (sauf `/tmp`), aucune capacité Linux, sondes `/health/live` (démarrage, vie) et `/health/ready` (prêt), requêtes et limites CPU/mémoire.

## Prérequis

- Kubernetes 1.27 ou plus, `kubectl` et Helm 3.
- Un contrôleur d'ingress NGINX et, en production, cert-manager.
- `metrics-server` (sans lui, le HPA ne voit pas le CPU et ne fait rien).
- Les images `ghcr.io/badre-eddine127/souqly-<service>:<version>`, publiées par le workflow `release` à chaque étiquette `vX.Y.Z`.
- Les bases `catalog_db`, `inventory_db`, `ordering_db`, `payment_db`, `notification_db`, `search_db` (script : `infra/postgres/init.sh`) avec l'extension `pg_trgm` pour `search_db`.
- Le realm `souqly` importé dans Keycloak (`infra/keycloak/realm-souqly.json`), avec l'adresse publique du site dans les *redirect URIs* du client `souqly-web`, et le client `souqly-dev` retiré.

## Installer en production

```bash
# 1. Secrets (une fois), hors du chart et hors de Git
kubectl create namespace souqly
kubectl -n souqly create secret generic souqly-secrets \
  --from-literal=CATALOG_DB_PASSWORD=... --from-literal=INVENTORY_DB_PASSWORD=... \
  --from-literal=ORDERING_DB_PASSWORD=... --from-literal=PAYMENT_DB_PASSWORD=... \
  --from-literal=NOTIFICATION_DB_PASSWORD=... --from-literal=SEARCH_DB_PASSWORD=... \
  --from-literal=ORDERING_CLIENT_SECRET=... --from-literal=CATALOG_CLIENT_SECRET=...

# 2. Installation ou mise à jour (même commande)
helm upgrade --install souqly oci://ghcr.io/badre-eddine127/charts/souqly --version 1.0.0 -n souqly \
  --set postgres.host=pg.exemple.internal --set kafka.bootstrapServers=kafka.exemple.internal:9092 \
  --set redis.address=redis.exemple.internal:6379 --set keycloak.authority=https://auth.souqly.ma/realms/souqly \
  --wait --timeout 10m

# 3. Vérifier
kubectl -n souqly get pods,hpa
helm test souqly -n souqly
```

Une mise à jour remplace les pods un par un (`maxUnavailable: 0`) : le site reste en ligne. Retour arrière : `helm rollback souqly <révision> -n souqly`.

## Démonstration sur un cluster local

[`values-demo.yaml`](souqly/values-demo.yaml) : 1 pod par service au départ, HPA de 1 à 4 pods, pas de TLS, adresses `souqly.localhost`, Secret créé par le chart avec des mots de passe factices. Il suppose PostgreSQL, Redis, Kafka et Keycloak dans l'espace de noms `data` (noms de service dans le fichier).

```bash
helm upgrade --install souqly deploy/helm/souqly -n souqly --create-namespace -f deploy/helm/souqly/values-demo.yaml
```

### Voir le HPA réagir à un pic

```bash
kubectl -n souqly get hpa -w                         # terminal 1
k6 run -e PROFILE=pic -e BASE_URL=http://api.souqly.localhost tests/load/souqly.js   # terminal 2
```

Pour ce test, relever la limite de débit (toutes les requêtes de k6 viennent de la même IP) : `--set services.gateway.rateLimit.permitsPerSecond=2000 --set services.gateway.rateLimit.burst=2000`.

## Réglages courants

| Valeur | Défaut | Rôle |
| --- | --- | --- |
| `image.tag` | `appVersion` du chart | Version des images |
| `defaults.replicas` | `2` | Pods par service sans HPA |
| `defaults.autoscaling.targetCPUUtilizationPercentage` | `70` | Cible CPU du HPA |
| `services.<nom>.autoscaling` | voir `values.yaml` | `enabled`, `minReplicas`, `maxReplicas` par service |
| `services.<nom>.resources` | 100m / 160Mi demandés, 1 CPU / 384Mi max | Ressources par service |
| `services.gateway.rateLimit.permitsPerSecond` | `20` | Requêtes par seconde et par utilisateur ou IP |
| `telemetry.otlpEndpoint` | vide | Collecteur OpenTelemetry (Grafana) |
| `networkPolicy.enabled` | `true` | Isolement réseau |
| `secrets.create` | `false` | Créer le Secret depuis les valeurs (démo) |

## Vérifier le chart sans cluster

```bash
helm lint deploy/helm/souqly --strict
helm template souqly deploy/helm/souqly | kubeconform -strict -summary -kubernetes-version 1.31.0
```

La CI le fait à chaque push (job `deploiement`), avec les valeurs par défaut et avec `values-demo.yaml`.
