# ADR-016 : Déploiement Kubernetes avec un chart Helm unique et autoscaling horizontal

- **Statut** : accepté
- **Date** : 2026-11-23
- **Contexte du sprint** : Sprint 5 (mise en production)

## Contexte

Souqly tourne en local avec Aspire (F5) et en démonstration avec Docker Compose. La production demande ce que Compose ne donne pas : plusieurs instances par service, remplacement sans coupure, mise à l'échelle automatique pendant une vente flash, isolement réseau et secrets hors du dépôt.

La charge n'est pas uniforme ([ADR-001](ADR-001-microservices.md)) : le catalogue et la recherche reçoivent environ 50 fois plus de lectures que la commande. Pendant un pic, seuls la passerelle, Catalog, Search, Cart et Ordering doivent grandir.

Options étudiées :

1. **Manifestes Kubernetes écrits à la main** (un fichier par objet et par service) : environ 40 fichiers presque identiques, faciles à désynchroniser.
2. **Un chart Helm par service** : 9 charts à versionner et à installer dans le bon ordre.
3. **Un seul chart Helm** pour les 9 services, piloté par un bloc de valeurs par service.
4. **Kustomize** : pas de valeurs typées ni de paquet versionné à publier.

## Décision

- **Un seul chart** `deploy/helm/souqly`. Les gabarits parcourent `services:` dans `values.yaml` ; un service se règle (répliques, ressources, HPA, variables) sans toucher aux gabarits. Le chart et les images portent la même version (`appVersion`), publiés ensemble sur GHCR par le workflow `release`.
- **Dépendances hors du chart** : PostgreSQL, Redis, Kafka et Keycloak sont des services gérés ou leurs opérateurs. Un chart applicatif ne doit pas porter le cycle de vie des données.
- **HPA sur le CPU (cible 70 %)** pour gateway (2→6), catalog (2→8), search (2→6), cart (2→6) et ordering (2→6). Montée immédiate (jusqu'à doubler toutes les 15 s), descente d'un pod par minute après 5 minutes de calme, pour ne pas osciller. Inventory, Payment et Notification ont un nombre fixe de pods : leur travail suit le rythme des commandes, pas celui de la navigation.
- **Disponibilité** : au moins 2 pods pour les services exposés, `PodDisruptionBudget` (1 pod minimum), répartition sur plusieurs nœuds, mise à jour progressive sans pod indisponible (`maxUnavailable: 0`).
- **Sécurité** : `NetworkPolicy` (l'ingress n'atteint que web et gateway ; seule la passerelle atteint les services ; Ordering et Search atteignent Catalog), conteneurs non root en lecture seule sans capacités Linux, pas de jeton de compte de service monté, secrets dans un `Secret` créé hors du chart, TLS terminé par l'ingress (cert-manager).
- **Configuration** : uniquement par variables d'environnement (mêmes clés qu'en Compose). Les chaînes de connexion citent les mots de passe par `$(NOM)`, résolus par Kubernetes depuis le Secret.
- **Migrations** : chaque service applique ses migrations EF Core au démarrage (`Database__MigrateOnStartup`). EF Core 9+ pose un verrou de migration : deux pods qui démarrent ensemble ne migrent pas deux fois.
- **Vérification** : `helm lint` et `kubeconform` en CI ; `helm test` après chaque installation.

## Conséquences

- (+) Un ajout de service = un bloc de valeurs ; les 9 services suivent les mêmes règles de sécurité et de santé.
- (+) Le pic d'une vente flash est absorbé par les seuls services concernés ; vérifiable avec le profil k6 `pic` et `kubectl get hpa -w`.
- (+) Les messages Kafka restent fiables avec plusieurs pods : l'outbox utilise `FOR UPDATE SKIP LOCKED` et l'inbox ignore les doublons ([ADR-004](ADR-004-kafka-outbox-inbox.md)).
- (−) **Sessions du site** : le cookie est chiffré avec une clé propre à chaque pod. L'ingress envoie un navigateur toujours vers le même pod (affinité par cookie) ; un pod remplacé déconnecte ses utilisateurs. À corriger en partageant les clés ASP.NET Core Data Protection (Redis).
- (−) Le HPA sur le CPU réagit avec un délai (collecte des métriques toutes les 15 s environ). Un HPA sur la latence ou le débit demanderait un adaptateur de métriques (KEDA, Prometheus Adapter).
- (−) Les dépendances doivent exister avant l'installation ; le chart ne les crée pas.
