# Earcon Workshop

Static, dependency-free browser page that generates a coherent family of short earcons (one per event, one timbre
identity) and exports them as WAVs plus a JSON pack. **Nothing here has been heard by anyone.** The automated checks
measure signals; no person has judged whether any earcon sounds good, is pleasant, is distinguishable in a real room, or
suits a real product. A human ear is owed (recorded as a follow-up on the forge PR).

## What is live

- `web/earcon.js`: the whole engine. Events (success, fail, warning, message, done, waiting), four identities taken from the
  Dings catalog (warm bell, glass chime, soft triangle, muted pluck), five sliders (tempo, transpose, brightness, decay,
  level), WAV encoder, pack builder, stored-ZIP writer. Pure functions, deterministic, no randomness, no noise voices.
- `web/index.html` + `app.js`: audition one earcon or the whole family, tweak sliders, download one WAV, `pack.json`, or a zip
  of WAVs + `pack.json` + `README.md`. Playback is source -> gain -> analyser -> destination with a peak meter, the same graph shape as `tools/aqua-playground`.
  The page plays the exact buffers it exports (offline render, not a second path).
- `cli.mjs`: the same export from a shell, e.g. `node cli.mjs warm-bell ./earcons tempo=1.2 level=0.6`.
- `test.mjs`: `node test.mjs` (about 10 s).

Not the playground's Faust/AudioWorklet engine: that pulls the Faust compiler (wasm) into the page, which is not dependency-free.
The synthesis here is plain JS (FM bell and additive triangle voices, two-pole lowpass); it is a reimplementation of the Dings
instruments' ideas, not a lowering of their `.aqua` scripts, so it will not sound like the shipped Dings.

## Run

Serve `web/` from any static host (ES modules need http, not `file://`):

```sh
cd tools/earcon-workshop/web && python3 -m http.server 8080   # then open http://localhost:8080/
```

Never opened on a real display by a person. A headless Chromium smoke (load, audition, zip download, no console errors) passed once; no audio device was attached.

## What the check covers (`test.mjs`)

For each identity x event at the default setting and at two slider extremes (882 checks at the time of writing):

- finite, non-silent, onset (first sample at or above -40 dBFS) under 20 ms, sample peak < 1.0 and <= 0.9, true peak < 1.0 (cross-checked
  with `tools/aqua-feel/metrics.js`), no over-full-scale samples or flat tops, ends without a step, under 2.6 s, WAV round trip.
- Sibling distinctness: every pair of events within one identity, and every pair of identities on the same event, must differ by a
  log-spectrogram distance of at least 8 dB (mean absolute dB over 32 log bands, 80 Hz to 16 kHz, 2048-point frames, cells where both sides sit
  under -75 dB ignored). Controls: a clip against itself is 0, against silence is far, a 2% tempo change falls under the threshold.
- Determinism, sliders actually change the output, event selection honoured, ZIP entries and CRCs read back, pack lists its files and says `heard: false`.

Limits worth knowing: distance is a spectrogram number, not a perceptual one; two earcons can pass it and still confuse a listener.
The metric and threshold were adjusted after the first run (see the history comment in `test.mjs`): the first averaging let silence
dilute the distance, motifs for warning, message, waiting and fail were reworked, and the threshold went from 4 to 8 dB after the
minimum came out at 13.8 dB. Brightness is limited to +-0.25 because a darker voice made the pluck family hard to tell apart under the first metric. True-peak overs in the first run were
FM aliasing at the bright/high extreme; fixed with a 9 kHz two-pole lowpass.

## Using an exported pack

The zip's own `README.md` carries the CI, chat-bot and app recipes (play on pass/fail in a shell, attach the file to a chat message, `new Audio(url).play()`). Summary:

- **CI:** play `success`/`fail`/`done` from the runner or a dev box; never fail the build when no audio device exists.
- **Chat bots:** look up `pack.json -> events[id].file` and upload that WAV with the message.
- **Apps:** one preloaded audio element per event; browsers need a user gesture before first play.
- Every file peaks at or under 0.9 of full scale at the default level (0.7); the `level` slider tops out at 0.9.

## Planned, not built

- Ogg/Opus export (see `docs/research/dings-playback-and-ogg-export.md`); WAV only today.
- Custom motifs and a custom voice editor; only the six events and four identities exist.
- Lowering the voices from the real `.aqua` Dings scripts so the pack matches the Dings service.
- Any listening: a human trial, then tuning motifs and levels by ear.
