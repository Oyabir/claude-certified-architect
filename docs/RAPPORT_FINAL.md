# Rapport final — PC Santé (MVP + serveur de licences)

22 septembre 2026 · Branche `claude/pc-sante-app-license-server-7xm4xs`

## En bref

- **Livré** : l'application Windows complète du périmètre MVP (interface WPF, service SYSTEM, mini-affichage, rapports PDF), le **serveur de licences** avec son administration, l'installeur WiX et les guides.
- **Qualité** : toute la solution compile en Release **sans erreur ni avertissement** (analyseurs .NET activés) ; **311 tests automatisés** passent. Couverture : Core 96,9 %, Licensing 97,8 %, serveur de licences 98,1 %.
- **À savoir** : le chantier a été mené sur une machine **Linux** (aucun Windows disponible, voir `DECISIONS.md`). Tout compile, mais les accès Windows réels (WMI, COM, P/Invoke), l'affichage WPF et l'installeur MSI **n'ont pas pu être exécutés**. Ils sont couverts par des simulations et doivent être validés en VM avec `docs/TESTS_MANUELS_VM.md` avant la bêta.

## 1. Ce qui est livré et fonctionne

### Application (sections 2, 4, 7)

| Module MVP | Livré | Vérification |
| --- | --- | --- |
| M1 Tableau de bord | Score 0–100, 4 sous-scores, 18 problèmes détectables (quoi / pourquoi / Corriger), « Tout corriger » avec récapitulatif, historique du score | Tests unitaires (règles, score, couleurs) + service |
| M2 Antivirus | Pilotage de Defender : état, scans rapide/complet/dossier, signatures, historique des menaces, quarantaine (restauration), protections temps réel / cloud / dossiers contrôlés, message sur la protection contre les falsifications ; aucun faux problème si un autre antivirus est actif | Tests avec simulations ; VM |
| M3 Processus | Liste CPU/RAM/disque, historique 7 jours, réputation (utile, inutile, inconnu, suspect), signature Authenticode (intégrée + catalogue), arrêter, retirer du démarrage, désactiver le service, ouvrir l'emplacement | Tests ; VM |
| M4 Optimisations (démarrage, nettoyage, alimentation) | Programmes au démarrage (réversible via StartupApproved) et tâches tierces ; nettoyage temporaires / cache Windows Update / corbeille / navigateurs ; plan d'alimentation ; point de restauration avant chaque optimisation et bouton « Annuler » par action | Tests ; VM |
| M5 Mini-affichage | Barre Win32 superposée, click-through, masquée en plein écran, icône de notification en alternative, position / taille / opacité / indicateurs réglables, températures CPU si Windows les expose | Tests du formatage ; VM (mémoire < 30 Mo) |
| M6 Actions en un clic (pare-feu, Defender, Update, SFC/DISM, restauration) | Activer/désactiver le pare-feu par profil, réinitialiser (avec export), rechercher/installer/réparer Windows Update, SFC, DISM, créer un point / activer la restauration | Tests ; VM |
| M8 Reporting (PDF local) | Rapport PDF hebdomadaire ou mensuel lisible (QuestPDF), en français, anglais, arabe ; journal des actions | PDF généré dans les tests (3 langues) |
| M9 Tâches planifiées (3 modèles) | Scan antivirus quotidien, nettoyage hebdomadaire, point de restauration hebdomadaire ; jour, heure, conditions (PC inactif, sur secteur) ; journal d'exécution ; activation en un clic | Tests ; VM |

**Écrans** : Accueil, Protection, Nettoyage/Optimisation, Performance, Processus, Système, Rapports, Planification, Paramètres, Licence, assistant de premier lancement (langue → licence → analyse → 3 tâches recommandées).

**UX (section 11)** : mode Simple par défaut (Accueil, Protection, Nettoyage, Rapports), mode Avancé dans les Paramètres ; un seul bouton principal par écran ; vocabulaire simple avec terme technique en petit (« Protection contre les intrusions » / Pare-feu Windows) ; vert/orange/rouge constants ; aucun code d'erreur brut (lien « Détails ») ; confirmation avant fermeture de programme, suppression ou redémarrage ; message de résultat après chaque action ; texte ≥ 14 px, thèmes clair/sombre, noms pour le Narrateur, arabe de droite à gauche. 565 textes en 3 langues, complétude vérifiée par test.

