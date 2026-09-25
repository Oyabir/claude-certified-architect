# Message à coller dans Claude Code

Décompresser ce dossier à la racine du dépôt (par exemple dans `docs/redesign/`), puis coller le message ci-dessous dans Claude Code.

---

Je veux appliquer la refonte visuelle de PC Santé à l'application existante. Tout est dans `docs/redesign/` :

- `HANDOFF.md` : la spécification complète. Lis-la entièrement avant de commencer, en particulier le § 0 (règles de travail) et le § 10 (ordre des lots).
- `maquettes/*.png` : les écrans cibles. `maquettes-html/*.html` : les mêmes écrans avec les valeurs exactes.
- `avant/` : captures de l'application actuelle, pour faire la correspondance.
- `tokens/` : couleurs, tailles et polices (XAML pour l'app Windows, CSS pour la console web). `tokens.json` est la source de vérité.
- `assets/` : logo, icône d'application (`.ico`), icônes SVG, polices TTF.

Règles :
1. Commence par explorer le code et me présenter un plan : technologie détectée, fichiers à modifier pour chaque lot du § 10, points bloquants. Attends ma validation avant de modifier quoi que ce soit.
2. Ne change que la présentation. Aucune modification de la logique métier, des services, du calcul du score ni des appels système.
3. Travaille lot par lot, avec un commit par lot. À la fin de chaque lot, compare le résultat à la maquette concernée et liste les écarts.
4. N'invente aucune donnée : si une maquette affiche une information que le code ne fournit pas, masque l'élément et note-le (§ 11).
5. Termine chaque lot par la liste de contrôle du § 12 pour les écrans concernés.

Commence par l'exploration et le plan.
