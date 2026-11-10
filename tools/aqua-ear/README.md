# aqua-ear

Audio perception for AquaSynth: give it a wav, ogg or flac and it writes pictures a vision model can read, an onset list at 1 ms, a level
envelope, and five detectors that each print one line with the numbers behind it. A compare mode does the same for two files and says what
changed. It is the counterpart of `../aqua-feel` (which renders and measures AquaSynth's own patches): aqua-ear looks at any finished audio
file, including a decoded Ogg Vorbis clip.

**Nothing here hears anything.** Every number is a signal fact about a decoded buffer. A detector firing is a thing to look at or listen to,
not a defect; a detector staying quiet is not a pass.

## Run

```
# once: numpy, scipy, matplotlib and soundfile (libsndfile does the Vorbis). Nothing else; no network at run time.
python3 -m venv ear-venv && ear-venv/bin/pip install numpy scipy matplotlib soundfile
# (this sandbox: /var/lib/mind-aqua-self/ogg-venv/bin/python already has all four)

python aqua_ear.py analyze clip.ogg --out out/clip         # one file
python aqua_ear.py compare before.ogg after.ogg --out out/cmp   # two files, B relative to A
python aqua_ear.py --help        # add --strict to either to exit 3 when any detector fires
python -m unittest -v test_aqua_ear   # 25 tests on synthetic fixtures generated in the test; ~6 s
```

Exit code: 0 whatever the findings, 2 on a tool error (unreadable file), 3 only with `--strict` and a firing detector.

## Output

`analyze` writes, to `--out`:

| file | what |
|---|---|
| `spectrogram.png` | whole file, log frequency 40 Hz to Nyquist, dBFS colour scale (80 dB range, a full-scale sine reads 0 dBFS), onsets as labelled dashed lines. Fonts are 15 pt and up on a 1600 px canvas |
| `zoom.png` | one row per onset window (15 ms before to 85 ms after; overlapping windows merge; at most 6): waveform on the left, 5.3 ms-frame spectrogram on the right, onset times printed in boxes |
| `envelope.png` | level in dBFS (1 ms envelope) against ms, onsets marked |
| `onsets.json`, `onsets.txt` | onset list, 1 ms resolution, with `band` (`full` or `hf`), peak level and peak time |
| `report.json`, `report.txt` | file facts (peak, true peak, BS.1770 loudness, rms), onsets, the five verdict lines, and every threshold used |

`compare` writes `compare.png` (A and B spectrograms stacked on one time axis, envelopes overlaid), `compare.txt`/`compare.json` (onset diff
with matches inside 3 ms, per-detector verdict diff with CHANGED marked, B minus A deltas for peak, true peak, loudness, rms, duration) and the
full single-file reports in `a/` and `b/`.

## Onsets

Two channels, both on a 1 ms grid. `full`: Hilbert magnitude of the signal, 1 ms smoothing, onset where the level rises 4 dB above its trough
of the last 20 ms (trough clamped to max(-60 dBFS, file peak -45 dB)); the reported ms is where the rise crosses trough +2 dB, so it is the start of
the rise, good to a millisecond or two. `hf`: the same detector on the signal above 2 kHz (4th-order Butterworth, zero-phase), ignored below
-75 dBFS. The `hf` channel exists because a tick riding on a tone (the Dings Void tick, 20 dB under the tone) barely moves the full-band level and is
plain in the high band. An onset that lands while the previous one is still rising is merged into it, and a precursor that never dips is one onset.

## Detectors

Each verdict line carries its own stated assumption. All boundaries are the `TH` table at the top of `aqua_ear.py` and are printed in `report.json`.

| detector | fires when | notes |
|---|---|---|
| `double_hit` | two onsets in one band closer than 80 ms, within one note, with peaks within 20 dB | **one note** = the full-band level between the two never falls more than 15 dB below the first onset's peak (for `hf` onsets: below the level there). Two staccato notes that die between hits are not a flam; two hits 150 ms apart are not; a precursor 30 dB under the main hit is reported as "precursor", not fired |
| `click_step_dc` | `\|mean\|` >= 0.005 (-46 dBFS), or a discontinuity: second difference >= 0.02 and >= 25x its local (+-2.5 ms) mean | labelled `step` if the level 2 ms after differs from 2 ms before by half the jump or more, else `click`. A bright continuous tone does not fire; a sample-accurate jump on a quiet low tone (below 0.02) does not either |
| `clipping_true_peak` | any sample over 1.0, or a run of 3+ equal samples at >= 0.99, or true peak > 0 dBTP | true peak is 4x oversampled. Plateaus below 0.99 (a limiter at -2 dBFS) are not reported as clipping. A note at -1 dBFS does not fire. After a lossy encode a clip shows as overs and true peak, not equal samples |
| `overlapping_tails` | onsets >= 80 ms apart where the lowest level in the 5 ms before the new onset is above -40 dB re the previous note's peak | a long ring-out always trips this; it states a fact about overlap, not about whether the overlap is wanted |
| `abrupt_end` | last 3 ms above -40 dB re file peak and above -70 dBFS, and not 6 dB below the same window 9 ms earlier | a fade, a decayed tail, or trailing digital silence are clear |

## Reused from aqua-feel, and how

`true_peak` (the Hann-windowed 48-tap sinc, 4x) and `loudness_lufs` (BS.1770-4 K-weighting, 400 ms blocks, -70 absolute and -10 LU relative gates)
are ports of `../aqua-feel/metrics.js` `truePeak` and `integratedLoudness`, about 25 lines, not calls into it. Reason: aqua-ear is Python for
numpy/scipy/matplotlib and libsndfile (the only Vorbis reader on the box); calling JS would add node as a run-time dependency and a process per file.
A test runs both on the same buffer when `node` is present and requires agreement (true peak to 1e-5, loudness to 0.01 LU); it skips otherwise.
Coefficients are 48 kHz; other sample rates are resampled to 48 kHz for loudness only. Multichannel files are mixed to mono for everything.
aqua-feel's own onset and clip logic is not reused: it is tuned to its rendered patches (0.17 ms peak follower, first-onset only).

## What it cannot judge

Timbre. Annoyance. Whether a sound is pleasant, harsh, cheap, muddy, clear in a room or readable as its object. Musical feel, timing feel,
how an onset interacts with the next one in a listener's ear. Anything a listener hears as character. Whether a click is audible (it measures
size, not audibility: a 0.02 second-difference click on a quiet tone can be inaudible, and a barely-over-threshold tail can be a swamp). It also
cannot see what is under -60 dBFS in the `full` channel, cannot separate two onsets inside one rising attack, and reads pitch only as
a smear on the spectrogram. Verdicts are signal facts: the thresholds are tripwires chosen after looking at synthetic fixtures and the Dings Void
clips, not taste.

## Planned versus live

Live: everything above. Not built: per-onset spectral-flux detector for noise-on-noise, an audibility model, batch mode over a directory, `--strict`
per-detector selection. None of these is needed by a current consumer.

Examples are published to one place, https://minds.gamecult.org/aqua/ear/, with `python3 tools/publish-stable/publish_stable.py ear <dir of runs>` (see tools/dings-voices/README.md, Publishing).
