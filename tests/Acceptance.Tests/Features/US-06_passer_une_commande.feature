# language: fr
@US-06 @US-09 @US-15 @MUST @commande
Fonctionnalité: Passer une commande
  En tant que client
  Je veux transformer mon panier en commande et payer
  Afin de recevoir mes articles

  Le paiement est simulé : un montant au-delà de 10 000,00 MAD est refusé par le prestataire de test.

  Contexte:
    Étant donné que le vendeur "fatima" propose le produit "Tapis berbère 120x80" à 1 200,00 MAD avec 3 unités en stock
    Et que la cliente "sara" a mis 1 "Tapis berbère 120x80" dans son panier

  Scénario: Paiement accepté
    Quand la cliente "sara" valide son panier
    Alors la commande passe au statut "CONFIRMED" en moins de 5 secondes
    Et le stock disponible de "Tapis berbère 120x80" vaut 2
    Et le paiement de la commande est "CAPTURED" pour 1200.00 MAD
    Et "sara" reçoit la notification "Commande confirmée"

  Scénario: Paiement refusé
    Étant donné que le vendeur "fatima" propose le produit "Tapis royal" à 12 000,00 MAD avec 2 unités en stock
    Et que la cliente "sara" a mis 1 "Tapis royal" dans son panier
    Quand la cliente "sara" valide son panier
    Alors la commande passe au statut "REJECTED" avec le motif "PAYMENT_DECLINED"
    Et le stock disponible de "Tapis berbère 120x80" vaut de nouveau 3
    Et le stock disponible de "Tapis royal" vaut de nouveau 2

  Scénario: Double clic (US-09)
    Quand la cliente "sara" envoie deux fois sa commande avec la même clé d'idempotence
    Alors une seule commande existe pour "sara"
    Et la commande passe au statut "CONFIRMED" en moins de 5 secondes
    Et un seul paiement est autorisé

  Scénario: Stock insuffisant
    Étant donné que le stock de "Tapis berbère 120x80" est 0
    Quand la cliente "sara" valide son panier
    Alors la commande passe au statut "REJECTED" avec le motif "OUT_OF_STOCK"
    Et aucun paiement n'est demandé

  Scénario: Le prix est figé au moment de la commande (RG-02)
    Quand la cliente "sara" valide son panier
    Et le vendeur "fatima" change le prix de "Tapis berbère 120x80" à 1 500,00 MAD
    Alors la commande passe au statut "CONFIRMED" en moins de 5 secondes
    Et le total de la commande est 1200.00 MAD
