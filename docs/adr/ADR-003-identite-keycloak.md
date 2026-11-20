# ADR-003 : Keycloak comme fournisseur d'identité, jetons JWT validés par chaque service

- **Statut** : accepté
- **Date** : 2026-11-20
- **Preuves** : `tests/ApiGateway.Tests` (jetons absent, falsifié, expiré, mauvaise audience → 401), `tests/Ordering.ApiTests` (403/404 par rôle et propriétaire)

## Contexte

Souqly a trois profils (client, vendeur, administrateur) et des appels entre services (Ordering lit les prix dans Catalog). Il faut une connexion sûre (mots de passe, anti-force brute, session), des rôles, des jetons de service, et une documentation d'API où l'on peut se connecter pour tester. Écrire notre propre gestion de mots de passe est une source de failles classique (OWASP A07).

## Options étudiées

1. **ASP.NET Core Identity + jetons maison** : tout en .NET, mais nous devrions écrire l'émission et la rotation des clés, la page de connexion, l'anti-force brute et le flux client credentials ; beaucoup de code de sécurité à maintenir.
2. **Duende IdentityServer** : très complet, mais licence commerciale au-delà d'un certain chiffre d'affaires ; compliqué à justifier pour un projet destiné à évoluer.
3. **OpenIddict** : libre et en .NET, mais c'est une bibliothèque : écrans de connexion, administration des comptes et politique de mots de passe restent à notre charge.
4. **Keycloak 26** : serveur OIDC libre (Apache 2.0, CNCF), console d'administration, politique de mots de passe, protection force brute, rôles de realm, comptes de service, import du realm en JSON versionné.

## Décision

Option 4. Le realm `souqly` est décrit dans `infra/keycloak/realm-souqly.json` et importé au démarrage (Aspire et Docker Compose).

- **Clients** : `souqly-web` (public, code d'autorisation + PKCE S256, utilisé aussi par Scalar), `souqly-dev` (mot de passe direct, environnement de développement uniquement), `ordering-service` (confidentiel, client credentials, rôle `SERVICE`).
- **Rôles de realm** : `CUSTOMER` (attribué par défaut à toute inscription), `VENDOR`, `ADMIN`, `SERVICE`.
- **Audience** `souqly-api` ajoutée par un mappeur sur chaque client : un jeton émis pour une autre application est refusé.
- **Validation dans chaque service et dans la passerelle** (défense en profondeur, pas seulement à l'entrée) : signature RS256 via les clés JWKS, émetteur, audience, expiration (5 min, tolérance 30 s). Les rôles `realm_access.roles` sont convertis en rôles .NET (`KeycloakRolesTransformation`) et utilisés par des politiques nommées (`customer`, `vendor`, `admin`, `internal`).
- **L'identité vient toujours du jeton** (`sub`) : aucun identifiant client n'est accepté dans le corps ou les en-têtes. Une ressource d'un autre utilisateur renvoie 404, pas 403, pour ne pas révéler son existence.

## Conséquences

- (+) Aucun mot de passe stocké par nos services ; politique (12 caractères minimum) et blocage après échecs répétés fournis par Keycloak.
- (+) Les tests n'ont pas besoin de Keycloak : `Souqly.Testing.TestJwt` signe des jetons avec une clé RSA de test et remplace la configuration de validation ; le même code de validation est exercé.
- (+) Le bouton « Authorize » de Scalar ouvre la vraie page de connexion (PKCE) : l'enseignant peut tester l'API avec les comptes de démonstration.
- (−) Un conteneur de plus (≈ 500 Mo de mémoire) et un démarrage plus lent (≈ 20 s). Le conteneur Aspire est persistant pour ne pas payer ce coût à chaque F5.
- (−) L'émetteur dépend de l'URL publique (`http://localhost:8180` en local) : en Compose, `KC_HOSTNAME` fixe l'URL publique et les services lisent les clés par le réseau interne (`MetadataAddress`).
- (−) Le client `souqly-dev` (mot de passe direct) ne doit pas exister en production : il sera retiré du realm de production (sprint 4).
