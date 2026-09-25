# Construit la version de TEST « toutes fonctions » de PC Santé et la dépose dans UN dossier fixe,
# en remplaçant la précédente. Ce dossier contient aussi Installer.cmd et Desinstaller.cmd.
#
#   powershell -ExecutionPolicy Bypass -File tools\installation-test\construire.ps1
#
# - Premium débloqué sans licence (build.ps1 -TestPremium) : NE JAMAIS DISTRIBUER ce MSI.
# - Signé avec le certificat de test du développeur (magasin utilisateur, créé au besoin) : le service
#   n'accepte l'interface que si les deux sont signés par le même certificat.
# Console PME de test : lancer server/PcSante.PmeConsole en local (http://127.0.0.1:5090/), voir tools/pme/.
param([string]$Destination = "C:\dev\PcSante-Installation", [string]$ConsoleUrl = "http://127.0.0.1:5090/")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dev = Join-Path $env:LOCALAPPDATA "PcSante-dev"

# signtool : outil de développement conservé hors dépôt (paquet NuGet Microsoft.Windows.SDK.BuildTools).
if (-not (Get-Command signtool.exe -ErrorAction SilentlyContinue)) {
    if (-not (Test-Path "$dev\signtool\signtool.exe")) { throw "signtool.exe introuvable (attendu dans $dev\signtool)." }
    $env:PATH = "$dev\signtool;$env:PATH"
}

# Certificat de signature de TEST (jamais dans le dépôt ; clé privée dans le magasin de l'utilisateur).
$subject = "CN=PC Sante - TEST uniquement (ne pas distribuer)"
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(1)
}
$env:PCSANTE_SIGN_THUMBPRINT = $cert.Thumbprint

$installer = Join-Path $root "installer"
foreach ($d in "bin", "obj") { if (Test-Path "$installer\$d") { Remove-Item "$installer\$d" -Recurse -Force } }
& (Join-Path $installer "build.ps1") -Configuration Release -TestPremium -ConsoleUrl $ConsoleUrl
$msi = Get-ChildItem "$installer\bin" -Recurse -Filter *.msi | Select-Object -First 1

New-Item -ItemType Directory -Force $Destination | Out-Null
Copy-Item $msi.FullName (Join-Path $Destination "PcSante-test.msi") -Force
Export-Certificate -Cert $cert -FilePath (Join-Path $Destination "PcSante-test.cer") | Out-Null
foreach ($f in "Installer.cmd", "Installer.ps1", "Desinstaller.cmd", "Desinstaller.ps1") {
    Copy-Item (Join-Path $PSScriptRoot $f) (Join-Path $Destination $f) -Force
}
$commit = (git -C $root log -1 --format="%h %s") 2>$null
@"
PC Santé - version de TEST « toutes fonctions » (Premium débloqué sans licence). NE PAS DISTRIBUER.
Construite le : $(Get-Date -Format "yyyy-MM-dd HH:mm")
Commit       : $commit
Installer    : double-cliquer Installer.cmd (remplace la version déjà installée), puis « Oui ».
Désinstaller : double-cliquer Desinstaller.cmd (retire aussi le certificat de test).
"@ | Set-Content (Join-Path $Destination "VERSION.txt") -Encoding UTF8
Write-Host "Version de test déposée dans $Destination"
