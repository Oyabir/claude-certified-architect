# Installe (ou remplace) PC Santé version de TEST sur ce PC. Lancé par Installer.cmd ; demande les droits administrateur.
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    return
}

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
try {
    Get-Content (Join-Path $here "VERSION.txt") -TotalCount 3
    ""
    try { Checkpoint-Computer -Description "Avant installation PC Santé (test)" -RestorePointType APPLICATION_INSTALL; "1. Point de restauration créé" }
    catch { "1. Point de restauration non créé (Windows en limite un par 24 h) : on continue" }

    # Remplacement : la version déjà installée est retirée avant d'installer la nouvelle.
    Get-Process PcSante, PcSante.Overlay -ErrorAction SilentlyContinue | Stop-Process -Force
    $code = (Get-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\* | Where-Object DisplayName -eq "PC Santé").PSChildName
    if ($code) {
        $p = Start-Process msiexec.exe -ArgumentList "/x $code /qb /norestart" -Wait -PassThru
        "2. Ancienne version retirée (code $($p.ExitCode))"
    } else {
        "2. Aucune version précédente"
    }

    # Certificat de TEST approuvé sur ce PC (retiré par Desinstaller.cmd).
    Import-Certificate (Join-Path $here "PcSante-test.cer") -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    Import-Certificate (Join-Path $here "PcSante-test.cer") -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
    "3. Certificat de test approuvé ; signature du MSI : " + (Get-AuthenticodeSignature (Join-Path $here "PcSante-test.msi")).Status

    $log = Join-Path $env:TEMP "PcSante-installation.log"
    $p = Start-Process msiexec.exe -ArgumentList "/i `"$(Join-Path $here 'PcSante-test.msi')`" /qb /norestart /l*v `"$log`"" -Wait -PassThru
    "4. Installation : " + $(if ($p.ExitCode -eq 0) { "réussie" } else { "ÉCHEC (code $($p.ExitCode)), journal : $log" })
    "5. Service PC Santé : " + (Get-Service PcSanteService -ErrorAction SilentlyContinue).Status
    ""
    "Ouvrez « PC Santé » depuis le menu Démarrer."
} catch {
    "ERREUR : $($_.Exception.Message)"
}
Read-Host "Appuyez sur Entrée pour fermer"
