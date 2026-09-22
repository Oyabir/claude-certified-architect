# Décisions prises seul

Format : date · sujet · option retenue · raison.

- 2026-09-22 · CLAUDE.md existant (cours Python) · Réécrit pour PC Santé, ancien contenu conservé en fin de fichier · la consigne demande un CLAUDE.md PC Santé sans perdre l'information existante.
- 2026-09-22 · Machine de développement · Chantier mené sous Linux (conteneur cloud), SDK .NET 8 Microsoft ajouté, WPF compilé via `EnableWindowsTargeting` · pas de Windows disponible ; compilation et tests non-Windows possibles, actions Windows couvertes par simulations + checklist VM.
- 2026-09-22 · Nom du produit · `branding.props` unique (ProductName, Company, noms de pipe/dossiers) lu par tous les projets et l'installeur · section 10 : nom centralisé dans un seul fichier.
- 2026-09-22 · Gestion des paquets · Central Package Management (`Directory.Packages.props`) · versions homogènes, un seul endroit à auditer.
- 2026-09-22 · Analyseurs · `EnableNETAnalyzers` + `AnalysisLevel=latest` + `TreatWarningsAsErrors` · section 13 : zéro avertissement, analyseurs activés.
- 2026-09-22 · FluentAssertions · Version 6.12.x (Apache 2.0) · la v8 est sous licence commerciale payante (interdit de dépenser).
- 2026-09-22 · Ed25519 · BouncyCastle.Cryptography (managé, MIT) · .NET 8 n'a pas Ed25519 natif ; bibliothèque éprouvée, sans dépendance native.
- 2026-09-22 · Températures · WMI `MSAcpi_ThermalZoneTemperature` au lieu de LibreHardwareMonitor · LibreHardwareMonitor embarque le pilote WinRing0 détecté par Defender (risque n°1 de faux positif, section 8) ; si WMI ne fournit rien : « non disponible ».
- 2026-09-22 · PowerShell · Aucun script PowerShell : WMI, COM et exécutables système à chemin absolu avec arguments fixes · plus strict que « scripts signés en mode contraint » (section 5), surface d'attaque réduite.
- 2026-09-22 · Stockage des réglages · SQLite (service, ProgramData) pour historique/audit/tâches ; JSON utilisateur (%LOCALAPPDATA%) pour langue/thème/mode/mini-affichage · les préférences d'affichage sont par utilisateur et ne sont pas des actions système.
- 2026-09-22 · Mini-affichage · Fenêtre Win32 pure (P/Invoke + GDI), sans WPF · WPF consomme > 50 Mo ; objectif < 30 Mo (section 5).
- 2026-09-22 · Rapports PDF · Projet `src/PcSante.Reporting` (QuestPDF), généré côté interface avec les données du service · générer un PDF n'est pas une action système ; isolé pour être testable hors WPF.
- 2026-09-22 · Tâches planifiées · Planificateur de tâches Windows (bibliothèque TaskScheduler) déclenchant l'exécutable du service en mode `--run-task`, qui passe par le named pipe · gère nativement « PC inactif » et « sur secteur » (section 3), exécution toujours via le catalogue.
- 2026-09-22 · 3 modèles MVP · Scan antivirus quotidien, nettoyage hebdomadaire, point de restauration hebdomadaire · ce sont les plus protecteurs et ceux proposés au premier lancement.
- 2026-09-22 · Démarrage · Désactivation via les clés `StartupApproved` (comme le Gestionnaire des tâches) · réversible à 100 %, aucune suppression.
- 2026-09-22 · Licence : empreinte · 4 hachages SHA-256 salés séparés (un par élément) envoyés au serveur · seule façon de permettre la tolérance 3/4 sans envoyer de donnée brute.
- 2026-09-22 · Licence : validité du jeton · Jeton valable 14 jours, revalidation tous les 7 jours · implémente directement la tolérance hors ligne de 14 jours.
- 2026-09-22 · Licence : qui la gère · Le service (SYSTEM) active, stocke (DPAPI machine) et contrôle le jeton ; l'interface ne fait que demander · l'interface n'est pas de confiance (section 12).
- 2026-09-22 · Clés de licence en base serveur · Stockées hachées (SHA-256) avec un indice des 4 derniers caractères · une fuite de la base ne révèle pas les clés.
- 2026-09-22 · Transfert multi-postes (Famille) · Le transfert libère l'activation vue le moins récemment · comportement prévisible sans question à l'utilisateur.
- 2026-09-22 · Clé publique de licence · Fournie à la compilation (`PcSanteLicensePublicKey` dans `branding.props`), vide par défaut = licence non configurée (offre Gratuite) · aucune paire de clés de production dans le dépôt ; outil `keygen` fourni.
- 2026-09-22 · Vérification du client du pipe · Release : exécutable dans le dossier d'installation ET signé par le même certificat que le service ; Debug : signature facultative (dossier d'installation toujours exigé) · permet le développement sans certificat sans affaiblir les builds Release.
- 2026-09-22 · Sessions (M7), BitLocker, réseau, comptes, effets visuels, profils de services · Non développés · hors MVP selon la section 7 (V2).
- 2026-09-22 · Réseau par processus · Non affiché (« non disponible ») · nécessite ETW en temps réel, coûteux en CPU (objectif < 1 %) ; reporté.
- 2026-09-22 · Suppression d'un élément en quarantaine · Non proposée (restauration seule) · aucune API documentée pour supprimer un élément précis (MpCmdRun, WMI MSFT_MpThreat examinés) ; Defender purge lui-même la quarantaine et un élément en quarantaine est déjà neutralisé.
- 2026-09-22 · Exécution des actions · Une seule action système à la fois (verrou global du service), lectures en parallèle · évite deux modifications concurrentes du système (option la plus prudente).
- 2026-09-22 · Actions longues · Scans Defender en arrière-plan (résultat dans le journal) ; SFC, DISM, installation des mises à jour en attente du résultat avec indicateur de progression · un scan complet peut durer des heures, les réparations donnent un résultat clair en fin d'action.
- 2026-09-22 · Premier lancement et changement de langue · Changer la langue recharge la fenêtre (l'assistant reprend à l'étape 2) · les textes sont résolus au chargement ; plus simple et plus fiable qu'une liaison dynamique de chaque texte.
