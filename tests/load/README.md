# Tests de charge (k6)

Le script [`souqly.js`](souqly.js) joue deux types d'utilisateurs en même temps :

- **navigation** (visiteur) : liste du catalogue, recherche (« tapi berbre », « ARGANE »…), fiche produit ;
- **commande** (cliente `sara`) : `POST /api/v1/orders` avec une clé d'idempotence unique.

Les objectifs de service (SLO, [arc42 §10](../../docs/architecture/arc42.md#10-exigences-de-qualité)) sont écrits comme **seuils** : si l'un n'est pas tenu, k6 affiche `KO` et se termine avec le code 99.

| Seuil | Objectif |
| --- | --- |
| `http_req_duration{endpoint:catalogue}` et `{endpoint:fiche}` | p95 < 300 ms |
| `http_req_duration{endpoint:recherche}` | p95 < 500 ms |
| `http_req_duration{endpoint:commande}` | p95 < 800 ms (réponse 202 ; la saga continue ensuite) |
| `http_req_failed` | < 1 % (un 429 ou un 5xx compte comme un échec) |
| `checks` | > 99 % |

## Profils

| `PROFILE` | Trafic | Durée | Usage |
| --- | --- | --- | --- |
| `fumee` (défaut) | 1 visiteur + 1 client | 30 s | Vérifier que tout répond, avant une démo ou après un déploiement |
| `charge` | jusqu'à 50 parcours/s (≈ 150 requêtes/s) + 2 commandes/s | 4 min 30 | Trafic nominal |
| `pic` | 50 → 200 parcours/s en 20 s + 5 commandes/s | 3 min 20 | Vente flash : le HPA doit ajouter des pods |

Les profils `charge` et `pic` utilisent un **débit d'arrivée constant** : si la plateforme ralentit, k6 n'envoie pas moins de requêtes (un nombre fixe d'utilisateurs masquerait la lenteur).

## Lancer

### 1. Relever la limite de débit de la passerelle

La passerelle limite chaque adresse IP à 20 requêtes/s. Tous les utilisateurs virtuels de k6 partagent la même IP : sans réglage, `charge` et `pic` recevraient des 429. Le profil `fumee` reste sous la limite.

| Environnement | Réglage |
| --- | --- |
| Aspire (F5) | Dans Visual Studio, propriétés de **Souqly.AppHost** › Déboguer › Arguments : `--RateLimiting:PermitsPerSecond=2000` ; ou `dotnet run --project src/Aspire/Souqly.AppHost -- --RateLimiting:PermitsPerSecond=2000` |
| Docker Compose | Ajouter `RateLimiting__PermitsPerSecond: "2000"` et `RateLimiting__Burst: "2000"` au service `gateway` |
| Kubernetes (Helm) | `--set gateway.rateLimit.permitsPerSecond=2000` (uniquement sur l'environnement de test de charge) |

### 2. Exécuter

Avec k6 installé (`winget install k6` sous Windows) :

```bash
k6 run tests/load/souqly.js                                     # fumée
k6 run -e PROFILE=charge tests/load/souqly.js
k6 run -e PROFILE=pic -e BASE_URL=https://api.souqly.ma -e KEYCLOAK_URL=https://auth.souqly.ma tests/load/souqly.js
```

Sans installation, avec Docker Desktop (Windows ou macOS) :

```bash
docker run --rm -v "$PWD/tests/load:/scripts" -e BASE_URL=http://host.docker.internal:8080 \
  -e KEYCLOAK_URL=http://host.docker.internal:8180 grafana/k6 run /scripts/souqly.js
```

Variables : `BASE_URL` (passerelle, défaut `http://localhost:8080`), `KEYCLOAK_URL` (défaut `http://localhost:8180`), `SOUQLY_USER` / `SOUQLY_PASSWORD` (défaut `sara`), `SUMMARY_FILE` (résumé JSON, défaut `k6-<profil>-resume.json`).

## Lire le résultat

Exemple de format (valeurs fictives) :

```
Souqly · profil « charge » · http://localhost:8080

  OK  http_req_duration{endpoint:catalogue}  p(95)<300  (p95 = 41 ms)
  OK  http_req_duration{endpoint:recherche}  p(95)<500  (p95 = 63 ms)
  KO  http_req_duration{endpoint:commande}   p(95)<800  (p95 = 912 ms)
  ...
```

Pendant le test, ouvrir Grafana (**Souqly — Vue d'ensemble**) : latence p95 par service, commandes par minute et durée de la saga. Une ligne `KO` se diagnostique avec le [runbook](../../docs/operations/runbook.md).

Le résumé complet (toutes les métriques) est écrit dans `k6-<profil>-resume.json`, à joindre au compte rendu.

## Points d'attention

- Les commandes réservent vraiment du stock : lancer `charge` ou `pic` sur une base de démonstration, jamais en production.
- Le script choisit des produits à moins de 2 000 MAD, pour que la saga ne soit pas refusée par le paiement simulé (plafond de 10 000 MAD).
- La durée de la saga complète (objectif p95 < 5 s) se lit dans Grafana (`souqly.order.saga.duration`), pas dans k6 : la réponse HTTP arrive dès l'acceptation de la commande (202).
