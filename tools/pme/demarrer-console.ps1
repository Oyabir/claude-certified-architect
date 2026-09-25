# Démarre la console PME de TEST en local (http://127.0.0.1:5090/). Données hors dépôt : %LOCALAPPDATA%\PcSante-dev\console.
# Créer d'abord une organisation (une fois) :
#   dotnet run --project server\PcSante.PmeConsole -c Release -- create-organization "Mon entreprise" gerant@exemple.ma 5
# (avec les mêmes variables ConnectionStrings__Console, Logging__Directory et Email__PickupDirectory que ci-dessous).
$ErrorActionPreference = "Stop"
$dev = Join-Path $env:LOCALAPPDATA "PcSante-dev\console"
New-Item -ItemType Directory -Force $dev | Out-Null
$env:ASPNETCORE_URLS = "http://127.0.0.1:5090"
$env:ConnectionStrings__Console = "Data Source=$(Join-Path $dev 'pmeconsole.db')"
$env:Logging__Directory = Join-Path $dev "logs"
# Sans serveur SMTP configuré, les e-mails sont écrits dans ce dossier (fichiers .eml).
$env:Email__PickupDirectory = Join-Path $dev "mails"
dotnet run --project (Join-Path $PSScriptRoot "..\..\server\PcSante.PmeConsole") -c Release --no-launch-profile
