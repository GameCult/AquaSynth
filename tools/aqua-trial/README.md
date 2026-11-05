# AquaSynth listening trial

Blinded, randomized A/B ratings of a synthesized thruster, to test Deru's "dead radio" finding: that the
thruster's noise layers (rasp, rattle) are what makes it sound dead, and that a tone-forward mix lands better.

**Status: the page has never been run in a browser, and nothing has been heard.** Node covers the logic and
the patch through the same faustwasm runtime the page loads (`node test.mjs`, 15 checks). The DOM glue
(`web/trial.js`), AudioContext playback and the download are untested. Follow-up: browser-verify.

## Entry points

- `web/`: the page. Consent checkbox gates Start; the page compiles `thruster-trial.dsp` in-browser, renders four
  4 s variants at the browser's sample rate, and runs six comparisons (every pair once, order and A/B position
  randomized from a random seed). Per sound: rating 1-5 ("does it land?") and one word. Variant identity is never shown.
- `thruster-trial.dsp`: `patches/thruster/thruster.dsp` plus a `noise` control (percent). `noise` scales the rasp and
  rattle layers only; the rumble (tone) is untouched, so lowering it moves the mix tone-forward. That reading of
  "tone-forward mix" is this author's; at 100 the signal equals the thruster (asserted sample for sample).
  Variants: rpm 600, noise 100 / 50 / 20 / 0. Not loudness-matched, so level and noise are confounded by design.
- `logic.js`: design, statistics, validation and export shape (shared by page and tests).
- `analyze.mjs`: dependency-free. `node analyze.mjs export1.json [export2.json ...]` prints per-variant n, mean,
  sd, min/max, peak, rms, words, and whether peak / rms predicted the ratings (Spearman rho, permutation p;
  "predicts" means |rho| >= 0.5 and p < 0.05, fixed before any data).
- `build.mjs`: assembles `dist/` and prints the content hash used in the publish path. Needs
  `(cd ../aqua-playground && npm ci)` for the faustwasm runtime. `test.mjs` needs `dist/`.

## Privacy

Answers live in the page's memory only. The page makes no network request after loading its own assets and
writes nothing to storage. **Export** downloads a JSON file (patch, params, peak, rms, rating, word, time,
user agent, sample rate); sending it is the listener's act.

## Measured finding before any listening

Peak and rms hardly move across the sweep (48 kHz, current mix: rms 0.0725 / 0.0724 / 0.0723 / 0.0723; peak 0.160 / 0.151 /
0.148 / 0.147). **The sweep got narrower on 2026-11-05:** `patches/thruster/thruster.dsp` (and this file, which must equal it
at noise 100) was changed to cut the idle-level rasp and rattle floors and open the rumble low-pass at low load, after
`tools/aqua-feel` measured the A-weighted noise-to-tone ratio at rpm 300/600 as +21/+23 dB. At rpm 600 the noise layers are now
already small, so noise 100 vs 0 differs far less than it did in the original mix (rms 0.0734 / 0.0712 / 0.0706 / 0.0705, peak
0.189 / 0.161 / 0.148 / 0.140). A trial of Deru's "dead radio" claim against the old mix needs that mix restored as a variant;
open as `aqua-body:follow_up:trial-add-v5-variant`. Nothing here was heard, before or after the change.
