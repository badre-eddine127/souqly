# Runbook d'exploitation Souqly

Que faire quand une alerte se déclenche ou qu'un composant tombe. Chaque section suit le même plan : **symptôme → vérifier → agir → après**.

Outils : tableau de bord Grafana **Souqly — Vue d'ensemble** (http://localhost:3000), Kafka UI (http://localhost:8085), tableau de bord Aspire (traces, logs).

---

<a id="alerte-echecs-de-commande"></a>
## Alerte « Échecs de commande > 2 % » (US-20, critique)

**Symptôme.** Plus de 2 % des commandes échouent pour une raison technique depuis 5 minutes. Échec = réponse 5xx sur `POST /api/v1/orders` (Catalog indisponible, base indisponible…) ou message de saga envoyé en DLQ. Les refus métier (rupture de stock, paiement refusé) ne comptent pas.

**Vérifier.**
1. Grafana › panneau « Erreurs 5xx par service » : quel service répond en erreur ?
2. Si c'est `ordering` avec des **503** : Catalog est lent ou arrêté (le disjoncteur Polly renvoie 503 + `Retry-After`). Vérifier `catalog` dans Aspire (état, logs) et `GET /health/ready`.
3. Si le panneau « Messages en DLQ » augmente : voir la section suivante.
4. Aspire › Traces : filtrer sur `ordering`, statut Error ; la trace montre l'appel en échec.

**Agir.**
- Service arrêté → le redémarrer (Aspire : bouton ▶ ; Kubernetes : `kubectl rollout restart deploy/<service>`).
- Base saturée → vérifier les connexions (`pg_stat_activity`), relancer le service fautif.
- Le disjoncteur se referme seul 10 s après le retour de Catalog : pas d'action sur Ordering.

**Après.** L'alerte se résout seule quand le taux repasse sous 2 % pendant 5 min. Si l'incident a duré plus de 15 min ou touché des paiements : post-mortem (`docs/operations/post-mortem-template.md`).

---

<a id="message-en-dlq"></a>
## Alerte « Message de saga en DLQ » (avertissement)

**Symptôme.** Un message a échoué 3 fois et a été copié dans `<topic>.dlq`. La commande concernée peut rester bloquée (PENDING ou STOCK_RESERVED).

**Vérifier.**
1. Kafka UI › Topics › `souqly.*.dlq` › Messages : lire la clé (= n° de commande), l'en-tête `message-type` et le contenu.
2. Logs du service consommateur (Aspire › Structured logs, filtre `DLQ`) : l'exception donne la cause.

**Agir.**
- Cause corrigée (bug déployé, dépendance revenue) : republier le message sur le topic d'origine **avec le même `message-id`** (Kafka UI › Produce message, recopier clé, valeur et en-têtes). L'inbox garantit qu'il ne sera pas appliqué deux fois s'il l'avait été partiellement.
- Message réellement invalide : noter la commande, l'annuler côté métier si besoin (`POST /api/v1/orders/{id}/cancellation` en tant que client, ou intervention admin).
- Une réservation de stock non confirmée expire seule au bout de 15 min (RG-03) : pas de stock bloqué durablement.

---

## Payment arrêté

**Symptôme.** Les commandes restent en `STOCK_RESERVED` ; aucun message `PaymentSucceeded`.

**Ce qui se passe.** Rien n'est perdu : les messages `PaymentRequested` attendent dans Kafka (groupe `payment`). Les clients reçoivent toujours 202.

**Agir.** Redémarrer Payment. Il reprend à son dernier offset validé et traite les commandes en attente ; prouvé par le test `Payment_arrete_la_commande_attend_puis_est_confirmee_au_redemarrage_sans_perte`. Attention : au-delà de 15 min, les réservations expirent (RG-03) ; Inventory rendra le stock et la commande sera refusée si le stock a été vendu entre-temps.

---

## Kafka arrêté

**Symptôme.** Les commandes restent en `PENDING` ; logs « Envoi du message … impossible : il reste dans l'outbox » ; métrique `souqly_messaging_publish_failures_total` en hausse.

**Ce qui se passe.** Les commandes sont acceptées et les événements restent dans la table `outbox_messages` de chaque service.

**Vérifier.** `SELECT count(*) FROM outbox_messages WHERE published_at IS NULL;` dans `ordering_db`.

**Agir.** Redémarrer Kafka. Les publieurs d'outbox envoient l'arriéré dans l'ordre, sans intervention.

---

## Keycloak arrêté

**Symptôme.** Nouvelles connexions impossibles ; les services renvoient 401 après expiration des jetons (5 min) ; Ordering ne peut plus obtenir son jeton de service (503 sur les commandes).

**Agir.** Redémarrer Keycloak. Les services rechargent les clés de signature automatiquement.

---

## Commandes utiles

| Besoin | Commande |
| --- | --- |
| État de toute la plateforme | Tableau de bord Aspire, ou `docker compose ps` |
| Santé d'un service | `curl http://localhost:<port>/health/ready` |
| Arriéré d'outbox | `SELECT topic, count(*) FROM outbox_messages WHERE published_at IS NULL GROUP BY topic;` |
| Retard d'un consommateur Kafka | Kafka UI › Consumers › groupe (`ordering`, `inventory`, `payment`, `notification`) › Lag |
| Commandes bloquées | `SELECT id, status, updated_at FROM orders WHERE status IN ('Pending','StockReserved') AND updated_at < now() - interval '5 minutes';` |
