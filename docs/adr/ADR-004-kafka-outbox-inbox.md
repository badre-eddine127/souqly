# ADR-004 : Kafka avec outbox transactionnelle et inbox idempotente

- **Statut** : accepté
- **Date** : 2026-11-20
- **Preuves** : `tests/Messaging.Tests` (outbox, inbox, panne du broker), `tests/Saga.IntegrationTests` (livraison en double, DLQ), `KafkaTransportTests` et `KafkaSagaTests` (vrai Kafka, CI)

## Contexte

La commande traverse quatre services (Ordering, Inventory, Payment, Notification). Des appels HTTP en chaîne coupleraient leur disponibilité : si Payment est arrêté, plus personne ne peut commander. Il faut un échange asynchrone, mais sans les deux pièges classiques :

1. **Double écriture** : enregistrer la commande puis publier l'événement. Si le service tombe entre les deux, la commande existe sans que la saga démarre ; dans l'autre ordre, on annonce une commande qui n'existe pas.
2. **Doublons** : Kafka garantit « au moins une fois ». Un consommateur qui tombe avant de valider son offset relit le message : sans précaution, on débiterait deux fois.

## Options étudiées

| Option | Pour | Contre |
| --- | --- | --- |
| RabbitMQ | Simple, routage riche | Pas de relecture de l'historique ; l'énoncé vise Kafka |
| MassTransit sur Kafka | Saga, outbox et réessais intégrés | Licence commerciale depuis la v9 ; beaucoup de « magie » difficile à expliquer en soutenance |
| **Confluent.Kafka + outbox/inbox maison** | Client officiel (Apache 2.0), comportement visible et testable, ≈ 500 lignes | Code de plomberie à maintenir |

## Décision

Brique `Souqly.Messaging`, utilisée par les quatre services :

- **Outbox** : `IEventPublisher.Publish()` ajoute une ligne à `outbox_messages` dans le **même SaveChanges** que la modification métier (atomique). `OutboxPublisher` lit les lignes non envoyées (`FOR UPDATE SKIP LOCKED`, plusieurs instances possibles), les envoie dans l'ordre et s'arrête au premier échec pour ne pas inverser deux événements d'une même commande.
- **Inbox** : chaque message porte un `message-id`. Le gestionnaire s'exécute dans une transaction qui écrit aussi `(message_id, consumer)` dans `inbox_messages` (clé primaire) et les événements suivants dans l'outbox. Message déjà vu → ignoré. Effet : **exactement une fois** du point de vue métier.
- **Producteur** : `acks=all`, idempotence activée. **Consommateur** : un groupe par service, offset validé après traitement.
- **Clé = identifiant de commande** : une commande = une partition = ordre garanti.
- **Réessais puis DLQ** : 3 essais (200 ms, 400 ms), puis copie dans `<topic>.dlq` et passage au message suivant, pour qu'un message illisible ne bloque pas toutes les commandes.
- **Traces** : en-tête `traceparent` ; une commande est suivie dans le tableau de bord Aspire de la requête HTTP jusqu'à la confirmation (US-19).
- **Contrat** : `contracts/asyncapi/souqly.yaml`. Chaque service garde ses propres records d'événements (ADR-009) ; seul le JSON est partagé.
- **Tests sans Docker** : un `InMemoryBroker` imite Kafka (groupes, ordre, relivraison) ; les mêmes scénarios tournent sur un vrai Kafka dans la CI.

## Conséquences

- (+) Aucune perte : broker arrêté → les messages attendent en base et partent au retour (test `Broker_en_panne_…`).
- (+) Aucun double effet : chaque message livré deux fois → un seul débit, une seule sortie de stock (test `Livraison_en_double_…`).
- (−) Latence de 0,1 à 0,5 s entre l'écriture et la publication (intervalle de lecture de l'outbox).
- (−) Deux tables de plus par service, purgées après 7 jours.
- (−) Le rejeu depuis la DLQ est manuel pour l'instant (outil d'administration prévu au sprint 4).
