# PC Santé — Dossier de refonte visuelle (handoff développeurs)

Version 1.0 — 25/09/2026 · Application Windows + console web PME

Ce dossier décrit **la nouvelle interface de PC Santé** et comment l'appliquer **au code existant**. Il ne change pas le fonctionnement de l'application : il change ce que l'utilisateur voit et la façon dont les actions sont présentées. Chaque valeur (couleur, taille, espacement) est donnée dans `tokens/` et chaque écran cible est dans `maquettes/`.

---

## 0. Instructions pour Claude Code (à lire en premier)

1. **Explorer avant de modifier.** Identifier la technologie de l'application Windows (WinUI 3, WPF, WinForms, Electron/WebView2…) et celle de la console web (framework, CSS). Repérer : le dictionnaire de ressources ou thème global, la fenêtre principale et sa navigation, chaque page (Accueil, Protection, Optimisation, Performance, Processus, Système, Sessions, Planification, Rapports, Licence, Paramètres), le parcours de premier lancement, les fichiers de langue (FR / AR).
2. **Ne toucher qu'à la présentation.** Services, analyse du score, appels Windows (Defender, pare-feu, SFC/DISM, tâches planifiées, etc.), licences et communication avec la console restent tels quels. Si une maquette affiche une donnée que le code ne fournit pas encore, ne pas l'inventer : laisser l'emplacement masqué et le signaler dans le compte rendu (voir § 11).
3. **Travailler par lots, dans l'ordre du § 10**, avec un commit par lot, et vérifier chaque lot par rapport à la maquette correspondante (`maquettes/*.png`). Les valeurs exactes sont lisibles dans `maquettes-html/*.html` (styles en ligne) ; ces fichiers sont des **références visuelles**, pas du code à copier dans l'app Windows.
4. **Tout passe par les jetons.** Aucune couleur en dur dans les écrans : utiliser les ressources `Pcs…Brush` (XAML) ou les variables `--pcs-…` (CSS). Supprimer toute dépendance à la couleur d'accent Windows (`SystemAccentColor` et dérivés).
5. **Conserver les clés de traduction existantes** ; ajouter les nouvelles chaînes dans les fichiers FR et AR (textes fournis au § 7 et dans les maquettes).
6. **Compte rendu final** : liste des écrans modifiés, écarts restants avec la maquette, données manquantes, captures après modification si l'environnement le permet.

---

## 1. Contenu du dossier

| Dossier / fichier | Contenu |
|---|---|
| `maquettes/` | 12 écrans cibles en PNG (×2, 2880 × 1880). `00` identité et composants → `11` barre latérale. |
| `maquettes-html/` | Les mêmes écrans en HTML statique : valeurs exactes (px, couleurs, graisses) lisibles dans le code. |
| `avant/` | Captures de l'application actuelle (25/09/2026), pour faire la correspondance avant → après. |
| `tokens/tokens.json` | **Source unique** : couleurs clair/sombre + usage, typographie, espacements, rayons, tailles, animations, seuils du score, contrôles de contraste. |
| `tokens/PcSante.Colors.Light.xaml` / `.Dark.xaml` | Couleurs et pinceaux (`PcsColorX`, `PcsXBrush`) — dictionnaires plats compatibles WPF et WinUI 3. |
| `tokens/PcSante.Metrics.xaml` | Rayons, tailles, espacements, tailles de police, familles de polices. |
| `tokens/pcsante-tokens.css` | Variables CSS clair/sombre + classes de base (boutons, puces, cartes, tableau, navigation) pour la console web. |
| `assets/logo/` | Logo (SVG vectoriel, texte vectorisé), icône d'application PNG 16 → 512, `pcsante.ico` multi-tailles. |
| `assets/icons/` | 38 icônes SVG au trait (24 × 24, `currentColor`) + `icons.json` (tracés, réutilisables en `PathIcon`/`Path.Data`). |
| `assets/fonts/` | Plus Jakarta Sans (400–800) et IBM Plex Sans Arabic (400–700) en TTF, licence OFL (redistribution libre). |

---

## 2. Principes (à respecter partout)

1. **Un écran répond à une question.** Pas de longues pages : regrouper, utiliser des onglets ou des tuiles.
2. **Chaque problème a son bouton.** Le libellé du bouton dit ce qui va se passer (« Nettoyer 1,2 Go », pas « Voir »).
3. **Statut = icône + mot + couleur.** Jamais la couleur seule (daltonisme, impression, arabe).
4. **La couleur de marque (`Brand`) est réservée aux actions et à la navigation.** Vert / orange / rouge sont réservés à l'état du PC.
5. **Rassurer.** Mentionner la durée, la réversibilité (« peut être annulé », « point de restauration créé avant ») et ce qui n'est pas touché.
6. **Format local.** Nombres, tailles, dates et heures formatés avec la culture de l'interface : `28,7 Go` en FR (corrige le bug actuel `28.7 Go`), `15:20`, `24/09/2026`.

