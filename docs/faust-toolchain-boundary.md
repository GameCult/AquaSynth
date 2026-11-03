# Faust Toolchain Boundary

AquaSynth owns the path from patch intent to loadable DSP product. Aquarium
Engine owns audio hosting. That split keeps the compiler brain near the patch
model and keeps the engine from becoming a synth compiler with a game loop
stapled to it.

Package ownership follows that boundary:

- `AquaSynth.Core` owns patch meaning: model records, `.aqua` parsing, analysis,
  presets, and Faust source emission.
- `AquaSynth.Faust` depends on Core and owns toolchain/rendering: Faust CLI
  validation, target-code generation, live patch compilation, native `libfaust`
  loading, compile manifests, factory lifetime, and offline/sample rendering.

## Pipeline Map

1. AquaSynth parses `.aqua` script into an explicit `SynthPatch`.
2. AquaSynth emits stable, inspectable Faust source.
3. AquaSynth resolves a pinned Faust toolchain or caller-provided Faust path.
4. AquaSynth compiles the patch into a target artifact.
5. AquaSynth emits a manifest for parameters, buses, MIDI behavior, sample-rate
   assumptions, cache keys, diagnostics, and artifact provenance.
6. Aquarium loads the artifact, binds parameters, schedules notes and buffers,
   and calls the DSP from its realtime path.

Aquarium may request compilation, cache lookup, reload, or diagnostics. It
should not need to know how AquaSynth chooses Faust flags, target languages,
architecture files, generated source layout, or artifact identity.

## Ownership

AquaSynth owns:

- patch graph contracts
- Faust source emission
- Faust toolchain selection and version pinning
- compile cache keys
- generated source/native artifact layout
- parameter and bus manifests
- diagnostics and compile provenance
- dynamic patch compilation outside the realtime callback
- native Faust factory lifetime and offline/sample-buffer rendering for hosts
- live patch compiler sessions requested by engine hosts

Aquarium owns:

- device I/O
- buffer scheduling
- audio-thread rules
- note/event delivery
- plugin or engine integration
- artifact loading policy for its platform
- presentation and editor UX

## Runtime Rule

Compilation is never an audio-thread operation. Dynamic patches compile in a
background worker or build step, then Aquarium swaps in a finished DSP artifact
through a realtime-safe handoff.

Parameter changes are not recompiles. Recompile only when graph shape changes;
ordinary patch controls move through the compiled DSP parameter API.

## Target Lanes

- Authoring/tests may use Faust-generated C# because it is easy to render,
  inspect, and compare inside the .NET test suite.
- Browser-hosted tools should use Faust-generated WebAssembly plus Faust JSON UI
  metadata. AquaSynth owns those artifacts; the web app owns WebAudio loading,
  scheduling, user gesture unlocking, and parameter binding.
- Shipping builds should prefer Faust-generated native targets when the platform
  permits it.
- A bundled Faust compiler belongs in an AquaSynth toolchain package or
  installer lane, not as an accidental dependency of the core DSL package.
- Consumer runtimes should normally bundle compiled DSP artifacts plus manifests,
  not the compiler itself, unless live user patch authoring is a product feature.

Current API foothold: `AquaSynthPatchCompiler` is the host-facing live compiler
surface. It owns lazy `libfaust` loading through AquaSynth, compiles `.aqua`
scripts into native Faust factories, writes optional `.dsp` artifacts, and
returns `AquaSynthCompiledPatch` products with `AquaSynthNativeManifest`.
`AquaSynthRenderSession` is only a render-proof convenience wrapper for tests
and offline one-shot buffers.

Library lookup is platform-aware: `libfaust.so` on Linux, `libfaust.dylib` on
macOS, `libfaust.dll` (then `faust.dll`) on Windows, always at `<home>/lib` with
`<home>/share/faust` beside it. `AquaSynthNativeOptions.FaustLibraryPath` or
`AQUASYNTH_FAUST_LIBRARY` names the library file directly and overrides the home
search; `FaustHome` then supplies `share/faust` if the prefix is not the file's
grandparent. The loader prepends `lib` and `bin` to `PATH` using the platform
path separator.

## Linux Native Toolchain

Built and run on Debian 13 (2026-11-03). Faust `aa30fd7` (2.90.20), LLVM 19
(`LLVM_CONFIG=/usr/lib/llvm-19/bin/llvm-config`, `INCLUDE_DYNAMIC=ON`,
`backends/most.cmake`), installed to a prefix; `make -j4`. The first link
failed with `cannot find -lPolly -lPollyISL -lzstd`; the install prefix that
resulted has `lib/libfaust.so` but `share/faust` holds only the architecture
files, not `stdfaust.lib` and the other `.lib` files (they live in the separate
faustlibraries repo). Compiling any patch that imports `stdfaust.lib` then
fails with `unable to open file stdfaust.lib`. Point `AQUASYNTH_FAUST_HOME` at a
home whose `lib/` has `libfaust.so` and whose `share/faust` has both.

`tools/FaustRender` renders one `.dsp` with an `rpm` control to WAVs plus
`manifest.json` (hashes, peak, rms, non-finite flag). It measures; it does not
listen. `docs/renders/thruster-v5-manifest.json` is its output for
`patches/thruster/thruster.dsp` at PR #3 head `c6561da`.

## Invariants

- AquaSynth remains the authority for what a patch means.
- Faust remains the pure DSP target, not the host runtime.
- Aquarium remains the realtime host, not the compiler.
- Generated artifacts are reproducible from script, AquaSynth version, Faust
  version, target, compile options, and architecture inputs.
- Licenses and provenance travel with toolchains, reference inputs, generated
  artifacts, and packaged output.
