# language: fr
@US-05 @MUST @panier
Fonctionnalité: Gérer son panier
  En tant que client
  Je veux ajouter, modifier et retirer des articles de mon panier
  Afin de préparer ma commande

  Contexte:
    Étant donné que le vendeur "fatima" propose le produit "Tapis berbère 120x80" à 1 200,00 MAD avec 3 unités en stock
    Et que le vendeur "karim" propose le produit "Huile d'argan 100 ml" à 89,00 MAD avec 10 unités en stock

  Scénario: Ajouter puis retrouver ses articles
    Quand la cliente "sara" met 1 "Tapis berbère 120x80" dans son panier
    Et la cliente "sara" met 2 "Huile d'argan 100 ml" dans son panier
    Alors le panier de "sara" contient 3 articles

  Scénario: Modifier une quantité
    Étant donné que la cliente "sara" a mis 1 "Huile d'argan 100 ml" dans son panier
    Quand la cliente "sara" met 4 "Huile d'argan 100 ml" dans son panier
    Alors le panier de "sara" contient 4 articles

  Scénario: Retirer un article
    Étant donné que la cliente "sara" a mis 1 "Tapis berbère 120x80" dans son panier
    Et que la cliente "sara" a mis 1 "Huile d'argan 100 ml" dans son panier
    Quand la cliente "sara" retire "Tapis berbère 120x80" de son panier
    Alors le panier de "sara" contient 1 articles

  Scénario: Plus de 10 unités par ligne sont refusées (RG-04)
    Quand la cliente "sara" met 11 "Huile d'argan 100 ml" dans son panier
    Alors la réponse a le statut 400

  Scénario: Chaque client a son propre panier
    Étant donné que la cliente "sara" a mis 2 "Huile d'argan 100 ml" dans son panier
    Alors le panier de "nadia" contient 0 articles
