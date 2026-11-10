# aqua-feel: faustwasm vs the daemon's native libfaust path

**Nothing here was heard.** Numbers from offline buffers, same Faust source, 48 kHz, mono, 512-frame blocks, same params.

- wasm side: `tools/aqua-feel/render.js` (faustwasm 0.20.0, libfaust 2.90.x, `-ftz 2`).
- native side: `tools/FaustNativeRender` over `AquaSynthPatchCompiler` (`NativeFaustRuntime.cs:530`: `-single -ftz 2 -vec -lv 1`), libfaust 2.90.20 + LLVM built in the Linux sandbox (`AQUASYNTH_FAUST_HOME`, overlay of the 2026-10-31 build).
- driver: `node tools/aqua-feel/native-diff.mjs <FaustNativeRender dir> --tract <source.dsp>`; raw numbers in `aqua-feel-native-diff-2026-11-10.json`.
- tract voice: `vowel-a` Faust source from the 2026-11-08 render kit (0.72 s, default controls).

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

**sfxr: the two paths agree** to float rounding (2e-6). The sfxr flags and waivers (+0.908 dBTP, -5.33 LUFS, held noise) are properties of the Faust source, not of the wasm host.

**Tract voice: agrees to -52.6 dB** (max 1.9e-4 on a 0.084 peak); every metric within 0.02 dB or 0.01% centroid. Its `onsetMs` gate failure appears identically on both sides.

**Thruster: waveforms diverge, metrics agree.** The error grows from ~1e-4 in the first 0.25 s to the size of the signal by 2 s at rpm 4800 (relative RMS error -8 dB), so the two renders are not the same waveform at high rpm. Level and spectrum metrics stay close: LUFS within 0.014 dB, true peak within 0.09 dB, sample peak within 1.2 %, centroid within 0.3 %, crest within 0.1 dB (rpm1200 0.11), hiss within 0.3 dB except rpm600 A-weighted, which reads 1.0 dB higher native.

### Cause (measured, one part not isolated)

The thruster takes its rotor frequency from `rpm = hslider(...) : si.smooth(ba.tau2pole(0.2))`, and every oscillator (`os.sawtooth`, `os.square`, `os.osc`) integrates that frequency into phase. Rendering `rpm/6000` alone at rpm=4800: the first 1000 samples match to 0.001 rpm, then wasm **stalls at 4796.632** from about 2.5 s on while native converges to **4799.999**. A float32 simulation of the one-pole without fused multiply-add reproduces 4796.6318 exactly, so the wasm value is plain float32 rounding of the smoother (the increment drops under half an ulp). Native does not stall, so it evaluates the smoother with different arithmetic; whether that is `-vec`, `-lv 1` or LLVM JIT contraction was **not isolated** (a fused simulation stalls at 4797.66, not 4799.999, so FMA alone does not explain it). The ~3.4 rpm difference moves the rotor tone by ~0.085 Hz, a phase drift that grows to a full cycle in seconds; discontinuous saw edges then differ by up to 2.0 sample-wise (isolated `os.sawtooth(shaft)`: max err 1.99). Removing the `wander` noise changes nothing (rpm300 2.62e-2, rpm4800 2.89e-1 unchanged), so the random wander is not the cause. Noise-only parts (`rattle` 6.7e-4, `rpm/6000` 5.6e-4) agree closely, consistent with the sfxr result: the LCG noise is the same on both paths.

### What this invalidates

- **Thruster v5 mix / hiss caps / loudness / true-peak / centroid claims:** stand. They are statistical and agree within the numbers above. The rpm600 hiss cap (8 dB) has 1.95 dB headroom natively, not the 2.96 dB the threshold note assumes; still passes.
- **Anything that compares thruster sample-by-sample or by phase** (before/after waveform diffs, null tests, onset or envelope timing at sub-ms resolution on the rotor tone, `correlate` against ratings tied to exact waveforms) is wasm-specific. A listener on the daemon hears the same level and spectrum, a different phase trajectory.
- **A listening trial on wasm renders of the thruster** heard rpm settling 0.07 % short of the target at rpm 4800. Not a timbre difference by these metrics, but the trial did not hear the daemon's path.
- **sfxr flags:** not invalidated. They hold on the native path.
- **Not tested here:** the other 75 aqua-feel items; the Ding mixes; the faust-vec flag itself in isolation; block sizes other than 512; live (non-offline) host behaviour.

## Sandbox note

Native libfaust is buildable here: libfaust 2.90.20 with LLVM, built 2026-10-31 (`faust-llvm-build-20261031`, `faust-home-overlay-20261031`); `dotnet` 10.0.403 builds `FaustNativeRender` with `env-review.sh`.
