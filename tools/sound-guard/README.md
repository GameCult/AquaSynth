# sound-guard

A regression check for the sound itself. One command renders (or is given) every shipped Dings clip (8 sender voices x 4 events, plus the 4 neutral fallbacks)
and every Earcon Workshop clip (4 identities x 6 events), runs `../aqua-ear` over each, and **exits 1 if any assertion fails**. It exists so a double hit,
a click, clipping, a cut tail or a clip that is suddenly louder than its siblings cannot come back unnoticed (the v3 Void knock, 95 ms into every note,
was a second hit that nothing measured until Meta heard it).

**It measures signal facts. It cannot judge annoyance, timbre or feel.** A clip can pass every check here and still sound bad; a clip can fail one and
sound fine. Nothing in this tool has heard anything.

## Run

```
python3 tools/sound-guard/guard.py                       # renders both families into a temp dir (about 10 s), checks 60 clips
python3 tools/sound-guard/guard.py --dings DIR --earcon DIR   # check existing output (DIR = finish.py's `out`, and a dir of cli.mjs packs, one subdir per identity)
python3 -m unittest -v test_guard                        # in tools/sound-guard: the guard's own tests, ~3 s
```

`python3` must have numpy, scipy and soundfile (libsndfile with Vorbis), as for aqua-ear; `node` renders and reads the intended note times. This sandbox:
`/var/lib/mind-aqua-self/ogg-venv/bin/python`. Self-render needs the neutral fallback clips (`finish.py` wants `out/neutral/*.ogg`): default
`config.neutral_source` in `thresholds.json`, or `--neutral DIR`. Exit 0 = pass, 1 = failed assertion or stale known failure, 2 = tool error (render failed,
aqua-ear's thresholds drifted from the pins, node missing).

## What is asserted, per clip

| # | assertion | how |
|---|---|---|
| 1 | one onset per intended note | Intended times: `RHYTHM` in `dings-voices/src/voices.mjs`; `EVENTS` x tempo in `earcon-workshop/web/earcon.js` (both machine readable, read through node). Each note owns the window [t - 20, t + 20] ms. Full band: each window has an onset, the loudest starts within 8 ms of t, an onset before it is a pre-echo only if it is 10 dB or more under it, an onset after it in the window is a double hit, any onset outside every window is stray. High band (>2 kHz): no onset outside any window. Kinds: `missing timing double stray stray_hf` |
| 2 | no click, step or DC | aqua-ear `click_step_dc`: discontinuities on the clip with 20 ms of silence in front (so a non-zero first sample counts), DC on the clip |
| 3 | no clipped samples | aqua-ear `clipping_true_peak` (overs, plateaus at >= 0.99), plus a plateau detector relative to the clip's own peak (WAV only) |
| 4 | true peak under a ceiling | measured on the decoded file; Dings -8 dBTP, Earcon and neutral -2.5 dBTP |
| 5 | tail settled | `abrupt_end` clear on every clip; for the voices built to end by the next onset (Kiko, Libby, Void, Nibu) `overlapping_tails` clear at each following note. Other voices and all earcons ring over the next note by design and are not asserted |
| 6 | loudness spread | LAE (A-weighted exposure, the quantity `finish.py` equalises) of every clip within +-0.5 dB of the median of the 32 sender clips |

Not asserted: Earcon loudness (the events differ on purpose), anything about the 12 non-shipped Nibu proposal clips.

## Thresholds: `thresholds.json`

Every number the guard compares against is in that one file, each with a `why`. `guard.py` has no magic numbers (a test parses its source to prove it) and
`test_guard.py` fails if a threshold has no reason or the code reads a name the file lacks. aqua-ear's own detector boundaries stay in `aqua_ear.py`, so
that file owns them; the guard **pins** the ones it relies on (`aqua_ear_pins`) and refuses to run (exit 2) if they drift, so loosening aqua-ear cannot
loosen the guard silently. Change a number by editing its value and its `why` together.

Voice policy (also in the file): `swell_voices` (Eureka, Heimdall: slow attack by design, so the full-band count is not asserted), `settle_before_next_voices`,
and `neutral_events` (the neutral clips are Earcon Workshop warm-bell renders; their note times are inferred from the audio).

## Known failures

`known_failures` in `thresholds.json` lists clips that fail today, each with the assertion, the measured number, a slack and a reason. The guard prints them on every
run. If one stops failing the run fails (`STALE`: remove the entry); if its number moves by more than the slack the run fails until the entry is re-justified. Today:
the four neutral fallback clips are 4.3 to 13.7 dB louder than the sender clips (assertion 6). Everything else passes.

## What the guard cannot see (measured, not guessed)

- Two hits of the same timbre inside one rising attack, or a flam under about 25 ms whose second hit adds less than 4 dB: the onset detector needs a 4 dB rise.
  Depends on phase: a copy of a Void note 15 ms later was caught (10 ms was not); the same copy 10 to 20 ms later was missed on Kiko and Weksa. A spectral-flux detector (listed as not built in aqua-ear) would close it.
- The v3 knock: caught down to 20 dB under its v3 level on every Void motif tried; at 22 dB under only on the loudest clip. Below about -76 dBFS in the high band the
  legitimate clips (Aqua's glide, Heimdall's swell) have their own rises, so the floor cannot go lower without false alarms.
- Eureka and Heimdall full-band onsets (swell voices): only high-band strays are asserted for them.
- Flat-topped limiting below 0.98 of the clip's peak on a decoded Ogg (Vorbis rounds the plateau off); a WAV shows it.
- Anything under -80 dBFS in the high band, and everything about how the sound feels.

aqua-ear gaps the guard works around, found while building it: it misses an onset that is already at full level at sample 0 (so the guard pads 20 ms of silence); it counts
the envelope ripple of low harmonic tones as onsets (so the guard peak-holds the envelope 10 ms; Heimdall gave 50 to 90 onsets a clip); and its `double_hit`/`overlapping_tails`
verdicts fire on 13 of the 32 sender clips (Vorbis pre-echo 12 to 15 ms ahead of an attack, Nibu's 8 kHz grain, ring-over voices), which are not defects. The guard does its own note-aware
check on top of aqua-ear's onset table. These could move into aqua-ear; they were not changed there.
