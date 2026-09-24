# Checklist de tests manuels en machine virtuelle

Les actions système réelles (Defender, pare-feu, Windows Update, SFC/DISM, nettoyage, démarrage, services…) ne sont **jamais** exécutées sur la machine de développement (section 9). Elles sont couvertes par des tests automatisés avec simulations, puis vérifiées **à la main dans une VM Windows avec instantanés** selon cette liste.

## Préparation

| # | Étape | OK |
| --- | --- | --- |
| P1 | VM Windows 11 Pro 64 bits à jour + VM Windows 10 22H2 Home (idéalement), 4 Go de RAM minimum | ☐ |
| P2 | **Prendre un instantané « propre »** avant toute installation | ☐ |
| P3 | Installer le .NET 8 Desktop Runtime x64 | ☐ |
| P4 | Serveur de licences de test démarré (voir `GUIDE_DEPLOIEMENT.md` ou la variante Windows Sandbox ci-dessous) et MSI construit avec sa clé publique (`build.ps1 -LicenseServerUrl … -LicensePublicKey …`, sans modifier `branding.props`) | ☐ |
| P5 | MSI **signé** avec un certificat de test (ou build Debug + variable `PCSANTE_DEV_UNSIGNED=1` pour le service — jamais en Release) | ☐ |
| P6 | Outils d'observation : Gestionnaire des tâches, Observateur d'événements, `services.msc`, `taskschd.msc`, Process Explorer | ☐ |

### Variante Windows Sandbox (tests Premium, sans VM)

Scripts dans `tools/sandbox/`. Les clés du serveur de test restent dans `%LOCALAPPDATA%\PcSante-dev\secrets-test.env` (générées par `keygen`, jamais dans le dépôt ni dans la Sandbox).

