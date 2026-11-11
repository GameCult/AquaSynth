# aqua-feel sync

Checks audio-visual synchrony of a capture: finds audio onsets and visual onsets, pairs them, reports the lag per event in ms and
compares it with published detectability windows. Standalone: Node 18+, no npm dependencies (`feel.mjs` imports `sync.mjs`, which
imports one FFT from `metrics.js`). ffmpeg (no ffprobe) is needed only to read a video file directly.

**The metric is not the verdict.** It compares two onset lists extracted from signals. A human ear and eye still decide whether a
capture feels in sync. Nothing here heard or saw anything.

## Run

```
# WAV + a per-frame track (no ffmpeg needed)
node feel.mjs sync --wav capture.wav --track frames.csv [--events hits.txt] [--fps 60] [--kind luma|flux]
# a video with one audio and one video stream (needs ffmpeg on PATH, or FFMPEG=/path/to/ffmpeg)
node feel.mjs sync --video capture.mp4 [--events hits.txt] [--kind luma|flux]
# options: --profile default|bt1359|ebu-r37|atsc-is191   --lead-ms N --lag-ms N   --pair-window-ms N (default 200)
#          --min-gap-ms N   --allow-unpaired   --json out.json   --no-gate   --shape step|ramp (ramp: slow swells, see below)
node sync.test.mjs                   # 37 checks on synthetic fixtures; 46 with FFMPEG=/path/to/ffmpeg (also builds clips with known stream offsets and runs a real browser capture)
node sync-fixture.mjs DIR 80 60      # write a fixture (audio 80 ms late, 60 fps) to try the CLI on
```

Exit 0 when no pair is detectably outside the window; 1 when any pair is outside, an event or onset is unpaired or has no partner, a pairing is ambiguous,
or nothing paired; 2 on a tool error (bad option, unreadable or malformed input). `--no-gate` always exits 0. `--allow-unpaired` stops stray
onsets (background hits that have no counterpart) from failing the check.

**Inputs.** `frames.csv`: one row per video frame, `t,value` in seconds (a header line is skipped), or one value per line with `--fps`.
`value` is mean frame luminance (`--kind luma`, default: a flash or cut is a positive step) or a frame-difference magnitude
(`--kind flux`, motion flux: a start of movement is a rise). `hits.txt`: seconds, optionally `,label` per line (`#` comments), or JSON
`[0.5, {"t":1.3,"label":"hit"}]`. With a video, luminance and flux are computed on a 64x36 gray downscale; stream `start_time`
offsets are applied so both clocks are the container's.

**Lag convention.** `lag = audio onset - visual onset`. Positive means the sound comes after the picture (audio late); negative means
the sound comes first (audio early). With `--events`, each logged event is matched to the nearest audio and nearest visual onset
within the pairing window (200 ms), and `a-ev` / `v-ev` give each side's offset from the logged time, so you can tell which side is late. Without an
event log, audio onsets pair with visual onsets by proximity (within the pairing window, one-to-one, nearest first). **A pair is `ambiguous-pairing` and fails the check when
another onset of the same kind lies closer than twice the pairing window** (400 ms by default). Nearest-onset pairing is right only if the true lag is under half
the spacing of the onsets, and the tool cannot know the lag, so it takes the pairing window as the largest lag you consider plausible. The cost is real: material
faster than 2.5 hits per second cannot pass at the default, however good the sync. Narrow `--pair-window-ms` if the lag is known to be small (`50` clears 100 ms
spacing), or space the events out. A lag beyond the window reads as `no-audio-onset`, or, when the previous hit's sound lands inside the window, as that hit's (wrong sign, e.g.
+250 ms at 0.45 s spacing reads -200 ms); it fails either way, but do not trust the sign of a lag larger than half the hit spacing.
Parsers refuse input they cannot read (timecodes, a third column, empty cells, non-increasing times) rather than guess.

## How onsets are found

- **Audio:** log-magnitude spectral flux (1024-sample window, 128-sample hop, 60 Hz and up), half-wave rectified; peaks over
  median + 8 robust sigmas, at least 50 ms apart; each is then refined on the samples to the first one that rises above both 10% of the local peak and
  2.5 times the steady bed (the median block peak of the 85 ms before, after removing the median as DC). A bed louder than a tenth of the hit still defeats it.
- **Visual:** the positive step (luma) or the value (flux) of the track; peaks over median + 8 robust sigmas and at least 10% of the
  track's range, at least 100 ms apart (`--min-gap-ms` changes it, for both trains; flashes closer than that merge into one onset and the events beyond the first go unpaired); the onset is the first frame of the rise reaching 50% of the peak.

## Verdict

Per pair, against a window of how far audio may lead (`lead`) or trail (`lag`) the picture before the offset is detectable:

