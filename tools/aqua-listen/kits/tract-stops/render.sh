#!/bin/bash
# Renders the tract-stops kit (WordRender, 140 Hz, 48 kHz mono 16-bit): /apa aba ata ada aka ala/ (the vowel is /ɑ/) and the
# --no-burst twins of ata ada aka, whose release_flow splines are dropped so a pair isolates the burst.
#   render.sh <repo-root> <out-root> [jobs]
# Needs CULTLIB_ROOT at the admitted CultLib revision and AQUASYNTH_FAUST_HOME. Under load each render is minutes (a Faust compile).
# <out-root>/out/<w>/<w>.wav   <out-root>/outnb/<w>/<w>.wav (--no-burst)   <out-root>/clips/*.wav  the files kit.json names
# <out-root>/images, burst-notes.*  clip_images.py: PNGs per clip and the burst times (needs numpy + matplotlib: PYTHON=<python with both>)
# Build the page: node tools/aqua-listen/build.mjs --manifest tools/aqua-listen/kits/tract-stops/kit.json --clips <out-root>/clips --out <out-root>/dist [--images <out-root>/images]
set -eu
REPO=$(cd "$1" && pwd); OUT=$2; JOBS=${3:-4}; HERE=$REPO/tools/aqua-listen/kits/tract-stops
dotnet build "$REPO/tools/WordRender" -c Release -v q -o "$OUT/wr" | grep -E "rror\(s\)"
mkdir -p "$OUT/out" "$OUT/outnb" "$OUT/clips"
job() { n=$1; o=$2; f=$3; shift 3; dotnet "$OUT/wr/WordRender.dll" "$OUT/$o/$n" "$n" "$f" "$HERE/phones/$n.json" "$@" > "$OUT/$o/$n.log" 2>&1 && echo "$n $o ok" || echo "$n $o FAILED (see $OUT/$o/$n.log)"; }
export -f job; export OUT HERE
{
  for n in ata ada aka ala apa aba; do echo "$n out 140"; done
  for n in ata ada aka apa aba; do echo "$n outnb 140 --no-burst"; done   # apa aba twins feed the burst notes only, no clip
} | xargs -P "$JOBS" -L1 bash -c 'job "$@"' _
C=$OUT/clips
for n in ata ada aka ala apa aba; do cp "$OUT/out/$n/$n.wav" "$C/$n.wav"; done
for n in ata ada aka; do cp "$OUT/outnb/$n/$n.wav" "$C/${n}_noburst.wav"; done
${PYTHON:-python3} -I "$REPO/tools/aqua-listen/clip_images.py" "$C" "$OUT/images" --kit "$HERE/kit.json" --renders "$OUT"
