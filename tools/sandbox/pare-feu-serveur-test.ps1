# À lancer en ADMINISTRATEUR. Autorise Windows Sandbox (sous-réseaux locaux uniquement) à joindre le serveur de test.
# Pour retirer la règle : .\pare-feu-serveur-test.ps1 -Retirer
param([switch]$Retirer)
$nom = "PC Santé - serveur de licences de TEST (5080)"
if ($Retirer) { Remove-NetFirewallRule -DisplayName $nom; "Règle retirée."; return }
New-NetFirewallRule -DisplayName $nom -Direction Inbound -Protocol TCP -LocalPort 5080 -RemoteAddress LocalSubnet -Action Allow -Profile Any | Out-Null
"Règle ajoutée : port 5080, sous-réseaux locaux uniquement."
