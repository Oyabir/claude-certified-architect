<#
  Construit l'installeur MSI de PC Santé (Windows uniquement).

  Étapes : publication des binaires (win-x64, dépendants du runtime .NET 8), signature facultative
  des binaires, compilation WiX, signature facultative du MSI.

  Signature : définir PCSANTE_SIGN_THUMBPRINT (empreinte du certificat de signature de code installé
  dans le magasin de l'utilisateur ou de la machine). Sans cette variable, rien n'est signé et le
  service n'acceptera pas l'interface en Release (vérification de signature du named pipe).

  Exemple :
    $env:PCSANTE_SIGN_THUMBPRINT = "0123…"
    ./installer/build.ps1 -Configuration Release
#>
param(
    [string]$Configuration = "Release",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $PSScriptRoot "publish"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

$projects = @(
    "src/PcSante.App/PcSante.App.csproj",
    "src/PcSante.Service/PcSante.Service.csproj",
    "src/PcSante.Overlay/PcSante.Overlay.csproj"
)
foreach ($p in $projects) {
    dotnet publish (Join-Path $root $p) -c $Configuration -r win-x64 --self-contained false -o $publish -p:DebugType=none
    if ($LASTEXITCODE -ne 0) { throw "Échec de la publication de $p" }
}

$thumb = $env:PCSANTE_SIGN_THUMBPRINT
function Sign-Files([string[]]$files) {
    if (-not $thumb) { Write-Warning "PCSANTE_SIGN_THUMBPRINT absent : fichiers NON signés."; return }
    & signtool.exe sign /sha1 $thumb /fd SHA256 /tr $TimestampUrl /td SHA256 $files
    if ($LASTEXITCODE -ne 0) { throw "Échec de la signature" }
}

# Signature des exécutables et bibliothèques de PC Santé (les dépendances tierces sont déjà signées par leurs éditeurs).
Sign-Files (Get-ChildItem $publish -Filter "PcSante*.exe").FullName
Sign-Files (Get-ChildItem $publish -Filter "PcSante*.dll").FullName

dotnet build (Join-Path $PSScriptRoot "PcSante.Installer.wixproj") -c $Configuration -p:PublishRoot="$publish\"
if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation WiX" }

$msi = Get-ChildItem (Join-Path $PSScriptRoot "bin") -Recurse -Filter "*.msi" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Sign-Files @($msi.FullName)
$sizeMb = [math]::Round($msi.Length / 1MB, 1)
Write-Host "Installeur : $($msi.FullName) ($sizeMb Mo)"
if ($sizeMb -gt 50) { Write-Warning "L'installeur dépasse 50 Mo (exigence section 5)." }
