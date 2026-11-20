# ADR-000 : .NET 10 au lieu de Java 21 / Spring Boot

- **Statut** : accepté
- **Date** : 2026-11-19
- **Décideurs** : équipe Souqly, avec l'accord de l'enseignant (Pr Annaki), donné le 19/11/2026 sur Google Classroom

## Contexte

Le dossier SUP351 propose Java 21 et Spring Boot 3.5 comme stack de référence et précise que « toute autre brique fait l'objet d'un ADR ». L'équipe a plus de cinq ans d'expérience professionnelle en ASP.NET Core et Entity Framework, et beaucoup moins en Spring. Le projet est court (5 sprints) et ambitieux (saga, outbox, observabilité, Kubernetes).

## Options étudiées

1. **Java 21 + Spring Boot 3.5** — stack de référence ; les exemples de code du dossier sont directement réutilisables ; mais une partie du temps irait à l'apprentissage de Spring plutôt qu'aux sujets évalués (systèmes distribués, tests, livraison).
2. **.NET 10 LTS + ASP.NET Core + Entity Framework Core** — stack maîtrisée, version LTS supportée jusqu'en 2028, outillage équivalent pour chaque brique (YARP, Polly, Testcontainers, OpenTelemetry) ; mais les exemples du dossier sont à traduire et l'enseignant corrige un autre écosystème.

## Décision

Option 2 : .NET 10 LTS, ASP.NET Core 10 et Entity Framework Core 10 (Npgsql). Tout le reste de la stack de l'énoncé est conservé : PostgreSQL 16, Kafka (KRaft), Keycloak 26, Redis, Docker, Kubernetes/Helm, GitHub Actions, k6, OpenTelemetry/Grafana.

| Brique Java | Équivalent .NET retenu |
| --- | --- |
| Spring Boot, Maven multi-modules | ASP.NET Core 10, solution `Souqly.slnx` + `Directory.Packages.props` |
| JPA/Hibernate + Flyway | Entity Framework Core 10 + migrations EF |
| Bean Validation | FluentValidation |
| Spring Cloud Gateway | YARP |
| Resilience4j | Microsoft.Extensions.Http.Resilience (Polly v8) |
| Spring Kafka | Confluent.Kafka |
| JUnit 5, AssertJ, Mockito | xUnit, Shouldly, NSubstitute |
| ArchUnit, JaCoCo | NetArchTest, Coverlet |

## Conséquences

- (+) Plus de temps pour les sujets évalués : saga, outbox, idempotence, tests de concurrence, observabilité.
- (+) Les exigences fonctionnelles, les SLO, les livrables et la grille restent identiques.
- (−) Chaque exemple Java du dossier doit être traduit ; la table d'équivalence ci-dessus sert de référence.
- (−) Les bibliothèques sont choisies en vérifiant leur licence : FluentAssertions 8 et MediatR étant devenus commerciaux, on utilise Shouldly et des handlers écrits à la main (voir ADR-011).