---

## 3. Jetons de design

### 3.1 Couleurs (extrait — liste complète dans `tokens.json`)

| Jeton | Clair | Sombre | Usage |
|---|---|---|---|
| `Background` | `#F4F6F5` | `#0C1412` | Fond de la zone de contenu |
| `Surface` | `#FFFFFF` | `#141F1C` | Cartes |
| `SurfaceSubtle` | `#F7F9F8` | `#17221F` | Tuiles internes |
| `Sidebar` | `#FBFCFB` | `#101816` | Barre latérale + barre de titre |
| `Border` / `Divider` | `#E2E7E5` / `#EDF1EF` | `#23302D` / `#1E2A27` | Bordures / séparateurs de lignes |
| `TextPrimary` | `#102321` | `#E6EEEC` | Titres |
| `TextSecondary` | `#465956` | `#BCCAC6` | Descriptions |
| `TextMuted` | `#5E6D68` | `#93A39F` | Métadonnées |
| `Brand` | `#0B6E69` | `#4FC7BC` | Bouton principal, nav active, liens |
| `BrandHover` / `BrandPressed` | `#084F4B` / `#063E3B` | `#7FDCD2` / `#A6E8E0` | États du bouton principal |
| `BrandSoft` | `#E2F0EE` | `#15332F` | Fond nav active, tuile d'icône de marque |
| `Night` | `#0B3F3C` | `#0B3F3C` | Panneaux de marque (Veille auto, onboarding) |
| `Good` / `GoodText` / `GoodSoft` | `#1D7A48` / `#17663B` / `#E3F3EA` | `#5CC98A` / `#7FD9A3` / `#16301F` | « Tout va bien » |
| `Warn` / `WarnText` / `WarnSoft` | `#C06A0B` / `#8A4705` / `#FBEEDC` | `#F2A94B` / `#F6C27A` / `#3A2A14` | « À surveiller » |
| `Critical` / `CriticalText` / `CriticalSoft` | `#B42318` / `#9A1D13` / `#FBE6E3` | `#F07A6E` / `#F5A097` / `#3A1A17` | « À corriger » |

**Contrastes vérifiés** (`tokens.json → contrastChecks`) : tous les couples texte/fond ≥ 4,5:1 en clair et en sombre. `Warn` sur `Surface` (3,96:1) est réservé aux **graphiques** (anneau, barres) ; pour du texte orange, utiliser `WarnText`.

### 3.2 Seuils du score (identiques app + console)

| Score | Libellé FR | Libellé AR | Couleur | Icône |
|---|---|---|---|---|
| 85 – 100 | Tout va bien | كل شيء على ما يرام | `Good` | `check` |
| 60 – 84 | À surveiller | يحتاج إلى متابعة | `Warn` | `alert-circle` |
| 0 – 59 | À corriger | يحتاج إلى تصحيح | `Critical` | `x` |

> Si le code actuel utilise d'autres seuils, **garder ceux du code** et le signaler : les maquettes ne doivent pas changer la logique.

### 3.3 Typographie

- Latin : **Plus Jakarta Sans** · Arabe : **IBM Plex Sans Arabic** · Secours : Segoe UI Variable.
- Embarquer les TTF de `assets/fonts/` (WPF : `Resource` + `pack://application:,,,/Assets/Fonts/#Plus Jakarta Sans` ; WinUI 3 : `Content` + `ms-appx:///Assets/Fonts/<fichier>.ttf#Plus Jakarta Sans`).
- Chiffres **tabulaires** partout où une valeur change (score, %, Go, heures) : `Typography.NumeralAlignment="Tabular"` (WPF) / `font-variant-numeric: tabular-nums`.

| Style | Taille | Graisse | Interligne | Lettres | Où |
|---|---|---|---|---|---|
| Score | 52 | 800 | 1.0 | −3 % | Chiffre dans l'anneau |
| Display | 40 | 800 | 1.15 | −3 % | Onboarding |
| PageTitle | 28 | 800 | 1.25 | −2 % | Titre d'écran |
| HeroTitle | 22 | 700 | 1.3 | −1 % | Phrase de conclusion (Accueil) |
| SectionTitle | 17 | 700 | 1.35 | 0 | Titre de carte principale |
| CardTitle | 16 | 700 | 1.35 | 0 | Titre de carte secondaire |
| RowTitle | 15 | 700 | 1.4 | 0 | Titre de ligne / problème |
| Body | 14 | 400 | 1.5 | 0 | Texte courant |
| Caption | 13 | 400 | 1.45 | 0 | Descriptions de ligne |
| Small | 12 | 400 | 1.4 | 0 | Métadonnées |
| Overline | 11 | 700 | 1.3 | +8 %, MAJUSCULES | Groupes de navigation |
| KpiValue | 30 | 800 | 1.0 | −2 % | Chiffres clés (console) |

