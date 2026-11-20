# ADR-005 : Saga de commande orchestrée par Ordering

- **Statut** : accepté
- **Date** : 2026-11-20
- **Preuve** : `tests/Saga.IntegrationTests/SagaScenarios.cs`

## Contexte

Une commande engage trois bases différentes (commandes, stock, paiements) : pas de transaction distribuée possible. Il faut une saga, c'est-à-dire une suite de transactions locales, avec une compensation pour chaque étape qui peut être défaite.

## Options étudiées

1. **Chorégraphie pure** : chaque service réagit aux événements des autres. Peu de couplage, mais l'état de la commande est dispersé ; pour savoir « où en est ma commande ? », il faut reconstituer le fil.
2. **Orchestration par Ordering** : Ordering décide de l'étape suivante à partir de chaque réponse. La machine à états de l'agrégat `Order` (sprint 2) est le seul endroit où le parcours est décrit.
3. **Moteur de workflow** (Temporal, Elsa) : puissant, mais une brique d'infrastructure de plus pour un seul processus.

## Décision

Option 2, avec des **événements** (et non des commandes adressées à un service précis) :

```
Client ── POST /orders ──▶ Ordering : PENDING ── OrderPlaced ─────────────────▶ Inventory
                                                                                   │ réserve tout ou rien (ADR-007)
          Ordering ◀── StockReserved ───────────────────────────────────────────────┤
          STOCK_RESERVED ── PaymentRequested ──▶ Payment (plafond simulé 10 000 MAD) │
          Ordering ◀── PaymentSucceeded ── Payment                                   │
          CONFIRMED ── OrderConfirmed ──▶ Inventory (sortie du stock) + Notification

Échecs et compensations
  StockReservationFailed → REJECTED (OUT_OF_STOCK)    → rien à défaire
  PaymentFailed          → REJECTED (PAYMENT_DECLINED) → Inventory libère la réservation
  Annulation client      → CANCELLED                  → Inventory libère ou remet en stock ; Payment rembourse si payé
```

Règles :

- **Chaque étape est idempotente** (inbox + garde métier) : réserver deux fois la même commande, rembourser deux fois, etc. n'a qu'un effet.
- **Réponse tardive** : si une réponse arrive pour une commande déjà annulée, Ordering l'ignore. Comme tous les événements d'une commande sont ordonnés dans leur partition, Inventory et Payment ont déjà reçu `OrderCancelled` après leur propre étape et ont compensé.
- **Pas de blocage** : une réservation non confirmée expire au bout de 15 min (RG-03), même si un message était perdu côté métier.

## Conséquences

- (+) `GET /orders/{id}` donne toujours l'état exact du parcours.
- (+) Les scénarios métier sont testés de bout en bout : succès, rupture de stock, paiement refusé avec compensation, annulation après paiement avec remise en stock et remboursement, vente flash (20 clients pour 5 unités : 5 confirmées, 15 refusées, 0 survente).
- (−) Ordering connaît l'enchaînement des étapes : ajouter une étape (ex. contrôle anti-fraude) modifie Ordering.
- (−) Cohérence à terme : la commande reste PENDING quelques centaines de millisecondes (le client reçoit 202, puis suit l'état).
