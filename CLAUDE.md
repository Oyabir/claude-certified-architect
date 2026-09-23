# PC Santé — application Windows et serveur de licences

**Source de vérité unique : [`docs/CAHIER_DES_CHARGES.md`](docs/CAHIER_DES_CHARGES.md).** En cas de doute, relire le cahier des charges ; ce fichier n'en est qu'un résumé.

- Avancement : [`docs/PLAN.md`](docs/PLAN.md) (fait / en cours / à faire). Pour reprendre : « Reprends le chantier à partir de PLAN.md ».
- Décisions prises seul : [`docs/DECISIONS.md`](docs/DECISIONS.md) (date, sujet, option, raison — une ligne chacune).

## Règles de travail (section 9)

1. Lire tout le cahier des charges avant d'écrire du code.
2. Tenir `docs/PLAN.md` à jour : lots, ordre, critères de fin.
3. Chaque décision prise seul → une ligne dans `docs/DECISIONS.md`.
4. Avancer lot par lot, dans l'ordre du planning (section 7). Un lot est terminé seulement s'il compile **sans avertissement** et que ses tests passent.
5. Commit Git à la fin de chaque étape cohérente, message clair **en français**.
6. Blocage : au moins deux approches, puis contourner, documenter dans `DECISIONS.md`, continuer. Ne jamais attendre une réponse.
7. Fonction impossible proprement → désactivée dans l'interface + signalée dans `docs/RAPPORT_FINAL.md`.
8. Aucune fonctionnalité hors cahier des charges. Périmètre = MVP (section 7) + serveur de licences (section 12).
9. Ne jamais réduire la sécurité (section 5) pour gagner du temps.

**Interdits sans accord** : dépenser de l'argent / service payant ; publier en ligne ; modifier la machine de dev au-delà des outils de dev ; exécuter des optimisations ou actions système destructives sur la machine hôte (tests par simulations + checklist VM `docs/TESTS_MANUELS_VM.md`) ; mettre une clé privée ou un secret dans le dépôt.

## Décisions par défaut (section 10)

| Sujet | Décision |
| --- | --- |
| Nom | « PC Santé », centralisé dans `branding.props` (un seul fichier) |
| Interface | WPF .NET 8 + WPF-UI (style Windows 11) |
| Langues | Français par défaut, anglais, arabe (RTL) ; textes en fichiers de ressources, aucun texte en dur |
| Base locale | SQLite via EF Core |
| Serveur de licences | ASP.NET Core Minimal API + SQLite (portable PostgreSQL) |
| Installeur | WiX Toolset (MSI), prêt pour la signature |
| PDF | QuestPDF (Community) |
| Journalisation | Serilog, fichiers tournants |
| Tests | xUnit + FluentAssertions |
| Assistant IA | Hors MVP : abstraction seulement (`IAiAssistant`) |
| Console PME | Hors MVP |

**Règle d'arbitrage (dans l'ordre)** : 1. le cahier des charges ; 2. la sécurité de l'utilisateur et de son PC ; 3. la simplicité pour un non-informaticien ; 4. la fiabilité (solution éprouvée) ; 5. le délai. Doute sur une action système → option la plus prudente et réversible.

## Exigences de code (sections 5 et 13)

- Service SYSTEM : **liste fermée de commandes** (`CommandId` + catalogue), aucun script arbitraire, paramètres validés ; named pipe protégé par ACL, client signé vérifié ; **journal d'audit** (qui, quoi, quand, résultat).
- Toute action système suit le cycle : **vérifier → point de restauration ou sauvegarde → exécuter → contrôler → journaliser** (`SystemActionPipeline`).
- Chaque accès à Windows passe par une interface (simulable dans les tests).
- Zéro avertissement (`TreatWarningsAsErrors`), analyseurs .NET activés. Aucun secret dans le code.
- Licence : empreinte SHA-256 (4 éléments, tolérance 3/4), jeton signé Ed25519 (clé privée uniquement côté serveur, via variable d'environnement), DPAPI machine, hors ligne 14 jours, détection du recul d'horloge, 2 transferts/an.

## Règles UX (section 11)

- Un seul bouton principal par écran, grand et coloré.
- Vocabulaire simple, terme technique en petit dessous (« Protection contre les intrusions » / pare-feu).
- Chaque problème : ce qui ne va pas, pourquoi c'est important, bouton « Corriger ».
- Couleurs constantes : vert = OK, orange = à surveiller, rouge = à corriger.
- Jamais de code d'erreur brut : message clair + action, détail technique derrière « Détails ».
- Confirmation avant toute action qui ferme un programme, redémarre le PC ou supprime des fichiers.
- Après chaque action : message de résultat clair, jamais de silence.
- **Mode Simple par défaut** (Accueil, Protection, Nettoyage, Rapports) ; Mode Avancé dans les Paramètres.
- Premier lancement en 3 étapes max : langue, licence, première analyse ; puis proposer les 3 tâches planifiées.
- Texte ≥ 14 px, contraste élevé, thème clair/sombre, clavier + Narrateur, arabe en RTL.

## Commandes

```bash
dotnet build PcSante.sln -c Release      # compile tout (installeur WiX : Windows uniquement)
dotnet test PcSante.sln -c Release       # tests automatisés
```

Voir `docs/GUIDE_DEPLOIEMENT.md` pour l'installeur et le serveur de licences.

---

## Contenu historique du dépôt (cours « Claude Certified Architect »)

Les fichiers Python à la racine (`agent.py`, `capstone_project.py`, etc.) et `docs/agent-guide.md` sont des supports de cours antérieurs, indépendants de PC Santé. Ne pas les modifier dans le cadre du chantier. Leurs conventions propres : modèle `claude-haiku-4-5`, schéma d'erreurs à quatre catégories (`transient`, `permission`, `validation`, `internal`), sortie de boucle sur `stop_reason == "end_turn"`, données fictives uniquement.
