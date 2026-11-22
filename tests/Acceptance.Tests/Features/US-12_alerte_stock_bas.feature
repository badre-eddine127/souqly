# language: fr
@US-12 @COULD @stock
Fonctionnalité: Être alerté quand le stock passe sous un seuil
  En tant que vendeur
  Je veux être alerté quand un produit passe sous un seuil de stock
  Afin de réapprovisionner avant la rupture

  Scénario: Une commande fait passer le stock sous le seuil
    Étant donné que la vendeuse "fatima" propose le produit "Pouf du seuil" à 300,00 MAD avec 5 unités en stock
    Et que la cliente "sara" a mis 3 "Pouf du seuil" dans son panier
    Quand la cliente "sara" valide son panier
    Alors la commande passe au statut "CONFIRMED" en moins de 5 secondes
    Et "fatima" est alertée que "Pouf du seuil" est en stock bas avec 2 disponibles
    Et l'alerte de "fatima" nomme le produit "Pouf du seuil"

  Scénario: Une seule alerte par passage sous le seuil
    Étant donné que la vendeuse "fatima" propose le produit "Babouches du seuil" à 250,00 MAD avec 10 unités en stock
    Et que la cliente "sara" a mis 8 "Babouches du seuil" dans son panier
    Et que la cliente "sara" a validé son panier
    Et que la commande est au statut "CONFIRMED"
    Quand la vendeuse "fatima" ajuste le stock de "Babouches du seuil" de -1 pour le motif "Casse"
    Alors "fatima" est alertée que "Babouches du seuil" est en stock bas avec 2 disponibles
    Et "fatima" a reçu exactement 1 alerte de stock pour "Babouches du seuil"

  Scénario: La rupture est signalée
    Étant donné que la vendeuse "fatima" propose le produit "Lanterne du seuil" à 540,00 MAD avec 2 unités en stock
    Et que la vendeuse "fatima" a fixé l'alerte de stock de "Lanterne du seuil" à 0
    Quand la vendeuse "fatima" ajuste le stock de "Lanterne du seuil" de -1 pour le motif "Inventaire"
    Alors "fatima" a reçu exactement 0 alerte de stock pour "Lanterne du seuil"
    Quand la vendeuse "fatima" ajuste le stock de "Lanterne du seuil" de -1 pour le motif "Casse"
    Alors "fatima" est alertée de la rupture de "Lanterne du seuil"
