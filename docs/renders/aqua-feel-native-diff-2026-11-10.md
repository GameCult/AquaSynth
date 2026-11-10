# aqua-feel: faustwasm vs the daemon's native libfaust path

**Nothing here was heard.** Numbers from offline buffers, same Faust source, 48 kHz, mono, 512-frame blocks, same params.

- wasm side: `tools/aqua-feel/render.js` (faustwasm 0.20.0, libfaust 2.90.19 and the stdlib bundled with it, `-ftz 2`).
- native side: `tools/FaustNativeRender` over `AquaSynthPatchCompiler` (`NativeFaustRuntime.cs:530`: `-single -ftz 2 -vec -lv 1`), libfaust 2.90.20 + LLVM built in the Linux sandbox 2026-10-31, with the stdlib `faustlibraries-20261031` (master 3d8c6ef) from `AQUASYNTH_FAUST_HOME`. **The two sides compile against different standard-library text**, and that decides the thruster result below; "native" here means this sandbox's library, which the repo does not pin (`docs/faust-toolchain-boundary.md`: the host's `share/faust`).
- driver: `node tools/aqua-feel/native-diff.mjs <FaustNativeRender dir> --tract <source.dsp>`; raw numbers in `aqua-feel-native-diff-2026-11-10.json`.
- tract voice: `vowel-a` Faust source from the 2026-11-08 render kit (0.72 s, default controls). Not in the repo (`--tract` takes a path), not an aqua-feel catalog item, so its `FAIL onsetMs` is the patch-group rule applied by this script, not a repo gate result.
- the script has no tolerance and exits 1 only on a render error or a length mismatch; every other judgement is the reader's.
- Nothing here was heard.

Columns are wasm / native.

| item | max abs err (at sample) | err vs signal RMS | sample peak | centroid Hz | LUFS | true peak dBTP | gate flags |
|---|---|---|---|---|---|---|---|
| thruster rpm300 | 2.6e-2 (142707) | -28.8 dB | .1426/.1424 | 751.9/751.5 | -30.227/-30.216 | -16.90/-16.92 | none / none |
| thruster rpm600 | 4.9e-2 | -23.5 dB | .1596/.1601 | 1228.0/1224.4 | -28.695/-28.709 | -15.93/-15.90 | none / none |
| thruster rpm1200 | 9.0e-2 | -16.6 dB | .1916/.1894 | 2114.2/2115.6 | -26.051/-26.051 | -14.34/-14.42 | none / none |
| thruster rpm4800 | 2.9e-1 | -8.2 dB | .3638/.3622 | 4914.2/4908.1 | -17.419/-17.425 | -8.72/-8.74 | none / none |
| sfxr classic abstract-golf | 2.2e-6 | -134.5 dB | .9896/.9896 | 8147.6/8147.6 | -5.328/-5.328 | +0.908/+0.908 | waived truePeakDb, lowMotion, loudnessLufs; warn truePeakDb / identical |
| sfxr explosion | 2.2e-6 | -127.1 dB | .6732/.6732 | 9795.3/9795.3 | -8.634/-8.634 | -3.06/-3.06 | waived lowMotion / identical |
| tract vowel-a | 1.9e-4 | -52.6 dB | .08378/.08391 | 1937.1/1937.2 | -34.252/-34.235 | -21.52/-21.51 | FAIL onsetMs / identical |

Hiss caps (noiseToToneADb, wasm / native; cap): rpm300 13.09/13.06 (16), rpm600 5.04/6.05 (8), rpm1200 12.65/12.72 (15). rpm4800 9.68/9.41 (no cap).

## Verdict

**sfxr (two items): the paths agree** to float rounding (2e-6). These two patches use no library smoother (`smooth01` and a local `seg_smooth`), so the agreement says nothing about the library difference below. The gate flags are the script's `gate()` applied to each side's metrics for these two items only; they match.

