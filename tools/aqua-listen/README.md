# aqua-listen

A listening-test kit: a folder of sound clips and a manifest become **one HTML file** you can send to a listener. They open it, listen, say keep / fix / drop for each sound, and send back your answers.

**Status:** the page was driven in headless Chromium, desktop and a mobile-emulated context (touch, 412 px viewport, Android Chrome user agent; `MOBILE=1 node smoke.mjs`): consent gate, autosave across reload, the export panel with its copy button and read-only textarea, zero network requests. It has not run on a real Android device and `node test.mjs` covers validation, ordering and the export shape. Nobody has listened with it yet, and the headless browser played nothing audibly; real playback on a phone or other browser is untried.

## For the listener (no technical knowledge needed)

1. Open the `listen.html` file you were sent by double-clicking it. Any recent browser works.
2. Read the short note and tick the box. You can type a nickname or leave it blank.
3. Pick **headphones** or **speakers** at the top, whichever you are using. Change it if you switch.
4. For each sound: press play (as often as you like), read what it is *meant* to feel like, then choose
   - **keep** (good as it is), **fix** (nearly, but something is wrong), or **drop** (not worth having),
   - how well it **landed**, from 1 (not at all) to 5 (completely),
   - and one line on why.
5. Some sounds come as a pair, **A** and **B**. Rate both, then say which you prefer, or that there is no difference.
6. The sounds are in a random order and their names are hidden on purpose. Please don't hunt for them.
7. Your answers save themselves in your browser as you go. You can close the tab and come back to the same file later.
8. When done, press **Export my answers**. Your answers appear in a text box. Press **Copy answers (JSON)** and paste them into a message to whoever gave you the page (or press **Share**, or **Select all** and use your phone's Copy). **Download verdict.json** and **Download verdict.md** are separate buttons, one file per tap, for browsers that allow downloads. Nothing leaves your computer except what you copy, share or download.

Privacy: the page has no network access at all (the browser is told to refuse it), no account, no tracking. "Erase my answers" at the bottom wipes what the page saved.

## For the person building a kit

```
node build.mjs --manifest kit.json --clips ./clips --out ./dist     # writes dist/listen.html
node test.mjs                                                        # 17 checks, no dependencies, no browser
node smoke.mjs dist/listen.html                                      # optional browser smoke, see below
```

`kit.json`:

```json
{ "kit": { "id": "my-kit-1", "title": "Thruster sweep", "note": "optional line shown under the title" },
  "clips": [
    { "id": "t1", "file": "thr-a.wav", "title": "Thruster, low", "feel": "heavy, steady, not hissy", "group": "thruster", "pair": "t2" },
    { "id": "t2", "file": "thr-b.wav", "title": "Thruster, low, tonal", "feel": "heavy, steady, not hissy", "group": "thruster", "pair": "t1" }
] }
```

- `id`, `file` (wav, mp3, ogg or flac, relative to `--clips`), `title`, `feel` (intended feel; shown to the listener), `group`: required. `pair`: optional, must name another clip whose `pair` names this one. Anything wrong stops the build with the full list of problems.
- Title, id and group are hidden from the listener and appear only in the export. The intended feel is shown, since "did it land" needs it.
- Order is shuffled from a random seed kept with the listener's answers (it is in the export). A pair is shuffled as one unit, with A/B position shuffled inside it.
- Audio is embedded as-is (base64), so the page is about 1.4x the clip bytes. The build warns over 100 MB.
- Autosave key is the kit id plus the sorted clip ids (`aqua-listen.v2:<kit id>:<ids>`), not the content hash: a rebuilt page (edited note, re-rendered clip) keeps the listener's answers, and a kit with a different clip-id set starts fresh while the old entry stays in the browser. Saved state is the shuffle seed, answers by clip id and pair choices by `idA|idB`; a pair's A/B/same value is positional (A is the first of the pair in the seed's order), so it holds only while the manifest's clip order is unchanged. When the new key is empty, the page adopts the fullest earlier `aqua-listen:<kit id>:<hash>` save whose answers name only this kit's clips; a save already under the new key is never overwritten. "Erase my answers" removes the new key and any adoptable hash-keyed save.

### Export

`verdict.json` (`schema: "aqua.listen.verdict.v1"`): `kit`, `exportedAt`, `seed`, `consent`, `listener`, `device`, `notes`, `summary` (counts), `clips[]` in the order heard (`position, id, title, group, feel, file, pair, slot, verdict, reason, landed, device`), `pairs[]` (`a, b, preferred`: a clip id or `no-difference`). `verdict.md` is the same as a table. Unanswered fields are `null`.

The blank verdict files this was meant to match were not found under `/opt/commons/handoff/`, so this shape is the kit's own. If a house format exists, the change is in `buildVerdict` / `verdictMarkdown` in `logic.cjs`.

### Browser smoke

`PLAYWRIGHT_CORE=/path/to/playwright-core CHROME=/path/to/chrome node smoke.mjs dist/listen.html` ticks through consent, answers a clip, reloads, exports, then serves the page over a local http server, rebuilds it with a different content hash and checks that answers survive, that a hash-keyed save is adopted, and that erase leaves nothing to resurrect; it fails on any network request or page error. It is not in `node test.mjs` because the repo carries no browser dependency.

### Kits in the repo

`kits/tract-weak-spots` and `kits/tract-stops` hold a `kit.json`, the `phones/*.json` that WordRender reads, and a `render.sh` that renders the clips (`render.sh <repo-root> <out-root>`; needs CULTLIB_ROOT and AQUASYNTH_FAUST_HOME, see the script header). Build a page from the rendered clips:
`node tools/aqua-listen/build.mjs --manifest tools/aqua-listen/kits/tract-stops/kit.json --clips <out-root>/clips --out <out-root>/dist`. The titles in those kit.json files carry measured figures, a prediction and a falsifier; the page hides them from the listener.

### Pictures and burst notes (for a reader who cannot listen)

`clip_images.py <clips> <images> [--kit kit.json --renders <out-root>]` writes `<stem>.spec.png` (spectrogram, Hann 512 / hop 128, 0-8 kHz, -90..0 dB re the clip peak) and `<stem>.wave.png` (fixed +-0.5) for every wav, byte-identical between runs, and with `--kit --renders` also `burst-notes.md` and `burst-notes.json` next to the images: per plosive closure start, release (start of the with-minus-without `--no-burst` difference above 1/20 of its peak), burst end and the window to look in. Both kits' `render.sh` end with it. Requirement: `python3` with numpy and matplotlib (Agg), set `PYTHON=` to one that has them; no scipy, nothing for the dotnet build. Test: `python3 -I test_clip_images.py`.

`build.mjs --images <images>` embeds a clip's two pictures in a collapsed "Pictures of this sound" block (matched by the wav's stem) and lets the page's policy admit `data:` images, only then. The burst notes are never read by the build and never enter `listen.html` or `kit.json`: give the page to the listener and the notes to whoever reads the pictures.

## Files

`clip_images.py` + `test_clip_images.py` pictures and burst notes · `kits/` · `logic.cjs` validation, ordering, export shape (shared by build, page, tests) · `build.mjs` assembles the page · `web/` page template, glue and styles · `test.mjs` · `smoke.mjs`
