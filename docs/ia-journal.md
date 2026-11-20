# Journal d'usage de l'IA

Une ligne par usage significatif. L'IA est autorisée (séance 8) : chaque ligne fusionnée reste sous notre responsabilité et doit pouvoir être expliquée en démo.

| Date | Membre | Outil | Tâche | Prompt (résumé) | Ce qui a été gardé / corrigé | Vérification | PR |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 19/11 | Badre | Claude | Cahier des charges .NET | Adapter le dossier Souqly à .NET 10 + EF Core, table d'équivalence Java → .NET | Structure gardée ; ajout des sections organisation, OWASP, Git, démo après relecture contre l'énoncé | Relu section par section contre le PDF de l'énoncé | — |
| 20/11 | Badre | Claude | Socle du dépôt (Sprint 0) | Monorepo .NET : `Directory.Build.props`, versions centralisées, CI, ADR-000 à 002 | Versions NuGet vérifiées une par une sur nuget.org (paquets .NET 11 écartés au profit de 10.0.x) ; FluentAssertions remplacé par Shouldly (licence) | `dotnet build` : 0 avertissement, 0 erreur | — |
| 20/11 | Badre | Claude | Service Catalog (US-01, 02, 03) | Clean Architecture, EF Core + Npgsql, FluentValidation, ProblemDetails, tests | Code relu ; règle « 404 au lieu de 403 » pour un produit d'un autre vendeur conservée ; tri refusé s'il n'est pas dans la liste blanche | 49 tests verts dont 16 tests API sur PostgreSQL 16 réel | — |
| 20/11 | Badre | Claude | Lancement par F5 (Aspire) | Démarrer toute la plateforme depuis Visual Studio | AppHost Aspire 13.6.5 + OpenTelemetry ; version 17 écartée (cible .NET 11) | Lancement complet vérifié : 4 ressources « Running », traces passerelle → service → base | — |
