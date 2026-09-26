# Refonte visuelle — compte rendu (HANDOFF § 0.6)

25/09/2026 · branche `claude/refonte-visuelle` · un commit par lot (lots 1 à 11).

Présentation seulement : services, calcul du score, catalogue de commandes, appels Windows, licences et échanges avec la console sont inchangés. Seules exceptions assumées côté application, toutes de présentation : une case par correction dans « Tout corriger » (décision du commanditaire), la création des 3 tâches en fin de premier lancement seulement si l'interrupteur a été activé (décision du commanditaire), et des lectures déjà existantes du catalogue affichées sur l'Accueil (nom du PC, tâches planifiées, journal).

Vérification : 385 tests automatisés verts, compilation sans avertissement. Les écrans ont été comparés aux maquettes par captures, avec le mode `--apercu` (données fictives, compilé en Debug seulement, absent de la version livrée). La console a été vérifiée dans Edge sans interface, avec sa base de démonstration.

## Écrans modifiés

| Lot | Écrans | Maquette |
|---|---|---|
| 1 | Jetons clair/sombre, polices embarquées, nouvelle icône (application et mini-affichage), accent Windows remplacé par la couleur de marque | 00 |
| 2 | Composants : boutons (principal, secondaire, discret, destructif, Nuit ; chargement), puces de statut, étiquettes, tuiles d'icône, anneau de score, sous-scores, lignes de problème/réglage, interrupteurs, onglets segmentés, filtres, recherche, barres, tableaux. Galerie Debug (`--galerie`) | 00 |
| 3 | Coque : barre de titre 40 px, barre latérale ESSENTIEL/AVANCÉ, Licence et Paramètres au même style, messages de résultat (bandeau et message court de réussite) | 11 |
| 4 | Accueil | 01 |
| 5 | Protection, Optimisation (4 onglets), Système | 02, 03, 04 |
| 6 | Performance, thème sombre | 05 |
| 7 | Processus, Sessions, Planification, Rapports, Licence, Paramètres | § 6.7 |
| 8 | Premier lancement | 07, 08, 09 |
| 9 | Arabe sur les 11 écrans et le premier lancement | 06 |
| 10 | Console PME : connexion, en-tête, postes, détail, alertes, rapports, organisation | 10 |
| 11 | Formats de nombres et de dates, revue d'accessibilité | § 12 |

## Écarts restants avec les maquettes

- **Texte à 14 px au minimum.** Les tailles 11, 12 et 13 px des maquettes sont relevées à 14 px (cahier des charges § 11, prioritaire). Les cartes sont donc un peu plus hautes. Pour que Système tienne à 1440 × 940, les descriptions des tuiles ont été raccourcies (texte complet en infobulle).
- **Seuils du score.** Ce sont ceux du code (vert à partir de 80, orange à partir de 50), pas 85 et 60 comme dans les maquettes (§ 3.2). Ils s'affichent dans la légende du premier lancement et dans la console.
- **Un seul bouton principal par écran** (cahier des charges § 11). Sur l'Accueil, les boutons des lignes de problème sont secondaires, y compris « Nettoyer 1,2 Go » que la maquette montre en principal. Le bouton d'un problème qui ouvre un écran annonce ce qu'on y fera (« Libérer de l'espace », « Choisir quoi nettoyer ») au lieu de lancer le nettoyage : le problème correspondant ne porte pas de commande.
- **Premier lancement.** Étape 1 : langue (français, anglais, arabe) et mode Simple/Avancé. Étape 2 : licence (décision du commanditaire). Étape 3 : analyse. L'interrupteur « veiller automatiquement » est désactivé par défaut (décision du commanditaire).
- **Langues.** L'anglais est conservé (trois tuiles au lieu de deux).
- **Performance.** Le rafraîchissement reste à 2 s pour garder une consommation processeur basse, donc l'historique de 60 points couvre 2 minutes (le sous-titre le dit). Il est collecté à l'ouverture de l'écran : « Collecte des données… » s'affiche d'abord.
- **Réglages Windows.** Les modes d'alimentation restent une liste avec un bouton « Utiliser » plutôt qu'une liste déroulante, pour ne pas déclencher une action système au simple changement de sélection.
- **Paramètres.** L'enregistrement reste explicite : changer la langue ou l'apparence recharge la fenêtre.
- **Console.** « Ajouter un poste » ouvre Organisation, où se trouve le code d'inscription (il n'y a pas de fonction d'ajout côté console). Le sélecteur de langue affiche « FR · Français » plutôt que « FR » seul, pour les lecteurs d'écran.
- **Transitions de couleur de 120 ms.** Elles ne sont pas animées dans l'application : les états changent instantanément. L'anneau et les indicateurs d'activité sont animés, et ces animations se coupent quand Windows désactive les animations.
- **Mini-affichage** (fenêtre Win32 séparée). Seule son icône change ; il n'a pas de maquette.

