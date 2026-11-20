# language: fr
@US-03 @MUST @catalogue
Fonctionnalité: Gérer ses produits
  En tant que vendeur
  Je veux créer, modifier et archiver mes produits
  Afin de tenir mon catalogue à jour

  Scénario: Créer un produit publié
    Quand le vendeur "fatima" crée le produit "Pouf en cuir" à 650,00 MAD dans la catégorie "cuir"
    Alors la réponse a le statut 201
    Et un visiteur trouve "Pouf en cuir" à 650,00 MAD dans le catalogue

  Scénario: Modifier le prix d'un produit
    Étant donné que le vendeur "fatima" publie le produit "Babouches jaunes" à 220,00 MAD dans la catégorie "cuir"
    Quand le vendeur "fatima" change le prix de "Babouches jaunes" à 199,00 MAD
    Alors la réponse a le statut 200
    Et un visiteur trouve "Babouches jaunes" à 199,00 MAD dans le catalogue

  Scénario: Archiver un produit le retire du catalogue
    Étant donné que le vendeur "fatima" publie le produit "Lanterne ancienne" à 480,00 MAD dans la catégorie "artisanat"
    Quand le vendeur "fatima" archive le produit "Lanterne ancienne"
    Alors la réponse a le statut 204
    Et un visiteur ne trouve plus "Lanterne ancienne" dans le catalogue

  # Le produit d'un autre vendeur est traité comme inexistant (404) : on ne révèle pas ce qui ne nous appartient pas.
  Scénario: Un vendeur ne peut pas modifier le produit d'un autre vendeur
    Étant donné que le vendeur "fatima" publie le produit "Plateau en thuya" à 300,00 MAD dans la catégorie "artisanat"
    Quand le vendeur "karim" change le prix de "Plateau en thuya" à 1,00 MAD
    Alors la réponse a le statut 404
    Et un visiteur trouve "Plateau en thuya" à 300,00 MAD dans le catalogue

  Scénario: Un client ne peut pas créer de produit
    Quand la cliente "sara" essaie de créer le produit "Faux produit" à 10,00 MAD dans la catégorie "cuir"
    Alors la réponse a le statut 403

  Scénario: Un prix négatif est refusé
    Quand le vendeur "fatima" crée le produit "Produit gratuit" à -5,00 MAD dans la catégorie "cuir"
    Alors la réponse a le statut 400
