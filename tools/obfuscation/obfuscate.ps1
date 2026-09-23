<#
  Obfusque PcSante.Licensing.dll et PcSante.Service.dll d'un dossier publié (appelé par installer/build.ps1).
  La table de correspondance est écrite dans -MappingDir : la conserver HORS du dépôt (analyse de plantages).
#>
param(
    [Parameter(Mandatory = $true)][string]$Target,
    [string]$MappingDir = (Join-Path ([System.IO.Path]::GetTempPath()) "pcsante-obfuscar")
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
dotnet restore (Join-Path $here "Obfuscation.proj") | Out-Null
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME ".nuget\packages" }
$tool = Join-Path $packages "obfuscar.globaltool\2.2.49\tools\net8.0\any\GlobalTools.dll"
$Target = (Resolve-Path $Target).Path
New-Item -ItemType Directory -Force -Path $MappingDir | Out-Null
$modules = ""
foreach ($m in @("PcSante.Licensing.dll", "PcSante.Service.dll")) {
    if (Test-Path (Join-Path $Target $m)) { $modules += "<Module file=`"$(Join-Path $Target $m)`" />" }
}
$config = (Get-Content (Join-Path $here "obfuscar.xml") -Raw).Replace('$(InPath)', $Target).Replace('$(OutPath)', $MappingDir).Replace('<!--MODULES-->', $modules)
$configFile = Join-Path $MappingDir "obfuscar.xml"
Set-Content -Path $configFile -Value $config -Encoding UTF8
dotnet $tool $configFile
if ($LASTEXITCODE -ne 0) { throw "Échec de l'obfuscation" }
foreach ($m in @("PcSante.Licensing.dll", "PcSante.Service.dll")) {
    if (Test-Path (Join-Path $MappingDir $m)) { Copy-Item (Join-Path $MappingDir $m) $Target -Force }
}
Write-Host "Obfuscation terminée. Table de correspondance : $MappingDir\obfuscar-mapping.txt"
