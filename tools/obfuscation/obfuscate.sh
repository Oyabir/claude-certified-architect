#!/usr/bin/env bash
# Obfusque PcSante.Licensing.dll et PcSante.Service.dll d'un dossier (usage : obfuscate.sh <dossier>).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
target="$(cd "$1" && pwd)"
dotnet restore "$here/Obfuscation.proj" >/dev/null
tool="$(ls -d "${NUGET_PACKAGES:-$HOME/.nuget/packages}"/obfuscar.globaltool/2.2.49/tools/net8.0/any)"
out="$(mktemp -d)"
modules=""
for m in PcSante.Licensing.dll PcSante.Service.dll; do
  [ -f "$target/$m" ] && modules="$modules<Module file=\"$target/$m\" />"
done
sed -e "s#\$(InPath)#$target#g" -e "s#\$(OutPath)#$out#g" -e "s#<!--MODULES-->#$modules#" "$here/obfuscar.xml" > "$out/obfuscar.xml"
dotnet "$tool/GlobalTools.dll" "$out/obfuscar.xml"
for m in PcSante.Licensing.dll PcSante.Service.dll; do
  [ -f "$out/$m" ] && cp "$out/$m" "$target/"
done
echo "Obfuscation terminée ($target), table de correspondance : $out/obfuscar-mapping.txt (à conserver hors du dépôt)"
