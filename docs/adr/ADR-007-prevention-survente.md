# ADR-007 : Prévenir la survente par une mise à jour conditionnelle

- **Statut** : accepté
- **Date** : 2026-11-20
- **Preuve** : `tests/Inventory.ApiTests/FlashSaleTests.cs`

## Contexte

Lors des ventes flash (Ramadan, Black Friday), des centaines de réservations concurrentes visent le même produit. L'invariant RG-01 interdit tout stock disponible négatif. Une lecture suivie d'une écriture (`var s = Find(); if (s.Available >= q) s.Reserved += q; Save();`) laisse passer plusieurs acheteurs sur la dernière unité.

## Options étudiées

1. **Jeton de concurrence `xmin` + nouvel essai** : simple avec EF Core, mais sous forte contention presque toutes les tentatives échouent et réessaient ; débit faible et latence imprévisible.
2. **Verrou pessimiste `SELECT … FOR UPDATE`** : correct, mais les requêtes s'attendent une à une et le risque d'interblocage augmente avec les commandes multi-produits.
3. **`UPDATE … WHERE on_hand - reserved >= @qty`** via `ExecuteUpdateAsync` : une seule requête, évaluée et écrite de façon atomique par PostgreSQL sous verrou de ligne ; 0 ligne modifiée = stock insuffisant.

## Décision

Option 3, complétée par :

- des contraintes `CHECK (on_hand >= 0)`, `CHECK (reserved >= 0)` et `CHECK (reserved <= on_hand)` en base, qui protègent même contre un bug applicatif ;
- un tri des lignes par `product_id` avant réservation, pour que deux commandes multi-produits verrouillent dans le même ordre (pas d'interblocage) ;
- une transaction par commande : si une ligne échoue, tout est annulé (tout ou rien) ;
- un index unique `(order_id, product_id)` sur les réservations pour l'idempotence (message rejoué).

Le jeton `xmin` reste utilisé pour les modifications du vendeur (`PUT` du stock), qui sont rares et doivent détecter une écriture concurrente.

## Conséquences

- (+) `FlashSaleTests` : 500 réservations concurrentes pour 100 unités donnent exactement 100 réservations, 400 refus, 0 survente, en environ 1,5 s sur PostgreSQL 16.
- (+) Pas d'attente active ni de nouvel essai côté application.
- (−) Une partie de la règle est en SQL : elle est documentée ici et couverte par les tests d'intégration sur un vrai PostgreSQL (jamais InMemory).
- (−) Les commandes multi-produits imposent l'ordre de verrouillage par `product_id`.
