## Quoi
<!-- Ce que fait la PR, en 2-3 phrases. Lien : Closes #… -->

## Pourquoi
<!-- Le besoin ou le bug ; lien vers la story ou l'ADR. -->

## Comment tester
<!-- Étapes, requêtes, données ; résultat attendu. -->

## Check-list
- [ ] Tests ajoutés ou mis à jour (cas nominal + cas limites)
- [ ] Contrat OpenAPI / AsyncAPI à jour si l'API change
- [ ] Migration EF Core ajoutée si le schéma change (jamais de modification d'une migration existante)
- [ ] Aucun secret, aucune donnée personnelle dans les logs
- [ ] Usage de l'IA consigné dans `docs/ia-journal.md` (le cas échéant)
