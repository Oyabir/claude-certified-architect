# Retire PC Santé version de TEST de ce PC, ainsi que le certificat de test. Lancé par Desinstaller.cmd.
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    return
}

Get-Process PcSante, PcSante.Overlay -ErrorAction SilentlyContinue | Stop-Process -Force
$code = (Get-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\* | Where-Object DisplayName -eq "PC Santé").PSChildName
if ($code) {
    $p = Start-Process msiexec.exe -ArgumentList "/x $code /qb /norestart" -Wait -PassThru
    "1. PC Santé désinstallé (code $($p.ExitCode), 0 = réussite)"
} else {
    "1. PC Santé n'était pas installé"
}

foreach ($store in "Root", "TrustedPublisher") {
    Get-ChildItem "Cert:\LocalMachine\$store" | Where-Object Subject -like "*PC Sante - TEST*" | ForEach-Object { $_ | Remove-Item }
}
"2. Certificat de test retiré de l'ordinateur"
Read-Host "Appuyez sur Entrée pour fermer"
