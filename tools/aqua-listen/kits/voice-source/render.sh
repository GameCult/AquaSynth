#!/bin/bash
# Renders the voice-source-1 kit: the phrase /ɑmi/ (WordRender, 140 Hz, 48 kHz mono 16-bit), six renders that differ from the scaffold in ONE source field each:
#   ref (none)  ros (pulse=rosenberg)  w20 (noise=.33, white, HNR ~20)  h20 (noise_hp=2000 noise=.33)  h25 (noise_hp=2000 noise=.165)  h15 (noise_hp=2000 noise=.6)
# then ros_matched: ros scaled by the measured output-rms ratio saw/rosenberg, so a pair can be heard without the level difference.
#   render.sh <repo-root> <out-root> [jobs]
# Needs CULTLIB_ROOT at the admitted CultLib revision and AQUASYNTH_FAUST_HOME. Under load each render is minutes (a Faust compile).
# <out-root>/out/<label>/<label>.wav   <out-root>/clips/*.wav  the files kit.json names   <out-root>/measures.txt  HNR, rms, peak per clip
# Build the page: node tools/aqua-listen/build.mjs --manifest tools/aqua-listen/kits/voice-source/kit.json --clips <out-root>/clips --out <out-root>/dist
set -eu
REPO=$(cd "$1" && pwd); OUT=$2; JOBS=${3:-6}; HERE=$REPO/tools/aqua-listen/kits/voice-source
PY=${PYTHON:-python3}
dotnet build "$REPO/tools/WordRender" -c Release -v q -o "$OUT/wr" | grep -E "rror\(s\)"
mkdir -p "$OUT/out" "$OUT/clips"
job() { n=$1; shift; dotnet "$OUT/wr/WordRender.dll" "$OUT/out/$n" "$n" 140 "$HERE/phones/ami.json" --source "$*" > "$OUT/out/$n.log" 2>&1 && echo "$n ok" || echo "$n FAILED (see $OUT/out/$n.log)"; }
export -f job; export OUT HERE
{
  echo "ref noise=.05"; echo "ros pulse=rosenberg"; echo "w20 noise=.33"
  echo "h20 noise_hp=2000 noise=.33"; echo "h25 noise_hp=2000 noise=.165"; echo "h15 noise_hp=2000 noise=.6"
} | xargs -P "$JOBS" -L1 bash -c 'job "$@"' _
$PY -I "$HERE/clips.py" "$OUT"
