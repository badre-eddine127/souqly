# Modèle de menaces (STRIDE) — Sprint 3

- **Périmètre** : passerelle, Catalog, Inventory, Cart, Ordering, Payment, Notification, Keycloak, Kafka, PostgreSQL, Redis.
- **Mise à jour** : à chaque sprint ou à chaque nouveau flux entre services.
- **Légende** : ✅ en place et testé · 🟡 partiel · ⏳ prévu (sprint indiqué).

## Flux et frontières de confiance

```
Navigateur / Scalar ──HTTPS(*)──▶ Passerelle YARP ──▶ Catalog / Inventory / Cart / Ordering ──▶ PostgreSQL (une base par service)
        │                                                            │                       └──▶ Redis (Cart)
        └──── connexion OIDC + PKCE ──▶ Keycloak ◀── client credentials ── Ordering ──jeton SERVICE──▶ Catalog /internal
```

(*) HTTP en local ; TLS terminé par l'ingress en déploiement (sprint 4).

Frontière 1 : Internet → passerelle (seul port publié). Frontière 2 : passerelle → services (réseau interne). Frontière 3 : services → bases (un compte SQL par service, sans droit sur les autres bases).

## Menaces

| # | STRIDE | Menace | Contre-mesure | État | Preuve |
| --- | --- | --- | --- | --- | --- |
| S1 | Usurpation | Appeler l'API sans être connecté ou avec un jeton fabriqué | Validation JWT (signature RS256 JWKS, émetteur, audience `souqly-api`, expiration) dans la passerelle **et** dans chaque service | ✅ | `ApiGateway.Tests` : absent, falsifié, expiré, mauvaise audience → 401 ; démo : jeton modifié → 401 |
| S2 | Usurpation | Se faire passer pour un autre client en envoyant son identifiant | Identité lue uniquement dans `sub` ; aucun `customerId` accepté dans les requêtes ; anciens en-têtes `X-User-*` supprimés | ✅ | `RG07_un_client_ne_voit_pas_la_commande_d_un_autre_404` |
| S3 | Usurpation | Un service compromis appelle les autres | Jeton client credentials propre à chaque service (rôle `SERVICE`), secret hors dépôt (paramètre Aspire généré, `.env` en Compose) | 🟡 | `RG02_le_prix_vient_du_catalogue_avec_un_jeton_technique` ; mTLS ⏳ sprint 4 |
| S4 | Usurpation | Force brute sur les mots de passe | Politique Keycloak : 12 caractères min., blocage temporaire après échecs ; limite de débit par utilisateur/IP à la passerelle | ✅ | realm `bruteForceProtected: true` ; test 429 de la passerelle |
| T1 | Falsification | Le client modifie le prix dans sa requête | La commande ne contient que `productId` + `quantity` ; le prix est lu dans Catalog puis figé dans la ligne (RG-02) | ✅ | `US06_…` : total calculé à partir des prix du catalogue |
| T2 | Falsification | Deux écritures concurrentes sur la même commande | Jeton de concurrence `xmin` → 409 | ✅ | `OrderRepository` ; même mécanisme testé sur Inventory |
| T3 | Falsification | Survente lors d'une vente flash | `UPDATE … WHERE disponible >= qté` + contraintes `CHECK` (ADR-007) | ✅ | `FlashSaleTests` : 500 acheteurs, 100 unités, 0 survente |
| T4 | Falsification | Injection SQL | EF Core paramétré partout ; tri limité à une liste blanche | ✅ | `Catalog.ApiTests` (tri inconnu → 400) |
| R1 | Répudiation | « Je n'ai jamais passé cette commande » | Commande liée au `sub` Keycloak, horodatée ; journaux structurés avec `traceId` ; clé d'idempotence conservée ; événements Kafka conservés 7 jours | 🟡 | Journal d'audit dédié ⏳ sprint 4 |
| I1 | Divulgation | Lire la commande d'un autre client ou les lignes d'un autre vendeur | Filtre par propriétaire dans chaque requête ; 404 au lieu de 403 ; le vendeur ne reçoit que ses lignes (RG-05) | ✅ | `RG05_…`, `RG07_…` |
| I2 | Divulgation | Exposer les endpoints internes ou l'adresse des services | `/internal/**` → 404 à la passerelle ; `Location` relative ; services non publiés en Compose | ✅ | `ApiGateway.Tests` ; démo : `Location: /api/v1/orders/…` |
| I3 | Divulgation | Fuite d'informations dans les erreurs | ProblemDetails (RFC 9457) sans pile d'appels ; seul le `traceId` est renvoyé | ✅ | `ProblemDetailsExceptionHandler` |
| I4 | Divulgation | Secrets dans le dépôt | `.env` ignoré, `.env.example` sans vraie valeur, gitleaks en CI | ✅ | job `secrets` de la CI |
| D1 | Déni de service | Inonder l'API | Limite de débit par jeton (token bucket) à la passerelle → 429 + `Retry-After` | ✅ | `ApiGateway.Tests` |
| D2 | Déni de service | Catalog lent ou arrêté bloque toutes les commandes | Polly : délai 1,2 s par essai, 1 nouvel essai, budget total 2,8 s, disjoncteur → 503 + `Retry-After` | ✅ | `Catalogue_en_panne_…` et `Catalogue_trop_lent_…` : 503 en < 3 s |
| D3 | Déni de service | Commandes énormes | RG-04 : 1 à 20 lignes, 1 à 10 unités, total ≤ 50 000 MAD ; pagination max 100 | ✅ | `Ordering.UnitTests` |
| D4 | Déni de service | Double clic = double commande | En-tête `Idempotency-Key` obligatoire, clé unique (client, clé) en base | ✅ | `US09_dix_envois_simultanes_…` : 10 envois → 1 commande |
| T5 | Falsification | Message Kafka forgé (faux PaymentSucceeded) | Kafka sur le réseau interne uniquement (aucun port publié en Compose) ; chaque étape revérifie l'état de la commande | 🟡 | ACL et SASL/mTLS sur Kafka ⏳ sprint 4 |
| R2 | Répudiation | « Je n'ai jamais été débité » / « remboursé deux fois » | Un paiement par commande (index unique), remboursement idempotent, chaque message tracé (`message-id`, `traceparent`) | ✅ | `Livraison_en_double_…` ; `US08_annulation_…` |
| D5 | Déni de service | Message illisible bloquant une partition | 3 essais puis DLQ, la file continue | ✅ | `Message_illisible_envoye_en_DLQ_…` |
| D6 | Déni de service | Broker indisponible | Outbox : rien n'est perdu, envoi au retour ; les commandes restent acceptées (202) | ✅ | `Broker_en_panne_…` |
| E1 | Élévation | Un client utilise les endpoints vendeur ou internes | Politiques par rôle (`vendor`, `internal`) dans chaque service | ✅ | démo : cliente → 403 sur `/vendor/orders` et `POST /products` |
| E2 | Élévation | Un vendeur modifie le produit d'un autre vendeur | Filtre `vendor_id = sub` à chaque écriture | ✅ | `Catalog.ApiTests` |
| E3 | Élévation | Client `souqly-dev` (mot de passe direct) utilisé en production | Réservé au développement ; retiré du realm de production | ⏳ | sprint 4 |

## Risques acceptés pour l'instant

- HTTP sans TLS en local (TLS en déploiement, sprint 4).
- Keycloak en mode `start-dev` avec base H2 intégrée (PostgreSQL dédié en déploiement).
- Pas encore de journal d'audit séparé (sprint 4).
- Kafka sans authentification (réseau interne) ; SASL/mTLS et ACL au sprint 4.