## Données supposées par les maquettes et absentes (§ 11), éléments masqués

| Élément | Traitement |
|---|---|
| Durée estimée de « Tout corriger » (« environ 2 minutes ») | Masquée : la ligne dit seulement « n points à regarder · rien n'est supprimé sans votre accord ». |
| Phrase de conclusion | Construite à partir des catégories des problèmes réellement détectés. |
| « Tout le reste va bien » : contrôles précis (« Pare-feu activé sur tous les réseaux »…) | Remplacé par un message par catégorie sans problème (« Protection : tout est en ordre »). |
| Nombre de mises à jour en attente (Système) | Non fourni par le service. Le bandeau dit « Mises à jour vérifiées récemment » ou « à vérifier », jamais « Windows est à jour ». |
| Mot « Normale / Élevée » sous la température | Masqué, faute de seuil fourni par le code. La valeur et la barre sont affichées. |
| Historique déjà collecté par le service (Performance) | Non fourni : l'historique est collecté à l'ouverture de l'écran. |
| Édition de Windows par poste dans le tableau de la console | Absente de la liste des postes : affichée seulement dans le détail d'un poste. |
| Nombre de licences de l'organisation | Fourni (sièges) et affiché : « 4 postes sur 5 licences ». |

## Liste de contrôle § 12

| Critère | État |
|---|---|
| Aucune couleur en dur dans les écrans, aucune référence à l'accent Windows | ✅ Tout passe par les pinceaux `Pcs…Brush` et les variables `--pcs-*`. Le blanc des interrupteurs et du texte des cartes Nuit est lui aussi un jeton. L'accent WPF-UI est forcé sur la couleur de marque. |
| Chaque statut combine icône, mot et couleur | ✅ Puces de statut, états Activée/Désactivée, réputation, résultats du journal, anneau et libellé. |
| Chaque problème de l'Accueil a un bouton au verbe explicite | ✅ Plus aucun « Voir » seul (« Libérer de l'espace », « Créer un point de restauration »…). |
| Nombres et dates au format de la langue | ✅ 28,7 Go, dates relatives (« aujourd'hui à 15:20 »), heures sans secondes, chiffres latins en arabe (application et console). |
| Pas de défilement à 1440 × 940 sur Accueil, Protection, Système, Performance | ✅ Vérifié avec les données d'aperçu. L'Accueil peut défiler au-delà de 3 problèmes. |
| Clavier et focus visible | ✅ Anneau de focus de 2 px sur les composants. Interrupteurs en Espace ou Entrée, onglets aux flèches, lignes de tableau de la console avec Entrée ou Espace. Noms accessibles sur les boutons à icône seule (fermer, « ⋯ », interrupteurs). |
| Contrastes ≥ 4,5:1 | ✅ Les valeurs de `tokens.json` sont inchangées. Le texte orange utilise `WarnText`. |
| Mode Simple : groupe Avancé masqué, choix du premier lancement synchronisé avec Paramètres | ✅ |
| Arabe : miroir, anneau antihoraire, chiffres latins isolés, aucun texte français | ✅ Voir `DECISIONS.md` pour l'isolement des fragments latins. Les textes arabes sont à faire relire par un arabophone (§ 7). |
| Thème sombre : tous les écrans lisibles, aucun fond blanc | ✅ Le fond de la zone de contenu est peint avec le jeton (WPF-UI laissait un gris #202020). |
| Animations coupées quand Windows les désactive | ✅ `SystemParameters.ClientAreaAnimation`. |
| Console : en-tête sur une ligne à 1280 px en français et en arabe, lignes cliquables | ✅ |

## Captures

Elles sont dans `%LOCALAPPDATA%\PcSante-dev\captures-refonte\` : `clair-*`, `sombre-*`, `simple-*`, `arabe-*`, `premier-lancement-*`, `galerie-*` et `console-r-*`. Elles ne sont pas versionnées, car elles contiennent des données d'aperçu.
