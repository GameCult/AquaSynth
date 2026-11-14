#!/bin/bash
# Negative check for WordRender --source: every malformed field must exit 2 with a REFUSED line naming it, never crash (134) or render.
#   source-refusals.sh <WordRender.dll> <phones.json>      e.g. tools/aqua-listen/kits/voice-source/phones/ami.json
set -u
DLL=$1; PHONES=$2; OUT=$(mktemp -d); bad=0
check() { # <expected substring> <source string>
  msg=$(dotnet "$DLL" "$OUT/o" refuse 140 "$PHONES" --source "$2" 2>&1 >/dev/null); code=$?
  if [ $code -eq 2 ] && [[ "$msg" == *REFUSED*"$1"* ]]; then echo "ok   [$2] -> 2 $msg"; else echo "FAIL [$2] -> exit $code: $msg"; bad=1; fi
}
check pulze "pulze=rosenberg"
check Noise "Noise=.33"
check noise "noise"
check noise=abc "noise=abc"
check noise_hp=-5 "noise_hp=-5"
check noise_hp=9000 "noise_hp=9000"
check noise "noise=.1 noise=.2"
check pulse=triangle "pulse=triangle"
rm -rf "$OUT"; exit $bad
