# language: fr
@US-01 @MUST @catalogue
Fonctionnalité: Parcourir les produits par catégorie
  En tant que visiteur
  Je veux parcourir les produits par catégorie, avec pagination et tri
  Afin de trouver ce que je cherche sans créer de compte

  Contexte:
    Étant donné que le vendeur "fatima" publie les produits suivants :
      | nom                 | catégorie | prix     |
      | Tapis kilim bleu    | tapis     | 1 200,00 |
      | Tapis berbère beige | tapis     | 850,00   |
      | Tapis du Haut Atlas | tapis     | 2 400,00 |
      | Tajine de Safi      | poterie   | 180,00   |

  Scénario: Parcourir une catégorie triée par prix croissant
    Quand un visiteur parcourt la catégorie "tapis" triée par "prix croissant", 2 produits par page
    Alors la page affiche dans l'ordre "Tapis berbère beige, Tapis kilim bleu"
    Et le catalogue annonce 3 produits sur 2 pages

  Scénario: Passer à la page suivante
    Quand un visiteur parcourt la page 2 de la catégorie "tapis" triée par "prix croissant", 2 produits par page
    Alors la page affiche dans l'ordre "Tapis du Haut Atlas"

  Scénario: Trier par prix décroissant
    Quand un visiteur parcourt la catégorie "tapis" triée par "prix décroissant", 10 produits par page
    Alors la page affiche dans l'ordre "Tapis du Haut Atlas, Tapis kilim bleu, Tapis berbère beige"

  Scénario: Un produit en brouillon n'apparaît pas dans le catalogue
    Étant donné que le vendeur "fatima" a un brouillon "Tapis pas encore prêt" à 300,00 MAD dans la catégorie "tapis"
    Quand un visiteur parcourt la catégorie "tapis" triée par "prix croissant", 10 produits par page
    Alors la page ne contient pas "Tapis pas encore prêt"
