# Contribuer à Souqly

## Flux Git (GitHub Flow)

- `main` est toujours déployable ; push direct interdit, CI verte et une approbation obligatoires.
- Branches courtes (< 3 jours) : `feat/US-06-place-order`, `fix/inventory-expiry`, `chore/ci-cache`.
- Commits en [Conventional Commits](https://www.conventionalcommits.org/fr/) avec le service en portée :
  `feat(catalog): paginate products by category`, `fix(inventory): release stock on expiry`.
- PR < 400 lignes, une seule idée, modèle rempli, `Closes #<issue>` ; fusion en *squash merge*.

## Conventions de code

- Chaque service suit la même découpe : `Domain` (aucune dépendance), `Application` (cas d'usage, ports), `Infrastructure` (EF Core, HTTP, Kafka), `Api` (endpoints, validation, composition).
- Les règles métier vivent dans le domaine, jamais dans les endpoints.
- Montants en `decimal`, stockés en `NUMERIC(12,2)` ; jamais `double` pour de l'argent.
- Le client n'envoie jamais un prix : le serveur le lit dans le catalogue.
- Erreurs au format `application/problem+json` (RFC 9457).
- Formatage : `dotnet format` ; les avertissements font échouer le build.

## Lancer les tests

```bash
dotnet test Souqly.slnx                                   # tout
dotnet test tests/Catalog.UnitTests                       # un projet
dotnet test --filter "Category!=Integration"              # sans base de données
```

## Ajouter une migration EF Core

```bash
dotnet tool restore
dotnet ef migrations add <Nom> \
  --project src/Services/Catalog/Souqly.Catalog.Infrastructure \
  --startup-project src/Services/Catalog/Souqly.Catalog.Api \
  --output-dir Persistence/Migrations
```

Ne jamais modifier une migration déjà fusionnée : en ajouter une nouvelle.

## Ajouter un service

1. Copier la structure d'un service existant (`Catalog`).
2. Ajouter les projets à `Souqly.slnx`, la base dans `infra/postgres/init.sh`, la route dans la passerelle.
3. Écrire le contrat OpenAPI dans `contracts/openapi/` et le faire relire avant le code.

## Usage de l'IA

Autorisé, à condition de comprendre et tester ce qui est fusionné. Toute utilisation significative est consignée dans `docs/ia-journal.md`.
