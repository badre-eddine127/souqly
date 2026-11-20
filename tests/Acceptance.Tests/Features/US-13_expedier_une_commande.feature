# language: fr
@US-13 @SHOULD @livraison
Fonctionnalité: Préparer et expédier les commandes
  En tant que vendeur
  Je veux voir les commandes payées à préparer et les marquer expédiées
  Afin que mes clients sachent quand leur colis part

  Contexte:
    Étant donné que le vendeur "fatima" propose le produit "Tapis à livrer" à 900,00 MAD avec 3 unités en stock
    Et que le vendeur "karim" propose le produit "Argan à livrer" à 120,00 MAD avec 3 unités en stock
    Et que la cliente "sara" a mis 1 "Tapis à livrer" dans son panier
    Et que la cliente "sara" a mis 1 "Argan à livrer" dans son panier
    Et que la cliente "sara" a validé son panier
    Et que la commande est au statut "CONFIRMED"

  Scénario: Le vendeur voit seulement ses lignes à préparer
    Quand le vendeur "fatima" consulte ses commandes à préparer
    Alors il voit la commande avec seulement "Tapis à livrer"

  Scénario: La commande est expédiée quand chaque vendeur a expédié sa part
    Quand le vendeur "fatima" expédie sa part avec "AMANA" et le suivi "AM123456789MA"
    Alors la réponse a le statut 200
    Et la commande est au statut "CONFIRMED"
    Quand le vendeur "karim" expédie sa part avec "CTM" et le suivi "CTM-998877"
    Alors la commande passe au statut "SHIPPED" en moins de 5 secondes

  Scénario: Un transporteur inconnu est refusé
    Quand le vendeur "fatima" expédie sa part avec "PIGEON" et le suivi "AM123456789MA"
    Alors la réponse a le statut 400
    Et la réponse est un ProblemDetail

  Scénario: Une part ne s'expédie qu'une fois
    Étant donné que le vendeur "fatima" a expédié sa part avec "AMANA" et le suivi "AM123456789MA"
    Quand le vendeur "fatima" expédie sa part avec "AMANA" et le suivi "AM000000000MA"
    Alors la réponse a le statut 409