**Tract voice: agrees to -52.6 dB** (max 1.9e-4 on a 0.084 peak); metrics within 0.02 dB and 0.01 % centroid. The residual is the same library difference: the source calls `si.smoo` 101 times; with the old smoother form pinned on both sides the error is -61.2 dB, with the new form pinned on both it is 3.1e-7 (-109.3 dB).

**Thruster: waveforms diverge, level and spectrum metrics stay close.** Relative RMS error -28.8 dB at rpm300 to -8.2 dB at rpm4800. At rpm4800 it grows over time: windowed -53, -33, -22, -15, -10 and -4 dB for 0-0.25, 0.25-0.5, 0.5-1, 1-1.5, 1.5-2 and 2-3 s. Differences in the metrics: LUFS 0.015 dB, true peak 0.081 dB, sample peak 1.2 %, centroid 0.3 %, crest 0.11 dB, hiss within 0.27 dB except rpm600 A-weighted, 1.0 dB higher native.

### Cause (isolated by a separate Soul pass, 2026-11-10)

The thruster smooths `rpm` with `si.smooth(ba.tau2pole(0.2))`, and every oscillator integrates that into phase. The two library texts define `si.smooth` differently:

- the wasm bundle (libfaust 2.90.19, `/usr/share/faust`): `smooth(s) = *(1.0 - s) : + ~ *(s)`. In float32 it stalls: the rpm freezes at **4796.632** from sample ~72063 (1.5 s) for a target of 4800. A float32 simulation without fused multiply-add reproduces 4796.6318359375.
- the native overlay (faustlibraries master 3d8c6ef, `signals.lib:391`): `smooth = imp(3)`, an error-form recursion whose comment says it cannot stall. It reaches 4800.0001.

Evidence that the library text, not `-vec`, `-lv`, `-double` or contraction, decides it: the `faust` CLI emits the same update expression for the same `si.smooth` source under scalar, `-vec -lv 0/1` and `-double`. With the old explicit form pinned in the DSP on both sides, native ends at 4797.6573 (LLVM does contract to FMA, which moves the stall point from 4796.632 to 4797.657, matching the FMA simulation) and wasm at 4796.6319. With the explicit `imp(3)` form pinned on both sides, thruster error falls from 2.61e-2 to 5.7e-7 (rpm300, -120.3 dB) and from 2.87e-1 to 1.13e-4 (rpm4800, -80.5 dB). The stall point is also hypersensitive: one ulp on the pole constant gives 4799.38 or 4798.57.

The earlier version of this report said the cause was the float32 rounding of the smoother on a native path that "does not stall for an unisolated reason" and suggested `-vec`; that was wrong. The `wander` term is not the cause (removing it leaves rpm300 2.62e-2, rpm4800 2.89e-1).

The isolation probes are scratch scripts in `/var/lib/mind-aqua-self/soul-nd-1` (not committed); the numbers above are not in the `.json`, which holds only the seven main rows.

### What this does and does not invalidate

- Level, true-peak, centroid, crest and hiss claims for the thruster hold on both libraries within the numbers above. The rpm600 hiss cap (8 dB) has 1.95 dB of headroom with the native library, not the 2.96 dB the threshold note assumes; still passes.
- Sample- or phase-level comparisons of the thruster (null tests, exact waveform diffs, sub-ms timing of the rotor tone) are specific to the library that rendered them.
- Which library a given daemon uses depends on its `share/faust`. A daemon shipping a release stdlib with the old `si.smooth` would stall like wasm; this sandbox's would not. The report cannot say what the production daemon does.
- Other sources that import `si.smooth` or `si.smoo` are expected to show the same skew and were **not tested**: 11 sources by count (7 patches, 3 Ding instruments, plus `advanced-human-larynx-graph`), which puts most of the 36 Ding items (27 or more) on the divergent library.
- **Not tested:** the other 75 of the 81 aqua-feel items, the sfxr-family patches beyond the two here, Ding mixes, block sizes other than 512, and any live (non-offline) host.

## Toolchain

dotnet 10.0.403 builds `FaustNativeRender` with `/var/lib/mind-aqua-self/env-review.sh`. Re-running the script reproduces the JSON byte for byte.
