# Registre des traitements de données personnelles (loi 09-08)

Souqly s'adresse à des clients au Maroc : la loi n° 09-08 relative à la protection des personnes physiques à l'égard du traitement des données à caractère personnel s'applique, sous le contrôle de la CNDP. Ce registre décrit ce que la plateforme collecte, pourquoi et pour combien de temps. Il prépare la déclaration à la CNDP ; il ne la remplace pas.

> Projet pédagogique : les comptes de démonstration (sara, fatima, karim, admin-souqly) sont fictifs. Aucune donnée réelle n'est traitée.

| # | Traitement | Finalité | Données | Personnes concernées | Où (service / base) | Durée de conservation | Accès |
| --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Comptes et connexion | Authentifier, attribuer les rôles | Nom d'utilisateur, e-mail, prénom, nom, empreinte du mot de passe (Keycloak), date de connexion | Clients, vendeurs, administrateurs | Keycloak (base propre) | Durée du compte ; suppression sur demande | Keycloak, administrateurs |
| T2 | Catalogue vendeur | Publier des produits | Identifiant vendeur (`sub`), contenu des fiches | Vendeurs | Catalog / `catalog_db` | Durée du compte vendeur | Public (fiches publiées), vendeur |
| T3 | Panier | Préparer un achat | Identifiant client (`sub`), produits, quantités | Clients | Cart / Redis | 30 jours sans activité (TTL) | Client |
| T4 | Commandes | Exécuter la vente, suivi, service après-vente | Identifiant client (`sub`), lignes (produit, prix figé, quantité), statut, dates | Clients ; vendeurs (pour leurs lignes) | Ordering / `ordering_db` | 10 ans (obligations comptables, art. 22 du Code de commerce) | Client ; vendeur limité à ses lignes ; administrateurs |
| T5 | Clés d'idempotence | Éviter les doubles commandes | Identifiant client, clé, empreinte SHA-256 de la requête | Clients | Ordering / `ordering_db` | 24 h (purge, sprint 3) | Service uniquement |
| T7 | Paiements (simulés) | Encaisser, rembourser | Identifiant client, montant, statut, motif de refus ; **aucune donnée de carte** | Clients | Payment / `payment_db` | 10 ans (pièces comptables) | Client (son paiement), administrateurs |
| T8 | Notifications | Informer de l'issue d'une commande | Identifiant destinataire (`sub`), texte du message, date de lecture | Clients, vendeurs | Notification / `notification_db` | 1 an | Destinataire |
| T9 | Événements Kafka | Faire avancer la saga | Identifiants client et vendeur, lignes de commande, montants | Clients, vendeurs | Kafka (réseau interne) ; outbox/inbox des services | 7 jours (rétention des topics et des tables outbox/inbox) | Services uniquement |
| T6 | Journaux et traces | Exploitation, sécurité | `traceId`, route, code HTTP, identifiant utilisateur ; ni mot de passe, ni jeton, ni corps de requête | Tous | Sortie standard / OpenTelemetry | 30 jours | Équipe d'exploitation |

## Principes appliqués

- **Minimisation** : les services ne stockent que l'identifiant `sub`, jamais l'e-mail ou le nom ; ces données restent dans Keycloak. L'adresse de livraison n'est collectée qu'au sprint 3.
- **Cloisonnement** : une base et un compte SQL par service ; un service ne lit pas la base d'un autre.
- **Sécurité** : jetons signés et courts (5 min), mots de passe gérés par Keycloak, secrets hors dépôt (voir `threat-model.md`).
- **Droits des personnes** (accès, rectification, opposition — art. 7 à 9) : export et suppression des données d'un compte à outiller au sprint 4 (suppression dans Keycloak et Cart, anonymisation de `customer_id` dans les commandes conservées pour la comptabilité).
- **Transferts hors du Maroc** : aucun en local. Tout hébergement à l'étranger devra être autorisé par la CNDP.
