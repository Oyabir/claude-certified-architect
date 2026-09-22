# Guide utilisateur — PC Santé

*Votre PC en bonne santé, expliqué simplement, réparé en un clic.*

## 1. Installer PC Santé

1. Double-cliquez sur le fichier `PcSante-1.0.0.msi`.
2. Si Windows indique qu'il manque « .NET 8 Desktop Runtime », installez-le depuis le site de Microsoft (gratuit), puis relancez le fichier.
3. Acceptez les conditions puis cliquez sur **Installer**.
4. Ouvrez **PC Santé** depuis le menu Démarrer.

PC Santé fonctionne sous Windows 10 (22H2) et Windows 11, en 64 bits.

## 2. Le premier lancement (3 étapes)

1. **Langue** : français, anglais ou arabe.
2. **Licence** : collez la clé reçue à l'achat (elle ressemble à `PCS-XXXXX-XXXXX-XXXXX-XXXXX`), puis **Activer ma licence**. Sans clé, choisissez **Continuer en version gratuite**.
3. **Première analyse** : elle se lance toute seule et affiche votre score. PC Santé vous propose ensuite d'activer 3 tâches automatiques (scan antivirus quotidien, nettoyage et point de restauration chaque semaine) en un seul clic.

## 3. L'écran d'accueil

- **Le score (0 à 100)** et sa couleur :
  - **vert** : tout va bien ;
  - **orange** : à surveiller ;
  - **rouge** : à corriger.
- **Quatre sous-scores** : Sécurité, Performance, Stabilité, Stockage.
- **La liste des problèmes** : pour chacun, ce qui ne va pas, pourquoi c'est important, et un bouton pour le corriger.
- **Tout corriger** : PC Santé affiche d'abord la liste de ce qu'il va faire, puis corrige tout.
- **Analyser mon PC** : relance une analyse (moins d'une minute).

Après chaque action, un message en haut de l'écran dit clairement ce qui s'est passé. Le lien **Détails** donne les informations techniques si un technicien vous les demande.

## 4. Les écrans

| Écran | À quoi il sert |
| --- | --- |
| **Accueil** | Score, problèmes, « Tout corriger ». |
| **Protection** | Antivirus (scan rapide, complet, d'un dossier ; mise à jour), protections à activer, *protection contre les intrusions* (le pare-feu), menaces trouvées, quarantaine. |
| **Nettoyage** | Supprimer les fichiers inutiles, choisir les programmes qui se lancent au démarrage, annuler une modification. |
| **Rapports** | Évolution du score et **rapport PDF** de la semaine ou du mois. |
| **Licence** | Activer, transférer ou désactiver votre licence. |
| **Paramètres** | Langue, apparence (clair / sombre), **mode Avancé**, mini-affichage, mises à jour de PC Santé. |

En **mode Avancé** (Paramètres), quatre écrans s'ajoutent : **Performance** (graphiques en temps réel), **Processus** (programmes en cours et ce qu'ils consomment), **Système** (mises à jour de Windows, réparation, points de restauration) et **Planification** (tâches automatiques).

## 5. Sécurité : ce que PC Santé fait pour vous protéger

- **Avant chaque modification**, PC Santé crée un point de restauration Windows ou sauvegarde l'état précédent. Si quelque chose ne va pas, rien n'est modifié.
- La plupart des modifications peuvent être annulées depuis **Nettoyage → Annuler une modification**.
- **Confirmation obligatoire** avant de fermer un programme, de supprimer des fichiers ou de redémarrer.
- PC Santé ne ferme jamais un programme indispensable à Windows, ni un programme d'un autre utilisateur du PC.
- Windows interdit à toute application de désactiver Microsoft Defender : PC Santé peut l'activer, jamais le désactiver.

## 6. Le mini-affichage

Une petite barre en haut de l'écran indique en permanence le processeur, la mémoire, le disque et le réseau. Les clics passent au travers : elle ne gêne jamais. Elle se cache toute seule pendant les jeux, les vidéos en plein écran et les présentations.

Pour l'activer : **Paramètres → Mini-affichage** (ou **Performance → Afficher le mini-affichage** en mode Avancé). Vous pouvez choisir sa position, la taille du texte, l'opacité, les indicateurs, ou une simple icône près de l'horloge.

## 7. La licence

- **Une clé = un PC.** Si vous essayez la même clé sur un autre ordinateur, PC Santé affiche : « Cette licence est déjà utilisée sur un autre ordinateur ».
- **Vous changez d'ordinateur ?** Sur le nouveau PC, saisissez votre clé puis cliquez **Transférer ma licence** (2 fois par an maximum). Ou, sur l'ancien PC, **Désactiver la licence sur ce PC** avant de la saisir sur le nouveau.
- **Sans Internet**, PC Santé reste en Premium pendant 14 jours. Au-delà, il repasse en version gratuite jusqu'à la prochaine connexion, sans rien perdre.
- **Offre Gratuite** : score, diagnostic et mini-affichage. **Premium** : toutes les corrections en un clic, nettoyage, tâches automatiques, rapports PDF.

## 8. Questions fréquentes

**Le bandeau rouge « Le service PC Santé ne répond pas » s'affiche.** Redémarrez le PC. Si le problème continue, réinstallez PC Santé.

**Une action a échoué.** Lisez le message : il indique quoi faire. Cliquez **Détails** pour obtenir l'information à transmettre au support.

**Je veux revenir en arrière.** Utilisez **Nettoyage → Annuler une modification**, ou la restauration du système de Windows (un point est créé avant chaque changement important).

**Comment désinstaller ?** Paramètres Windows → Applications → PC Santé → Désinstaller. Le service, les tâches automatiques et les données de PC Santé sont supprimés.