| profile | lead ms | lag ms | source |
|---|---|---|---|
| `default` | 25 | 100 | the conservative range this tool was asked for; it is not one standard: it sits between the ATSC and ITU figures. Treat as a tripwire |
| `bt1359` | 45 | 125 | ITU-R BT.1359-1 (1998), "Relative timing of sound and vision for broadcasting": detectability threshold +45 / -125 ms (acceptability +90 / -185) |
| `ebu-r37` | 40 | 60 | EBU R37 (2007) limit for emission, sound before / after picture. Figure taken from a secondary summary, the EBU text was not read |
| `atsc-is191` | 15 | 45 | ATSC IS-191 (2003) limit at the DTV encoder input. Secondary summary; the same document gives +30 / -90 ms end to end. The document itself was not read |

Verdict words: `inside-window` (the whole truth interval, below, lies inside the window); `audio-late-detectable` / `audio-early-detectable` (the whole interval lies outside);
`borderline` (the interval straddles a window edge: not shown to be outside, so it does not fail the gate, and the report says "NOT DETECTABLY OUTSIDE" rather than "WITHIN");
`ambiguous-pairing`; `no-audio-onset` / `no-visual-onset` / `no-onsets` (the logged event had no onset within the pairing window, so a sound further than 200 ms from its event
reads as absent). `pass` means no pair is detectably outside, ambiguous or missing, and nothing is unpaired (unless `--allow-unpaired`).
Sources: [ITU-R BT.1359-1](https://www.itu.int/dms_pubrec/itu-r/rec/bt/R-REC-BT.1359-1-199811-I!!PDF-E.pdf) (the PDF text was
fragmentary in the search; figures confirmed against secondary summaries), and the TV Technology articles
[AV synchronization, how bad is bad](https://www.tvtechnology.com/opinions/av-synchronization-how-bad-is-bad) and
[ATSC recommends AV sync rules](https://tvtechnology.com/opinions/atsc-recommends-av-sync-rules). Windows depend on content (speech
lip-sync is more sensitive than a game impact) and on the viewer; they are population thresholds, not a property of your capture.

## Ramp-shaped visuals (`--shape ramp`)

Some visuals answer a sound with a slow swell (a scene brightening over a few hundred ms), not a step. The default detector looks for a peak in the frame-to-frame step, finds none, and says no visual onset
(`step` found 0 of 5 hits on `fixtures/perlines-trails-ramp.webm`; `ramp` paired 3). `--shape ramp` takes the track's rise over 300 ms as the novelty and picks its peaks (at least 15% of the track's range, 6 noise sigmas,
at least 500 ms apart). For each it finds the quiet level before the rise (the last sample within 2 noise sigmas of the minimum of the 0.6 s before) and the 20% and 80% crossings of the rise to its top.

**What it claims.** Only this, and only in one direction. The picture had started to change by the first frame whose raw value stood above the quiet level by 20% of the rise (and at least 4 noise sigmas): `hiS`. If the sound still came
later than the window after that frame, the verdict is `audio-late-detectable`; that claim does not depend on the ramp's shape. Everything else is `borderline`, always: **a ramp is never reported `inside-window` or `audio-early-detectable`,**
because the start of a swell cannot be bounded from below by looking at it (a slow start sits under the noise; the same sound can sit 0 to 300 ms after the real start). The report still prints a lag interval, from `hiS` back to the quiet
minimum backed off by the valley-to-crossing time and one frame; the back-off is a heuristic that held on the synthetic sweep, not a bound, and no verdict leans on it. The point value (`visual s`, `lag ms`) is the foot of the line through
the 20% and 80% crossings: it fits a linear ramp, reads early for exponential approach and late for slow-start shapes, and is not the instant anyone perceives. The synthetic sweep (`sync.test.mjs`, 120 shapes x rises 40..800 ms x decays x 24..120 fps x
noise x offsets) never produces a wrong ramp verdict; a Soul pass found two earlier versions that did (a centred 60 ms average crossing before the picture; the lower bound taken as a bound).

**What it cannot claim.** That the sound is in sync with a swell (only that it is not detectably late). When the viewer sees the swell begin (later than the physical start; depends on contrast, size and attention). That the swell was caused by this sound: pairing
is by nearness to the foot, and a bed that swells on its own, a scene change or a second sound within 500 ms gives a false pair. Ramps closer than 500 ms merge (so the ambiguity guard, which fires on neighbours closer than twice the pairing window, cannot fire at the default 200 ms
window; the merged hits show up as unpaired audio). Ramps under 15% of the track's range, or on a steady drift that rises over the whole clip, are not found. `--kind flux` with `--shape ramp` means a swell of motion energy; it has not been tried on anything
but one fixture. The constants (300 ms rise window, 15% floor, 6 sigmas, 500 ms gap) were set on Perlines v2's Vector trails and the synthetic shapes only.

