# aqua-feel: faustwasm vs the daemon's native libfaust path

**Nothing here was heard.** Numbers from offline buffers, same Faust source, 48 kHz, mono, 512-frame blocks, same params.

- wasm side: `tools/aqua-feel/render.js` (faustwasm 0.20.0, libfaust 2.90.19 and the stdlib bundled with it, `-ftz 2`).
- native side: `tools/FaustNativeRender` over `AquaSynthPatchCompiler` (`NativeFaustRuntime.cs:530`: `-single -ftz 2 -vec -lv 1`), libfaust 2.90.20 + LLVM built in the Linux sandbox 2026-10-31, with the stdlib `faustlibraries-20261031` (master 3d8c6ef) from `AQUASYNTH_FAUST_HOME`. **The two sides compile against different standard-library text**, and that decides the thruster result below; "native" here means this sandbox's library, which the repo does not pin (`docs/faust-toolchain-boundary.md`: the host's `share/faust`).
- driver: `node tools/aqua-feel/native-diff.mjs <FaustNativeRender dir> --tract <source.dsp>`; raw numbers in `aqua-feel-native-diff-2026-11-10.json`.
- tract voice: `vowel-a` Faust source from the 2026-11-08 render kit (0.72 s, default controls). Not in the repo (`--tract` takes a path), not an aqua-feel catalog item, so its `FAIL onsetMs` is the patch-group rule applied by this script, not a repo gate result.
- the script exits 1 on a render error, a length mismatch, or a violation of its `LIMITS` (see "Tolerance in `native-diff.mjs`" below); the verdicts here are the reader's.
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

The isolation probes are scratch scripts in `/var/lib/mind-aqua-self/soul-nd-1` (not committed); the numbers above are not in the `.json`, which holds the seven main rows plus the tract.

### What this does and does not invalidate

- Level, true-peak, centroid, crest and hiss claims for the thruster hold on both libraries within the numbers above. The rpm600 hiss cap (8 dB) has 1.95 dB of headroom with the native library, not the 2.96 dB the threshold note assumes; still passes.
- Sample- or phase-level comparisons of the thruster (null tests, exact waveform diffs, sub-ms timing of the rotor tone) are specific to the library that rendered them.
- Which library a given daemon uses depends on its `share/faust`. A daemon shipping a release stdlib with the old `si.smooth` would stall like wasm; this sandbox's would not. The only Windows Faust install found (Starfire, 2.85.5) has the old form; see the section on the production stdlib below.
- **Not tested:** the other aqua-feel items that do not call `si.smooth`/`si.smoo` (the 81 items are 45 patch renders and 36 Ding event mixes; this report covers 12 sources plus the tract), the Ding event mixes (only one note per instrument is rendered below), block sizes other than 512, and any live (non-offline) host.

## Toolchain

dotnet 10.0.403 builds `FaustNativeRender` with `/var/lib/mind-aqua-self/env-review.sh`. Re-running the script reproduces the JSON byte for byte.

## Which stdlib the production daemon compiles against (2026-11-10)

The repo neither vendors nor pins faustlibraries (`.gitmodules` holds only zynaddsubfx and pink-trombone-mod). `NativeFaustRuntime.cs:530` passes `-I <home>/share/faust`, and `AquaSynthFaustToolchain.Find` (`NativeFaustRuntime.cs:651`) takes `home` from `AQUASYNTH_FAUST_HOME`, then `AQUARIUM_FAUST_HOME`, then `Tools/Faust` beside the app, then `C:\Program Files\Faust` (Windows) or `/usr/local`, `/usr`. So the daemon compiles against whatever Faust install the host has.

| host | Faust | `si.smooth` in its `signals.lib` | stalls (float32)? |
|---|---|---|---|
| Starfire `C:\Program Files\Faust` (read over ssh; faust.exe 2.85.5, `signals.lib` 2026-03-20, `smooth = smooth_imp(0)`, line 382) | 2.85.5 | `(1.0 - s) * x + s * y`, the same recursion as wasm's | yes, by the same mechanism |
| aqua-feel's wasm bundle (faustwasm 0.20.0, libfaust 2.90.19; embedded `signals.lib`) | 2.90.19 | `smooth(s) = *(1.0 - s) : + ~ *(s)` | yes: 4796.632 |
| this sandbox (`faustlibraries-20261031`, master 3d8c6ef, a one-commit shallow clone) | 2.90.20 | `smooth = imp(3)` with a comment on the stall | no |

I read only that Starfire file's `smooth` definition; I did not render on Starfire. On the evidence, the Windows daemon uses the old form, so **aqua-feel's wasm numbers are the closer model of that host than this sandbox's native render is**; the sandbox's imp(3) library is not what any shipped host has. Whether the daemon runs on Starfire in production is not established here; it is the only Windows Faust install found.

