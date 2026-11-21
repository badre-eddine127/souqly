# language: fr
@US-18 @SHOULD @admin
Fonctionnalité: Journal d'audit des actions sensibles
  En tant qu'administrateur
  Je veux consulter le journal d'audit des actions sensibles (qui, quoi, quand)
  Afin de pouvoir expliquer toute décision et repérer les abus

  Scénario: Les actions sensibles sont tracées, sans donnée personnelle
    Étant donné que la cliente "nadia" a demandé l'ouverture de la boutique "Atelier Nadia" à "Fès"
    Et que l'administrateur valide la demande de "nadia"
    Quand l'administrateur consulte le journal d'audit
    Alors la réponse a le statut 200
    Et le journal indique que "admin" a validé la demande de "nadia" avec le statut 200
    Et le journal indique que "nadia" a demandé une boutique
    Et le journal ne contient aucune lecture
    Et le journal ne contient ni e-mail ni jeton

  Scénario: Une tentative refusée est tracée elle aussi
    Étant donné que la cliente "nadia" a demandé l'ouverture de la boutique "Atelier Nadia" à "Fès"
    Et que la cliente "leila" essaie de valider la demande de "nadia"
    Quand l'administrateur consulte le journal d'audit
    Alors le journal indique que "leila" a validé la demande de "nadia" avec le statut 403

  Scénario: Seul l'administrateur consulte le journal
    Quand le vendeur "karim" essaie de consulter le journal d'audit
    Alors la réponse a le statut 403
