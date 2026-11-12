#!/bin/bash
# Renders the tract-weak-spots kit and the renders tools/formant-check/weak_spots.py measures (WordRender, 140 Hz, 48 kHz mono 16-bit).
#   render.sh <repo-root> <out-root> [<pre-fix-repo-root>] [jobs]
# <pre-fix-repo-root> is a checkout of main c0e6b98 (before the release_flow fix); without it the leak-nibu-flat-before clip is not made.
# Needs CULTLIB_ROOT at the admitted CultLib revision and AQUASYNTH_FAUST_HOME. Under load each render is minutes (a Faust compile).
# <out-root>/out/<w>/<w>.wav   140 Hz renders; v_<vowel>_s is the vowel with primary stress, nibu has it on /i/
# <out-root>/out148/v_<vowel>  the unstressed vowel at 148.4 Hz (140 x 1.06: the f0 rise of primary stress alone)
# <out-root>/outnb/<w>         the same word with --no-burst (plosive release_flow splines dropped)
# <out-root>/clips/*.wav       the files kit.json names;   weak_spots.py <out-root> measures the rest
set -eu
REPO=$(cd "$1" && pwd); OUT=$2; PRE=${3:-}; JOBS=${4:-4}; HERE=$REPO/tools/aqua-listen/kits/tract-weak-spots
dotnet build "$REPO/tools/WordRender" -c Release -v q -o "$OUT/wr" | grep -E "rror\(s\)"
mkdir -p "$OUT/out" "$OUT/out148" "$OUT/outnb" "$OUT/clips" "$OUT/pre"
job() { n=$1; o=$2; f=$3; shift 3; dotnet "$OUT/wr/WordRender.dll" "$OUT/$o/$n" "$n" "$f" "$HERE/phones/$n.json" "$@" > "$OUT/$o/$n.log" 2>&1 && echo "$n $o ok" || echo "$n $o FAILED (see $OUT/$o/$n.log)"; }
export -f job; export OUT HERE
{
  for n in nibu nibu_flat v_i v_i_s bin bun ba ama ana v_ih v_oo_short v_ah v_ah_s v_e v_e_s v_o v_o_s v_u v_u_s; do echo "$n out 140"; done
  for n in bin ba bun; do echo "$n outnb 140 --no-burst"; done
  for v in i e ah o u ih oo_short; do echo "v_$v out148 148.4"; done
} | xargs -P "$JOBS" -L1 bash -c 'job "$@"' _
if [ -n "$PRE" ]; then
  dotnet build "$PRE/tools/WordRender" -c Release -v q -o "$OUT/wr-pre" | grep -E "rror\(s\)"
  dotnet "$OUT/wr-pre/WordRender.dll" "$OUT/pre/nibu_flat" nibu_flat 140 "$HERE/phones/nibu_flat.json" > "$OUT/pre/nibu_flat.log" 2>&1
  cp "$OUT/pre/nibu_flat/nibu_flat.wav" "$OUT/clips/nibu_flat_before.wav"
fi
C=$OUT/clips
cp "$OUT/out/nibu_flat/nibu_flat.wav" "$C/nibu_flat.wav"; cp "$OUT/out/nibu_flat/nibu_flat.wav" "$C/nibu_flat_after.wav"   # one render, two ids: the before/after pair is one-to-one
for n in nibu v_i v_i_s v_ih v_oo_short ama ana ba bun; do cp "$OUT/out/$n/$n.wav" "$C/$n.wav"; done
for n in ba bun; do cp "$OUT/outnb/$n/$n.wav" "$C/${n}_noburst.wav"; done
