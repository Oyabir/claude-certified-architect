# Guide de déploiement — PC Santé

Public : le commanditaire ou son prestataire technique.

## 1. Vue d'ensemble

| Élément | Où | Rôle |
| --- | --- | --- |
| `PcSante.exe` (interface WPF) | PC client, sans privilège | Affiche, demande, n'agit jamais sur le système |
| `PcSante.Service.exe` | PC client, service Windows SYSTEM | Exécute uniquement les commandes du catalogue fermé ; journal d'audit ; licence |
| `PcSante.Overlay.exe` | PC client, sans privilège | Mini-affichage, lecture seule |
| `PcSante.LicenseServer` | Serveur (VPS Linux ou Windows) | Activer, revalider, transférer, désactiver ; administration |

Communication PC ↔ service : named pipe `\\.\pipe\PcSante.Service.v1` (ACL : SYSTEM, Administrateurs, utilisateurs authentifiés **locaux** ; réseau refusé), client vérifié (dossier d'installation + même signature Authenticode que le service).

## 2. Prérequis

- **Poste de build Windows 10/11 x64** (WiX ne fonctionne que sous Windows) avec .NET 8 SDK (8.0.100 ou plus), Git, PowerShell 7 ou Windows PowerShell 5.1, Windows SDK (pour `signtool.exe`).
- Certificat de signature de code **OV** (EV ensuite) — voir section 5 du cahier des charges.
- Serveur de licences : .NET 8 ASP.NET Core Runtime, un nom de domaine avec HTTPS.

## 3. Compiler et tester

```bash
dotnet build PcSante.sln -c Release          # zéro avertissement exigé (TreatWarningsAsErrors)
dotnet test  PcSante.sln -c Release          # tous les tests automatisés
dotnet test  PcSante.sln -c Release --collect:"XPlat Code Coverage"   # couverture (Cobertura)
```

La solution compile aussi sous Linux/macOS (`EnableWindowsTargeting`) ; seuls l'installeur et l'exécution réelle exigent Windows.

## 4. Marque, clé publique et URL du serveur (un seul fichier)

Tout se règle dans **`branding.props`** à la racine :

| Propriété | Exemple | Rôle |
| --- | --- | --- |
| `PcSanteProductName` | `PC Santé` | Nom commercial affiché partout |
| `PcSanteCompany` | `Ma Société SARL` | Éditeur |
| `PcSanteTechnicalName` | `PcSante` | Dossiers, service, pipe (ne pas changer après la première diffusion) |
| `PcSanteVersion` | `1.0.1` | Version (incrémenter à chaque mise à jour) |
| `PcSanteLicenseServerUrl` | `https://licences.mondomaine.ma/` | Serveur de licences (HTTPS obligatoire) |
| `PcSanteLicensePublicKey` | `base64…` | **Clé publique** Ed25519 du serveur (vide = licence non configurée : offre Gratuite seulement) |

> Changer `PcSanteTechnicalName` modifie le nom du service et du pipe : à faire avant la bêta publique uniquement. Les exécutables gardent les noms `PcSante*.exe` (liste fermée vérifiée par le service).

## 5. Serveur de licences

### 5.1 Générer les clés (une seule fois)

```bash
dotnet run --project server/PcSante.LicenseServer -c Release -- keygen
```

Affiche trois valeurs, **rien n'est écrit sur disque** :

- `PCSANTE_LICENSE_PRIVATE_KEY=…` → secret du serveur (gestionnaire de secrets, **jamais** dans Git) ;
- `PCSANTE_ADMIN_API_KEY=…` → clé de la page d'administration (≥ 32 caractères) ;
- la **clé publique** → à copier dans `branding.props` (`PcSanteLicensePublicKey`), puis recompiler le MSI.

Perdre la clé privée = impossible d'émettre de nouveaux jetons compatibles : la sauvegarder hors ligne.

### 5.2 Publier et installer (exemple Linux / systemd)

```bash
dotnet publish server/PcSante.LicenseServer -c Release -o /opt/pcsante-licences
sudo useradd --system --home /var/lib/pcsante-licences pcsante
sudo mkdir -p /var/lib/pcsante-licences && sudo chown pcsante /var/lib/pcsante-licences
```

`/etc/pcsante-licences.env` (droits `600`, propriétaire root) :

```
PCSANTE_LICENSE_PRIVATE_KEY=...
PCSANTE_ADMIN_API_KEY=...
ConnectionStrings__Licenses=Data Source=/var/lib/pcsante-licences/licenses.db
Logging__Directory=/var/lib/pcsante-licences/logs
ASPNETCORE_URLS=http://127.0.0.1:5080
```

`/etc/systemd/system/pcsante-licences.service` :

```ini
[Unit]
Description=Serveur de licences PC Santé
After=network.target

[Service]
User=pcsante
WorkingDirectory=/opt/pcsante-licences
EnvironmentFile=/etc/pcsante-licences.env
ExecStart=/usr/bin/dotnet /opt/pcsante-licences/PcSante.LicenseServer.dll
Restart=always
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=/var/lib/pcsante-licences

[Install]
WantedBy=multi-user.target
```

Le `WorkingDirectory` doit être le dossier publié (la page d'administration est servie depuis `wwwroot`).

### 5.3 HTTPS (proxy inverse)

Exemple Caddy (certificat Let's Encrypt automatique et gratuit) :

```
licences.mondomaine.ma {
    reverse_proxy 127.0.0.1:5080
}
```

Le serveur lit `X-Forwarded-For` : la limitation par IP (20 requêtes/min par défaut, `RateLimiting__PublicPerMinute`) s'applique à l'IP réelle. N'exposer **que** le proxy HTTPS.

### 5.4 PostgreSQL (optionnel)

```
Database__Provider=PostgreSql
ConnectionStrings__Licenses=Host=...;Database=pcsante;Username=...;Password=...
```

Le schéma est créé au premier démarrage. Hébergement : Maroc ou Europe (décision du commanditaire, loi 09-08 / CNDP).

### 5.5 Administration

`https://licences.mondomaine.ma/admin/` → saisir la clé d'administration :

- **Générer des clés** (Premium 1 PC, Famille 3 PC, durée en jours) : affichées **une seule fois** (le serveur ne garde qu'une empreinte SHA-256 et les 4 derniers caractères) ;
- **Voir les activations** d'une licence (recherche par les 4 derniers caractères) ;
- **Révoquer** une clé (le PC repasse en Gratuit à la revalidation suivante, sans perte de données) ;
- **Remettre les transferts à zéro** ;
- **Tentatives refusées** (IP, indice de clé, raison).

API équivalente (en-tête `X-Admin-Key`) : `POST /admin/api/licenses`, `GET /admin/api/licenses?hint=ABCD`, `POST /admin/api/licenses/{id}/revoke`, `POST /admin/api/licenses/{id}/reset-transfers`, `GET /admin/api/refused`.

### 5.6 Sauvegardes

Sauvegarder chaque jour `licenses.db` (ou la base PostgreSQL) et, **hors ligne**, la clé privée.

## 5 bis. Console PME (offre PME, V2)

Projet `server/PcSante.PmeConsole` : postes des entreprises clientes, alertes, rapports consolidés, e-mails aux gérants. Service indépendant du serveur de licences (ASP.NET Core, SQLite ou PostgreSQL), à héberger au Maroc ou en Europe (loi 09-08, CNDP) derrière un proxy HTTPS, comme le serveur de licences (§ 5.3).

1. **Publier** : `dotnet publish server/PcSante.PmeConsole -c Release -o /opt/pcsante-console` ; le dossier de travail doit être le dossier publié (interface web dans `wwwroot`).
2. **Configurer** (variables d'environnement) :
   - `ASPNETCORE_URLS=http://127.0.0.1:5090` (seul le proxy HTTPS est exposé) ;
   - `ConnectionStrings__Console=…` et `Database__Provider=PostgreSql` en production ;
   - `Email__From`, `Email__SmtpHost`, `Email__SmtpPort`, `Email__UserName` ; **mot de passe SMTP uniquement dans `PCSANTE_SMTP_PASSWORD`** (jamais dans un fichier du dépôt). Sans SMTP, les e-mails sont écrits en `.eml` dans `Email__PickupDirectory`.
3. **Créer une organisation cliente** (une par entreprise) :
   `dotnet PcSante.PmeConsole.dll create-organization "Nom de l'entreprise" gerant@entreprise.ma <nombre de postes>`
   La commande affiche **une seule fois** le mot de passe provisoire du gérant (à changer à la première connexion) et le code d'inscription `PME-XXXXX-XXXXX-XXXXX`.
4. **Postes** : dans `branding.props`, renseigner `PcSanteConsoleUrl` (URL HTTPS de la console, terminée par « / ») puis reconstruire le MSI. Sur chaque PC : PC Santé → Paramètres → Console d'entreprise → code → « Rattacher ce PC ». Le poste envoie ensuite son état après chaque analyse et toutes les 6 heures.
5. **Données** : score, sous-scores, codes des problèmes, version de Windows et de PC Santé, nom du PC ; ni nom d'utilisateur ni fichier. Sauvegarder la base comme celle du serveur de licences.

Test local : `tools/pme/demarrer-console.ps1` (http://127.0.0.1:5090/), et la version de test `tools/installation-test/construire.ps1` vise cette adresse par défaut.

## 6. Construire l'installeur MSI (Windows)

```powershell
$env:PCSANTE_SIGN_THUMBPRINT = "<empreinte SHA-1 du certificat de signature>"
./installer/build.ps1 -Configuration Release
```

Le script :

1. publie l'interface, le service et le mini-affichage (`win-x64`, dépendants du .NET 8 Desktop Runtime) dans `installer/publish/` ;
2. **obfusque** `PcSante.Licensing.dll` et `PcSante.Service.dll` (Obfuscar, téléchargé depuis NuGet ; table de correspondance dans `%TEMP%\pcsante-obfuscar\` — à archiver hors du dépôt) ;
3. **signe** les exécutables et DLL `PcSante*` (signtool, horodatage) ;
4. compile `installer/PcSante.Installer.wixproj` (WiX 5) ;
5. signe le MSI et vérifie sa taille (< 50 Mo).

Sans `PCSANTE_SIGN_THUMBPRINT`, rien n'est signé : en Release, **le service refuse alors l'interface** (vérification de signature du named pipe). Pour un essai en VM sans certificat : compiler en Debug et définir `PCSANTE_DEV_UNSIGNED=1` pour le service (jamais en production).

### Contenu de l'installation

- `C:\Program Files\PcSante\` : binaires ; service `PcSanteService` (SYSTEM, automatique, redémarrage en cas d'échec).
- `C:\ProgramData\PcSante\` : base SQLite (historique, audit), `license.dat` (DPAPI machine), `logs\`, `backups\`, `updates\` — accès SYSTEM et Administrateurs uniquement.
- `%LOCALAPPDATA%\PcSante\settings.json` : préférences de l'utilisateur (langue, thème, mode, mini-affichage).
- Planificateur : dossier `\PcSante\` (tâches activées par l'utilisateur).

## 7. Publier une mise à jour de PC Santé

1. Incrémenter `PcSanteVersion` dans `branding.props`, reconstruire et signer le MSI.
2. Déposer le MSI sur un hébergement **HTTPS**.
3. Calculer son empreinte : `Get-FileHash .\PcSante-1.0.1.msi -Algorithm SHA256`.
4. Sur le serveur de licences, renseigner :

```
Updates__Version=1.0.1
Updates__DownloadUrl=https://telechargement.mondomaine.ma/PcSante-1.0.1.msi
Updates__Sha256=<empreinte>
Updates__PublishedAt=2026-10-15T00:00:00Z
```

Le serveur signe ce manifeste avec sa clé Ed25519. Le service n'installe la mise à jour que si : le manifeste est signé par le serveur, l'empreinte SHA-256 du MSI correspond, **et** le MSI porte la signature Authenticode du même certificat que le service installé.

## 8. Journaux

| Composant | Emplacement |
| --- | --- |
| Service | `C:\ProgramData\PcSante\logs\service-AAAAMMJJ.log` (30 jours) |
| Audit des actions | base `pcsante.db`, table `Audit` ; visible dans Rapports → Journal des actions (mode Avancé) |
| Serveur de licences | `Logging__Directory/licenseserver-AAAAMMJJ.log` (90 jours) |

## 9. Checklist avant bêta publique

- [ ] Certificat de signature acheté, `build.ps1` exécuté avec signature
- [ ] Serveur de licences hébergé en HTTPS, clé publique dans `branding.props`
- [ ] Nom commercial et logo définitifs (`branding.props`, `src/PcSante.App/Assets/pcsante.ico`)
- [ ] Conditions d'utilisation définitives (`installer/License.rtf`)
- [ ] Checklist `docs/TESTS_MANUELS_VM.md` passée sur Windows 10 et 11 (Home et Pro), puis tests sur 20 PC réels
- [ ] Soumission des binaires signés à Microsoft et aux éditeurs antivirus
