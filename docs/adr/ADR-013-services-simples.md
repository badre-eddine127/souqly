# ADR-013 : Un seul projet pour les services simples (Payment, Notification)

- **Statut** : accepté
- **Date** : 2026-11-20

## Contexte

Catalog, Inventory et Ordering suivent la Clean Architecture en quatre projets (Domain, Application, Infrastructure, Api), et des tests d'architecture vérifient les dépendances. Payment (simulé) et Notification ont chacun une entité, deux ou trois gestionnaires d'événements et un ou deux endpoints.

## Décision

Un seul projet `Souqly.<Service>.Api` par service simple, découpé en **dossiers** `Domain/`, `Application/`, `Infrastructure/` avec les mêmes règles (le domaine ne dépend de rien). On passera à quatre projets si le service grossit, par exemple avec un vrai prestataire de paiement.

## Conséquences

- (+) Moins de fichiers de projet et de références : le code métier reste lisible en un coup d'œil.
- (−) Le respect des couches n'est plus garanti par le compilateur, seulement par la revue de code.
