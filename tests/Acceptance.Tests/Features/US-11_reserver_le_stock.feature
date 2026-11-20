# language: fr
@US-11 @RG-01 @MUST @stock
Fonctionnalité: Réserver le stock pendant la commande
  En tant que plateforme
  Je veux réserver le stock le temps du paiement
  Afin de ne jamais vendre plus que ce qui existe

  Scénario: Vente flash : jamais de survente
    Étant donné que le vendeur "karim" propose le produit "Lampe en cuivre" à 350,00 MAD avec 5 unités en stock
    Quand 20 clients commandent chacun 1 "Lampe en cuivre" en même temps
    Alors 5 commandes sont "CONFIRMED"
    Et 15 commandes sont "REJECTED" avec le motif "OUT_OF_STOCK"
    Et le stock de "Lampe en cuivre" vaut 0 en main, 0 réservé et 0 disponible

  Scénario: Le stock reste réservé tant que le paiement n'est pas fait
    Étant donné que le vendeur "fatima" propose le produit "Miroir en os" à 900,00 MAD avec 2 unités en stock
    Et que le service de paiement est arrêté
    Et que la cliente "sara" a mis 1 "Miroir en os" dans son panier
    Quand la cliente "sara" valide son panier
    Alors la commande passe au statut "STOCK_RESERVED" en moins de 5 secondes
    Et le stock de "Miroir en os" vaut 2 en main, 1 réservé et 1 disponible
    Quand le service de paiement redémarre
    Alors la commande passe au statut "CONFIRMED" en moins de 5 secondes
