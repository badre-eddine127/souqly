# language: fr
@US-17 @COULD @admin
Fonctionnalité: Tableau de bord vendeur
  En tant que vendeur
  Je veux un tableau de bord : ventes du jour, commandes à préparer, ruptures
  Afin de savoir en un coup d'œil ce que je dois faire aujourd'hui

  Contexte:
    Étant donné que la vendeuse "fatima" propose le produit "Tapis du jour" à 1 200,00 MAD avec 10 unités en stock
    Et que la vendeuse "fatima" propose le produit "Pouf presque épuisé" à 300,00 MAD avec 2 unités en stock
    Et que le vendeur "karim" propose le produit "Argan du jour" à 150,00 MAD avec 10 unités en stock
    Et que la cliente "sara" a mis 2 "Tapis du jour" dans son panier
    Et que la cliente "sara" a mis 1 "Argan du jour" dans son panier
    Et que la cliente "sara" a validé son panier
    Et que la commande est au statut "CONFIRMED"

  Scénario: Ventes du jour, commandes à préparer et stocks bas
    Quand la vendeuse "fatima" ouvre son tableau de bord
    Alors la réponse a le statut 200
    Et le tableau de bord indique 1 commande payée, 2 unités vendues et 2 400,00 MAD de ventes du jour
    Et 1 commande est à préparer
    Et "Pouf presque épuisé" est signalé en stock bas avec 2 disponibles
    Et "Tapis du jour" n'est pas signalé en stock bas

  Scénario: Une commande expédiée n'est plus à préparer mais reste dans les ventes
    Étant donné que la vendeuse "fatima" a expédié sa part avec "AMANA" et le suivi "AM123456789MA"
    Quand la vendeuse "fatima" ouvre son tableau de bord
    Alors 0 commande est à préparer
    Et le tableau de bord indique 1 commande payée, 2 unités vendues et 2 400,00 MAD de ventes du jour

  Scénario: Le tableau de bord est réservé aux vendeurs
    Quand la cliente "sara" essaie d'ouvrir le tableau de bord vendeur
    Alors la réponse a le statut 403
