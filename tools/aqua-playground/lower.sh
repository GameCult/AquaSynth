#!/usr/bin/env bash
# Regenerates lowered/*.dsp and catalog.json from patches/ through the AquaLower tool (needs the .NET SDK
# and a CultLib checkout at the admitted revision: set CULTLIB_ROOT). The two largest patches
# (additive-pad, electro-drop-demo lower to about 1.5 MB of Faust each) stay out of the catalog.
set -euo pipefail
cd "$(dirname "$0")/../.."
entries=(thruster=thruster/thruster.dsp)
while IFS= read -r rel; do
  case "$rel" in advanced/additive-pad.aqua|advanced/electro-drop-demo.aqua) continue;; esac
  name=$(echo "${rel%.aqua}" | tr '/' '-')
  entries+=("$name=$rel")
done < <(cd patches && find . -name '*.aqua' | sed 's|^\./||' | sort)
dotnet run -c Release --project tools/AquaLower -- tools/aqua-playground/lowered patches "${entries[@]}"