## Uncertainty

A visual onset can only land on a frame, and a frame is stamped when its picture started to be displayed. A change at time tau first shows in the first frame
stamped at or after tau, so the measured visual time is late by 0 to one frame period, and the measured lag (audio minus visual) is **biased low** by that much. The audio
onset is good to about half a hop (1.4 ms). Each pair therefore reports `lagLowMs = lag - 1.4` and `lagHighMs = lag + frame period + 1.4`: the truth lies in that interval
(16.7 + 2.7 ms wide at 60 fps; 33.3 + 2.7 ms at 30 fps). A video is resampled to constant frame rate, which snaps each frame to the nearest source frame, so for a video the interval is half a period wider on each side (`[lag - period/2 - hop/2, lag + 1.5 period + hop/2]`), and the verdict is read off the interval. This holds for events on and off the frame grid in the fixtures
(`sync.test.mjs`; the video clips there are built with frame stamps at or after the change, the model's assumption). The display's own latency (the time from stamp to light) is not in it.

## What the metric cannot tell you

- **Whether it feels right.** Two onsets agreeing says the energy rose at the same time, not that the sound belongs to the picture.
  A person must watch and listen.
- **That the pairing is the right pairing.** Onsets are matched by nearness. A sound that belongs to an earlier or later visual
  event can be paired with the wrong one; with an event log, the log is trusted, with none, nothing checks the pairing but you.
- **Which onset the viewer attends to.** A swell, a reverb tail, a ramped attack or a low-frequency thump has a perceptual onset
  later than the first sample above the threshold; a flash with a fade reads late the same way. A decaying visual (afterimage, a bloom
  that outlives the event) is not modelled.
- **Anything about the chain you did not capture.** If the capture is of the app's output, display latency, speaker/Bluetooth
  latency and a player's own sync are not in it. A capture can be in sync and the playback chain still be 150 ms off.
- **Variable frame rate.** A video is resampled by presentation time to constant frame rate (ffmpeg `fps` filter). The rate is the stream's declared one; a browser capture declares none (`1k tbr`), so it is the median gap between the frames' own times (24 to 120 fps; before this it silently fell back to 30). The interval's frame period is the larger of the resampled period and the 95th-percentile gap between source frames, so a capture that stalls for 50 ms cannot be reported as tight as a 60 fps one; the report says so when the gap sets the period. A CSV track keeps its own timestamps.
- **Stereo is mixed to mono**, so an antiphase stereo track cancels to silence and reads as no onsets.
- **Content that is not onset-shaped.** Continuous music against continuous motion has no onsets to compare; the tool returns
  few or none, and silence in either track is reported as no pass, not as sync.
- **Measurements from downscaled luminance.** A flash that is local (a small icon) moves the mean luminance little and may fall
  below the 10%-of-range floor; use a cropped frame-diff track in that case.
- **Known stream offsets.** `sync.test.mjs` builds clips with ffmpeg whose video or audio stream starts late (`-itsoffset`, stream copy) and checks that the known lag lies in every pair's interval, so the container start-time bookkeeping has a ground truth; the clips are synthetic flashes and tones.
- **A pulsing bed.** The bed estimate is a median of block peaks and cannot follow dynamics: a 4 Hz tremolo bed at 10% depth under the hits pulled some lags off by up to 28 ms in testing. Beds louder than a tenth of the hit and noise gates that cut the sound to digital silence (the noise resuming reads as an onset) can also give a wrong onset that passes when the true lag is well inside the window.
- **One real clip.** `fixtures/browser-capture.webm` is a headless-Chromium MediaRecorder capture (WebAudio beeps and canvas flashes scheduled at the same instants, vp8 + opus, recipe in `fixtures/record-browser-capture.cjs`). It is a test that the video path runs and pairs, not a validation of the lag it reports: nothing independent says what the true lag of that clip is. `fixtures/perlines-aurora-vfr.webm` is 6 s of Perlines v2 (Aurora disk scene, software-rendered headless Chromium) playing a click-train WAV, with the app's audio output and its canvas recorded; it declares no frame rate and stalls up to 34 ms, and is there to test that. A longer take of all three scenes and what it showed is on the minds.gamecult.org/aqua page named in the PR. No Playground UI clip has been run.
- **The tuned constants.** The 8-sigma threshold, window sizes, 200 ms pairing window, 10% floors and 2.5x bed factor were set against synthetic
  fixtures (`sync-fixture.mjs`: tone-burst hits against 4-frame flashes, known offset, with noise, tone and DC beds) and one browser clip. They have not been tuned on game or music content.