Arabe : +1 px sur Body/Caption/Small/RowTitle, interligne ≥ 1.6, espacement des lettres 0, pas de majuscules (Overline en 13 px). Les chiffres restent en police latine.

### 3.4 Espacements, rayons, tailles

- Grille de 4 px. Marges de page : **28 haut / 40 côtés / 32 bas**. Entre blocs : **24**. Entre cartes d'une grille : **20** (grandes) / **14–16** (tuiles). Padding de carte : **22–28** horizontal, **22–28** vertical.
- Rayons : carte principale **20**, tuile/carte secondaire **16**, tuile interne **14**, tuile d'icône **12**, bouton / élément de nav **10**, petite puce **6–8**, puces de statut et interrupteurs : **complètement arrondis**.
- Barre de titre **40**, barre latérale **248**, élément de nav **42**, bouton **40** (petit 34, grand 48), tuile d'icône **44** (petite 34), icône **20** (trait 1,8), interrupteur **44 × 26**, anneau de score **168** (trait 14), barre de progression **6**.
- Ombres : aucune (profondeur donnée par `Border` + `Surface` sur `Background`). Seule exception : onglet segmenté actif, ombre `0 1 2 rgba(16,35,33,.08)`.

### 3.5 Animations

Transitions de couleur 120 ms ; ouverture/fermeture 180 ms ; courbe `cubic-bezier(0.2, 0, 0, 1)`. Anneau de score animé de 0 à la valeur en 600 ms à l'affichage de l'Accueil. **Désactiver** si « Afficher les animations » est coupé dans Windows (`UISettings.AnimationsEnabled` / `SystemParameters.ClientAreaAnimation`).

---

## 4. Composants

Créer ces composants **une fois** (styles/contrôles réutilisables), puis les utiliser dans les écrans.

| Composant | Spécification | États |
|---|---|---|
| **Bouton principal** | Fond `Brand`, texte `OnBrand` 14/600, h 40, padding 0 18, rayon 10, icône 18 à gauche (à droite en RTL), espacement 8 | Survol `BrandHover` · Appuyé `BrandPressed` · Désactivé opacité 45 % · Focus : anneau 2 px `FocusRing` décalé 2 px · **Chargement** : icône remplacée par un anneau de progression 16 px, libellé au participe (« Nettoyage… »), bouton non cliquable |
| **Bouton secondaire** | Fond `Surface`, texte `BrandText`, bordure 1 px `ControlBorder` | Survol fond `BrandSoft` |
| **Bouton discret (ghost)** | Transparent, texte `BrandText`, padding 0 10 | Survol fond `BrandSoft` |
| **Bouton destructif** | Fond `Surface`, texte `Critical`, bordure `DangerBorder` | Toujours suivi d'une confirmation |
| **Puce de statut** | h 28, rayon complet, padding 0 12, icône 14 + libellé 13/700 ; couleurs `*Soft` / `*Text` | — |
| **Étiquette** (« Important », « Conseillé », « Recommandé ») | 11/700, padding 3 8, rayon 6, `WarnSoft/WarnText` ou `BrandSoft/BrandText` | — |
| **Carte** | Fond `Surface`, bordure 1 px `Border`, rayon 20 (16 pour tuile), padding 22–28 | Carte cliquable : survol fond `SurfaceSubtle` |
| **Ligne de problème** | Tuile d'icône 44 (fond `*Soft`, icône `*` 22) · titre RowTitle + étiquette · description Caption `TextSecondary` · bouton d'action à droite · séparateur `Divider` | — |
| **Ligne de réglage** | Titre 14/700 + sous-titre 12 `TextMuted` · à droite : statut (icône + mot) **ou** interrupteur **ou** bouton | — |
| **Interrupteur** | 44 × 26, rayon complet, curseur 20 blanc ; actif `Brand`, inactif `SwitchOff` ; libellé d'état à côté (« Activé ») | Désactivé : opacité 45 % + infobulle expliquant pourquoi |
| **Anneau de score** | 168 px, rail `RingTrack` trait 14, arc couleur du seuil, bouts arrondis, départ à 12 h, sens horaire (**antihoraire en arabe**) ; chiffre Score + « sur 100 » | Pendant l'analyse : arc indéterminé qui tourne + « Analyse… » |
| **Tuile de sous-score** | Fond `SurfaceSubtle`, rayon 14, padding 14 16 ; icône 30 sur fond `*Soft`, libellé 13/600, valeur 16/800 couleur du seuil ; barre 6 px | — |
| **Élément de navigation** | h 42, rayon 10, padding 0 12, icône 20 + libellé 14/500 ; actif : fond `BrandSoft`, texte `BrandHover` 700, repère vertical 3 × 20 `Brand` côté début | Survol fond `SurfaceSubtle` · Pastille de compteur (`WarnSoft`/`WarnText`) |
| **Onglets segmentés** | Conteneur `ChipNeutral` rayon 12 padding 4 ; onglet h 38, 14/600 ; actif fond `Surface` + ombre légère | — |
| **Filtres (pilules)** | h 32, rayon complet ; actif fond `TextPrimary` texte blanc ; inactif `ChipNeutral` | — |
| **Champ de recherche** | h 38, bordure `InputBorder`, rayon 10, icône loupe 16 | Focus : bordure `Brand` |
| **Carte de marque (Night)** | Fond `Night`, rayon 20, titre blanc, texte `NightText`, bouton fond `NightAccent` texte `OnNightAccent` | — |