### Sécurité du service SYSTEM (section 5)

- **Liste fermée** : 61 commandes décrites dans `CommandDefinitions` (offre, sauvegarde, confirmation, paramètres typés). Nom de commande strict, paramètres inconnus refusés, chemins réseau/`..` refusés, tailles bornées. Tout refus est journalisé.
- **Aucun script** : ni PowerShell ni interpréteur ; WMI, COM, P/Invoke, et outils système à chemin absolu avec arguments passés un par un.
- **Named pipe protégé** : ACL (SYSTEM, Administrateurs, utilisateurs authentifiés locaux ; réseau et anonyme refusés ; première instance obligatoire). Le client est identifié par le noyau (PID) et doit être un exécutable de l'application, **dans le dossier d'installation et signé par le même certificat que le service** (Release). L'interface vérifie aussi que le serveur du pipe est bien le service.
- **Cycle obligatoire** pour chaque action : vérifier → point de restauration et/ou sauvegarde → exécuter → contrôler (retour arrière automatique si le contrôle échoue) → journaliser (qui, quoi, quand, résultat).
- Durcissements supplémentaires : une seule action système à la fois ; processus et services indispensables protégés ; arrêt limité aux programmes de l'utilisateur appelant ; suppression de fichiers par handle avec contrôle du chemin final (anti-jonction / lien symbolique) ; dossier de données réservé à SYSTEM et aux administrateurs ; tâche planifiée exécutable seulement si l'utilisateur l'a programmée.
- **Mises à jour signées** : manifeste signé Ed25519 par le serveur + empreinte SHA-256 + signature Authenticode du MSI identique à celle du service.

### Licence (section 12)

- Empreinte : 4 hachages SHA-256 salés (carte mère, disque système, processeur, MachineGuid), **tolérance 3/4**, aucune donnée brute envoyée.
- Jeton **signé Ed25519** (clé privée uniquement côté serveur, lue depuis une variable d'environnement ; l'application ne contient que la clé publique), stocké **chiffré DPAPI machine**.
- Hors ligne toléré 14 jours ; revalidation tous les 7 jours ; recul d'horloge détecté ; clé expirée ou révoquée → offre Gratuite sans perte de données.
- Règles d'activation vérifiées par tests d'intégration réels (client HTTP ↔ serveur) : clé neuve, réinstallation sur le même PC sans nouvelle activation, **refus sur un second PC** (« Cette licence est déjà utilisée sur un autre ordinateur »), transfert limité à 2 par an, remise à zéro par l'administrateur, offre Famille (3 postes), expiration, révocation, hors ligne, limitation par IP.
- Serveur : ASP.NET Core Minimal API + SQLite (PostgreSQL par configuration), page d'administration (générer des clés, voir les activations, révoquer, remettre à zéro un transfert, tentatives refusées), journal des refus, clés stockées hachées. Démarrage réel vérifié (keygen → activation → refus sur un 2e PC).
- Binaire obfusqué (Obfuscar) : `PcSante.Licensing.dll` et `PcSante.Service.dll` ; les tests du service et du serveur passent sur les binaires obfusqués.

### Définition de « terminé » (section 13)

