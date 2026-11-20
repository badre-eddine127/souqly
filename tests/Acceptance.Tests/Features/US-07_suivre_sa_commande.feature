# language: fr
@US-07 @MUST @commande
Fonctionnalité: Suivre le statut de sa commande
  En tant que client
  Je veux suivre le statut de ma commande
  Afin de savoir où elle en est

  Contexte:
    Étant donné que le vendeur "fatima" propose le produit "Coussin brodé" à 250,00 MAD avec 5 unités en stock
    Et que la cliente "sara" a mis 2 "Coussin brodé" dans son panier

  Scénario: Le client voit sa commande passer de PENDING à CONFIRMED
    Quand la cliente "sara" valide son panier
    Alors la commande est d'abord au statut "PENDING"
    Et la commande passe au statut "CONFIRMED" en moins de 5 secondes
    Et "sara" retrouve la commande dans la liste de ses commandes

  Scénario: Un client ne voit pas la commande d'un autre
    Quand la cliente "sara" valide son panier
    Et la cliente "nadia" consulte la commande de "sara"
    Alors la réponse a le statut 404
