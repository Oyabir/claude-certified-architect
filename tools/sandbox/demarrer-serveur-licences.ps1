# Démarre le serveur de licences de TEST de PC Santé (port 5080), accessible depuis Windows Sandbox.
# Les clés sont lues dans %LOCALAPPDATA%\PcSante-dev\secrets-test.env (hors dépôt) et placées dans les variables de CE processus uniquement.
$ErrorActionPreference = "Stop"
$dev = Join-Path $env:LOCALAPPDATA "PcSante-dev"
Get-Content (Join-Path $dev "secrets-test.env") | Where-Object { $_ -match '^(PCSANTE_[A-Z_]+)=(.+)$' } | ForEach-Object {
    [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], "Process")
}
$env:ASPNETCORE_URLS = "http://0.0.0.0:5080"
$env:ConnectionStrings__Licenses = "Data Source=$(Join-Path $dev 'licences-test.db')"
$env:Logging__Directory = Join-Path $dev "logs"
dotnet run --project (Join-Path $PSScriptRoot "..\..\server\PcSante.LicenseServer") -c Release --no-launch-profile