**États transverses à prévoir** (non dessinés, à appliquer avec les mêmes composants) :
- **Chargement de page** : squelettes (rectangles `SurfaceSubtle` aux dimensions du contenu), pas d'écran vide.
- **Action longue** (SFC, scan complet, mises à jour) : bouton en chargement + ligne d'état sous la carte (« En cours · environ 20 min · vous pouvez continuer à utiliser le PC ») + notification Windows à la fin.
- **Succès** : message court (toast en bas à droite, 4 s) « 1,2 Go libérés » + ligne ajoutée à l'Activité récente.
- **Erreur** : message en langage simple + bouton « Réessayer » + lien « Détails techniques » (repliable).
- **Liste vide** : icône 44 dans une tuile `SurfaceSubtle` + une phrase rassurante (« Aucune menace détectée »).

---

## 5. Structure de la fenêtre (commune)

Référence : `maquettes/01-accueil.png`, `11-composant-barre-laterale.png`.

- **Barre de titre personnalisée** 40 px, fond `Sidebar`, bordure basse `Border` : logo 22 px + « PC Santé » 13/700 à gauche, boutons fenêtre à droite (inversés en arabe). Supprimer la barre de titre système grise.
- **Barre latérale** 248 px, fond `Sidebar`, bordure droite `Border` (gauche en arabe), padding 8 12 16 :
  - Groupe **ESSENTIEL** : Accueil (pastille = nombre de points à regarder), Protection, Optimisation, Rapports.
  - Groupe **AVANCÉ** : Performance, Processus, Système, Sessions, Planification. **Masqué en mode Simple** (remplace la logique actuelle du mode Simple ; en mode Simple, « Optimisation » s'affiche « Nettoyage » comme aujourd'hui si c'est le comportement existant).
  - Bas : carte d'offre (tuile bouclier `BrandSoft`, « Premium » 14/700, « Licence active sur ce PC » 12 `TextMuted`), puis **Licence** et **Paramètres** avec **le même style** que les autres éléments (corrige l'incohérence actuelle).
- **Zone de contenu** fond `Background`, marges 28/40/32, défilement vertical seulement si nécessaire (les écrans sont conçus pour tenir à 1440 × 940 sans défiler, sauf listes longues).
- Fenêtre redimensionnable : sous 1200 px de large, les grilles 3 colonnes passent à 2, la colonne latérale droite (Accueil, Système) passe sous le contenu principal.

---

## 6. Écran par écran

Pour chaque écran : **Avant** (captures `avant/`) → **Après** (`maquettes/`). Les données citées viennent des captures ; elles doivent être liées aux données réelles du code.

### 6.1 Accueil — `01-accueil.png` (avant : `avant/1-application/a-theme-clair/clair-01-accueil.png`)

Problèmes corrigés : cercle de score confondu avec les boutons (même couleur), grand vide sous les problèmes, boutons « Voir » peu explicites, CTA isolé.

1. **En-tête** : titre « Bonjour, voici l'état de votre PC » (PageTitle) ; sous-titre `TextMuted` : « Dernière analyse aujourd'hui à 15:20 · {nom du PC} · {édition Windows} ». À droite : bouton secondaire « Relancer l'analyse » (icône `refresh`).
2. **Carte score** (rayon 20, padding 28 32, 3 zones en ligne, gap 40) :
   - Anneau de score 168 px (couleur = seuil).
   - Bloc central : puce de statut · phrase de conclusion HeroTitle générée selon le résultat (ex. « Votre PC est bien protégé. Seul le stockage demande votre attention. ») · ligne `TextSecondary` « {n} points à corriger · environ {durée} · rien n'est supprimé sans votre accord. » · boutons **« Tout corriger »** (principal, icône `check`) et « Voir le détail » (discret).
   - Grille 2 × 2 de sous-scores (420 px) : Sécurité, Performance, Stabilité, Stockage.
3. **Rangée basse** (gap 24) :
   - **« Ce qu'il faut regarder »** (carte, flex) : lignes de problème. Chaque ligne a **son action nommée** : « Libérer de l'espace » (secondaire, ouvre Optimisation › Nettoyage), « Nettoyer 1,2 Go » (principal, lance le nettoyage avec confirmation). Puis **« Tout le reste va bien »** : grille 2 colonnes de coches vertes (Antivirus actif et à jour · Pare-feu activé sur tous les réseaux · Windows à jour · Aucun plantage récent) — alimentée par les sous-scores à 100.
   - Colonne droite 380 px : carte Night **« Veille automatique »** (statut Inactive/Active, texte, bouton « Activer les 3 tâches » → même action que Planification) ; carte **« Activité récente »** : 3 dernières entrées du journal des actions (Rapports), lien « Tout voir » → Rapports.
