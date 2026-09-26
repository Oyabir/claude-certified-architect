# Plan du chantier PC Santé

Ordre = planning MVP (section 7 du cahier des charges). Statuts : ✅ fait · 🔄 en cours · ⏳ à faire · ⛔ désactivé/justifié · 🖥️ à valider en VM Windows (`docs/TESTS_MANUELS_VM.md`).

Critère commun à tous les lots : `dotnet build PcSante.sln -c Release` sans erreur ni avertissement, `dotnet test` vert, commit en français.
**État au 22/09/2026 : 0 avertissement, 311 tests verts.**

## Lot 0 — Démarrage ✅
- ✅ Lecture complète du cahier des charges
- ✅ CLAUDE.md, PLAN.md, DECISIONS.md
- ✅ Solution .NET 8 selon la structure de la section 13 (+ `src/PcSante.Reporting`, voir DECISIONS)

## Lot 1 — Fondations ✅
- ✅ Core : catalogue fermé (`CommandId`, `CommandDefinitions`), validation stricte, cycle d'action (`SystemActionPipeline`), abstractions Windows, audit
- ✅ Ipc : contrat JSON versionné, encadrement borné, `ClientTrustPolicy` (dossier d'installation + signature), ACL du pipe, vérification du serveur côté client
- ✅ Service : Worker Service Windows, répartiteur unique, verrou d'action, audit SQLite (EF Core), Serilog fichiers tournants, ACL du dossier de données
- ✅ App : WPF + WPF-UI, navigation latérale, mode Simple/Avancé, ressources fr/en/ar, RTL

Fin : ✅ commande hors catalogue refusée et journalisée ; ✅ client non signé refusé par le pipe (tests `SecurityTests`, `PipeEndToEndTests`).

## Lot 2 — Diagnostic ✅
- ✅ Collecte des métriques (CPU, RAM, disque, réseau, batterie ; température si WMI la fournit) 🖥️
- ✅ 8 familles de règles, 18 problèmes, score 0–100 + 4 sous-scores, couleurs cohérentes avec les problèmes
- ✅ Accueil : score, problèmes (quoi / pourquoi / Corriger), « Analyser mon PC », « Tout corriger » avec récapitulatif
- ✅ Historique du score en base

## Lot 3 — Protection et système ✅ 🖥️
- ✅ Defender : état, scans rapide/complet/dossier (arrière-plan), signatures, historique, quarantaine (restauration), protections temps réel/cloud/dossiers contrôlés, note « désactivation bloquée par Windows »
- ⛔ Suppression d'un élément de quarantaine : aucune API documentée (voir DECISIONS) ; Defender purge lui-même
- ✅ Pare-feu : activer/désactiver par profil (désactivation en mode Avancé + confirmation), réinitialisation avec export préalable et annulation
- ✅ Windows Update : rechercher, installer (confirmation), réparer (cache renommé, annulable)
- ✅ SFC, DISM, point de restauration, activation de la restauration

## Lot 4 — Performance ✅ 🖥️
- ✅ Processus : liste, CPU/RAM/disque, historique 7 jours, réputation, signature (intégrée + catalogue), arrêter (programmes de l'appelant, jamais les indispensables), retirer du démarrage, désactiver le service (liste protégée), emplacement
- ⛔ Réseau par processus : non affiché (ETW trop coûteux, voir DECISIONS)
- ✅ Optimisations : démarrage (StartupApproved) + tâches tierces, nettoyage (temp, cache WU, corbeille, navigateurs) avec suppression sécurisée anti-jonction, alimentation ; point de restauration + « Annuler »
- ✅ Mini-affichage Win32 click-through, masquage plein écran, icône de notification, réglages
- ✅ Écrans Performance, Processus, Optimisation/Nettoyage

## Lot 5 — Planification et rapports ✅
- ✅ 3 modèles (scan quotidien, nettoyage hebdo, point de restauration hebdo), jour/heure/conditions (inactif, secteur), journal ; exécution via Planificateur Windows → `--run-task` → pipe → catalogue 🖥️
- ✅ Rapport PDF hebdomadaire/mensuel (QuestPDF), fr/en/ar, testé
- ✅ Écrans Planification et Rapports (+ journal d'audit en mode Avancé)

## Lot 6 — Licence et installeur ✅
- ✅ Client : empreinte 4 hachages (tolérance 3/4), jeton Ed25519, DPAPI machine, hors ligne 14 j, revalidation 7 j, détection du recul d'horloge
- ✅ Serveur : activer / revalider / transférer (2/an) / désactiver, administration (page + API), limitation par IP, journal des refus, clé privée par variable d'environnement, SQLite/PostgreSQL, commande `keygen`
- ✅ Mises à jour signées (manifeste Ed25519 + SHA-256 + Authenticode) accessibles depuis les Paramètres
- ✅ Installeur WiX 5 (service, interface, mini-affichage, mise à jour, désinstallation propre) + `build.ps1` (publication, obfuscation, signature) — compilation Windows uniquement 🖥️
- ✅ Obfuscation Obfuscar (Licensing + Service), vérifiée par les tests
- ✅ Écran Licence + assistant de premier lancement

Fin : ✅ tests d'intégration serveur (activation, refus 2e PC, transfert, hors ligne, révocation, expiration, famille, limitation).

## Lot 7 — Tests ✅
- ✅ Couverture : Core 96,9 %, Licensing 97,8 %, serveur de licences 98,1 % (Service 82,8 %, Reporting 98,9 %)
- ✅ Checklist de tests manuels en VM (`docs/TESTS_MANUELS_VM.md`)
- 🖥️ Exécution de la checklist et tests sur 20 PC réels : à la charge du commanditaire (section 14)

## Lot 8 — Lancement ✅ (partie livrable)
- ✅ Guide utilisateur, guide de déploiement
- ✅ Rapport final (`docs/RAPPORT_FINAL.md`)
- ⛔ Bêta publique, site de vente, soumission antivirus : interdits (publication) → commanditaire

## V2 — périmètre étendu par le commanditaire (2026-09-24)

Accord du commanditaire : modules locaux V2 + console PME. Hors périmètre : M10 assistant IA (interface `IAiAssistant` seulement), V3 (modèle IA local, marque blanche). Toute action V2 est Premium, passe par le catalogue fermé et le cycle `SystemActionPipeline`, texte fr/en/ar, confirmation si elle ferme un programme, redémarre ou supprime. Critère de fin de chaque lot : compilation sans avertissement, tests verts, version déposée dans `C:\dev\PcSante-Installation`.

- ✅ V2-1 Planification (M9) : modèles « vérification des mises à jour » (hebdomadaire) et « rapport mensuel » (le 1er du mois, PDF produit par le service dans la langue choisie, Documents publics) ; fréquence « 1er du mois » pour tous les modèles 🖥️
- ✅ V2-2 Réseau (M4/M6) : vidage DNS, réinitialisation Winsock et TCP/IP (point de restauration, confirmation, redémarrage laissé à l'utilisateur), écran Système 🖥️
- ✅ V2-3 Comptes (M6) : comptes locaux et administrateurs affichés (groupe trouvé par SID, toutes langues), compte Invité désactivable et annulable, problème « compte Invité ouvert » dans l'analyse 🖥️
- ✅ V2-4 Antivirus tiers (M2) : produits du Centre de sécurité Windows (activé, à jour) sur l'écran Protection, consultation seulement ; « aucun antivirus » = problème rouge (fait lors des retours de test) 🖥️
- ✅ V2-5 BitLocker (M6, éditions Pro) : état, sauvegarde de la clé de récupération (fichier choisi par l'utilisateur), chiffrement de l'espace utilisé ; administrateur seulement, TPM prête, clé enregistrée avant tout chiffrement ; masqué sur Famille 🖥️
- ✅ V2-6 Sessions (M7, mode Avancé) : sessions locales et RDP (état, heure, IP) par wtsapi32, message (liste fermée), déconnexion, fermeture ; administrateurs seulement ; alerte « connexion à distance depuis une adresse inhabituelle » (relevé toutes les 2 min, adresse nouvelle depuis moins de 24 h) 🖥️
- ✅ V2-7 Optimisation (M4, mode Avancé) : profils de services (bureautique, jeu, portable ; liste prudente, passage en Manuel, jamais désactivé, annulable), effets visuels allégés (profil de l'utilisateur, texte lisse conservé, annulable), disque (defrag /O : TRIM SSD ou défragmentation HDD, en arrière-plan), fichier d'échange (état, retour à la gestion automatique, annulable) 🖥️
- ✅ V2-8 Rapports (M8) : export CSV de la période (scores, actions, menaces), « ; » et UTF-8 avec BOM pour Excel, cellules protégées contre l'injection de formules (Premium)
- ✅ V2-9 Processus (M3) : base de réputation enrichie dans un fichier de données embarqué (ReputationBase.json : programmes inutiles au démarrage et programmes courants), description en langage simple sur l'écran Processus (fr, en, ar)
- ⛔ V2-10 Températures GPU (M5) : LibreHardwareMonitor écarté (pilote WinRing0 détecté par Defender) ; reste « non disponible »
- ⏳ V2-11 Console PME — serveur : ASP.NET Core + SQLite/PostgreSQL, organisations, comptes gérants, codes d'inscription des postes, offre PME au serveur de licences
- ⏳ V2-12 Console PME — postes : inscription depuis l'application, envoi périodique par le service (HTTPS, métriques techniques uniquement, loi 09-08)
- ⏳ V2-13 Console PME — interface web : liste des postes avec score, carte des alertes, détail d'un poste, utilisateurs et licences
- ⏳ V2-14 Console PME — alertes e-mail et rapport consolidé mensuel (SMTP configurable, aucun service payant ; en local, fichiers .eml)
- ⛔ Mise en ligne de la console (hébergement, domaine, HTTPS) : payante et publique → accord du commanditaire requis
