# ADR-015 : Service Search sur PostgreSQL (pg_trgm) plutôt qu'OpenSearch

- **Statut** : accepté
- **Date** : 2026-11-22
- **Story** : US-04 (recherche en texte libre, tolérance aux fautes, filtres prix et catégorie)

## Contexte

Le dossier de projet prévoit un service `search-service` : un modèle de lecture (CQRS) alimenté par les événements de Catalog, avec OpenSearch. La recherche doit tolérer les fautes de frappe (« tapi berbre » doit trouver « Tapis berbère »), filtrer par prix et catégorie et donner des facettes. La plateforme complète doit tenir sur un poste de 16 Go (Aspire, Kafka, Keycloak, Grafana, sept services).

## Décision

- Un **service Search distinct** (projet unique, ADR-013) avec **sa propre base** `search_db` : il ne lit jamais la base de Catalog.
- **Catalog publie** `ProductCreated`, `ProductUpdated` et `ProductArchived` (topic `souqly.catalog.product-events.v1`, outbox, clé = identifiant du produit). Chaque événement porte l'**état complet** du produit : Search n'a pas besoin de rappeler Catalog.
- L'index est une table PostgreSQL avec l'extension **pg_trgm** : similarité de trigrammes sur les mots (`word_similarity`), seuil 0,45, index GIN `gin_trgm_ops`. Le texte est normalisé en C# (minuscules, sans accents) à l'écriture et à la lecture.
- Seuls les produits **en vente** (ACTIVE) sont indexés ; un brouillon ou un produit archivé est retiré. Une version plus ancienne (`updatedAt`) n'écrase jamais une plus récente.
- **Réconciliation au démarrage** : Search relit la liste publique de Catalog. Elle remplit un index vide (produits créés avant les événements) et rattrape un événement perdu.
- **Dégradation gracieuse** : si Search ne répond pas, le site repasse sur la recherche exacte de Catalog (`GET /api/v1/products?q=`).

## Conséquences

- (+) Aucune infrastructure de plus : PostgreSQL est déjà là ; démarrage en quelques secondes au lieu d'un nœud OpenSearch (1 à 2 Go de mémoire).
- (+) Même architecture que la cible (CQRS, événements, base dédiée) : passer à OpenSearch ne change que l'implémentation de l'index dans Search.
- (+) Tests simples : les tests d'acceptation hébergent Search en mémoire avec le reste de la plateforme.
- (−) Pas d'analyse linguistique avancée (racines, synonymes) ni de score BM25 ; suffisant pour quelques milliers de produits.
- (−) Cohérence à terme : un produit apparaît dans la recherche quelques centaines de millisecondes après sa création.
