# ADR-012 : Aspire pour lancer tout l'environnement depuis Visual Studio

- **Statut** : accepté
- **Date** : 2026-11-20

## Contexte

Développer Souqly demande de lancer PostgreSQL, Redis, trois services et la passerelle, dans le bon ordre et avec les bonnes chaînes de connexion. Avec Docker Compose seul, chaque modification de code impose de reconstruire une image, et le débogueur de Visual Studio ne s'attache pas aux services. L'équipe veut appuyer sur **F5** et retrouver toute la plateforme, points d'arrêt compris.

## Options étudiées

1. **Docker Compose uniquement** : identique à la production, mais boucle lente (reconstruction d'image) et pas de débogage direct.
2. **Démarrage multiple de projets dans Visual Studio** : débogage direct, mais PostgreSQL et Redis restent à lancer à la main, sans ordre de démarrage ni chaînes de connexion automatiques.
3. **Aspire (projet `Souqly.AppHost`)** : un seul projet de démarrage décrit en C# toutes les ressources ; Aspire lance PostgreSQL et Redis en conteneurs, crée les bases, injecte les chaînes de connexion, attend que chaque dépendance soit saine, attache le débogueur à chaque service et ouvre un tableau de bord (état, logs, traces OpenTelemetry).

## Décision

Option 3, en complément de Docker Compose : Aspire sert au **développement local** et Compose reste la référence pour la démo et le déploiement (`docker compose up` reste exigé par l'énoncé). Version : Aspire 13.6.5, compatible avec le SDK .NET 10.

Sans Docker, l'AppHost peut utiliser des serveurs existants : il suffit de renseigner `ConnectionStrings:CatalogDb`, `InventoryDb` et `Redis` dans ses secrets utilisateur.

## Conséquences

- (+) F5 lance toute la plateforme ; points d'arrêt dans n'importe quel service.
- (+) Les traces OpenTelemetry sont visibles dès maintenant dans le tableau de bord (préparation de US-19, Sprint 4).
- (+) Les conteneurs PostgreSQL et Redis sont persistants : les F5 suivants démarrent en quelques secondes et les données sont conservées.
- (−) Deux façons de lancer la plateforme : il faut garder l'AppHost et `compose.yaml` cohérents (mêmes noms de chaînes de connexion, mêmes variables).
- (−) Docker Desktop reste nécessaire pour les conteneurs.
