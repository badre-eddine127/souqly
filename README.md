# Souqly — marketplace marocaine en microservices (.NET 10)

Souqly permet à des artisans et petites entreprises marocaines de vendre en ligne : le client cherche, met au panier, commande, paie et est notifié ; le vendeur gère produits et stock. Projet de synthèse du module **SUP351 — Développement avancé des applications** (SupMTI Oujda, 2026-2027).

> Le projet est réalisé en **.NET 10 (ASP.NET Core + Entity Framework Core)** au lieu de Java/Spring Boot, avec l'accord de l'enseignant (voir [ADR-000](docs/adr/ADR-000-plateforme-dotnet.md)).

## Architecture

```mermaid
flowchart TB
    client[Client web / collection Bruno] --> gw[ApiGateway · YARP]
    kc[Keycloak 26] -. JWKS .-> gw
    gw --> catalog[Catalog · PostgreSQL]
    gw --> inventory[Inventory · PostgreSQL]
    gw --> cart[Cart · Redis]
    gw --> ordering[Ordering · saga]
    catalog & inventory & ordering --- kafka[(Kafka KRaft)]
    kafka --- payment[Payment] & notification[Notification] & shipping[Shipping]
```

| Service | Rôle | Stockage | État |
| --- | --- | --- | --- |
| `Catalog` | Produits, catégories, prix | PostgreSQL (`catalog_db`) | Sprint 1 |
| `Inventory` | Stock, réservations sans survente | PostgreSQL (`inventory_db`) | Sprint 1 |
| `Cart` | Panier par client (30 jours) | Redis | Sprint 1 |
| `ApiGateway` | Point d'entrée unique, routage, JWT, limite de débit | — | Sprints 1-2 |
| `Souqly.AppHost` | Lance tout l'environnement en local (Aspire, F5) | — | Sprint 1 |
| `Ordering` | Commandes idempotentes, prix lus dans Catalog (résilient) | PostgreSQL (`ordering_db`) | Sprint 2 |
| Keycloak | Connexion OIDC, rôles, comptes de service ([ADR-003](docs/adr/ADR-003-identite-keycloak.md)) | — | Sprint 2 |
| `Payment` | Paiement simulé (plafond 10 000 MAD), remboursement | PostgreSQL (`payment_db`) | Sprint 3 |
| `Notification` | Messages au client et aux vendeurs | PostgreSQL (`notification_db`) | Sprint 3 |
| Kafka (KRaft) | Saga de commande : outbox, inbox, DLQ ([ADR-004](docs/adr/ADR-004-kafka-outbox-inbox.md), [ADR-005](docs/adr/ADR-005-saga-orchestree.md)) | — | Sprint 3 |
| `Shipping`, `Search` | Niveau Pro | PostgreSQL, OpenSearch | Sprint 4 |

## Prérequis

- [.NET SDK 10](https://dotnet.microsoft.com/download) (version figée dans `global.json`)
- Docker Desktop (Compose v2)
- 16 Go de RAM recommandés

## Démarrer depuis Visual Studio (F5)

1. Lancer **Docker Desktop**.
2. Ouvrir `Souqly.slnx` dans Visual Studio 2026 (ou 2022 17.14+).
3. Vérifier que **Souqly.AppHost** est le projet de démarrage (clic droit › *Définir comme projet de démarrage*).
4. Appuyer sur **F5**.

Aspire démarre PostgreSQL 16, Redis 7, Keycloak 26 et Kafka en conteneurs, crée les bases, importe le realm `souqly`, lance les six services puis la passerelle quand ils sont sains, et ouvre le **tableau de bord** (état, logs, traces). Le lien « Documentation de l'API (Scalar) » de la ligne *gateway* ouvre `http://localhost:8080/scalar`. Les points d'arrêt fonctionnent dans tous les services ([ADR-012](docs/adr/ADR-012-aspire-dev-local.md)).

En ligne de commande : `dotnet run --project src/Aspire/Souqly.AppHost`.

## Démarrer avec Docker Compose (démo, CI)

```bash
cp infra/compose/.env.example infra/compose/.env
docker compose -f infra/compose/compose.yaml up -d --build --wait
open http://localhost:8080/scalar     # documentation de l'API via la passerelle
```

## Suivre une commande à travers Kafka

1. Passer une commande (Scalar › ordering › `POST /api/v1/orders`) : réponse **202**, statut `PENDING`.
2. Relire la commande quelques instants plus tard : `CONFIRMED` (ou `REJECTED` avec la raison).
3. **Kafka UI** (lien `kafka-ui` dans le tableau de bord Aspire, ou http://localhost:8085) : topics `souqly.*.v1`, un message par étape, clé = numéro de commande.
4. **Traces** (onglet Traces d'Aspire) : une seule trace va de la requête HTTP jusqu'à la confirmation, à travers les quatre services.
5. Pour voir la compensation : commander pour plus de 10 000 MAD → paiement refusé, stock rendu ; ou annuler une commande confirmée → remise en stock et remboursement.

Contrat des événements : [`contracts/asyncapi/souqly.yaml`](contracts/asyncapi/souqly.yaml).

## Comptes de démonstration

Realm Keycloak `souqly` (console : http://localhost:8180, utilisateur `admin`). Mot de passe des comptes : `Souqly-Dev-2026!` (développement uniquement).

| Compte | Rôles | Pour tester |
| --- | --- | --- |
| `sara` | CUSTOMER | panier, commandes |
| `fatima`, `karim` | VENDOR (+ CUSTOMER) | produits, stock, commandes reçues |
| `admin-souqly` | ADMIN (+ CUSTOMER) | administration |

Dans Scalar, **Authorize › oauth2 › Authorize** ouvre la page de connexion Keycloak (PKCE). En ligne de commande :

```bash
TOKEN=$(curl -s -X POST http://localhost:8180/realms/souqly/protocol/openid-connect/token \
  -d grant_type=password -d client_id=souqly-dev -d username=sara -d 'password=Souqly-Dev-2026!' | jq -r .access_token)
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/v1/orders
```

## Développer

```bash
dotnet build Souqly.slnx            # compile toute la solution (avertissements = erreurs)
dotnet test Souqly.slnx             # tests unitaires, API et intégration
dotnet format Souqly.slnx --verify-no-changes
```

Les tests d'intégration utilisent PostgreSQL via Testcontainers (Docker requis). Sans Docker, définir `SOUQLY_TEST_POSTGRES` avec une chaîne de connexion vers un PostgreSQL local.

## Structure

```
src/BuildingBlocks/Souqly.Chassis   # technique transverse : erreurs, pagination, santé
src/Services/<Service>/             # Domain · Application · Infrastructure · Api
tests/<Service>.*Tests              # unitaires, API, intégration, architecture
contracts/openapi · asyncapi        # contrats écrits avant le code
infra/compose · keycloak · postgres # environnement local
docs/adr · architecture · operations · security · compte-rendu
```

## Documentation

- [Décisions d'architecture (ADR)](docs/adr/)
- [Modèle de menaces (STRIDE)](docs/security/threat-model.md) et [registre des traitements (loi 09-08)](docs/security/registre-traitements.md)
- [Compte rendu d'avancement (PDF)](docs/compte-rendu/)
- [Contribuer](CONTRIBUTING.md)

## Usage de l'IA

L'IA (Claude) a été utilisée pour rédiger le cahier des charges .NET et générer une première version du code et des tests ; chaque usage est consigné dans [docs/ia-journal.md](docs/ia-journal.md), et tout le code fusionné a été relu, compilé et testé.