4. **Si aucun problème** : la carte « Ce qu'il faut regarder » affiche l'état vide « Rien à corriger. Votre PC est en bonne santé. » et la liste des coches.
5. **« Tout corriger »** : ouvre une fenêtre de confirmation listant les actions (cases cochées), puis exécute en séquence avec progression.

### 6.2 Protection — `02-protection.png` (avant : `clair-02-protection-1…3.png`)

1. En-tête + sous-titre « Antivirus, pare-feu et menaces. PC Santé peut activer une protection, jamais la désactiver. »
2. **Bandeau d'état** : tuile bouclier 72 (fond `GoodSoft` / `WarnSoft` / `CriticalSoft` selon état), « Votre PC est protégé » + étiquette « {n} recommandation(s) », ligne `TextMuted` « Microsoft Defender · définitions du … · dernier scan le … ». Actions : « Autres scans » (secondaire, menu : Scan complet, Scanner un dossier…, Mettre à jour l'antivirus) et **« Scan rapide »** (principal).
3. **Grille 2 colonnes** :
   - **Antivirus** : lignes Surveillance permanente, Détection des nouveaux virus, Protection de vos documents contre le chantage. État = icône + mot (« Activée » `GoodText` / « Désactivée » `CriticalText`) ; si désactivée : étiquette « Recommandé » + bouton principal petit « Activer ». **Pas d'interrupteur ici** (Defender ne peut pas être désactivé par l'app).
   - **Pare-feu** : Réseau d'entreprise, Réseau domestique, Wi-Fi public — tuile d'icône, libellé, « Activé » + **interrupteur** (désactiver demande confirmation, comme aujourd'hui).
4. **Historique des menaces** : étiquette « Toutes traitées » / « {n} à traiter », « Quarantaine vide » à droite ; lignes : icône d'état, nom de la menace, chemin raccourci (`Téléchargements › fichier.exe`, complet en infobulle), date.
5. « Antivirus installés » (liste actuelle) : déplacer en une ligne dans l'en-tête de la carte Antivirus (« Windows Defender · à jour ») ; si plusieurs antivirus, lien « {n} antivirus détectés » ouvrant la liste.

### 6.3 Optimisation — `03-optimisation.png` (avant : `clair-03-optimisation-1…6.png`)

Problème corrigé : une page de 6 écrans de haut avec des dizaines de cartes identiques.

1. **Onglets segmentés** : « Nettoyage & démarrage » · « Tâches en arrière-plan » (= tâches planifiées d'autres programmes) · « Réglages Windows » (mode d'alimentation, effets visuels, disque/TRIM, services selon l'usage) · « Historique » (= « Annuler une modification »).
2. Onglet 1, **colonne Nettoyage** (420 px) : total récupérable en grand (48/800), **barre empilée** par catégorie (couleurs `Brand` et `ChartSecondary`), liste de catégories cochables (ligne cochée = fond `RowSelected`) avec taille à droite, bouton pleine largeur « Nettoyer {total} » (48 px), avertissement « Les fichiers supprimés ne peuvent pas être récupérés ».
3. Onglet 1, **Programmes au démarrage** : recherche + filtres « Tous / Se lancent / Désactivés » ; tableau : initiale en tuile, nom, « Pour qui » (Votre compte / Tous les utilisateurs / Votre dossier Démarrage), état + **interrupteur** (remplace les boutons « Relancer au démarrage » / « Ne plus lancer »). Nom en `TextMuted` si désactivé.
4. Onglet 2 : même tableau (nom + chemin en `TextMuted`), interrupteur « Actif » ; lignes à identifiant SID tronquées au milieu.
5. Onglet 3 : une carte par réglage (Ligne de réglage) avec le choix actuel à droite (liste déroulante ou interrupteur) ; « Services selon votre usage » : liste déroulante de profil + bouton « Appliquer le profil » + résumé de l'effet.
6. Onglet 4 : liste chronologique des modifications avec bouton « Annuler » par ligne.

### 6.4 Performance — `05-performance-theme-sombre.png` (avant : `clair-04-performance.png`)

Problème corrigé : graphiques vides (un trait plat donne l'impression d'une panne).

1. Grille 2 × 2 : Processeur, Mémoire (« 10,6 Go sur 15,8 Go »), Disque, Réseau (↓ réception / ↑ envoi, 2 courbes : `Brand` pour la réception, `Warn` pour l'envoi, avec légende). Chaque carte : tuile d'icône, libellé, note à droite, valeur KpiValue, **graphique de 60 s** (aire à 14 % d'opacité + ligne 2 px, 3 lignes de grille `Divider`, échelle fixe 0–100 % ; réseau : échelle auto).
2. Rangée 3 colonnes : Température (valeur + barre + mot « Normale / Élevée »), Batterie (valeur + barre ; `Warn` + « Faible » sous 25 %), **Mini-affichage** (interrupteur + aperçu du widget + phrase) — remplace le bouton « Masquer le mini-affichage ».
3. Indicateur « En direct » (point `Brand`) en haut à droite. Mettre à jour une fois par seconde ; conserver 60 points.
4. Afficher le graphique **dès l'ouverture** avec l'historique déjà collecté par le service ; sinon, message « Collecte des données… » au lieu d'un trait plat.

### 6.5 Système — `04-systeme.png` (avant : `clair-06-systeme-1…4.png`)

1. **Bandeau Windows Update** : tuile 56 (icône `download`, fond selon état), « Windows est à jour » / « {n} mises à jour en attente », édition Windows, dernières dates ; bouton principal « Rechercher les mises à jour » (ou « Installer {n} mises à jour » si en attente).
2. **« Outils de réparation »** + phrase « Utilisez-les seulement si quelque chose ne marche pas. » ; **grille 3 colonnes de tuiles** (rayon 16, min-h 158) : catégorie en Overline + tuile d'icône 34, titre 14/700, description 12, **durée** (icône `clock`) et bouton secondaire petit à verbe court. Tuiles : Débloquer Windows Update · Réparer les fichiers de Windows · Réparation approfondie · Créer un point de restauration · Activer la restauration · Vider le cache des adresses · Réparer la connexion réseau · Réinitialiser le pare-feu · Installer les mises à jour en attente.
3. **Colonne droite 320 px** : « Chiffrement du disque » (statut BitLocker en puce + texte + « Sauvegarder la clé ») ; « Comptes du PC » (avatar initiale — `Brand` pour administrateur —, nom, rôle, état).
4. Les tuiles qui redémarrent le PC ou durent plus de 10 min demandent confirmation en rappelant la durée et le point de restauration.

### 6.6 Premier lancement — `07…09-premier-lancement-*.png` (avant : `avant/1-application/e-premier-lancement/`)

Mise en page : panneau de marque `Night` 520 px à gauche (rayon 24, logo, accroche Display, 3 promesses ou légende du score), contenu 560–600 px centré à droite ; indicateur « Étape n sur 3 » + barre en 3 segments.

1. **Langue** : deux grandes tuiles radio « Français » / « العربية » (sélection : bordure 2 px `Brand` + halo 4 px `BrandSoft`) ; bouton « Continuer ». Le choix bascule **immédiatement** la langue et le sens de l'écran.
2. **Mode** : tuiles radio « Simple » (étiquette « Conseillé ») et « Avancé » avec la liste des écrans concernés ; « Modifiable à tout moment dans Paramètres » ; boutons « Retour » / « Analyser mon PC ». Le choix écrit **le même réglage** que la case « Mode Avancé » des Paramètres.
3. **Première analyse** : anneau 128 + puce de statut + phrase de conclusion ; carte « Laisser PC Santé veiller automatiquement » avec **interrupteur activé par défaut** (décision validée) — à la fin, créer les 3 tâches recommandées si l'interrupteur est resté activé ; boutons « Retour » / « Voir mon tableau de bord ». Pendant l'analyse : anneau indéterminé + « Analyse de votre PC… (environ 30 s) ».

### 6.7 Écrans non dessinés — appliquer le système

| Écran | Consignes |
|---|---|
| **Processus** (`clair-05-processus-*`) | Tableau style « Programmes au démarrage » : recherche + filtres (Tous / Gourmands / Non signés). Colonnes : Programme (nom + description `TextMuted`), Processeur, Mémoire, Disque, Réputation (puce « Utile » `GoodSoft` / « Inconnu » `WarnSoft`), Éditeur. Action en fin de ligne dans un menu « ⋯ » (Arrêter, Emplacement) au lieu de boutons tronqués. Corriger les colonnes coupées (« Emplac », « Arrêter »). « Programmes gourmands sur 7 jours » : carte séparée, barre de % du temps en consommation élevée. « Non disponible » → tiret « — » avec infobulle. |
| **Sessions** | Carte par session : avatar, `DOMAINE\utilisateur`, « Sur ce PC », état en puce ; actions : « Envoyer un message » (secondaire), « Déconnecter » (secondaire), « Fermer la session » (**destructif** + confirmation). Liste déroulante des messages au-dessus. |
| **Planification** (`clair-08-*`) | Carte par tâche : titre, description, **interrupteur Active/Inactive** (remplace la puce rouge « Inactive » + bouton « Activer »), réglages jour/heure en ligne, cases « Seulement quand je n'utilise pas le PC » / « Seulement sur secteur », « Dernière exécution : … » en `TextMuted`. Bouton principal « Activer les 3 tâches recommandées » en tête. |
| **Rapports** (`clair-09-*`) | Carte « Évolution du score » : courbe du score avec bandes de fond des seuils (vert/orange/rouge à 8 % d'opacité), axe des dates. Carte « Rapport PDF » : sélecteur segmenté Semaine / Mois, bouton principal « Générer le rapport PDF », secondaires « Exporter en CSV » et « Ouvrir le dossier ». « Journal des actions » : tableau (Date, Par, Action, Résultat en puce). |
| **Licence** | Carte offre (tuile bouclier, « Premium », « Licence active sur ce PC », dates) ; champ de clé avec masque `PCS-XXXXX-…` ; bouton principal « Activer ma licence » ; « Désactiver la licence sur ce PC » en bouton secondaire dans une carte « Ce PC ». |
| **Paramètres** | Cartes « Général » (Langue, Apparence Clair/Sombre/Système en onglets segmentés, Mode Avancé en interrupteur) et « Mini-affichage » (interrupteurs, position, curseurs). Bouton « Enregistrer » → **enregistrement automatique** avec message « Enregistré » si le code le permet ; sinon garder le bouton, aligné en bas de carte. |

---

## 7. Arabe (droite à gauche) — `06-accueil-arabe-rtl.png`

- `FlowDirection="RightToLeft"` (XAML) / `dir="rtl"` (web) à la racine quand la langue est l'arabe : la barre latérale passe à droite, les boutons fenêtre à gauche, les icônes directionnelles (flèches, chevrons) s'inversent ; les icônes non directionnelles (bouclier, disque) ne s'inversent pas.
- Utiliser des propriétés « début / fin » (pas gauche / droite) pour marges, bordures et repère de navigation.
- Anneau de score : sens **antihoraire** ; barres de progression remplies depuis la droite.
- Police IBM Plex Sans Arabic, ajustements du § 3.3. Nombres, noms de PC et « Windows 11 Pro » en latin, isolés (`FlowDirection=LeftToRight` sur le fragment / `<bdi>`), décimale `28,7`.
- Nouvelles chaînes arabes à ajouter (à faire relire par un arabophone) :

| Clé suggérée | FR | AR |
|---|---|---|
| home.greeting | Bonjour, voici l'état de votre PC | مرحبًا، هذه حالة حاسوبك |
| home.rescan | Relancer l'analyse | إعادة الفحص |
| home.fixAll | Tout corriger | إصلاح الكل |
| home.details | Voir le détail | عرض التفاصيل |
| home.allGood | Tout le reste va bien | كل شيء آخر على ما يرام |
| home.freeSpace | Libérer de l'espace | تحرير مساحة |
| home.cleanN | Nettoyer {0} | تنظيف {0} |
| tag.important / tag.advised | Important / Conseillé | مهم / موصى به |
| watch.title / watch.inactive | Veille automatique / Inactive | المراقبة التلقائية / غير مفعّلة |
| watch.enable3 | Activer les 3 tâches | تفعيل المهام الثلاث |
| activity.title / activity.all | Activité récente / Tout voir | النشاط الأخير / عرض الكل |
| nav.essential / nav.advanced | Essentiel / Avancé | أساسي / متقدم |
| plan.active | Licence active sur ce PC | الترخيص مفعّل على هذا الحاسوب |

---

## 8. Thème sombre — `05-performance-theme-sombre.png`

- Charger `PcSante.Colors.Dark.xaml` à la place du clair (Apparence = Sombre, ou Système + Windows en sombre). Tous les écrans utilisent les mêmes clés : aucun changement de mise en page.
- En sombre, `Brand` devient `#4FC7BC` (texte `OnBrand` `#062A28`) ; les cartes `Night` restent identiques.
- Supprimer le bouton principal marron/beige actuel du thème sombre (il reprenait l'accent Windows).

---

## 9. Console PME (web) — `10-console-pme-postes.png` (avant : `avant/2-console-pme/`)

1. Importer `tokens/pcsante-tokens.css` et remplacer les couleurs de l'actuel thème (bleu générique) par les variables `--pcs-*`.
2. **En-tête 64 px** sur une seule ligne (corrige le choix de langue en pleine largeur et le bouton de déconnexion sur une ligne à part) : logo 30 + « PC Santé » / nom de l'organisation ; navigation `.pcs-nav` (Postes · Alertes + pastille rouge du nombre ouvert · Rapports · Organisation) ; à droite : sélecteur de langue compact (icône globe + « FR »), menu du compte (avatar initiale + e-mail + chevron → « Se déconnecter » dedans).
3. **Postes** : titre « Postes de l'entreprise » + sous-titre « Mis à jour aujourd'hui à 11:48 · 3 postes sur 5 licences » + bouton principal « Ajouter un poste ». 4 chiffres clés : Postes suivis (x / y licences + barre), Score moyen (petit anneau + libellé de statut), Alertes ouvertes (rouge + poste concerné), Postes silencieux (+ phrase verte si 0).
4. Tableau `.pcs-table` : Poste (icône + nom + édition Windows), État (`.pcs-score` + libellé de statut ; badge orange en `WarnStrong` pour garder le texte blanc lisible — légèrement plus foncé que la maquette), Alertes (nombre en rouge ou « — »), Dernier contact (« Aujourd'hui, 11:48 »), chevron. **Toute la ligne est cliquable** vers le détail.
5. Panneau droit **« À traiter »** 360 px : alertes ouvertes (fond `CriticalSurface`, icône triangle, « PC-ACCUEIL · 11:48 », texte, bouton petit « Marquer comme traitée »), lien « Toutes les alertes ».
6. **Détail d'un poste** (non dessiné) : fil d'Ariane « Postes › PC-ACCUEIL » au lieu du bouton « ← Retour » ; en-tête avec anneau 96 + nom + puce ; 4 tuiles de sous-score (composant du § 4) ; « Ce qu'il faut regarder » en lignes de problème ; « Retirer ce poste » en bouton destructif en bas, avec confirmation.
7. **Alertes / Rapports / Organisation / Connexion** : même en-tête, mêmes cartes et tableaux. Connexion : carte centrée 400 px, logo, champs 44 px, bouton principal pleine largeur.
8. Dates avec la locale (`Intl.DateTimeFormat`), y compris en arabe (corrige « 11:48:05 ص » : afficher `11:48` sans secondes).

---

## 10. Ordre de travail (lots)

| Lot | Contenu | Vérification |
|---|---|---|
| 1 | Jetons + polices + icône d'application (`pcsante.ico`, logo de la barre de titre) ; retrait de l'accent Windows | L'app compile, couleurs de marque visibles partout, plus de marron |
| 2 | Composants du § 4 (styles/contrôles) | Page ou fenêtre de test affichant tous les composants et états, comparée à `00-identite-composants.png` |
| 3 | Coque : barre de titre, barre latérale, zone de contenu, mode Simple/Avancé | `11-composant-barre-laterale.png`, `01-accueil.png` (cadre) |
| 4 | Accueil | `01-accueil.png` |
| 5 | Protection, Optimisation (onglets), Système | `02`, `03`, `04` |
| 6 | Performance + thème sombre | `05` |
| 7 | Processus, Sessions, Planification, Rapports, Licence, Paramètres (§ 6.7) | Cohérence avec les composants |
| 8 | Premier lancement | `07`, `08`, `09` |
| 9 | Arabe / RTL sur tous les écrans | `06` + passage complet en arabe |
| 10 | Console PME | `10` + § 9 |
| 11 | Format des nombres et dates (FR/AR), revue d'accessibilité | Critères du § 12 |

---

## 11. Données que les maquettes supposent (à vérifier dans le code)

Afficher seulement si la donnée existe ; sinon masquer l'élément et le lister dans le compte rendu.

- Phrase de conclusion de l'Accueil et durée estimée de correction (peut être construite à partir des problèmes détectés).
- Durée estimée par action (déjà présente dans les textes actuels de Système : « Environ 2 minutes », « 10 à 30 minutes »…).
- Historique de 60 s pour les graphiques de Performance.
- Nombre de licences de l'organisation (console : « 3 postes sur 5 licences » — visible dans la capture actuelle « 3 · من أصل 5 »).
- Liste « Tout le reste va bien » (déduite des contrôles passés avec succès).

---

## 12. Critères d'acceptation

- [ ] Aucune couleur codée en dur dans les écrans ; aucune référence à l'accent Windows.
- [ ] Chaque statut affiché combine icône + mot + couleur.
- [ ] Chaque problème de l'Accueil a un bouton d'action au verbe explicite ; plus aucun bouton « Voir » seul.
- [ ] Nombres et dates au format de la langue (`28,7 Go`, `24/09/2026`, heures sans secondes).
- [ ] Écrans à 1440 × 940 : pas de défilement sur Accueil, Protection, Système, Performance ; pas de texte ou bouton tronqué.
- [ ] Navigation au clavier complète (Tab, Entrée, Espace sur interrupteurs, flèches dans les onglets) avec anneau de focus visible ; noms accessibles sur les boutons à icône seule.
- [ ] Contrastes texte ≥ 4,5:1 (valeurs de `tokens.json` inchangées).
- [ ] Mode Simple : groupe Avancé masqué ; choix du premier lancement synchronisé avec Paramètres.
- [ ] Arabe : mise en page miroir, anneau antihoraire, chiffres latins isolés, aucun texte français résiduel.
- [ ] Thème sombre : tous les écrans lisibles, aucun fond blanc résiduel.
- [ ] Animations coupées quand Windows les désactive.
- [ ] Console : en-tête sur une ligne à 1280 px en FR et en AR ; lignes du tableau cliquables.
