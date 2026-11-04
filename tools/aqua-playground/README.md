# AquaSynth Playground

Static, serverless audition page for AquaSynth patches. Pick a patch, read the Faust it lowers to,
move the sliders the DSP itself declares, press Play.

**What is live:** 42 patches (`catalog.json`): the hand-written Faust `thruster` (with its `rpm`
control) and 41 of the 43 `.aqua` files, lowered by `tools/AquaLower` through `FaustEmitter.EmitScript`
(the lowering the daemon uses). Faust is compiled to WebAssembly in the page
(`@grame/faustwasm` 0.20.0, libfaust 2.90), played through an AudioWorklet, and the lowered source
is shown in an editable box with a Recompile button.

**What it is not:** `.aqua` is lowered ahead of time (`lowered/`), not in the page; editing Faust works,
editing `.aqua` does not. `advanced/additive-pad` and `advanced/electro-drop-demo` lower to ~1.5 MB of
Faust each and are left out. One-shot patches sound once per Play press and mostly declare no
controls, so their slider panel is empty.

## Entry points

- `lower.sh`: regenerate `lowered/*.dsp` and `catalog.json` (needs .NET SDK and `CULTLIB_ROOT`).
- `node build.mjs [outDir]`: assemble the site in `dist/` (run `npm install` first).
- `node test.mjs [dist]`: headless check of `dist/`: each patch compiles to wasm, renders 2 s
  offline, is non-silent and finite; the thruster is also run at rpm 600 and 4800.
  It cannot hear and does not run an AudioWorklet or a browser.
- `web/`: the page (`index.html`, `app.js`, `ui.js`, `styles.css`).

Serve `dist/` from any static host (`python3 -m http.server`); AudioWorklet needs `localhost` or https.
The older `tools/vocal-tract-playground` renders server-side through the .NET renderer; this one
does not need a server.
