# Dings voices (Ratatoskr sender voices) - generator

Generates eight sender voices (aqua, eureka, heimdall, kiko, libby, nibu, void, weksa) times four notifications (mention, dm, message, invite) as Ogg Vorbis
48 kHz mono, a `manifest.json` in the shape Ratatoskr's `dings.ts` reads (`{neutral, senders:{localpart:{motif:path}}}`), measurements, and a static listening page.
**Nobody has heard any output of this generator.** The metrics describe the files; they are not verdicts.

- `src/voices.mjs` - the voices and the shared rhythm family (event = rhythm, sender = timbre). Pure JS, deterministic; uses only the sample rate from `../earcon-workshop/web/earcon.js`.
- `src/build.mjs <rawDir>` - renders the 32 clips at unit scale. `src/finish.py <rawDir> <outDir>` - equal A-weighted exposure, Ogg encode, decode-back measurement, manifest, `metrics.json` (needs numpy and soundfile with Vorbis). `src/metriclib.py`, `src/measure.py` - measurements (true peak, A-weighted exposure, onset, end-of-clip checks).
- `src/gen_page.py <publishDir>` - writes `index.html` (no autoplay) from `metrics.json`, `voices.json` and the `inspect_clips.py` output under `findings/`. `src/verify.py [dir]` - checks the files against the limits and manifest schema. `src/inspect_clips.py <rawDir> <outDir> <tag>` - spectrogram + 1 ms envelope PNGs and onset lists for every Void clip, multi-note checks for Nibu. `src/polyphony.test.mjs` - a clip is the sum of per-note renders and notes do not depend on each other.
- v3: `renderNote` renders each note in its own buffer with its own noise stream; Libby/Kiko notes that something follows end by the next onset and the last rings; Nibu has three no-glide proposals (`NIBU_PROPOSALS`, encoded under `out/proposals/`); Kiko is struck glass; Void's tick is a soft knock.
- v4: Void has no tick (it was a second hit 95 ms into every note) and a note something follows ends by the next onset; `polyphony.test.mjs` fails if a Void clip has more onsets than notes. Nibu's voice is proposal 3 (the 8 kHz 5-bit sine) for all four events; proposals 1 and 2 stay as alternates under `out/proposals/`, not in the manifest.
- Not here: the rendered clips and the listening page. A staged copy, with the README of its measurements, is at `/opt/commons/handoff/aqua-dings-voices-20261110` on Yggdrasil; the neutral fallback files come from `/opt/commons/handoff/aqua-dings-ratatoskr-b3eb9d01/out/neutral`.

Run: `node src/polyphony.test.mjs && node src/build.mjs raw && python src/finish.py raw out && python src/verify.py .` (`finish.py` expects `out/neutral/*.ogg` to exist for the neutral comparison).

## Publishing (one place)

One dev, one URL: the listening page is **https://minds.gamecult.org/aqua/dings/** (`/srv/minds-web/aqua/dings/`), always the latest version, overwritten in place. Do not publish per-hash pages any more. After building a publish dir (the `Run` line, then `gen_page.py`), run

```
python3 tools/publish-stable/publish_stable.py dings <publishDir>    # idempotent; replaces the stable dir by renaming a finished staging copy
python3 tools/publish-stable/publish_stable.py banners               # old per-version pages get a pointer banner; deletes nothing
```

The script stamps a banner and a `VERSION` file (publish dir name and AquaSynth revision) into the stable copy. The aqua-ear examples are published the same way to `/srv/minds-web/aqua/ear/` (`publish_stable.py ear <dir of runs>`). Tests: `python3 tools/publish-stable/test_publish_stable.py`. Nobody has opened these pages in a browser; every link and audio file on them was checked with curl over https.
