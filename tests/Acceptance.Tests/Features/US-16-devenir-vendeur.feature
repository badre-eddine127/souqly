# language: fr
@US-16 @SHOULD @admin
Fonctionnalité: Devenir vendeur
  En tant que visiteur, je veux demander à devenir vendeur
  En tant qu'administrateur, je veux valider ou refuser la demande
  Afin que seuls des artisans vérifiés vendent sur Souqly

  Scénario: Une demande validée donne le rôle vendeur et une boutique publique
    Quand la cliente "nadia" demande l'ouverture de la boutique "Atelier Nadia" à "Fès"
    Alors la réponse a le statut 201
    Et la demande de "nadia" est "en attente"
    Quand l'administrateur valide la demande de "nadia"
    Alors la réponse a le statut 200
    Et la demande de "nadia" est "validée"
    Et "nadia" a reçu le rôle vendeur
    Et la boutique "Atelier Nadia" de "nadia" apparaît parmi les boutiques

  Scénario: Un refus est motivé et la demande peut être soumise à nouveau
    Étant donné que la cliente "nadia" a demandé l'ouverture de la boutique "Atelier Nadia" à "Fès"
    Quand l'administrateur refuse la demande de "nadia" pour le motif "Décrivez vos produits"
    Alors la demande de "nadia" est "refusée"
    Et "nadia" n'a pas reçu le rôle vendeur
    Quand la cliente "nadia" demande l'ouverture de la boutique "Atelier Nadia" à "Fès"
    Alors la réponse a le statut 200
    Et la demande de "nadia" est "en attente"

  Scénario: Un refus sans motif est rejeté
    Étant donné que la cliente "nadia" a demandé l'ouverture de la boutique "Atelier Nadia" à "Fès"
    Quand l'administrateur refuse la demande de "nadia" sans motif
    Alors la réponse a le statut 400
    Et la réponse est un ProblemDetail
    Et la demande de "nadia" est "en attente"

  Scénario: Une demande déjà en attente ne peut pas être doublée
    Étant donné que la cliente "nadia" a demandé l'ouverture de la boutique "Atelier Nadia" à "Fès"
    Quand la cliente "nadia" demande l'ouverture de la boutique "Atelier Nadia bis" à "Meknès"
    Alors la réponse a le statut 409

  Scénario: Seul un administrateur décide
    Étant donné que la cliente "nadia" a demandé l'ouverture de la boutique "Atelier Nadia" à "Fès"
    Quand la cliente "leila" essaie de valider la demande de "nadia"
    Alors la réponse a le statut 403
    Et la demande de "nadia" est "en attente"
    Et "nadia" n'a pas reçu le rôle vendeur
