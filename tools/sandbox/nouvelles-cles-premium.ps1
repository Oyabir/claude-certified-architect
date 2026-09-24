# Crée des clés Premium de TEST (1 PC chacune, 30 jours) et les ajoute à <Kit>\cles-premium.txt.
# Une Sandbox neuve est un « nouveau PC » : prévoir une clé par lancement de Sandbox.
param([int]$Nombre = 3, [ValidateSet("Premium", "Family")][string]$Offre = "Premium", [string]$Kit = "C:\dev\PcSante-Sandbox")
$ErrorActionPreference = "Stop"
$admin = (Get-Content (Join-Path $env:LOCALAPPDATA "PcSante-dev\secrets-test.env") | Where-Object { $_ -like "PCSANTE_ADMIN_API_KEY=*" }) -replace '^PCSANTE_ADMIN_API_KEY=', ''
$body = @{ count = $Nombre; tier = $Offre; validDays = 30; note = "Tests Windows Sandbox" } | ConvertTo-Json
$r = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:5080/admin/api/licenses" -Headers @{ "X-Admin-Key" = $admin } -ContentType "application/json" -Body $body
$r.keys | Add-Content (Join-Path $Kit "cles-premium.txt")
$r.keys

