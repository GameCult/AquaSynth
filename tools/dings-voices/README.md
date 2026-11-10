# Dings voices (Ratatoskr sender voices) - generator

Generates eight sender voices (aqua, eureka, heimdall, kiko, libby, nibu, void, weksa) times four notifications (mention, dm, message, invite) as Ogg Vorbis
48 kHz mono, a `manifest.json` in the shape Ratatoskr's `dings.ts` reads (`{neutral, senders:{localpart:{motif:path}}}`), measurements, and a static listening page.
**Nobody has heard any output of this generator.** The metrics describe the files; they are not verdicts.

- `src/voices.mjs` - the voices and the shared rhythm family (event = rhythm, sender = timbre). Pure JS, deterministic; uses only the sample rate from `../earcon-workshop/web/earcon.js`.
- `src/build.mjs <rawDir>` - renders the 32 clips at unit scale. `src/finish.py <rawDir> <outDir>` - equal A-weighted exposure, Ogg encode, decode-back measurement, manifest, `metrics.json` (needs numpy and soundfile with Vorbis). `src/metriclib.py`, `src/measure.py` - measurements (true peak, A-weighted exposure, onset, end-of-clip checks).
- `src/gen_page.py <publishDir>` - writes `index.html` (no autoplay) from `metrics.json`, `findings.json`, `voices.json`. `src/verify.py [dir]` - checks the files against the limits and manifest schema. `src/findings.py <v2senders> <v3out>` - the v2-vs-v3 numbers. `src/polyphony.test.mjs` - a clip is the sum of per-note renders and notes do not depend on each other.
- v3: `renderNote` renders each note in its own buffer with its own noise stream; Libby/Kiko notes that something follows end by the next onset and the last rings; Nibu has three no-glide proposals (`NIBU_PROPOSALS`, encoded under `out/proposals/`, not in the manifest); Kiko is struck glass; Void's tick is a soft knock.
- Not here: the rendered clips and the listening page. A staged copy, with the README of its measurements, is at `/opt/commons/handoff/aqua-dings-voices-20261110` on Yggdrasil; the neutral fallback files come from `/opt/commons/handoff/aqua-dings-ratatoskr-b3eb9d01/out/neutral`.

Run: `node src/polyphony.test.mjs && node src/build.mjs raw && python src/finish.py raw out && python src/verify.py .` (`finish.py` expects `out/neutral/*.ogg` to exist for the neutral comparison).
