# aqua-listen

A listening-test kit: a folder of sound clips and a manifest become **one HTML file** you can send to a listener. They open it, listen, say keep / fix / drop for each sound, and send back two small files.

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
8. When done, press **Export my answers**. Your answers appear in a text box. Press **Copy answers (JSON)** and paste them into a message to whoever gave you the page (or press **Share**, or **Select all** and use your phone's Copy). **Download verdict.json** and **Download verdict.md** are separate buttons, one file per tap, for browsers that allow downloads. Copying or sharing is the only way anything leaves your computer.

Privacy: the page has no network access at all (the browser is told to refuse it), no account, no tracking. "Erase my answers" at the bottom wipes what the page saved.

## For the person building a kit

```
node build.mjs --manifest kit.json --clips ./clips --out ./dist     # writes dist/listen.html
node test.mjs                                                        # 16 checks, no dependencies, no browser
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
- Autosave key is the kit id plus a hash of the manifest and clip bytes: a rebuilt kit with changed clips starts fresh instead of mixing answers.

### Export

`verdict.json` (`schema: "aqua.listen.verdict.v1"`): `kit`, `exportedAt`, `seed`, `consent`, `listener`, `device`, `notes`, `summary` (counts), `clips[]` in the order heard (`position, id, title, group, feel, file, pair, slot, verdict, reason, landed, device`), `pairs[]` (`a, b, preferred`: a clip id or `no-difference`). `verdict.md` is the same as a table. Unanswered fields are `null`.

The blank verdict files this was meant to match were not found under `/opt/commons/handoff/`, so this shape is the kit's own. If a house format exists, the change is in `buildVerdict` / `verdictMarkdown` in `logic.cjs`.

### Browser smoke

`PLAYWRIGHT_CORE=/path/to/playwright-core CHROME=/path/to/chrome node smoke.mjs dist/listen.html` ticks through consent, answers a clip, reloads, exports, and fails on any network request or page error. It is not in `node test.mjs` because the repo carries no browser dependency.

## Files

`logic.cjs` validation, ordering, export shape (shared by build, page, tests) · `build.mjs` assembles the page · `web/` page template, glue and styles · `test.mjs` · `smoke.mjs`
