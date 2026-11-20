# ADR-002 : Monorepo avec une solution .NET unique

- **Statut** : accepté
- **Date** : 2026-11-20

## Contexte

L'équipe a 5 sprints et un seul dépôt GitHub imposé. Les services partagent des conventions (structure, analyseurs, versions de paquets) mais doivent rester déployables indépendamment.

## Options étudiées

1. **Un dépôt par service** — indépendance totale ; mais duplication de la CI et des conventions, revues dispersées, versions de paquets qui divergent.
2. **Monorepo, une solution `Souqly.slnx`** — un seul endroit pour les conventions (`Directory.Build.props`), les versions NuGet (`Directory.Packages.props`), la CI et la documentation ; une image Docker par service.

## Décision

Option 2. Chaque service a ses propres projets `Domain`, `Application`, `Infrastructure`, `Api` et sa propre image. Le code partagé se limite à `Souqly.Chassis` (technique transverse, jamais de règle métier ni de DTO partagé, voir ADR-009). CODEOWNERS désigne le référent de chaque service.

## Conséquences

- (+) Un seul `dotnet build` / `dotnet test` vérifie toute la plateforme ; Dependabot monte les versions en une PR.
- (+) Refactorings transverses atomiques et revues centralisées.
- (−) La CI doit rester rapide : jobs par service filtrés par chemin quand la solution grossira.
- (−) Risque de couplage par le chassis : il reste minimal et versionné avec soin.
