# language: fr
@US-10 @MUST @stock
Fonctionnalité: Déclarer et ajuster son stock
  En tant que vendeur
  Je veux déclarer et ajuster mon stock par produit
  Afin que les clients ne commandent que ce qui est disponible

  Contexte:
    Étant donné que le vendeur "karim" publie le produit "Théière en argent" à 520,00 MAD dans la catégorie "artisanat"

  Scénario: Déclarer puis ajuster le stock
    Quand le vendeur "karim" déclare 10 unités de "Théière en argent"
    Et le vendeur "karim" ajuste le stock de "Théière en argent" de -3 pour le motif "casse"
    Alors la réponse a le statut 200
    Et le stock disponible de "Théière en argent" vaut 7

  Scénario: Le stock ne peut pas devenir négatif (RG-01)
    Étant donné que le vendeur "karim" a déclaré 2 unités de "Théière en argent"
    Quand le vendeur "karim" ajuste le stock de "Théière en argent" de -5 pour le motif "inventaire"
    Alors la réponse a le statut 422
    Et le stock disponible de "Théière en argent" vaut 2

  # Le stock d'un autre vendeur est traité comme inexistant (404) : on ne révèle pas ce qui ne nous appartient pas.
  Scénario: Un vendeur ne touche pas au stock d'un autre
    Étant donné que le vendeur "karim" a déclaré 2 unités de "Théière en argent"
    Quand le vendeur "fatima" déclare 100 unités de "Théière en argent"
    Alors la réponse a le statut 404
    Et le stock disponible de "Théière en argent" vaut 2