1. Hôte : `tools/sandbox/demarrer-serveur-licences.ps1` (port 5080) ; une fois, en administrateur : `tools/sandbox/pare-feu-serveur-test.ps1` (retrait : `-Retirer`).
2. Hôte : `tools/sandbox/nouvelles-cles-premium.ps1` → clés dans `C:\dev\PcSante-Sandbox\cles-premium.txt`. Une Sandbox neuve est un nouveau PC : une clé par lancement.
3. Hôte : MSI signé avec le certificat de test et `-LicenseServerUrl "http://127.0.0.1:5080/" -LicensePublicKey <clé publique>`, copié dans `C:\dev\PcSante-Sandbox` avec `PcSante-test.cer`, le .NET 8 Desktop Runtime, `preparer-sandbox.ps1` et `PcSante.wsb`.
4. Double-cliquer `PcSante.wsb`. Dans la Sandbox, double-cliquer `Preparer-la-Sandbox.cmd` dans le dossier du bureau (les scripts .ps1 y sont bloqués, et la commande de démarrage automatique ne s'exécute pas toujours), puis accepter la demande d'élévation (« Oui ») : la Sandbox fait confiance au certificat de test, redirige `127.0.0.1:5080` vers l'hôte (`netsh interface portproxy`), vérifie le serveur et installe .NET 8. À refaire à chaque nouvelle Sandbox (elle repart de zéro), et après un redémarrage de l'hôte (l'adresse de la passerelle change).

## 1. Installeur (Définition de terminé : installe, met à jour, désinstalle proprement)

| # | Test | Résultat attendu | OK |
| --- | --- | --- | --- |
| I1 | Double-clic sur le MSI sans .NET 8 Desktop | Message clair demandant d'installer le runtime, aucune installation partielle | ☐ |
| I2 | Installation normale (utilisateur administrateur) | Assistant minimal, raccourci « PC Santé » dans le menu Démarrer, service « PC Santé » en *Automatique* et *En cours*, compte *Système local* | ☐ |
| I3 | `sc qc PcSanteService` | `START_TYPE : AUTO_START`, `SERVICE_START_NAME : LocalSystem` | ☐ |
| I4 | Droits du dossier `C:\ProgramData\PcSante` | Seuls SYSTEM et Administrateurs ; un utilisateur standard ne peut pas l'ouvrir | ☐ |
| I5 | Mise à jour : installer une version 1.0.1 par-dessus 1.0.0 | Aucune double entrée dans « Applications », historique/score/licence conservés | ☐ |
| I6 | Tentative d'installer une version plus ancienne | Message « Une version plus récente est déjà installée » | ☐ |
| I7 | Désinstallation | Service supprimé, dossier `Program Files\PcSante` supprimé, `ProgramData\PcSante` supprimé, dossier `\PcSante\` du Planificateur supprimé, entrée Run du mini-affichage supprimée | ☐ |
| I8 | Taille du MSI | < 50 Mo | ☐ |

## 2. Sécurité du service (section 5)

| # | Test | Résultat attendu | OK |
| --- | --- | --- | --- |
| S1 | Lancer une copie de `PcSante.exe` depuis le Bureau | Bandeau « Le service ne répond pas » ; journal du service : « Client du named pipe refusé (OutsideInstallDirectory) » | ☐ |
| S2 | Remplacer `PcSante.exe` par un exécutable non signé (administrateur) | Refus « Unsigned » dans le journal du service | ☐ |
| S3 | Client PowerShell ouvrant `\\.\pipe\PcSante.Service.v1` et envoyant `{"command":"FormatDisk"}` | Connexion refusée (client non autorisé) ; aucune action | ☐ |
| S4 | Accès au pipe depuis une autre machine du réseau | Refusé (ACL : réseau interdit) | ☐ |
| S5 | Arrêter le service, lancer un faux serveur de pipe portant le même nom, ouvrir PC Santé | L'interface refuse le faux service (bandeau d'indisponibilité) | ☐ |
| S6 | Journal d'audit : Rapports → mode Avancé → « Journal des actions » | Chaque action : date, utilisateur, action, résultat ; refus visibles | ☐ |
| S7 | Rechercher `powershell.exe` lancé par le service (Process Monitor) pendant toutes les actions | Aucun | ☐ |
| S8 | Mettre une jonction `C:\Users\<u>\AppData\Local\Temp\piege` → `C:\Windows\System32` puis lancer le nettoyage | Aucun fichier de System32 supprimé ; la jonction elle-même n'est pas suivie | ☐ |

## 3. Licence (section 12)

| # | Test | Résultat attendu | OK |
| --- | --- | --- | --- |
| L1 | Activer une clé neuve (écran Licence) | « Licence activée », offre Premium affichée | ☐ |
| L2 | Réinstaller PC Santé sur la même VM, réactiver la même clé | Activée, l'administration ne montre qu'**une** activation | ☐ |
| L3 | Activer la même clé sur la 2e VM | « Cette licence est déjà utilisée sur un autre ordinateur » + proposition « Transférer ma licence » | ☐ |
| L4 | Transférer vers la 2e VM | Premium sur la 2e ; la 1re repasse en Gratuite à la revalidation suivante | ☐ |
| L5 | 3e transfert dans l'année | Refusé (limite de 2) ; « Remettre les transferts à zéro » dans l'administration le débloque | ☐ |
| L6 | Couper le réseau, avancer la date de 10 jours | Toujours Premium | ☐ |
| L7 | Avancer à +15 jours sans réseau | Retour Gratuit (« Pas de connexion depuis plus de 14 jours ») ; données conservées ; retour Premium après reconnexion + revalidation | ☐ |
| L8 | Reculer la date de 3 jours | État « La date du PC semble avoir reculé », offre Gratuite jusqu'à revalidation en ligne | ☐ |
| L9 | Copier `ProgramData\PcSante\license.dat` sur l'autre VM | Illisible (DPAPI machine) : « Aucune licence active » | ☐ |
| L10 | Révoquer la clé dans l'administration, attendre la revalidation (ou forcer via redémarrage du service après 7 jours simulés) | Retour Gratuit sans perte de données | ☐ |
| L11 | Journal des tentatives refusées (administration) | L3/L5 y figurent avec IP et indice de clé | ☐ |

## 4. Diagnostic et Accueil (M1)

| # | Test | Résultat attendu | OK |
| --- | --- | --- | --- |
| D1 | Premier lancement | 3 étapes : langue, licence (ou « Continuer en version gratuite »), analyse automatique avec score ; proposition des 3 tâches | ☐ |
| D2 | Durée de l'analyse | < 60 s | ☐ |
| D3 | Désactiver le pare-feu public (Paramètres Windows), relancer l'analyse | Problème rouge « Protection contre les intrusions désactivée (Wi-Fi public) », score rouge | ☐ |
| D4 | « Corriger » sur ce problème | « Protection contre les intrusions activée (Wi-Fi public) », score remonte | ☐ |
| D5 | « Tout corriger » avec plusieurs problèmes | Récapitulatif avant exécution, puis bilan clair | ☐ |
| D6 | Offre Gratuite : cliquer « Corriger » | « Cette action est disponible avec l'offre Premium » + bouton « Passer à Premium » | ☐ |
| D7 | Antivirus tiers installé | Aucun faux problème Defender | ☐ |

## 5. Protection et système (M2, M6)

| # | Test | Résultat attendu | OK |
| --- | --- | --- | --- |
| A1 | Scan rapide | « Scan lancé… » ; à la fin, entrée « terminé » dans le journal | ☐ |
| A2 | Scan d'un dossier contenant le fichier de test EICAR | Menace détectée dans l'historique, fichier en quarantaine | ☐ |
| A3 | Restaurer l'élément de quarantaine | Confirmation, puis « Fichier restauré » | ☐ |
| A4 | Désactiver la protection en temps réel (Sécurité Windows), puis Activer depuis PC Santé | « Surveillance permanente activée » | ☐ |
| A5 | Protection contre les falsifications active : tenter une désactivation manuelle via un autre outil | Note affichée par PC Santé expliquant la limite ; PC Santé ne propose jamais la désactivation | ☐ |
| A6 | Mise à jour de l'antivirus | « Antivirus mis à jour » | ☐ |
| A7 | Rechercher / installer les mises à jour Windows | Nombre trouvé, installation avec message « Redémarrez » si nécessaire | ☐ |
| A8 | Réparer une mise à jour bloquée | `C:\Windows\SoftwareDistribution.pcsante-…` présent ; bouton Annuler le remet en place | ☐ |
| A9 | SFC puis DISM | Progression visible, message final clair ; point de restauration créé avant | ☐ |
| A10 | Réinitialiser le pare-feu | Confirmation, export `.wfw` dans `ProgramData\PcSante\backups`, Annuler réimporte | ☐ |
| A11 | Restauration du système désactivée | Problème « Restauration désactivée » ; toute optimisation refusée tant qu'elle n'est pas activée | ☐ |

## 6. Performance, processus, optimisation (M3, M4, M5)

| # | Test | Résultat attendu | OK |
| --- | --- | --- | --- |
| O1 | Nettoyage (4 cases) | Confirmation listant les tailles, point de restauration, « Nettoyage terminé : X libérés » | ☐ |
| O2 | Navigateur ouvert pendant le nettoyage du cache | Ce navigateur est ignoré, aucun plantage | ☐ |
| O3 | Désactiver un programme au démarrage, redémarrer | Il ne démarre plus ; le Gestionnaire des tâches l'affiche « Désactivé » ; Annuler le réactive | ☐ |
| O4 | Désactiver une tâche planifiée tierce | Désactivée dans `taskschd.msc` ; Annuler la réactive | ☐ |
| O5 | Changer le mode d'alimentation, puis Annuler | Retour au mode précédent | ☐ |
| O6 | Processus : arrêter Notepad | Confirmation, « Programme arrêté » | ☐ |
| O7 | Processus : tenter d'arrêter `lsass`/`csrss` | Bouton absent ; si forcé via pipe : « indispensable à Windows » | ☐ |
| O8 | Processus d'un autre utilisateur (2 sessions) | Refus « appartient à un autre utilisateur » | ☐ |
| O9 | Désactiver le service d'un programme tiers ; tenter `WinDefend` | Tiers : désactivé + Annuler ; WinDefend : refus | ☐ |
| O10 | Historique 7 jours | Après quelques heures, les programmes gourmands apparaissent | ☐ |
| M1 | Mini-affichage activé | Barre en haut à droite, toujours visible, **les clics passent au travers** | ☐ |
| M2 | Jeu/vidéo plein écran, présentation PowerPoint | Barre masquée puis réaffichée | ☐ |
| M3 | Mode icône de notification | Info-bulle avec les mesures ; clic droit → « Fermer le mini-affichage » | ☐ |
| M4 | Position, taille, opacité, indicateurs | Appliqués sans redémarrer | ☐ |
| M5 | Températures | Valeur, ou « n/d » si le PC ne l'expose pas (pas d'erreur) | ☐ |

## 7. Planification et rapports (M8, M9)

| # | Test | Résultat attendu | OK |
| --- | --- | --- | --- |
| T1 | Activer les 3 tâches recommandées | 3 tâches dans `\PcSante\` (SYSTEM, conditions inactif/secteur respectées) | ☐ |
| T2 | Exécuter une tâche manuellement dans `taskschd.msc` | Entrée dans le journal d'exécution de PC Santé ; actions dans l'audit (« Planificateur ») | ☐ |
| T3 | Modifier jour/heure | Déclencheur mis à jour | ☐ |
| T4 | Lancer `PcSante.Service.exe --run-task WeeklyCleanup` alors que la tâche est désactivée | Refus « tâche non programmée » | ☐ |
| R1 | Générer le rapport PDF mensuel en français, anglais, arabe | PDF lisible, score en couleur, problèmes expliqués, menaces, actions ; arabe de droite à gauche | ☐ |

## 8. Interface pour non-informaticiens (section 11)

| # | Test | Résultat attendu | OK |
| --- | --- | --- | --- |
| U1 | Mode Simple par défaut | Seulement Accueil, Protection, Nettoyage, Rapports (+ Licence, Paramètres) | ☐ |
| U2 | Mode Avancé | Performance, Processus, Système, Planification apparaissent | ☐ |
| U3 | Un seul bouton principal (bleu, grand) par écran | Vérifié sur chaque écran | ☐ |
| U4 | Navigation complète au clavier (Tab, Entrée, flèches) | Toutes les actions atteignables | ☐ |
| U5 | Narrateur Windows | Boutons, problèmes et messages de résultat annoncés | ☐ |
| U6 | Thèmes clair/sombre, contraste élevé Windows | Lisible, couleurs vert/orange/rouge conservées | ☐ |
| U7 | Arabe | Interface entièrement de droite à gauche, aucun texte français restant | ☐ |
| U8 | **Critère de validation** : une personne sans compétence informatique installe, active, analyse, corrige, génère un rapport | < 5 minutes, sans aide | ☐ |

## 9. Performances (section 5)

Mesures avec l'Analyseur de performances (`perfmon`) sur 10 minutes, PC au repos, interface fermée.

| # | Mesure | Cible | Valeur mesurée | OK |
| --- | --- | --- | --- | --- |
| N1 | `Processus(PcSante.Service)\% temps processeur` / nombre de cœurs | < 1 % | | ☐ |
| N2 | Plage de travail privée du service | < 80 Mo | | ☐ |
| N3 | Plage de travail privée de `PcSante.Overlay` (Gestionnaire des tâches, colonne « Mémoire ») | < 30 Mo | | ☐ |
| N4 | Démarrage de l'interface (clic → fenêtre utilisable) | < 2 s | | ☐ |
| N5 | Analyse complète | < 60 s | | ☐ |

Si N3 dépasse 30 Mo : vérifier `System.GC.ConserveMemory` dans `PcSante.Overlay.runtimeconfig.json` ; en dernier recours, publier le mini-affichage en NativeAOT (compilation sous Windows).

**Restaurer l'instantané « propre » entre deux campagnes.**