## The twelve sources that call `si.smooth` / `si.smoo`

The earlier "eleven" was seven patches, three Ding instruments and the larynx graph; the catalog has seven patches besides the thruster **including** the larynx graph, and **four** Ding instruments (`muted-pluck` was missed). Rendered: the thruster at rpm 1200, those seven patches, and one note per instrument (261.6256 Hz, 1.5 s, not the mixed event). Three conditions, each the same Faust text on both sides: as shipped (each side's own stdlib), the old smoother pinned (`nd_smooth(s) = *(1.0 - s) : + ~ *(s)`), and imp(3) pinned. Pinning rewrites `si.smooth` and `si.smoo` in the source text; a smoother called inside another library function is not rewritten, the `dx7-algorithm-32` residual below is not that, see `dx7-algorithm-32-native-diff-cause.md`. The tract voice rides along as a thirteenth row. Columns are native minus wasm. Raw rows: `aqua-feel-native-diff-smoother-{library,old,imp3}-2026-11-10.json`.

| source | stdlib as shipped (wasm bundle vs sandbox): max err / rel RMS dB | old smoother pinned | imp3 pinned | ΔLUFS lib / old / imp3 | Δ true peak dB lib / old / imp3 | Δ centroid % lib / old / imp3 |
|---|---|---|---|---|---|---|
| thruster@rpm1200 | 9.0e-2 / -16.6 | 4.5e-2 / -21.2 | 7.1e-6 / -99.9 | 0.000 / 0.001 / -0.000 | -0.081 / -0.152 / 0.000 | 0.07 / -0.08 / 0.00 |
| advanced-alien-multivalve-graph | 2.9e-5 / -68.4 | 1.0e-5 / -72.5 | 7.9e-7 / -97.9 | -0.000 / -0.000 / -0.000 | -0.001 / -0.000 / -0.000 | -0.00 / 0.00 / 0.00 |
| advanced-bird-syrinx | 2.8e-3 / -27.3 | 1.6e-3 / -31.7 | 3.2e-6 / -88.3 | -0.001 / -0.001 / -0.000 | -0.000 / -0.000 / -0.000 | -0.00 / -0.00 / 0.00 |
| advanced-bird-syrinx-graph | 9.4e-4 / -38.4 | 2.7e-4 / -47.1 | 1.5e-5 / -75.4 | 0.000 / -0.000 / 0.000 | 0.000 / 0.000 / -0.000 | -0.01 / 0.00 / -0.00 |
| advanced-syrinx-voice | 1.3e-3 / -23.9 | 3.6e-4 / -34.6 | 2.3e-6 / -86.3 | 0.001 / 0.000 / 0.000 | 0.003 / 0.003 / -0.000 | -0.00 / -0.00 / 0.00 |
| advanced-human-larynx-graph | 9.6e-5 / -38.1 | 3.3e-5 / -46.6 | 7.6e-7 / -94.2 | 0.002 / 0.001 / -0.000 | 0.002 / 0.001 / -0.000 | 0.05 / 0.05 / 0.04 |
| dx7-algorithm-32-additive-organ | 1.7e-4 / -79.3 | 1.6e-4 / -79.5 | 1.6e-4 / -79.5 | 0.000 / -0.000 / 0.000 | -0.000 / -0.000 / -0.000 | -0.00 / -0.00 / -0.00 |
| dx7-algorithm-8-bright-pair | 4.1e-6 / -94.5 | 4.2e-6 / -94.2 | 3.9e-6 / -94.7 | 0.000 / 0.000 / 0.000 | -0.000 / -0.000 / -0.000 | 0.00 / 0.00 / -0.00 |
| ding-instrument/glass-chime | 3.8e-2 / -18.4 | 2.5e-2 / -21.9 | 3.3e-7 / -130.0 | 0.000 / 0.000 / 0.000 | -0.000 / 0.000 / -0.000 | 0.02 / 0.01 / -0.00 |
| ding-instrument/muted-pluck | 6.6e-4 / -49.8 | 6.1e-4 / -50.2 | 3.0e-8 / -141.7 | -0.000 / -0.000 / -0.000 | -0.000 / -0.000 / -0.000 | 0.00 / -0.01 / -0.00 |
| ding-instrument/soft-triangle | 4.3e-3 / -30.3 | 2.4e-3 / -35.2 | 4.5e-8 / -142.0 | 0.000 / -0.000 / -0.000 | -0.000 / -0.000 / 0.000 | 0.16 / 0.12 / -0.00 |
| ding-instrument/warm-bell | 5.4e-2 / -18.3 | 3.2e-2 / -22.9 | 2.3e-7 / -134.2 | 0.000 / 0.000 / -0.000 | -0.000 / 0.000 / -0.000 | 0.01 / 0.00 / -0.00 |
| tract/vowel-a | 1.9e-4 / -52.6 | 6.7e-5 / -61.2 | 3.1e-7 / -109.3 | 0.017 / 0.006 / -0.000 | 0.013 / 0.004 / -0.000 | 0.01 / 0.01 / -0.00 |

Largest metric deltas over all sources and the three conditions: |LUFS| 0.017 dB (tract), |true peak| 0.152 dB (thruster, old pinned), |centroid| 0.16 % (soft-triangle; the earlier core run has 0.3 % at thruster rpm600). **No gate flag differs on any source in any condition**; the tract's patch-group `FAIL onsetMs` is on both sides (see above).

What the data says:

- With imp(3) pinned on both sides all 13 rows agree to -75 dB or better (Ding instruments -130 to -142 dB, thruster -99.9 dB, tract -109 dB; worst: `advanced-bird-syrinx-graph` -75.4 dB). `dx7-algorithm-32-additive-organ` sits at -79.3 (library), -79.5 (old) and -79.5 dB (imp3), so the pinned smoother does not move it. Cause found 2026-11-12 (`dx7-algorithm-32-native-diff-cause.md`): the native phasor increment rounds one 2^-30 step differently once `ba.time / ma.SR` shares the reciprocal of the sample rate (about -93 dB without it), and `si.smoo` text on the macro params is the rest (-141 dB with both removed).
- With the old smoother pinned the thruster still differs (-21.2 dB): native's LLVM contracts the recursion to FMA and stalls at 4797.657 instead of wasm's 4796.632. The old form's stall point depends on the arithmetic; the imp(3) form does not.
- As shipped, nine of the 13 rows differ at waveform level by -16 to -50 dB (thruster, the three syrinx patches, the larynx graph, all four Ding instruments); `advanced-alien-multivalve-graph` (-68 dB), the tract (-52.6 dB) and the two dx7 patches are closer.

## Tolerance in `native-diff.mjs`

The limits (`LIMITS`) and the comparison now live in `tools/aqua-feel/native-compare.js`; `native-diff.test.mjs` trips each one. Added, because the data shows a gap. Across all 13 sources and the 3 conditions the metric deltas stay under |LUFS| 0.017 dB, |true peak| 0.152 dB, centroid 0.3 %. A deliberate +1 dB gain on the native buffer moves LUFS and true peak by 1.0 dB. The limits sit between: 0.1 dB, 0.5 dB, 1 %. Sample-level error has no limit with the library smoother (the waveforms legitimately diverge), but with `--smoother imp3` it has one at -60 dB (the worst row is -75.4 dB; a one-sample shift reads about -4 dB).

Mutation check on the tool, `--items smoother --smoother imp3`: unmutated, 12 of 12 clean, exit 0; +1 dB native gain, 12 of 12 flagged (`loudnessLufs`, `truePeakDb`, `relRmsErrDb`), exit 1; one-sample native shift, 12 of 12 flagged (`relRmsErrDb`), exit 1. A one-sample shift is not detectable with the library smoother; `loudnessLufs`, `truePeakDb` and centroid also cannot see it. The limits are tripwires fit to these 13 sources, not taste.

## What changes in aqua-feel's conclusions

- Nothing in the gate changes: no flag differs on any tested source, and every metric delta is far below any threshold in `thresholds.json` that I looked at except the rpm600 thruster hiss cap noted above (1.0 dB, still under the cap).
- aqua-feel's WAVs are a fair rendering of what a host with the old `si.smooth` produces (the only Windows install found). They are **not** sample-identical to this sandbox's native render, and no sample-level claim about the thruster, the syrinx patches or the Ding instruments should be carried across them.
- The listening trial heard wasm renders; for a Windows daemon that is the closer path. For a daemon built on the sandbox's library, the same sources have the same level and spectrum and a different phase trajectory.
- Nothing was heard.

## Proposed faustlibraries pin (not made)

The cause is that `si.smooth` is whatever the host's `share/faust` says. Options, in order of my preference (confidence moderate; none of this is Soul-checked):
1. Vendor a pinned faustlibraries tree under the daemon's `Tools/Faust/share/faust` at build, record its commit in the compile manifest next to `faustLibrary`, and have `AquaSynthNativeOptions` prefer it over the system install. This makes the wasm bundle, Starfire and the sandbox agree only if aqua-feel's wasm path pins the same text too (faustwasm takes a library via its virtual FS), so pin both.
2. Decide the smoother once, in AquaSynth's emitted source: lowering emits a local smoother definition instead of calling `si.smooth`, so no host's stdlib decides it. Smaller, but changes every lowered patch and does not cover library functions that smooth internally.
3. Leave unpinned and record the host library identity in each compile manifest, accepting skew.

Which smoother is the right one is a sound decision, not a measurement: imp(3) cannot stall (the library's own comment), the old form lands 0.07 % short of an rpm target at 4800. That is the owner's call.
