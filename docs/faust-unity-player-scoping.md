# Faust DSP in a built Unity player (Windows + Linux): scoping

Scoping only. No code changed. Follow-up `aqua-body:follow_up:chat-s3500-3500`, 2026-11-06.

Question: how does Faust DSP (the thruster, `patches/thruster/thruster.dsp`, v5) reach a
built Unity player on Windows and Linux? (a) ahead-of-time (AOT) Faust to C++ per patch
inside a native plugin, or (b) runtime `libfaust`/LLVM inside the player.

**Limitation:** AOT Faust-to-C++ fixes the patch set at build time. Player-authored or modded patches cannot run in a built player; live-edited patches run only in the editor (the developer-time lane of path (b)).

Marks: **[V]** verified here by reading source or running a tool on 2026-11-06.
**[U]** unverified: from memory, or needs a Unity editor/player or a Windows host this run
did not have. Nothing here has been run inside Unity.

## Recommendation

Take (a) for shipped patches such as the thruster. Keep (b) as a developer-time lane only, if
at all. Confidence: about 80% on the choice, lower on the details marked [U].

The cost of (a): the patch set is fixed at build time. A built player cannot run
player-authored or modded patches; live-edited patches run only in the editor, through (b).

- (a) fits the thruster: a fixed graph with ordinary controls, which
  `docs/faust-toolchain-boundary.md` ("Parameter changes are not recompiles"; "Shipping
  builds should prefer Faust-generated native targets") already says is the shipping shape.
- (b) buys live recompilation of `.aqua` text in the player. Nothing asks for that yet. It
  costs a ~130 MB LLVM dependency [V: `libLLVM.so.19.1` is 129,673,080 bytes on this host],
  the Faust library tree at runtime, and a JIT in the player.
- Faust already ships a Unity target that is path (a) with the bridge written
  (`architecture/unity/`, `faust2unity`, section "What Faust ships"). Start from it and do
  not hand-roll a plugin.

## What Faust ships for Unity [V]

Checked in the 2.90.20 source tree this host built (`faust-llvm-build-20261031/src/architecture/unity/`).

| File | Role |
|---|---|
| `unity.cpp` (253 lines) | Unity **native audio SDK** effect plugin: `UnityGetAudioEffectDefinitions`, `ProcessCallback`, `SetFloatParameterCallback`. |
| `unityplugin.cpp` (307 lines) | Plain C exports (`Faust_setParameterValue` etc.) for P/Invoke. |
| `FaustPlugin_template.cs` | `MonoBehaviour` on an `AudioSource`; `OnAudioFilterRead` calls `ctx.process(...)`. |
| `FaustUtilities_template.cs`, `FaustPolyUtilities_template.cs` | `DllImport` glue, inspector UI, polyphonic variant. |

So Faust provides both bridge shapes the question names. The `FaustPlugin_template.cs`
path is the **OnAudioFilterRead bridge**: Unity calls `OnAudioFilterRead(float[] buffer, int
numchannels)` on its audio thread and C# P/Invokes the native `process`. `unity.cpp` is the
**native audio SDK** shape: Unity's mixer calls `ProcessCallback` directly with no managed
frame in the audio path.

`faust2unity` builds the native libraries and C# scripts and packs a `.unitypackage`;
platform scripts are `faust2winunity` (MinGW cross-compile: `-w32`/`-w64`) and
`faust2linuxunity` (`g++`, `-fPIC -shared`, `.so`) [V: scripts and README read; not run].

## Path (a): AOT Faust to C++ in a native plugin

### Per-platform outputs

| Platform | Output | Toolchain | Status |
|---|---|---|---|
| Windows x64 | `FaustPlugin_<name>.dll` | `faust2winunity -64` uses MinGW (`x86_64-w64-mingw32-gcc`) [V: script]. An MSVC build of the generated C++ is plausible but not in Faust's scripts. | [U] runs in a Unity Windows player |
| Linux x86_64 | `libFaustPlugin_<name>.so` | `g++ -fPIC -shared` (`faust2linuxunity`) [V: script] | [U] runs in a Unity Linux player |
| Inputs | `thruster.dsp` + `stdfaust.lib` tree | `faust` CLI with `-I` at faustlibraries | [V] |

Generating the thruster as C++ works [V]: `faust -I faustlibraries-20261031 -a minimal.cpp
-single -ftz 2 -vec -lv 1 thruster.dsp` gave 1,105 lines (40,045 bytes) with Faust 2.90.20.
Those are the same flags `AquaSynthNativeCompiler.CompileSource` passes to the JIT
(`src/AquaSynth.Faust/NativeFaustRuntime.cs:530`). The AOT lane should pick its flags
deliberately, not inherit them: `-vec` is a choice that the measurement plan below should
test against scalar output.

Windows and Linux builds need two different hosts or a cross-compiler. Starfire (Windows)
has Faust and a compiler only if someone installs them there [U].

### What is reusable from AquaSynth

| Part | Reusable for (a)? | Evidence |
|---|---|---|
| `patches/thruster/thruster.dsp` | Yes, as is. It is hand-written Faust, "not a `.aqua` script" (file header). | [V] |
| `FaustExport` / `FaustEmitter` (`src/AquaSynth.Core/FaustExport.cs`, 2,558 lines) | Only for `.aqua` patches. The thruster does not go through it. For an `.aqua` patch it yields the `.dsp` source to feed `faust -lang cpp`; `DspSourceDirectory` already writes it (`NativeFaustRuntime.cs:628`). | [V] |
| `FaustCompiler.CompileAsync` (`src/AquaSynth.Faust/FaustCompiler.cs:56`) | Yes. Runs the `faust` CLI with `-lang Cpp`; `FaustTargetLanguage.Cpp` exists. This is the AOT generator step. | [V] |
| `NativeFaustRuntime.cs` (1,132 lines: `AquaSynthPatchCompiler`, `AquaSynthStreamingPatch`, `FaustNativeToolchain`) | **Not in the shipped player.** It is the libfaust loader and the managed host. Under (a) it stays on the build/authoring side: it renders reference audio and the manifest that the plugin's output is checked against. | [V] |
| `tools/FaustRender` + `docs/renders/thruster-v5-manifest.json` | Yes, as the golden reference: hashes, peak, RMS, `nonFinite` per rpm case, 48 kHz / 512-frame blocks. The native plugin should be diffed against this. The manifest hashes predate the 2026-11-05 edit to `thruster.dsp` (`docs/faust-toolchain-boundary.md`, "Linux Native Toolchain"), so re-render first. | [V] |
| `AquaSynthNativeManifest` (id, compile key, sample rate, I/O counts, Faust version) | Yes as the artifact provenance record. It has **no parameter table** (no min, max, default). That is a gap for crossing params (below). | [V] |

Missing for (a), not in AquaSynth today: a build step that runs `faust2unity`-style
generation per patch and records the result as artifacts. That should be a script in
`tools/`, not a new library. Per the boundary doc, AquaSynth owns "generated
source/native artifact layout" and "parameter and bus manifests", and Aquarium/Unity owns
"artifact loading policy for its platform".

### Hot-path hazards in the managed bridge (if OnAudioFilterRead + managed host)

If the player used `AquaSynthStreamingPatch` directly (managed host, native `libfaust`
instance), these are in the audio callback [V: `NativeFaustRuntime.cs`]:

- `ProcessBlock(float[][] ...)` (line 251) allocates `IntPtr[]` arrays and pins six
  `GCHandle`s per call. Use the pointer overload (line 235) with preallocated pinned buffers
  (what `tools/FaustRender/Program.cs:37-40` already does).
- `SetControls` does a string dictionary lookup per control per block and a `Marshal.WriteInt32`
  (lines 841-869). It also throws `ArgumentException` for an unknown path (line 865): an
  exception in an audio callback. Resolve zone pointers once, off-thread.
- The `FaustNativeToolchain` delegates are `Marshal.GetDelegateForFunctionPointer` thunks
  (line 886), not function pointers; fine for control rate, avoidable per block.

Under the AOT plugin, none of this exists: the cost moves to the plugin's `setParamValue`
and `compute`.

### Native audio SDK vs OnAudioFilterRead

| | Native audio SDK (`unity.cpp`) | `OnAudioFilterRead` bridge (`FaustPlugin_template.cs`) |
|---|---|---|
| Where it runs | Mixer effect, native audio thread, no managed code | Managed callback on the audio thread, then P/Invoke |
| Source/generator | Effect slot on a mixer group; `ProcessCallback` returns `ERR_UNSUPPORTED` if `inchannels != outchannels` (`unity.cpp:179`) [V] | Component on a GameObject with `AudioSource` |
| Params | `SetFloatParameterCallback`, set from mixer exposed params or `AudioMixer.SetFloat` [U] | `setParameter(int, float)` per patch component |
| Risk | Mixer setup per patch; 15-char parameter names (below) | GC pauses or managed allocation can overrun the callback if the managed side allocates [U] |
| Fit for thruster | Better on determinism; whether a generator-only (0-input) effect behaves well in a mixer slot is [U] | Matches Faust's template; many instances are easy (one component each) |

The thruster has 0 inputs and 1 output. In both shapes a 1-channel Faust output goes to
the first interleaved channel only and zero-fills the rest (`interleavedWrite`,
`architecture/faust/dsp/dsp-tools.h:216-223`) [V]. A stereo player would hear left only
unless the patch is made to output two channels or the plugin is edited.

## Path (b): runtime `libfaust`/LLVM in the player

`AquaSynthPatchCompiler` already is this, host-side: `createCDSPFactoryFromString` via
`NativeLibrary.Load` (`NativeFaustRuntime.cs:793`), the C API delegates (727-737), instances
created and computed through `computeCDSPInstance`.

Per-platform outputs:

| Platform | Ship | Evidence |
|---|---|---|
| Linux x64 | `libfaust.so.2.90.20` (8.4 MB) + system `libLLVM.so.19.1` (129.7 MB) + `libz3.so.4`, `libffi.so.8`, `libedit.so.2`, `libzstd`, `libz` (all dynamic deps) + `share/faust/*.lib` | [V: `ldd`, `ls`, doc] |
| Windows x64 | `libfaust.dll` plus `share/faust` | [U] no Windows libfaust built or run by this run. `AquaSynthFaustToolchain.LibraryFileNames` looks for `libfaust.dll`/`faust.dll` (`NativeFaustRuntime.cs:654-657`); the doc says `C:\Program Files\Faust` is the default home. |

Costs specific to AquaSynth's own history, from `docs/faust-toolchain-boundary.md`:
the Linux link first failed (`cannot find -lPolly -lPollyISL -lzstd`), and the prefix
`share/faust` lacked `stdfaust.lib` until the faustlibraries tree was added. The library
tree has to travel with the player (`AquaSynthFaustToolchain.Find` probes
`<AppContext.BaseDirectory>/Tools/Faust`, line 666).

Beyond size:

- A JIT needs executable memory. Desktop Windows/Linux allow it; iOS and consoles do not
  [U]. A player that needs those later cannot have (b) as its only path.
- The compile step is multi-hundred-millisecond to seconds and must run on a worker thread
  with a finished-DSP handoff (boundary doc, "Runtime Rule"). `AquaSynthNativeManifest.CompileMilliseconds`
  records it [V: field exists; no number measured here].
- Output from a JIT with `-vec` at optLevel `-1` is whatever LLVM emits on that machine.
  It is not bit-identical across hosts [U], so the reproducibility invariant in the
  boundary doc ("artifacts are reproducible from script, version, target, options") weakens.

## Licensing if shipped

Read from files in the tree this run built [V] unless marked.

| Component | Licence | Ship implication |
|---|---|---|
| Faust compiler / `libfaust` 2.90.20 | LGPL-2.1-or-later (header of `COPYING.txt`: "GNU Lesser General Public License ... version 2.1") | Dynamic loading of an unmodified `libfaust.so/.dll` satisfies LGPL if the user can replace it. Ship licence text and source offer. [U] legal review. |
| Faust architecture files (`unity.cpp`) | LGPL-3.0-or-later **with exception**: you may create a larger work with the architecture section and distribute it under terms of your choice, **so long as the architecture section is not modified** | Use `unity.cpp` / `unityplugin.cpp` unmodified and the rest of the plugin has no copyleft. Editing them (e.g. to add param clamping) drops the exception for that file. Prefer clamping elsewhere (below). |
| Generated C++ from `faust` (the DSP section of the output) | Not governed by the compiler licence per Faust's licence note [U: from Faust FAQ, not checked here] | Verify before shipping. |
| `faustlibraries` (`stdfaust.lib`, used by the thruster via `os`, `fi`, `no`, `si`, `ba`, `ma`) | Per-function: `oscillators.lib`, `noises.lib`, `signals.lib`, `basics.lib`, `maths.lib` headers say LGPL (GRAME); `filters.lib` says "Each function in this library has its own license" (e.g. Julius O. Smith III) | Compiled into the generated code. Need a per-function licence inventory of the functions the thruster calls before shipping. Not done. |
| LLVM (path (b) only) | Apache-2.0 with LLVM exceptions [U: from memory, not in tree] | Permissive; ship notice. Also notices for z3 (MIT), libffi (MIT), libedit (BSD) [U]. |

Net: (a) puts only unmodified-architecture-file and faustlibraries notices in the player. (b)
adds LGPL relinking obligations for `libfaust` and a third-party LLVM stack.

## Crossing bounded parameters (rpm now; load/velocity/quality later)

Today's thruster has one control:
`rpm = hslider("rpm", 1200, 300, 6000, 1) : si.smooth(ba.tau2pole(0.2))`
(`thruster.dsp` line 6). `load` is derived from it (`load = (rpm - 300) / 5700`). The
generated C++ declares it with `addHorizontalSlider("rpm", &fHslider0, 1.2e+03f, 3e+02f, 6e+03f, 1.0f)`
[V]. Later controls (load, velocity, quality) must each be declared the same way, with a
default, min and max.

### What crosses and how

- **AOT / `OnAudioFilterRead`**: C# calls `Faust_setParameterValue(ctx, index, float)`
  (`FaustUtilities_template.cs:307`), which does `*fZone = v` with no clamp
  (`APIUI.h` `setParamValue(int, FAUSTFLOAT)` = `*fItems[p].fZone = v`) [V]. Parameters are
  addressed by integer index in declaration order. The `.cs` file prints min/max/init
  (`getParameterMin/Max`).
- **AOT / native audio SDK**: `ComputeUnityParameters` fills Unity's
  `UnityAudioParameterDefinition` with `min`, `max`, `defaultval` from the Faust UI
  (`unity.cpp:116-118`) [V]. Unity then knows the range and the default; the inspector and
  mixer use it. **Names are cut to 15 characters** (`strncpy(pdef[i].name, ..., 15)`,
  `unity.cpp:112`) [V]. `rpm` fits; Aqua-style paths such as `/curves/<name>/depth` do not.
  The AquaSynth path lengths need a unique-prefix rule or the control must be
  addressed by index.
- **Runtime `libfaust` (b)**: `AquaSynthStreamingPatch.SetControls` writes the float's
  bits to the zone pointer with `Marshal.WriteInt32` (`NativeFaustRuntime.cs:868`). No
  range or default crosses; `FaustUiGlue.AddControl` discards `init, min, max, step`
  (line 1028) [V].

Float writes are the only thing crossing. A single aligned 32-bit store is atomic on x86-64,
so no lock is needed for one scalar control [U: assumes the compiler does not tear; the
generated code reads each zone once per block, `float fSlow0 = static_cast<float>(fHslider0)`,
`compute` line 430 in the generated file] [V for the read]. A control that moves within a
block takes effect at the next block, smoothed by `si.smooth` (rpm uses a 0.2 s time
constant).

### Needed for several bounded params

1. A parameter table per patch alongside the existing `AquaSynthNativeManifest`:
   `{path, index, default, min, max}`. (b) can get it from `FaustUiGlue` if
   `AddControl` stops discarding `init/min/max`; (a) can get it from the same
   `faust -json` output or from the C++ UI callbacks at build time.
2. One clamp site (below), driven from that table, owned by AquaSynth, not by
   Unity code.
3. A normalized-to-range mapping owned by AquaSynth for load/velocity/quality when the game
   supplies 0..1, if that is the game's contract. `ControlSurfaceCatalog` already holds
   normalized surfaces for `.aqua` (`hslider(path, DefaultNormalized, 0, 1, 0.001)`,
   `FaustExport.cs:157`) [V]; the thruster has none.

## Are param inputs clamped today?

No, for the thruster path. Layer by layer [V]:

| Layer | Clamp? |
|---|---|
| `thruster.dsp` | No. Slider min/max are UI metadata. The generated `compute` reads the zone raw (`fSlow0 = fHslider0`). An rpm of 20000 or -5 reaches `si.smooth` and every formula (`shaft = 28 + rpm*0.025`, `load = (rpm-300)/5700`, band centres, drive). |
| `FaustNativeToolchain.WriteControl` (`NativeFaustRuntime.cs:860-869`) | No. Only unknown-path rejection, and `NaN`/infinity written as-is. |
| `AquaSynthStreamingPatch.SetControls` / `ProcessBlock` | No. |
| `tools/FaustRender/Program.cs:51` | No; it only sweeps within its CLI arguments. |
| `NativeFaustRuntimeTests.cs` | No test for out-of-range, NaN, or infinity control values found (grep for clamp/throws/range: no hit). |
| `ControlSplineTimeline` (`src/AquaSynth.Core/ControlSplineTimeline.cs:167-231`) | Yes, but only normalized `[0,1]` for `.aqua` control splines. It is not in the thruster path. |
| Faust `APIUI::setParamValue` (Unity bridge) | No (`*fZone = v`). |
| Unity side | The mixer/inspector clamp to the declared `min/max` in the native SDK path [U]. A script calling `setParameter` or `SetFloat` directly is not clamped by Faust's glue [V]. |

The final `tanh` and the 0.425 trim (`thruster.dsp` last line) bound the output. Peak at
rpm 4800 is 0.36 (`docs/renders/thruster-v5-manifest.json`) [V]. Whether out-of-range rpm stays
finite and bounded is **not measured**: the manifest covers 300..6000 only. Hence
`nonFinite` coverage in the plan below includes out-of-range and non-finite inputs.

Recommended clamp site: in the AquaSynth-owned manifest/host layer (for (a), a small
wrapper in the managed script that Faust's unmodified architecture files sit under; for (b),
`WriteControl`), clamping to the declared min/max and rejecting NaN. Do not edit
`unity.cpp`/`APIUI.h` (loses the architecture exception). The DSP itself can also carry the
bound: `hslider(...) : max(300) : min(6000)` in the `.dsp` is the one place both
lanes would share. That is a patch edit and a design choice for the thruster's owner, not
decided here.

## Callback-deadline measurement plan (rerunnable)

Goal: show, per path and platform, that `compute` fits the audio callback with margin,
including at the worst rpm sweep, under CPU load. Nothing here has been run.

**Deadline.** `D = frames / sampleRate`. Unity's block comes from `AudioSettings.GetDSPBufferSize`
(`FaustPlugin_template.cs` `getBufferSize`). Test 256, 512, 1024 frames at 44.1 k and
48 k (the render manifest uses 48 k / 512, i.e. D = 10.67 ms). Proposed pass line:
p99.9 under 25% of D, max under 50% of D (Aqua's ruling, 2026-11-07). The spike's
shared-host outliers (max 10.8-17.1 ms, 4-5 blocks over deadline) mean the max line can only
be judged on a quiet host; a max miss on a loaded host is not a verdict.

**What to time.**

1. `compute(count, ...)` alone, pure C++ (AOT): build `thruster.cpp` with Faust's
   `architecture/bench.cpp` or a 30-line driver; `std::chrono::steady_clock` around each
   `compute`; 200,000 blocks; discard the first 1,000. [V: `bench.cpp` exists in
   `architecture/`; not run.]
2. The whole plugin callback: `ProcessCallback` or `OnAudioFilterRead` including
   `interleavedWrite` and the parameter write; timing taken inside the plugin with a
   preallocated ring of timestamps, no logging in the callback; dumped on unload.
3. Managed host (b): `FaustRender`-style loop over `AquaSynthStreamingPatch.ProcessBlock`
   with the pointer overload, `Stopwatch.GetTimestamp` around each call, GC allocation count
   (`GC.GetAllocatedBytesForCurrentThread`) asserted zero across the loop.

**Inputs.** The rpm cases from the manifest (600, 4800, sweeps) plus 300 and 6000 steady, a
full 300->6000->300 ramp changing rpm every block, and the out-of-range set
(`-1e9`, `0`, `1e6`, `NaN`, `+Inf`) with finite-and-bounded output asserted. Add the
rpm-and-load product of any new controls. Include denormal check (`-ftz 2` build vs without).

**Conditions.** Idle host and with the Starfire load other Minds create (record
`nvidia-smi` and `query user` before and after; she wants it saturated, but a GPU hang resets
her display, so the run is CPU-only); one thread pinned; power plan noted; Debug and Release
for managed pieces. Linux on Yggdrasil or this host, Windows on Starfire over `ssh starfire`
(not run, and it is Meta's shared machine, so one job at a time).

**Variants to compare**, same inputs: AOT `-vec` vs scalar; AOT `-O2` vs `-O3 -march=x86-64-v2`
(avoid `-march=native`, which the player's CPU may not match); JIT optLevel `-1` vs 3 [U: flag
meaning]. First-callback and instantiation time recorded separately from steady state.

**Output.** One JSON per run: host, OS, CPU model, Faust version, compile flags, block size,
sample rate, n, min/p50/p99/p99.9/max in microseconds, % of D, overrun count (`t > D`), peak,
rms, `nonFinite`, `dspSha256`. Same shape and location as `docs/renders/thruster-v5-manifest.json`
so the existing hashes can be compared. A script for it belongs in `tools/` next to
`FaustRender`.

**In the real player.** Unity does not expose callback timing directly. Time inside
the plugin as above, and read `AudioSettings` underrun counters / Profiler `Audio` markers
[U: exact counter names] over a 10-minute run in a development build on each OS.

## Unverified list, in one place

- That a built player can run anything but the patches compiled into it: player-authored or
  modded patches are out of reach by construction under (a); live edit needs the editor and (b).

- That the generated Windows `.dll` and Linux `.so` load and play in a Unity Windows/Linux
  player (no Unity available in this run).
- `faust2winunity` end-to-end on any host (MinGW not checked).
- Behaviour of a 0-input generator in a mixer effect slot; mixer parameter exposure.
- Generated-code licence position, LLVM/z3/libffi/libedit licences, and a per-function
  inventory of `faustlibraries` licences for the thruster's calls.
- A Windows `libfaust.dll` build and its dependencies.
- Compile latency in milliseconds for the thruster (the field exists, no number taken).
- Out-of-range / non-finite rpm behaviour of the thruster output.
- Atomicity of the float zone write across a block boundary in a real plugin.
- Unity's DSP buffer size defaults and underrun counter names.