| Critère | État |
| --- | --- |
| Toute la solution compile en Release sans erreur ni avertissement | ✅ `PcSante.sln` : 0 erreur, 0 avertissement. L'installeur WiX, hors solution, ne se compile que sous Windows (justifié ci-dessous). |
| Tous les tests automatisés passent | ✅ 311 / 311 |
| Chaque module MVP fonctionne ou est désactivé et justifié | ✅ tous livrés et testés avec simulations ; 2 fonctions désactivées et justifiées (section 2) ; validation réelle en VM à faire |
| Une licence activée sur un PC est refusée sur un second | ✅ tests d'intégration + essai réel du serveur |
| L'installeur MSI installe, met à jour et désinstalle proprement | ⚠️ Rédigé (service SYSTEM, MajorUpgrade, désinstallation propre, vérification .NET) et vérifié structurellement par tests ; **compilation et essai à faire sous Windows** (WiX ne fonctionne pas sous Linux) |
| Service au repos < 1 % CPU, mini-affichage < 30 Mo | ⚠️ Conçu pour (aucune mesure permanente : échantillonnage des processus toutes les 10 min, mesures à la demande ; mini-affichage Win32 sans WPF, GC économe) ; **mesure à faire en VM** (checklist, section 9) |
| Interface en français, anglais et arabe | ✅ 565 textes, test de complétude et de cohérence des paramètres, RTL |
| Guide utilisateur et guide de déploiement rédigés | ✅ `docs/GUIDE_UTILISATEUR.md`, `docs/GUIDE_DEPLOIEMENT.md` |

## 2. Ce qui est désactivé ou incomplet, avec la raison

