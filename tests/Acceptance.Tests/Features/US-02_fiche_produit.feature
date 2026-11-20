# language: fr
@US-02 @MUST @catalogue
Fonctionnalité: Voir la fiche d'un produit
  En tant que visiteur
  Je veux voir la fiche d'un produit (prix, vendeur, description)
  Afin de décider de l'acheter

  Scénario: Un visiteur consulte la fiche d'un produit publié
    Étant donné que le vendeur "karim" publie le produit "Huile d'argan bio 250 ml" à 189,90 MAD dans la catégorie "argan"
    Quand un visiteur ouvre la fiche du produit "Huile d'argan bio 250 ml"
    Alors la réponse a le statut 200
    Et la fiche indique le prix "189.90" MAD et le vendeur "karim"

  Scénario: Un brouillon n'est visible que par son vendeur
    Étant donné que le vendeur "karim" a un brouillon "Savon noir en test" à 45,00 MAD dans la catégorie "cosmetique"
    Quand un visiteur ouvre la fiche du produit "Savon noir en test"
    Alors la réponse a le statut 404
    Quand le vendeur "karim" ouvre la fiche du produit "Savon noir en test"
    Alors la réponse a le statut 200

  Scénario: Un produit inexistant renvoie une erreur claire
    Quand un visiteur ouvre la fiche d'un produit qui n'existe pas
    Alors la réponse a le statut 404
    Et la réponse est un ProblemDetail
