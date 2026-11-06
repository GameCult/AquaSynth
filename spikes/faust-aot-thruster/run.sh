#!/usr/bin/env bash
# Build the AOT thruster spike, run the boundary test, then the per-block timing report.
# Linux only. usage: run.sh [--bench-only] ; env: FAUST (faust CLI), FAUST_PREFIX (has include/faust),
# FAUST_LIBS (dir with stdfaust.lib), OUT (build dir), BLOCKS, RATE, FRAMES.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
FAUST=${FAUST:-faust}
FAUST_PREFIX=${FAUST_PREFIX:-$(dirname "$(dirname "$(command -v "$FAUST")")")}
FAUST_LIBS=${FAUST_LIBS:-$FAUST_PREFIX/share/faust}
OUT=${OUT:-$here/build}
mkdir -p "$OUT"
[ -f "$FAUST_LIBS/stdfaust.lib" ] || { echo "no stdfaust.lib in FAUST_LIBS=$FAUST_LIBS" >&2; exit 2; }
"$FAUST" -lang cpp -cn ThrusterDsp -I "$FAUST_LIBS" "$here/thruster.dsp" -o "$OUT/thruster_dsp.h"
"$FAUST" --version | head -1
CXXF="-O2 -std=c++17 -fvisibility=hidden -I$OUT -I$here -I$FAUST_PREFIX/include"
g++ $CXXF -shared -fPIC -o "$OUT/libaqs_thruster.so" "$here/aqs_thruster.cpp"
gcc -O2 -I"$here" -o "$OUT/test_thruster" "$here/test_thruster.c" -L"$OUT" -laqs_thruster -lm -Wl,-rpath,"$OUT"
gcc -O2 -I"$here" -o "$OUT/bench_thruster" "$here/bench_thruster.c" -L"$OUT" -laqs_thruster -lm -Wl,-rpath,"$OUT"
"$OUT/test_thruster"
echo "--- timing (Linux $(uname -r), $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | xargs)); Windows/Unity: unverified"
"$OUT/bench_thruster" "${BLOCKS:-20000}" "${RATE:-48000}" "${FRAMES:-512}"
