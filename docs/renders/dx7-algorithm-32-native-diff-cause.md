# dx7-algorithm-32-additive-organ: why native vs wasm reads -79.5 dB

**Nothing here was heard.** Offline buffers, 48 kHz, mono, 3 s, no params, same Faust text on both sides. Same toolchain as `aqua-feel-native-diff-2026-11-10.md` (wasm: faustwasm 0.20.0 / libfaust 2.90.19; native: `FaustNativeRender`, libfaust 2.90.20, `-single -ftz 2 -vec -lv 1`, faustlibraries master 3d8c6ef via `AQUASYNTH_FAUST_HOME`).

## Verdict: cause found, two causes, neither a float32 floor and neither a bug in AquaSynth

The -79.5 dB is a real, deterministic phase drift of the sine voices, not noise, not a quiet signal and not a sample shift.

1. **Increment rounding in `os.phasor`, selected by whether `1/ma.SR` is shared** (-79.3 to -93.0 dB when removed). Lowered patches define `time = ba.time / ma.SR;`. With that division in the graph, the native compiler computes the phasor increment as `freq * fround(1/SR)`; the wasm compiler keeps `freq / SR`. For 392 Hz at 48 kHz: `fround(392/48000) = 0.008166667074`, `fround(392 * fround(1/48000)) = 0.008166666143`, one 2^-30 step (9.31e-10) apart. Measured effective increments: wasm 0.008166667074 on both, native 0.008166667074 without the division in the graph and **0.008166666143** with it. The phase then drifts linearly (about 1.3e-4 cycles over 3 s between the sides), so the waveform error grows linearly with time (0 to 1.65e-4 over 0.45 s, then follows the envelope down).
2. **`si.smoo` library text** on `/macro/drawbar` and `/macro/shimmer` (-93.0 to -141.2 dB when also pinned). This is the known old-form vs `imp(3)` difference. The 2026-11-10 imp3 pin did not show it because cause 1 sat 14 dB above it.

With both removed the patch reads **-141.2 dB (max 1.8e-7)**, the same float floor as the Ding instruments (-130 to -142 dB).

## Evidence (isolating experiments; each changes one thing)

Baseline reproduced through `native-diff.mjs --items smoother`: 1.65e-4 max, -79.27 dB. The signal is not quiet: RMS 0.193, peak 0.686. The last 2 s are digital silence and contribute zero error (73 % exactly-equal samples is that silence, not agreement). The error is zero at nothing but silence and rises linearly while the voices sound; lag test: lag 0 summed squared error 6.3e-5 vs 18.4 at lag +-1 (sums over 144000 samples), so no shift.

| experiment | rel RMS err dB | max err |
|---|---|---|
| whole patch, as lowered | -79.3 | 1.65e-4 |
| filters stripped (`fi.lowpass`/`fi.highpass`) | -79.3 | 1.66e-4 |
| per voice, no filters: v0 / v1 / v2 / v3 / v4 / v5 | -85.8 / -79.3 / -143.3 / -73.8 / -143.1 / -109.0 | |
| `sin(2pi*os.phasor(1,392))*0.5` alone | -163.7 | 3e-8 |
| same sine times `age+1`, a ramp, or any slow signal | -64 to -74 | 1e-4 to 3e-3 |
| `os.phasor(1,392)` alone | exact (0) | 0 |
| `os.phasor(1,392) + (ba.time/ma.SR > 100)` (never true) | -33.2 | 1.0 (one wrap sample) |
| same with `ba.time > 1e9` or `ma.SR > 1e9` instead | exact (0) | 0 |
| same with `ba.time / 48000.0 > 100` | exact (0) | 0 |
| effective phasor increment, native, with / without the division | 0.008166666143 / 0.008166667074 | |
| whole patch, `time = ba.time / 48000.0;` only | -93.0 | 1.78e-5 |
| whole patch, every `ma.SR` a literal | -93.0 | 1.78e-5 |
| whole patch, phasor replaced by a float-accumulating one on both sides (library phasor text is fixed point in master, see below) | -84.0 | 9.4e-5 |
| age literal + `si.smoo` pinned to old form / imp3 | -114.0 / **-141.2** | 1.3e-6 / 1.8e-7 |
| `si.smoo` pinned to imp3 only (SR still shared) | -79.5 | 1.65e-4 |

Voices 2 and 4 (588 and 980 Hz) are clean because their increments round the same either way; 196, 392 and 784 are not (1176 Hz rounds the same either way). After cause 1 is removed only voice 1 (the one scaled by `patch_param_0`, the smoothed drawbar) stays at -91.5 dB, and voice 5 (shimmer) at -109 dB: cause 2.

Arithmetic check: `Math.fround(392 * Math.fround(1/48000))` = 0.008166666142642498, matching the measured native increment to 10 digits.

## Scope

- Other patches with the same `time = ba.time / ma.SR;` line are not affected the same way: with imp3 pinned and `time` made literal, `advanced-bird-syrinx-graph` stays -75.4, `advanced-human-larynx-graph` -94.2, `dx7-algorithm-8-bright-pair` -94.7, `advanced-alien-multivalve-graph` moves -97.9 to -110.9. Whether a patch is affected depends on whether its phasor increments round differently under division vs reciprocal multiply.
- `native-diff.mjs --smoother imp3` therefore cannot reach the float floor for patches whose phasor increment is affected; its `-60 dB` limit still holds with the new reading (-79.5 is explained, not a worse mismatch).

## Not verified

- I did not read the generated C++/wasm. "Native uses `freq * fround(1/SR)`, wasm `freq/SR`" is inferred from the measured increments and the arithmetic above (exact match), not from emitted code (the wasm build has no C++ backend; no `faust` CLI here). It is also not separated whether the difference is libfaust 2.90.19 vs 2.90.20, or the `-vec -lv 1` / LLVM path, or the library's phasor text (fixed point in master, which also looks different from what the wasm bundle holds). Settling it: emit the same DSP with one libfaust version's C++ backend and compare the `instanceConstants` lines, or render the wasm side with a 2.90.20 wasm build.
- Which side is nearer the ideal is not important: both are within one 2^-30 increment step of 392/48000 and in opposite directions (+4.1e-10, -5.2e-10).

## Reproduce

```
. /var/lib/mind-aqua-self/env-review.sh
export AQUASYNTH_FAUST_HOME=/var/lib/mind-aqua-self/faust-home-overlay-20261031
cd tools/aqua-feel && node native-diff.mjs /var/lib/mind-aqua-self/native-diff-bin --items smoother --out "$TMPDIR/out"
```
then edit the lowered DSP text on both sides (as the table says) and render with `renderOnce` and `FaustNativeRender`. The experiment scripts are scratch in `$TMPDIR` and not committed.
