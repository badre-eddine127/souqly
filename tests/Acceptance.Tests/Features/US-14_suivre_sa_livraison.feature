# language: fr
@US-14 @COULD @livraison
Fonctionnalité: Suivre sa livraison
  En tant que client
  Je veux suivre ma livraison
  Afin de savoir quand mon colis arrive

  Contexte:
    Étant donné que le vendeur "fatima" propose le produit "Pouf livré" à 400,00 MAD avec 2 unités en stock
    Et que la cliente "sara" a mis 1 "Pouf livré" dans son panier
    Et que la cliente "sara" a validé son panier
    Et que la commande est au statut "CONFIRMED"

  Scénario: Du colis expédié au colis livré
    Quand le vendeur "fatima" expédie sa part avec "AMANA" et le suivi "AM555666777MA"
    Alors la commande passe au statut "SHIPPED" en moins de 5 secondes
    Et "sara" voit le colis "AM555666777MA" de "AMANA" au statut "IN_TRANSIT"
    Et "sara" reçoit une notification qui mentionne "AM555666777MA"
    Quand le vendeur "fatima" confirme la livraison
    Alors la commande passe au statut "DELIVERED" en moins de 5 secondes
    Et "sara" voit le colis "AM555666777MA" de "AMANA" au statut "DELIVERED"
    Et "sara" reçoit la notification "Colis livré"
