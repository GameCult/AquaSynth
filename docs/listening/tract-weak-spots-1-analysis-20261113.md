# tract-weak-spots-1: why the predictions failed, and what was changed

The verdict is `tract-weak-spots-1-meta-20261113.md`. Every falsifiable prediction in the kit failed although the formant numbers
behind them were right. The kit measured the steady part of each vowel (35-85 % of the clip) and the peak of each burst; the faults
Meta heard sit in the first 100 ms of every clip, at the phone boundaries, and in how long the burst lasts. Nothing in the old
measurement set looked there. Everything below was measured on the clips Meta heard (`weak-kit-out/clips`, WordRender at 140 Hz) before
any change, and again after.

## Causes found (measured, not guessed)

| # | what she heard | cause | evidence (before) |
|---|---|---|---|
| 1 | nibu: "both consonants are just clicks"; leak-before: "reversed percussive hit" | The merged control curve of a surface holds the previous phone's value to the boundary and steps to the next phone's first value there (two points at one time). A step in a ladder cell's area is a one-sample transient. | Pitch-period window peaks: 0.399 at 95 ms and 0.487 at 275 ms, against vowel pulses of ~0.1-0.2; both instants are phone boundaries of the script (`start=0.095`, `0.275`). The whole-render peak the kit reported (0.487) was this click. Click ratio 3.54 (`ear_metrics.py`). The old report called 0.487 vs 0.399 a "leak" difference. |
| 2 | every clip starts as a fade-in; the "kazoo" with "no audible closure" (ba, no burst); the /ɪ/ not like "bit" | `si.smoo` starts every control slider at 0. A word began with the tract shut, the vowel weights at 0 and the source off, and climbed for ~100 ms. The 140 ms "closure" of ba was largely this climb. | Isolated /ɪ/: level -66 dB at 0 ms, -45 at 40 ms, -30 at 100 ms, -28 from 140 ms; LPC bandwidth of F1 367 Hz at 0 ms, 232 at 60 ms, 103 at 100 ms, 90 from 140 ms. 100 of the clip's 309 ms were the climb; the steady-state window (35-85 %) started after it. |
| 3 | ba with burst: "fah then bah" | The stop opened over 0.65 to 1.0 of its gesture: 33 ms of a 95 ms /b/. A burst of that length is a fricative. | Burst-notes: ba burst 0.0707-0.1039 s (33 ms). A recogniser reads the clip as `v ɑ` (with the burst), `b ɑ` without. |
| 4 | nasals: "basketball buzzer, no audible articulation", /n/ and /m/ alike | The murmur had no entry and exit transitions (the step of cause 1) and began after the climb of cause 2; the machine ear read /n/ as /m/ (`v ɑ m ɑ`). | recogniser, before: ana -> `v ɑ m ɑ`. |
| 5 | bun with burst: "a truck releasing its pneumatics" | cause 3 (long burst), same mechanism. | burst 0.0721-0.0998 s (28 ms). |

## What was changed (PR "phone transitions", this branch)

1. `PatchScript.WithPhoneTransition`: every authored gesture spline starts, when an earlier gesture left the surface at another value,
   with a 30 ms ramp from the previous value (at most 0.4 of the gesture, and before its second point). The first phone of a word and
   surfaces no earlier phone touched are unchanged. Test: `PhoneTransitionTests.NoSurfaceStepsAtAPhoneBoundary` (worst step in 1 ms
   around each boundary of nibu: below 0.03; before, 0.77: the vowel's 1 against the nasal's released 0.23).
2. A stop holds all its articulators shut until 12 ms before its end and opens in those 12 ms (`StopReleaseSeconds`, a design value);
   it used to open over 0.65 to 1 of its gesture. Test: `AStopOpensInTwelveMillisecondsNotAThirdOfItsGesture`.
3. `smoo_init` replaces `si.smoo` on every control-surface and spline-point slider of the emitted DSP: the smoother starts at its input
   and keeps si.smoo's pole (a 22.7 ms time constant), so live control moves as fast as before. Patch parameters keep `si.smoo`. The
   source's own 10 ms pitch-ratio smoother starts at its input too. Tests: `ControlSmoothersStartAtTheirInputNotAtZero` (text) and
   `ASmootherStartsAtItsInputAndMovesAtSiSmoosPace` (the emitted smoother, rendered).
