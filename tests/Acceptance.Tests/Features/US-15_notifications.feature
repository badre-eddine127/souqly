# language: fr
@US-15 @SHOULD @notification
Fonctionnalité: Être prévenu à chaque étape
  En tant que client ou vendeur
  Je veux recevoir un message à chaque étape clé d'une commande
  Afin de ne rien manquer

  Scénario: Chaque vendeur n'est informé que de ses propres lignes (RG-05)
    Étant donné que le vendeur "fatima" propose le produit "Tapis berbère" à 1 200,00 MAD avec 5 unités en stock
    Et que le vendeur "karim" propose le produit "Huile d'argan" à 189,90 MAD avec 10 unités en stock
    Et que la cliente "sara" a mis 2 "Tapis berbère" dans son panier
    Et que la cliente "sara" a mis 1 "Huile d'argan" dans son panier
    Quand la cliente "sara" valide son panier
    Alors la commande passe au statut "CONFIRMED" en moins de 5 secondes
    Et le total de la commande est 2589.90 MAD
    Et "fatima" reçoit une notification qui mentionne "2 × Tapis berbère"
    Et la notification de "fatima" ne mentionne pas "argan"
