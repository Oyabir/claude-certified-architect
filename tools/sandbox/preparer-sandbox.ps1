# Exécuté automatiquement au démarrage de Windows Sandbox (compte jetable). Rien ici ne touche le PC hôte.
# La commande de démarrage de la Sandbox n'a pas les droits administrateur : le script se relance élevé (un « Oui » à cliquer).
$kit = "C:\Users\WDAGUtilityAccount\Desktop\PcSante-Sandbox"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    return
}
Add-Type -AssemblyName PresentationFramework
$ErrorActionPreference = "Stop"
Start-Transcript "$env:TEMP\preparation.log"
# 1. Le certificat de TEST devient de confiance, dans la Sandbox uniquement.
Import-Certificate "$kit\PcSante-test.cer" -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
Import-Certificate "$kit\PcSante-test.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
# 2. Serveur de licences de test (sur l'hôte) : le MSI de test vise http://127.0.0.1:5080/, redirigé vers la passerelle
#    (= l'hôte). Le fichier hosts n'est pas pris en compte par le DNS de la Sandbox.
$gw = (Get-NetRoute -DestinationPrefix "0.0.0.0/0" | Sort-Object RouteMetric | Select-Object -First 1).NextHop
Set-Service iphlpsvc -StartupType Automatic; Start-Service iphlpsvc
netsh interface portproxy add v4tov4 listenaddress=127.0.0.1 listenport=5080 connectaddress=$gw connectport=5080 | Out-Null
try { Invoke-WebRequest "http://127.0.0.1:5080/health" -UseBasicParsing -TimeoutSec 5 | Out-Null; $serveur = "joignable" }
catch { $serveur = "INJOIGNABLE (serveur démarré sur l'hôte ? règle de pare-feu ajoutée ?)" }
# 3. Prérequis du MSI : .NET 8 Desktop Runtime.
Start-Process "$kit\windowsdesktop-runtime-win-x64.exe" -ArgumentList "/install /quiet /norestart" -Wait
Stop-Transcript
$sig = (Get-AuthenticodeSignature (Get-ChildItem "$kit\*.msi")[0].FullName).Status
$cle = if (Test-Path "$kit\cles-premium.txt") { (Get-Content "$kit\cles-premium.txt" | Select-Object -Last 1) } else { "(aucune)" }
[System.Windows.MessageBox]::Show("Préparation terminée.`n`nSignature du MSI : $sig`nServeur de licences (via $gw) : $serveur`nClé Premium à essayer : $cle`n`nDouble-cliquez sur PcSante-1.0.0.msi pour l'installer, puis suivez docs\TESTS_MANUELS_VM.md.", "PC Santé - Sandbox") | Out-Null