4. The word scaffold's vocal envelope gets a 12 ms attack (it was 0, so the now-instant start was itself a click: first-sample peak 0.22).
5. The ramp into a stop (item 1) sweeps the contact through every aperture between open and shut, and the exporter's burst noise is
   `4 * opening * (1 - opening)` times a gain that was held through the whole gesture: each closing ramp was a 15-20 ms hiss before the
   stop (>4 kHz share of the 6 ms frames at 188 ms of /ata/: -1 dB, against -57 dB with the closure stepped). The burst gain now exists
   only from the stop's release start (`PatchScript.StopReleaseStartSeconds`, the one owner of the release time), one ramp either side.
   A stop's lips are shut (aperture 0), not at 0.04: the word-initial /b/ voice bar read -19.1 dB re the vowel after it and /p/ -30.1
   against the design -34 to -26 and -60 to -38; shut they read -27.7 and -38.8. Spline handle times follow their segment (a third of
   it), so the C# timeline and the DSP agree on the 12 ms opening (the lowering ignores handle times). Tests:
   `ClosingIntoAStopMakesNoHighFrequencyNoise`, `TheDspRendersTheStopOpeningAsTheTimelineEvaluatesIt`, `PlosiveClosureCarriesTheVoiceBarOnlyWhenVoiced`.
6. `tools/formant-check/ear_metrics.py` (click ratio, largest sample step, LPC bandwidths, HNR, energy above 4 kHz) and
   `tools/formant-check/phone_probe.py` (a speech recogniser as a machine ear) so these faults are measured, not heard first by Meta.

### After (same words, same 140 Hz, `fix-out-c`)

| word | before | after |
|---|---|---|
| nibu_flat | click ratio 3.54, peaks 0.399 / 0.487; recogniser `i b u` | peaks at the boundaries 0.077 / 0.089; click ratio 1.79 (the vowel's own pulses); median sample step in the /b/ closure 0.0007 (0.0005 before; 0.0161 with the ramp hiss); recogniser `n i l t uː` as read before the closure-noise and shut-lips fixes (not re-run: no torch on this host; the /n/ is back, the /b/ reads as `l t`: see below) |
| ba (burst) | recogniser `v ɑ`; burst 33 ms | `b ɑ`; opening 12 ms |
| ba (no burst) | `b ɑ` | `m ɑ` |
| ana / ama | `v ɑ m ɑ` / `v ɑ m ɑ` | `ɑ n ɑ` / `ɑ m ɑ` |
| isolated /i/ | `h iː`; level climb 100 ms | `i`; level within 3 dB of the steady value by 20 ms |
| isolated /ɪ/ | `h i` | `ʔ i` |

## Not fixed, and why (open until she listens)

- **Voice quality: "kazoo with a two-pack-a-day smoking habit" (the two clips she kept), "reedy, like Pink Trombone" (/ʊ/), "basketball
  buzzer" (nasals).** Her comments on the two keeps say the vowel is right and the voice is wrong. Candidates, none tested by ear:
  (a) the glottal source is a band-limited saw through a one-pole lowpass with no closed phase (`FaustExport.PrimitiveSourceExpression`),
  where a Rosenberg/LF pulse has the open and closed phases real fold flow has; (b) harmonics-to-noise ratio of the steady part is 13-15 dB
  (healthy voices ~20+), from the source's `0.08 * noise` term and the saw's aliasing; (c) LPC bandwidths of the steady vowel are 90 Hz (F1),
  130 (F2), 100 (F3) for /ɪ/: plausible, so the wall-loss hypothesis is weak (the F2 of /ɑ/, 29 Hz, and the F1 of /i/, 14-24 Hz, are LPC
  artefacts of a pole sitting on a harmonic). Settling this needs a listening kit that varies one of them at a time; the next kit does.
- **/ɪ/ as the "eu" of cordon bleu.** The recogniser cannot judge this tract's vowel quality (every isolated vowel came back /i/). The
  confirmed fault in that clip is cause 2: a third of it was a muffled climb through bandwidths of 300 Hz. Whether the steady /ɪ/ then
  reads as "bit" is for her ear; lax vowels in isolation and without vowel-inherent spectral change are known to be misheard
  (Hillenbrand et al. 1995, Strange et al. 1983), so the next kit also carries /ɪ/ in a word.
- **The medial /b/ of nibu reads as `l t` to the recogniser after the fix, where it read `b` with the click and the long burst.** The closure
  voice bar sat 10 dB under the vowel (a murmur-like level; a stop's runs 20+ dB under; 16-19 dB with the lips shut), and a labial burst is injected at the glottal
  end in a word with no other plosive (`FaustExport` burst injection), so it is filtered by the whole tract like aspiration. Both are
  next, with the listening to decide.

## What the old measurements missed, as a rule

A steady-state window measures a steady state. Faults of onset, boundary and duration need metrics that look at onsets, boundaries
and durations: click ratio and step size at each phone boundary, time-to-full-level, burst length, bandwidths, HNR. These now exist
(`ear_metrics.py`) and the click and onset ones are pinned by tests.
