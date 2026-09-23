# Exécuté automatiquement au démarrage de Windows Sandbox (compte administrateur jetable).
# Rien ici ne touche le PC hôte : le dossier est monté en lecture seule et la Sandbox est effacée à la fermeture.
$ErrorActionPreference = "Stop"
$kit = "C:\Users\WDAGUtilityAccount\Desktop\PcSante-Sandbox"
Start-Transcript "$env:TEMP\preparation.log"
# 1. Le certificat de TEST devient de confiance, dans la Sandbox uniquement.
Import-Certificate "$kit\PcSante-test.cer" -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
Import-Certificate "$kit\PcSante-test.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
# 2. Serveur de licences de test : il tourne sur le PC hôte, joignable par la passerelle de la Sandbox.
$gw = (Get-NetRoute -DestinationPrefix "0.0.0.0/0" | Sort-Object RouteMetric | Select-Object -First 1).NextHop
Add-Content "$env:windir\System32\drivers\etc\hosts" "`r`n$gw licences.pcsante.test"
try { Invoke-WebRequest "http://licences.pcsante.test:5080/health" -UseBasicParsing -TimeoutSec 5 | Out-Null; $serveur = "joignable" }
catch { $serveur = "INJOIGNABLE (serveur démarré ? règle de pare-feu ajoutée sur l'hôte ?)" }
# 3. Prérequis du MSI : .NET 8 Desktop Runtime.
Start-Process "$kit\windowsdesktop-runtime-win-x64.exe" -ArgumentList "/install /quiet /norestart" -Wait
Stop-Transcript
$sig = (Get-AuthenticodeSignature (Get-ChildItem "$kit\*.msi")[0].FullName).Status
$cle = if (Test-Path "$kit\cles-premium.txt") { (Get-Content "$kit\cles-premium.txt" | Select-Object -Last 1) } else { "(aucune)" }
[System.Windows.MessageBox]::Show("Préparation terminée.`n`nSignature du MSI : $sig`nServeur de licences ($gw) : $serveur`nClé Premium à essayer : $cle`n`nDouble-cliquez sur PcSante-1.0.0.msi pour l'installer, puis suivez docs\TESTS_MANUELS_VM.md.", "PC Santé - Sandbox") | Out-Null
