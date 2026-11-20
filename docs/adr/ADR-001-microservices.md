# ADR-001 : Microservices plutôt que monolithe modulaire

- **Statut** : accepté
- **Date** : 2026-11-20

## Contexte

Un monolithe bien découpé suffit à la plupart des projets. Souqly a pourtant des contraintes qui poussent vers un découpage en services : le catalogue reçoit environ 50 fois plus de lectures que la commande ; lors d'une vente flash, seuls la commande et le stock doivent monter en charge ; une panne de l'expédition ne doit pas empêcher de vendre. Le module a aussi un objectif pédagogique : vivre les vrais problèmes des systèmes distribués.

## Options étudiées

1. **Monolithe modulaire** — un seul déploiement, transactions ACID, tests simples ; mais montée en charge et pannes globales, et aucun des problèmes distribués à apprendre.
2. **Microservices par contexte délimité** — Catalog, Inventory, Cart, Ordering, Payment, Notification (+ Shipping, Search) ; montée en charge et déploiement indépendants, isolation des pannes ; mais réseau, cohérence à terme, supervision et tests plus complexes.

## Décision

Option 2, avec une base de données par service et la communication asynchrone par défaut (Kafka). Le seul appel synchrone du parcours est l'instantané des prix (Ordering → Catalog).

## Conséquences

- (+) Montée en charge ciblée (Ordering, Inventory) et dégradation propre si un service secondaire tombe.
- (−) Cohérence à terme : la commande passe par une saga avec compensations (ADR-005).
- (−) Données dupliquées (prix figés dans les lignes de commande), supervision obligatoire (traces distribuées).
- (−) Coût assumé et mesuré : temps de démarrage local, mémoire, complexité des tests.
