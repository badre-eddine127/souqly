# Journal des modifications

Toutes les évolutions notables de Souqly sont consignées ici.
Format inspiré de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/) ; versions selon [SemVer](https://semver.org/lang/fr/).
Une version correspond à la fin d'un sprint.

## [1.0.0] — 2026-11-23 · Sprint 5 (mise en production)

### Ajouté
- Chart Helm `deploy/helm/souqly` : les 9 services sur Kubernetes, sondes de santé, limites de ressources, `HorizontalPodAutoscaler` (CPU 70 %) pour la passerelle, Catalog, Search, Cart et Ordering, `PodDisruptionBudget`, `NetworkPolicy` (seule la passerelle et le site sont joignables), secrets séparés des valeurs ([ADR-016](docs/adr/ADR-016-deploiement-kubernetes-helm.md)).
- Tests de charge k6 (`tests/load`) avec objectifs de service (SLO) écrits comme seuils : p95 du catalogue < 300 ms, p95 de la recherche < 500 ms, création de commande p95 < 800 ms, moins de 1 % d'erreurs ; scénarios fumée, charge et pic.
- Documentation d'architecture arc42 (`docs/architecture/arc42.md`).
- Ce journal des modifications.
- CI : validation du chart Helm (`helm lint` et `kubeconform`) et contrôle de syntaxe des scripts k6.

## [0.4.0] — 2026-11-23 · Sprint 4

### Ajouté
- US-04 : service **Search** (recherche tolérante aux fautes avec `pg_trgm`, filtres de prix et de catégorie, facettes), alimenté par les événements produit de Catalog ([ADR-015](docs/adr/ADR-015-recherche-postgresql-trigrammes.md)).
- US-12 : alerte de stock bas et de rupture pour le vendeur, seuil réglable par produit.
- US-13, US-14 : expédition par vendeur (Amana, CTM, Aramex, DHL) et suivi de livraison par le client.
- US-16 : demande de boutique, validation ou refus par l'administrateur, attribution du rôle VENDOR dans Keycloak.
- US-17 : tableau de bord vendeur (ventes du jour, commandes à préparer, ruptures).
- US-18 : journal d'audit des actions sensibles dans la passerelle.
- US-19, US-20 : observabilité Grafana (traces, métriques, logs), tableau de bord et alertes versionnés, runbook ([ADR-014](docs/adr/ADR-014-observabilite-grafana.md)).
- Site web ASP.NET Core en HTML, CSS et JavaScript : carrousel, catalogue, fiche produit en rotation 3D, connexion « lampe », inscription « lanterne », camionnette animée sur le suivi de commande.
- Page d'administration Gherkin : parcours réels des utilisateurs écrits en Gherkin (OK/KO) et rapport de recette.
- Critères d'acceptation exécutables (Reqnroll), un fichier `.feature` par story.

### Corrigé
- Attribution du rôle VENDOR sans le droit `view-realm` (503 lors de la validation d'une boutique).
- Statut produit `ACTIVE` au lieu de `PUBLISHED` : bouton de commande grisé et produits affichés « Archivé » à tort.
- Nom de la boutique affiché au lieu de l'identifiant du vendeur sur la page commande.
- Test du `Retry-After` de la passerelle rendu stable en CI.

## [0.3.0] — 2026-11-20 · Sprint 3

### Ajouté
- Saga de commande orchestrée par Ordering sur Kafka, avec outbox transactionnelle, inbox idempotente et file de messages en échec (DLQ) ([ADR-004](docs/adr/ADR-004-kafka-outbox-inbox.md), [ADR-005](docs/adr/ADR-005-saga-orchestree.md)).
- Services **Payment** (paiement simulé, refus au-delà de 10 000 MAD, remboursement) et **Notification**.
- Contrat AsyncAPI des événements.

### Corrigé
- Ordre des `using` dans les tests de saga (contrôle `dotnet format`).

## [0.2.0] — 2026-11-20 · Sprint 2

### Ajouté
- Identité Keycloak : realm `souqly`, rôles CUSTOMER, VENDOR et ADMIN, PKCE, comptes de service ; jetons JWT validés par chaque service ([ADR-003](docs/adr/ADR-003-identite-keycloak.md)).
- Service **Ordering** : commandes idempotentes, prix lus dans Catalog avec un appel résilient (Polly).

## [0.1.0] — 2026-11-20 · Sprints 0 et 1

### Ajouté
- Monorepo .NET 10, versions NuGet centralisées, avertissements traités comme des erreurs, CI GitHub Actions (format, build, tests, couverture, gitleaks).
- Services **Catalog**, **Inventory** (sans survente, [ADR-007](docs/adr/ADR-007-prevention-survente.md)) et **Cart** (Redis).
- Passerelle YARP (point d'entrée unique, limite de débit) et Docker Compose.
- Lancement de toute la plateforme par F5 dans Visual Studio avec Aspire ([ADR-012](docs/adr/ADR-012-aspire-dev-local.md)).

### Corrigé
- Rapport de couverture généré avec reportgenerator.

[1.0.0]: https://github.com/badre-eddine127/souqly/compare/d6b77f3...HEAD
[0.4.0]: https://github.com/badre-eddine127/souqly/compare/de05845...d6b77f3
[0.3.0]: https://github.com/badre-eddine127/souqly/compare/d5d7ae9...272a004
[0.2.0]: https://github.com/badre-eddine127/souqly/compare/6433cf9...d5d7ae9
[0.1.0]: https://github.com/badre-eddine127/souqly/commits/8f8cce1
