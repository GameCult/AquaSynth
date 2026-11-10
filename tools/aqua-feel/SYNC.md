# aqua-feel sync

Checks audio-visual synchrony of a capture: finds audio onsets and visual onsets, pairs them, reports the lag per event in ms and
compares it with published detectability windows. Standalone: Node 18+, no npm dependencies (`feel.mjs` imports `sync.mjs`, which
imports one FFT from `metrics.js`). ffmpeg and ffprobe are needed only to read a video file directly.

**The metric is not the verdict.** It compares two onset lists extracted from signals. A human ear and eye still decide whether a
capture feels in sync. Nothing here heard or saw anything.

## Run

```
# WAV + a per-frame track (no ffmpeg needed)
node feel.mjs sync --wav capture.wav --track frames.csv [--events hits.txt] [--fps 60] [--kind luma|flux]
# a video with one audio and one video stream (needs ffmpeg + ffprobe on PATH)
node feel.mjs sync --video capture.mp4 [--events hits.txt] [--kind luma|flux]
# options: --profile default|bt1359|ebu-r37|atsc-is191   --lead-ms N --lag-ms N   --json out.json   --no-gate
node sync.test.mjs                   # 19 checks on synthetic fixtures
node sync-fixture.mjs DIR 80 60      # write a fixture (audio 80 ms late, 60 fps) to try the CLI on
```

Exit 0 when every pair is `inside-window` or `borderline`; 1 when any pair is outside the window, an event has no onset, or nothing
paired; 2 on a tool error. `--no-gate` always exits 0.

**Inputs.** `frames.csv`: one row per video frame, `t,value` in seconds (a header line is skipped), or one value per line with `--fps`.
`value` is mean frame luminance (`--kind luma`, default: a flash or cut is a positive step) or a frame-difference magnitude
(`--kind flux`, motion flux: a start of movement is a rise). `hits.txt`: seconds, optionally `,label` per line (`#` comments), or JSON
`[0.5, {"t":1.3,"label":"hit"}]`. With a video, luminance and flux are computed on a 64x36 gray downscale; stream `start_time`
offsets are applied so both clocks are the container's.

**Lag convention.** `lag = audio onset - visual onset`. Positive means the sound comes after the picture (audio late); negative means
the sound comes first (audio early). With `--events`, each logged event is matched to the nearest audio and nearest visual onset
within 300 ms, and `a-ev` / `v-ev` give each side's offset from the logged time, so you can tell which side is late. Without an
event log, onsets pair by proximity (within 300 ms, one-to-one).

## How onsets are found

- **Audio:** log-magnitude spectral flux (1024-sample window, 128-sample hop, 60 Hz and up), half-wave rectified; peaks over
  median + 8 robust sigmas, at least 50 ms apart; each is then refined on the samples to the first one within 20 dB of the local peak.
- **Visual:** the positive step (luma) or the value (flux) of the track; peaks over median + 8 robust sigmas and at least 10% of the
  track's range, at least 100 ms apart; the onset is the first frame of the rise reaching 50% of the peak.

## Verdict

Per pair, against a window of how far audio may lead (`lead`) or trail (`lag`) the picture before the offset is detectable:

| profile | lead ms | lag ms | source |
|---|---|---|---|
| `default` | 25 | 100 | the conservative range this tool was asked for; it is not one standard: it sits between the ATSC and ITU figures. Treat as a tripwire |
| `bt1359` | 45 | 125 | ITU-R BT.1359-1 (1998), "Relative timing of sound and vision for broadcasting": detectability threshold +45 / -125 ms (acceptability +90 / -185) |
| `ebu-r37` | 40 | 60 | EBU R37 (2007) limit for emission, sound before / after picture. Figure taken from a secondary summary, the EBU text was not read |
| `atsc-is191` | 15 | 45 | ATSC IS-191 (2003) limit at the DTV encoder input. Secondary summary; the same document gives +30 / -90 ms end to end. The document itself was not read |

Verdict words: `inside-window`; `audio-late-detectable` / `audio-early-detectable` (outside the window by more than the measurement
uncertainty); `borderline` (the window edge lies within the uncertainty); `no-audio-onset` / `no-visual-onset` / `no-onsets` (the
logged event had no onset within 300 ms). `pass` means every pair is `inside-window` or `borderline` and none is missing.
Sources: [ITU-R BT.1359-1](https://www.itu.int/dms_pubrec/itu-r/rec/bt/R-REC-BT.1359-1-199811-I!!PDF-E.pdf) (the PDF text was
fragmentary in the search; figures confirmed against secondary summaries), and the TV Technology articles
[AV synchronization, how bad is bad](https://www.tvtechnology.com/opinions/av-synchronization-how-bad-is-bad) and
[ATSC recommends AV sync rules](https://tvtechnology.com/opinions/atsc-recommends-av-sync-rules). Windows depend on content (speech
lip-sync is more sensitive than a game impact) and on the viewer; they are population thresholds, not a property of your capture.

## Uncertainty

Reported as `uncertaintyMs`: half a frame period plus half an audio hop (about +/-9.7 ms at 60 fps, +/-18 ms at 30 fps). A visual
onset can only land on a frame, and a frame stamps when the picture started to be displayed, not when the light reached the eye.
Treat lags closer together than the uncertainty as equal.

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
- **Variable frame rate.** Constant fps is assumed from `avg_frame_rate` (video) or the track's own timestamps (CSV, which can be
  exact). A VFR video drifts.
- **Content that is not onset-shaped.** Continuous music against continuous motion has no onsets to compare; the tool returns
  few or none, and silence in either track is reported as no pass, not as sync.
- **Measurements from downscaled luminance.** A flash that is local (a small icon) moves the mean luminance little and may fall
  below the 10%-of-range floor; use a cropped frame-diff track in that case.
- **The video path is untested.** `--video` (ffmpeg/ffprobe decode) was written without ffmpeg on the machine it was built on and has never run; the WAV + track path is what the fixtures exercise.
- **The tuned constants.** The 8-sigma threshold, window sizes, 300 ms pairing window and 10% floor were set against synthetic
  fixtures only (`sync-fixture.mjs`: tone-burst hits against 4-frame flashes, known offset). They have not been tuned on a real capture.
