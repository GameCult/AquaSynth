# aqua-feel

Headless feel meter for AquaSynth. It renders every shipped patch and every Ding offline, measures the buffers, writes
JSON and a static HTML gallery, and gates the numbers against thresholds.

**Nothing here was heard.** Every number is a measurement of an offline-rendered buffer. Metrics are evidence for a
listener to check, not verdicts: a render can pass every threshold and sound wrong, and fail one and sound fine.
The WAVs and the gallery exist so a person can press play.

## Run

```
(cd ../aqua-playground && npm ci)      # faustwasm runtime, once
node test.mjs                          # 26 checks of the meter on synthetic signals; renders no patch
node feel.mjs run                      # render 81 items (~15 s), write out/{report.json,index.html,wav/}, gate; exit 1 on failure
node feel.mjs run --only thruster      # a subset (gate skips id-rule and waiver bookkeeping); --out DIR; --no-gate
node feel.mjs gate out/report.json     # re-gate a saved report (--partial for a subset)
node feel.mjs compare before.json after.json [--only SUBSTR]   # before/after table
node feel.mjs correlate trial-export.json ...                  # feel metrics vs aqua-trial listener ratings
```

Open `out/index.html` in a browser (it references `wav/` relatively). The gate is the CI step: `node feel.mjs run` exits
non-zero on any unwaived failure or stale waiver. No CI runner exists in this repo yet; nothing runs it automatically.

## What it renders

- Every patch in `tools/aqua-playground/lowered/` (41 `.aqua` files lowered by `tools/AquaLower`, plus the hand-written
  `thruster.dsp` at rpm 300 / 600 / 1200 / 4800), 3 s each at 48 kHz, no gate or trigger: the sound the patch makes when started.
  `advanced/additive-pad` and `advanced/electro-drop-demo` lower to ~1.5 MB of Faust each and are not in the playground, so not here.
- Every `DingCatalog` event on every instrument (9 x 4 = 36), mixed the way `DingsPlaybackHost` does it: each note rendered at
  `root * 2^(semitones/12)` for 1.5 s, scaled by command gain 0.6 and the note gain, placed at its delay.
  `lowered-dings/` is `dotnet run --project tools/AquaLower -- --dings tools/aqua-feel/lowered-dings`; `DingCatalogTests.LoweredDingsFedToTheFeelMeterMatchTheCatalog` fails when it drifts from `DingCatalog.cs`.
  The command gain 0.6 is an assumption (`DingsPlaybackHost` takes it from the caller as 0..1), so the Ding loudness window depends on it.

Render path: the same faustwasm offline processor the playground and the listening trial use (libfaust 2.90.x, `-ftz 2`),
mono channel 0. It is **not** the daemon's native libfaust path (`-vec`); on a thruster the two diverge in waveform because the wasm bundle's `si.smooth` and the sandbox's native stdlib are different library text (not the host, not `-vec`); level and spectrum metrics stay close: `docs/renders/aqua-feel-native-diff-2026-11-10.md`, `node native-diff.mjs`.

## What it measures (per item, `report.json` `items[].metrics`)

| metric | meaning | limits |
|---|---|---|
| `onsetMs` | first sample at or above max(-40 dB re peak, -70 dBFS), from note-on | Offline render has no device latency; this is the patch's own lead-in only |
| `attackMs`, `attackSlopeDbPerMs`, `attackWindowLimited` | 10% to 90% of the envelope maximum inside the first 150 ms after onset, on a 0.17 ms peak-hold envelope (30 ms release, so a low tone's zero crossings are not read as dips) | A faster attack reads as one hop. Ripple in the first peaks pulls the 90% point late (muted-pluck declares 1 ms and reads about 7); sustained noise beds read an arbitrary 60-130 ms. A swell longer than 150 ms reports a lower bound and sets `attackWindowLimited`. The envelope is a peak follower, so a tone below ~30 Hz would still ripple |
| `samplePeak`, `truePeakDb`, `overSamples`, `flatTopRuns` | peak, 4x-oversampled true peak, samples beyond 1.0, runs of 3+ samples at 0.999 | WAVs are float32 so overs survive to disk |
| `loudnessLufs` | BS.1770-4 integrated, mono, K-weighted (48 kHz coefficients; renders are fixed at 48 kHz), gated | A mono sound with no reference level; compare items, do not read it as a delivered loudness. A buffer under 400 ms is zero-padded to one block and reads low (0.1 s of full 1 kHz sine: -9.0, true -3.0); every shipped item is 1.5 s or longer |
| `crestDb`, `rmsDb` | over the active span (onset to last frame within 50 dB of peak) | |
| `centroidMedianHz`, `centroidCurve`, `fluxMean`, `fluxCurve` | STFT 2048/512 over active frames; flux is the change between unit-norm spectra | Flux on stationary noise is high; it is reported, not gated |
| `noiseToToneDb`, `noiseToToneADb` | energy not in stable spectral lines vs energy in them, flat and A-weighted | A heuristic split (see `metrics.js`): steady or decaying partials read tonal, noise-excited resonances read noise, gliding partials read noise. 8192-point window for spans of 1 s or more, else 2048, so compare the same sound across edits, not different sounds. It is window- and Q-dependent: white noise through a Q20 band-pass at 1 kHz reads as noise over 3 s (8192 window) but partly tonal over 0.5 s (2048 window), and a Q100 resonance reads tonal. The thruster rpm caps are fit to this heuristic at its 8192 window |
| `lowMotion` | span of 1 s or more with centroid spread under 0.08 octave and level range under 6 dB | The song-swarm collapse shape (docs/music-agent-orchestration-lessons.md): a held drone or a wash. A legitimate sustained drone is flagged too |

## The gate

`thresholds.json`: rules scoped to `all`, `patch`, `ding` or an id glob, each with a `why`; `level: warn` prints without failing.
`waivers` turn a known failure into a printed line and must name a reason and a follow-up id; a numeric waiver carries a bound
(`max`/`min`) and covers the item only up to it, so a waived item cannot get worse unseen. A waiver that no longer matches a
failing rule, and a rule with an `id` that matches no item (a renamed or dropped guard), are failures on a full run, so waivers
are deleted when the thing they excuse is fixed and a rename cannot silently disarm a cap. A missing metric fails its rule. The numbers were chosen after looking
at the shipped renders. They are tripwires, not taste.

Current waivers: `sfxr-classic-classic-sfxr-abstract-golf` (+0.9 dBTP, -5.3 LUFS, held noise) and `sfxr-explosion` (3 s of
steady noise without decay). Nobody has listened to either to see if the number is the sound.

## Effects shelf

`SHELF.md` and `shelf.json` list every renderable effect (thruster, Dings, earcon families, playground patches) with params, trigger, render command and duration; `node shelf.mjs check` runs every render command and checks the WAVs are finite and not silent. Nothing on it is heard (`listening: unheard`).

## Reuse

`report.js` imports `mean`, `spearman` and `permutationP` from `../aqua-trial/analyze.mjs` and the variants from
`../aqua-trial/logic.js`: `correlate` renders the trial's four thruster variants, measures them, and asks the trial's own
question (|rho| >= 0.5 and p < 0.05) of each metric against real ratings once listeners export some. With four variants
every correlation is really about the variant ordering.

## Recorded first run

`docs/renders/aqua-feel-2026-11-05.md`: before/after numbers for the PRC SYNTH1 and thruster edits.
