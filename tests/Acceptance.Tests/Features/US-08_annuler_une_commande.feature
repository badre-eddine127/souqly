# language: fr
@US-08 @SHOULD @commande
Fonctionnalité: Annuler une commande
  En tant que client
  Je veux annuler une commande tant qu'elle n'est pas expédiée et être remboursé
  Afin de changer d'avis sans risque

  Contexte:
    Étant donné que le vendeur "fatima" propose le produit "Tapis kilim" à 750,00 MAD avec 4 unités en stock
    Et que la cliente "sara" a mis 3 "Tapis kilim" dans son panier
    Et que la cliente "sara" a validé son panier
    Et que la commande est au statut "CONFIRMED"

  Scénario: Annulation après paiement : remise en stock et remboursement
    Quand la cliente "sara" annule sa commande
    Alors la réponse a le statut 200
    Et la commande passe au statut "CANCELLED" en moins de 5 secondes
    Et le stock disponible de "Tapis kilim" vaut de nouveau 4
    Et le paiement de la commande est "REFUNDED" pour 2250.00 MAD
    Et "sara" reçoit la notification "Remboursement effectué"

  Scénario: Une commande expédiée ne peut plus être annulée
    Étant donné que le vendeur "fatima" a expédié sa part avec "AMANA" et le suivi "AM123456789MA"
    Quand la cliente "sara" annule sa commande
    Alors la réponse a le statut 409
