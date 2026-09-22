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

## Hors MVP (non développé, section 7)
M7 Sessions, BitLocker, réseau (DNS/Winsock), comptes, effets visuels, profils de services, antivirus tiers, M10 assistant IA (interface `IAiAssistant` seulement), console PME, export CSV PME.
