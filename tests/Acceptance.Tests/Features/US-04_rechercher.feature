# language: fr
@US-04 @SHOULD @catalogue
Fonctionnalité: Rechercher en texte libre
  En tant que visiteur
  Je veux rechercher en texte libre (tolérance aux fautes, filtres prix et catégorie)
  Afin de trouver vite

  Contexte:
    Étant donné que la vendeuse "fatima" a publié "Tapis berbère Beni Ouarain" à 2 890,00 MAD dans la catégorie "tapis"
    Et que la vendeuse "fatima" a publié "Kilim tissé Azilal" à 1 450,00 MAD dans la catégorie "tapis"
    Et que le vendeur "karim" a publié "Tajine de Safi émaillé" à 260,00 MAD dans la catégorie "poterie"
    Et que le vendeur "karim" a publié "Huile d'argan bio" à 180,00 MAD dans la catégorie "argan"

  Scénario: Une faute de frappe ne m'empêche pas de trouver
    Quand un visiteur recherche "tapi berbre"
    Alors le premier résultat est "Tapis berbère Beni Ouarain"
    Et "Tajine de Safi émaillé" ne fait pas partie des résultats

  Scénario: Les accents et les majuscules n'ont pas d'importance
    Quand un visiteur recherche "TAJINE EMAILLE"
    Alors le premier résultat est "Tajine de Safi émaillé"

  Scénario: Filtrer par prix
    Quand un visiteur recherche "tapis kilim" entre 1 000,00 MAD et 2 000,00 MAD
    Alors les résultats sont, dans l'ordre : "Kilim tissé Azilal"

  Scénario: Filtrer par catégorie, les facettes comptent toutes les catégories
    Quand un visiteur recherche "argan" dans la catégorie "argan"
    Alors les résultats sont, dans l'ordre : "Huile d'argan bio"
    Et la facette "argan" compte 1 résultat de ce scénario

  Scénario: L'index suit le catalogue (modification, archivage)
    Quand la vendeuse "fatima" change le prix de "Kilim tissé Azilal" à 1 290,00 MAD
    Et un visiteur recherche "kilim"
    Alors le résultat "Kilim tissé Azilal" coûte 1 290,00 MAD
    Quand le vendeur "karim" archive le produit "Tajine de Safi émaillé"
    Et un visiteur recherche "tajine"
    Alors "Tajine de Safi émaillé" ne fait pas partie des résultats
