# language: fr
@US-19 @MUST @observabilite
Fonctionnalité: Suivre une commande à travers tous les services
  En tant qu'exploitant
  Je veux suivre une commande à travers tous les services grâce à un identifiant de trace unique
  Afin de diagnostiquer un incident rapidement

  Scénario: Une seule trace de la requête HTTP à la confirmation
    Étant donné que le vendeur "fatima" propose le produit "Tapis tracé" à 300,00 MAD avec 1 unités en stock
    Et que la cliente "sara" a mis 1 "Tapis tracé" dans son panier
    Quand la cliente "sara" valide son panier
    Alors la commande passe au statut "CONFIRMED" en moins de 5 secondes
    Et les messages de la commande sont, dans l'ordre, "OrderPlaced, StockReserved, PaymentRequested, PaymentSucceeded, OrderConfirmed"
    Et tous ces messages portent le même identifiant de trace
