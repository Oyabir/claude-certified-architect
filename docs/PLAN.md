# Plan du chantier PC Santé

Ordre = planning MVP (section 7 du cahier des charges). Statuts : ✅ fait · 🔄 en cours · ⏳ à faire · ⛔ désactivé/justifié.

Critère commun à tous les lots : `dotnet build PcSante.sln -c Release` sans erreur ni avertissement, `dotnet test` vert, commit en français.

## Lot 0 — Démarrage ✅
- ✅ Lecture complète du cahier des charges
- ✅ CLAUDE.md, PLAN.md, DECISIONS.md
- ✅ Solution .NET 8 selon la structure de la section 13

Fin : solution vide qui compile, structure en place.

## Lot 1 — Fondations (semaines 1–2) ⏳
- ⏳ Core : modèles, catalogue des identifiants de commande, cycle d'action système, abstractions Windows, audit
- ⏳ Ipc : contrat JSON, encadrement des messages, validation stricte, authentification du client (ACL + signature)
- ⏳ Service : Worker Service Windows, serveur named pipe, catalogue de commandes, refus hors catalogue, journal d'audit, SQLite (EF Core), Serilog
- ⏳ App : squelette WPF-UI, navigation latérale, mode Simple/Avancé, ressources fr/en/ar, RTL

Fin : commande hors catalogue refusée et client non signé refusé (tests de sécurité), audit écrit en base, l'interface s'ouvre sur l'Accueil.

## Lot 2 — Diagnostic (semaines 3–4) ⏳
- ⏳ Collecte des métriques (CPU, RAM, disque, réseau, batterie, températures si disponibles)
- ⏳ Règles de diagnostic + score 0–100, 4 sous-scores, couleurs
- ⏳ Écran Accueil : score, problèmes (quoi / pourquoi / Corriger), « Analyser mon PC », « Tout corriger » avec récapitulatif

Fin : tests unitaires du score et des règles verts ; historique du score enregistré.

## Lot 3 — Protection et système (semaines 5–6) ⏳
- ⏳ Defender : état, scans rapide/complet/personnalisé, signatures, quarantaine, historique, protections (temps réel, cloud, dossiers contrôlés), limite « désactivation bloquée par Windows »
- ⏳ Pare-feu : activer/désactiver par profil, réinitialiser (avec export préalable)
- ⏳ Windows Update : rechercher, installer, réparer
- ⏳ SFC, DISM, point de restauration
- ⏳ Écrans Protection et Système

Fin : chaque action passe par le cycle complet, testée avec simulations.

## Lot 4 — Performance (semaines 7–8) ⏳
- ⏳ Processus : liste, CPU/RAM/disque, historique 7 jours, réputation, signature, arrêter / retirer du démarrage / désactiver le service
- ⏳ Optimisations : démarrage (+ tâches tierces), nettoyage (temp, cache WU, corbeille, navigateurs), plan d'alimentation ; point de restauration + « Annuler »
- ⏳ Mini-affichage Win32 click-through, masquage plein écran, icône de notification
- ⏳ Écrans Performance, Processus, Optimisation (Nettoyage)

Fin : annulation testée pour chaque optimisation réversible.

## Lot 5 — Planification et rapports (semaine 9) ⏳
- ⏳ 3 modèles de tâches (scan quotidien, nettoyage hebdo, point de restauration hebdo), jour/heure/conditions, journal
- ⏳ Rapport PDF hebdomadaire/mensuel lisible (QuestPDF)
- ⏳ Écrans Planification et Rapports

Fin : PDF généré en test, planification simulée testée.

## Lot 6 — Licence et installeur (semaine 10) ⏳
- ⏳ Licensing client : empreinte, jeton Ed25519, DPAPI, hors ligne 14 j, horloge, revalidation 7 j
- ⏳ Serveur de licences : activer / revalider / transférer / désactiver, administration, limitation par IP, journal des refus, clé privée par variable d'environnement
- ⏳ Mises à jour signées et vérifiées
- ⏳ Installeur WiX (service, interface, mini-affichage, mise à jour, désinstallation propre)
- ⏳ Écran Licence + assistant de premier lancement

Fin : tests d'intégration serveur (activation, refus 2e PC, transfert, hors ligne, révocation) verts.

## Lot 7 — Tests (semaine 11) ⏳
- ⏳ Couverture ≥ 70 % sur Core, Licensing, serveur de licences
- ⏳ Checklist de tests manuels en VM (`docs/TESTS_MANUELS_VM.md`)

## Lot 8 — Lancement (semaine 12) ⏳
- ⏳ Guide utilisateur, guide de déploiement
- ⏳ Rapport final (`docs/RAPPORT_FINAL.md`)
- ⛔ Bêta publique, site de vente, soumission antivirus : interdits (publication) → à la charge du commanditaire