| Élément | État | Raison |
| --- | --- | --- |
| Exécution réelle sous Windows (WPF, WMI, COM, P/Invoke) | Non exécutée | Machine de développement Linux ; tout compile et la logique est testée avec simulations. Des ajustements peuvent apparaître pendant la checklist VM (par ex. mise en page, codes de retour d'outils Windows). |
| Compilation du MSI | Non réalisée | WiX 4.0.5 et 5.0.2 essayés : « The WiX Toolset only supports Windows » (WIX0389). `installer/build.ps1` fait tout sous Windows. |
| Suppression d'un élément de la quarantaine | Désactivée (restauration seule) | Aucune API documentée pour supprimer un élément précis ; Defender purge lui-même la quarantaine et un élément en quarantaine est déjà neutralisé. |
| Consommation réseau par processus | Affichée « non disponible » | Nécessite ETW en temps réel, trop coûteux pour l'objectif < 1 % CPU. |
| Températures | Selon le PC | Lecture via WMI (`MSAcpi_ThermalZoneTemperature`), souvent absente ; LibreHardwareMonitor écarté car son pilote WinRing0 est détecté par Defender (risque n°1 du projet). Affiche « non disponible » sinon. |
| Signature des binaires | Prête, non réalisée | Aucun certificat (achat interdit sans accord). En Release, le service **refuse** une interface non signée : signer avant tout test Release en VM. |
| Hors MVP (section 7) | Non développés | M7 Sessions, BitLocker, réseau (DNS/Winsock), comptes, effets visuels, profils de services, antivirus tiers, défragmentation/TRIM, M10 assistant IA (seule l'interface `IAiAssistant` existe), console PME, export CSV PME. |

## 3. Décisions prises seul (résumé de `DECISIONS.md`)

- **Environnement** : développement sous Linux, SDK .NET 8 Microsoft, WPF compilé avec `EnableWindowsTargeting` ; WiX compilé sous Windows uniquement.
- **Nom et identité** : un seul fichier `branding.props` (nom, éditeur, version, URL et clé publique du serveur).
- **Bibliothèques** : BouncyCastle (Ed25519), FluentAssertions 6 (licence libre, la v8 est payante), WiX 5.0.2 (avant la redevance OSMF), QuestPDF Community, Obfuscar (ConfuserEx ne gère pas .NET 8).
- **Sécurité** : aucun PowerShell ; vérification du client du pipe (dossier d'installation + même certificat, signature facultative seulement en Debug) ; une action système à la fois ; arrêt limité aux programmes de l'appelant ; suppression par handle anti-jonction ; tâches planifiées exécutées via le catalogue seulement si programmées.
- **Licence** : 4 hachages séparés (tolérance 3/4), jeton de 14 jours revalidé tous les 7 jours, gestion par le service (jamais par l'interface), clés stockées hachées côté serveur, transfert Famille = activation la moins récemment vue, clé publique vide par défaut (Gratuit).
- **Produit** : températures par WMI (pas de WinRing0), démarrage via StartupApproved (réversible), 3 modèles de tâches (scan quotidien, nettoyage et point de restauration hebdomadaires) via le Planificateur Windows, rapports générés côté interface (`PcSante.Reporting`), préférences utilisateur en JSON local, mini-affichage Win32 sans WPF, scans en arrière-plan / réparations avec attente visible, applications dépendantes du runtime .NET 8 (installeur < 50 Mo), désinstallation qui retire tâches et données.

## 4. Actions restant à faire par le commanditaire

1. **Certificat de signature de code** (OV, ~200 $/an) puis `installer/build.ps1` avec `PCSANTE_SIGN_THUMBPRINT`.
2. **Héberger le serveur de licences** (VPS + domaine + HTTPS) : `keygen`, variables d'environnement, clé publique dans `branding.props`, reconstruire le MSI (`docs/GUIDE_DEPLOIEMENT.md`).
3. **Passer la checklist** `docs/TESTS_MANUELS_VM.md` sur Windows 11 et Windows 10 (Home et Pro), puis **tests sur 20 PC réels** ; mesurer CPU du service et mémoire du mini-affichage.
4. **Nom commercial et logo définitifs** (`branding.props`, `src/PcSante.App/Assets/pcsante.ico`), **conditions d'utilisation** (`installer/License.rtf`).
5. **Soumission** des binaires signés à Microsoft (SmartScreen) et aux éditeurs antivirus.
6. Choix de l'hébergement (Maroc ou Europe), conformité loi 09-08 / CNDP ; conservation hors ligne de la clé privée et des tables d'obfuscation.
7. Revoir, si souhaité, les décisions de `docs/DECISIONS.md`.

## 5. Commandes

```bash
# Compiler toute la solution (Windows, Linux ou macOS)
dotnet build PcSante.sln -c Release

# Lancer tous les tests automatisés
dotnet test PcSante.sln -c Release

# Couverture de code (rapports Cobertura dans ./coverage)
dotnet test PcSante.sln -c Release --collect:"XPlat Code Coverage" --results-directory ./coverage

# Générer les clés du serveur de licences (rien n'est écrit sur disque)
dotnet run --project server/PcSante.LicenseServer -c Release -- keygen

# Lancer le serveur de licences en local
PCSANTE_LICENSE_PRIVATE_KEY=... PCSANTE_ADMIN_API_KEY=... dotnet run --project server/PcSante.LicenseServer -c Release
```

```powershell
# Produire l'installeur MSI (Windows) : publication, obfuscation, signature, WiX
$env:PCSANTE_SIGN_THUMBPRINT = "<empreinte du certificat>"
./installer/build.ps1 -Configuration Release
```

## Annexe — Structure du dépôt

| Dossier | Contenu |
| --- | --- |
| `src/PcSante.Core` | Modèles, catalogue des commandes, validation, cycle d'action, score et règles, abstractions Windows, IA (interface) |
| `src/PcSante.Service` | Service SYSTEM : répartiteur, actions, requêtes, base SQLite, audit, tâches de fond, mises à jour |
| `src/PcSante.WindowsApi` | WMI, Defender, pare-feu, Windows Update, SFC/DISM, restauration, processus, services, démarrage, nettoyage, alimentation, métriques, signature, empreinte, DPAPI, Planificateur |
| `src/PcSante.Ipc` | Contrat et sécurité du named pipe |
| `src/PcSante.App` | Interface WPF (WPF-UI), ressources fr/en/ar |
| `src/PcSante.Overlay` | Mini-affichage Win32 |
| `src/PcSante.Licensing` | Empreinte, jeton Ed25519, client de licence |
| `src/PcSante.Reporting` | Rapport PDF (QuestPDF) |
| `server/PcSante.LicenseServer` | Serveur de licences et administration |
| `installer/` | Projet WiX 5, localisation, `build.ps1` |
| `tools/obfuscation/` | Configuration et scripts Obfuscar |
| `tests/` | 7 projets de tests (311 tests) |
| `docs/` | Cahier des charges, PLAN, DECISIONS, guides, checklist VM, ce rapport |
