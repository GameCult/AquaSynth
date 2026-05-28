# AquaSynth Audio Doctrine

## Mission

AquaSynth is the authoring and compiler surface for AquaSynth
patches. It should let us sketch sound quickly, compile to Faust, inspect the
graph, compare results, and hand stable contracts to the C#/Vortice side.

The point is not to rebuild a whole audio engine in C# because we got excited
near a compiler. The point is to make patch intent portable, analyzable, and
pleasantly dangerous.

## Realtime Law

Realtime audio has one primitive moral fact: the callback deadline wins.

Runtime-facing code must avoid operations with unbounded or scheduler-dependent
latency:

- dynamic allocation or deallocation
- filesystem, network, console, or logging I/O
- locks, mutexes, blocking waits, sleeps, or context switches
- host/API calls not explicitly documented as callback-safe
- graph compilation, parsing, reflection, or cache misses

Authoring tools can do expensive work. Audio callbacks cannot. Build patches,
compile Faust, allocate buffers, score references, and serialize contracts
outside the audio thread. Feed the realtime side precompiled DSP, fixed buffers,
plain parameters, and lock-free or double-buffered control updates.

## Dataflow Shape

Patch scripts are a textual dataflow language. Their job is to describe a graph:
voices, controls, modulators, envelopes, filters, and output routing.

Pure Data is useful discipline here:

- A patch is a graph of objects and connections, not a bag of imperative steps.
- Audio-rate work is block-based, and block size is part of the execution model.
- Subpatches can change block size, overlap, resampling, and on/off DSP state;
  those ideas map to explicit compile/runtime boundaries, not hidden globals.
- Control events and signal streams are different animals. They can meet, but
  the crossing should be named.

For this repo that means:

- Keep `SynthPatch` explicit and serializable.
- Keep parser sugar as sugar. Defaults, templates, SFXR atoms, and buses should
  all lower into visible patch graph data.
- Keep block size, sample rate, channel count, and latency visible in APIs that
  consume or produce audio buffers.
- Prefer deterministic lowering. Same script plus same options must produce the
  same patch and Faust source.

Filters own post-source spectral selection. Low-pass, high-pass, band-pass, and
notch are native AquaSynth filter authorities. Formants remain vowel/body color,
and PAD spectrum/profile fields remain source-table construction; do not use
those surfaces as substitutes for ordinary subtractive filters.

## Faust Boundary

Faust describes pure DSP: inputs to outputs. Architecture files and host code
connect that pure processor to drivers, UI, MIDI, sensors, plugins, and the
outside world.

That separation is the model for this repo:

- The DSL owns musical intent and graph construction.
- The Faust emitter owns pure signal expression.
- AquaSynth owns the compile product contract: Faust toolchain selection,
  target-language options, cache keys, generated artifact layout, parameter and
  bus manifests, diagnostics, and provenance.
- The engine owns scheduling, buffers, device I/O, threading, and presentation.
- The engine may request compilation or load a cached artifact, but dynamic
  patch compilation belongs to AquaSynth and must complete outside the realtime
  callback before the engine swaps in the finished DSP.
- Patch parameters are runtime controls, not compile-time constants. A compiled
  DSP must be able to expose stable parameter paths so AquaSynth can vary a sound
  without recompiling the patch.
- Host parameters should be smoothed where they cross into signal-rate behavior.

Generated Faust should be stable, boring, and inspectable. Cleverness belongs in
the DSL compiler only when it lowers to a graph a tired engineer can still read
after midnight.

Authoring and parity tests may render through Faust-generated C# because that
keeps comparison buffers inside the .NET test harness. Shipping/runtime paths
should prefer Faust native targets when the platform permits it. Bundled Faust
belongs in an explicit AquaSynth toolchain lane, while consumer runtimes should
usually ship compiled DSP artifacts plus manifests instead of silently dragging a
compiler into the engine package.

## Control And Modulation

The Serum-style promise is not "one primitive that puts an oscillator on
everything." The promise is that any meaningful parameter can be a modulation
target in an ergonomic, visible way.

Rules:

- Every modulation target must have a stable semantic name.
- Every exposed parameter must declare a stable path, default, min, max, step,
  unit or scale when meaningful, and whether it is safe to automate at control
  rate.
- Global controls and voice-local modulators should share target names.
- Modulators need waveform, rate, depth, phase, and bias.
- Buses are authoring conveniences; compiled patches should expose individual
  target lanes.
- UI/control-rate movement should be smoothed before it becomes audible zipper
  noise.
- Recompilation is for graph shape changes. Parameter changes must flow through
  the hosted DSP control API.

## Notes And Envelopes

Notes own pitch and gate. Envelopes own level shape. Do not mix those
authorities.

- `Note` carries note frequency, gate duration for one-shot patches, and source
  selection for host/MIDI-driven patches.
- `Envelope` is ADSR-shaped: attack time, decay time, sustain level, and release
  time.
- Legacy SFXR sustain maps to note gate duration, because it is how long the
  generated sound is held before release.
- Legacy SFXR punch maps to a lower sustain level plus voice gain compensation
  when importing old SFXR material: ADSR peak stays implicit at 1, and the
  sustain level falls below that peak. Keep that as compatibility mapping, not
  as a general envelope field.
- Host/MIDI note mode should expose stable note frequency and note gate controls
  that the engine can wire to MIDI note-on/note-off behavior.
- Faust-managed polyphony is the preferred MIDI path. AquaSynth should describe a
  single voice graph and emit Faust's standard `freq`, `gain`, and `gate`
  controls with `[midi:on][nvoices:n]` options, leaving MIDI decoding and voice
  allocation to Faust architectures unless a concrete target proves that
  boundary insufficient.
- DX7 rate/level envelopes may be lowered to labeled ADSR approximations for
  rebuild work, but that is not exact DX7 envelope execution. Keep exactness
  pressure visible in reference features instead of smuggling it into ADSR.

## Phonetic Tract Boundary

IPA is an authoring surface, not a synth model. Weksa or another language layer
owns text, phonology, allophones, and phonetic intent. AquaSynth owns the
articulatory realization: gesture planning, morphology constraints, tract-area
targets, excitation events, and Faust lowering.

## Graph Vocal Sources

Graph voices do not let `freq=` own vocal pitch. For acoustic-graph voices,
`freq=` is only an initializer/hint for defaults such as valve stiffness when a
script has not supplied a physical value. The live source authority is the
source port and its acoustic load.

Current authority map:

- Owner: `AcousticSourcePort` owns local excitation semantics at a graph
  terminal.
- Inputs: pressure, tension, aperture/opening, mass, damping, stiffness,
  saturation, drive, load coupling, rest opening, balance, source position, and
  the incident pressure at the graph node.
- Outputs: source flow injected into the acoustic graph; rendered pitch is an
  outcome of valve state plus tract/radiation load.
- Derived state: `kind=syrinx` and `kind=labial` are authoring aliases for
  `model=tissue_valve`; they are not species modules.
- Forbidden writers: labial/syrinx lowering must not emit a commanded
  `os.phasor(... freq ...)` oscillator as the source of truth.
- Shared path: larynx, syrinx, reed-like, and alien morphologies should become
  graphs plus source ports, not bespoke renderer families.

The reusable primitive is `model=tissue_valve`: a Faust-friendly nonlinear
valve state with pressure drive, delayed velocity/displacement state, cubic
aperture saturation, and incident-pressure load feedback. A syrinx is two such
valves feeding an acoustic graph; a larynx is one such valve feeding a tract.
Different species should change morphology and gestures, not add hidden source
renderers.

The current bird-golf tool extracts reference envelope, dominant pitch,
onset/offset, active-duty, and flux features, then maps those into tissue-valve
candidate parameters. It does not yet express arbitrary time-varying source
gestures through the DSL. That missing automation surface is the next coherent
language feature: general control curves must target stable field paths such as
`/acoustic/sources/0/pressure`, not a bird-shaped side channel.

The current speech-learning lane adds a trainable surface above that boundary:
structured utterance metadata lowers into an utterance embedding, then into a
synth-driver automation vector for tract controls, envelopes, LFOs, filters, and
spectral lanes. Those learned models are controllers. They do not get to erase
the tract boundary or make morphology optional.

The boundary must stay split into three inspectable records:

- `PhoneticIntent`: language-owned phones, features, timing, and prosody.
- `ArticulatoryPlan`: AquaSynth-owned gestures and tract/source targets.
- `ArticulatoryConstraintReport`: host-visible diagnostics when morphology
  cannot produce the requested articulation.
- `VocalTractPlanResult`: the host reaction surface that either carries an
  accepted plan or a rejection with diagnostic codes.

Anatomy has veto power. If a beaked morphology cannot form a bilabial closure,
the planner must report the missing capability instead of silently substituting
a fake sound and calling it expressive.

The first parity reference is eSpeak NG because it is compact, deterministic,
and cheap to render. It is timing and intelligibility pressure, not anatomy
authority. Use it to populate loss surfaces and train controller weights; do not
mistake matching its formant shortcuts for building a vocal tract.

Speech training receipts must carry timing and confidence, not just loss. For
each metadata-to-embedding, embedding-to-automation, render, score, and
backpropagation step, record the decision time, observed latency, intended
latency budget, confidence estimate, and artifact/model identifiers. A correct
sound from an unbounded or unobservable path is not a reliable witness.

## Metrics

Analysis exists to catch regressions and support search, not to crown winners.
AquaSynth Faust output can be rendered through Faust-generated C# in authoring
tests, which gives us candidate audio buffers without depending on the old Rust
renderer. That is only half of parity: reference targets still need a real
external render or a captured, lawful fixture before the comparison means
anything.

Useful metrics:

- envelope distance for shape and timing
- log-mel spectrogram distance for perceptual-ish spectral shape
- speech-parity loss over tiny utterance fixtures before broader IPA claims
- peak/RMS ratios for loudness sanity
- zero-crossing and centroid ratios for noise/brightness drift
- script readability and terseness scores for DSL golf work

Do not confuse high metric agreement with musical success. The game context,
the reference sound, and the user’s ear still outrank the spreadsheet. Annoying,
but civilization has survived worse.

## Source Distillations

- PortAudio callback guidance: callbacks are delicate realtime contexts; avoid
  unbounded calls such as allocation, I/O, context switching, mutex operations,
  and unsafe API calls.
  <https://files.portaudio.com/docs/v19-doxydocs/writing_a_callback.html>
- Pure Data `block~`/`switch~`: DSP is block-structured; subpatches can set
  block size, overlap, resampling, and switched DSP state. Pd’s default block
  size is 64 samples.
  <https://pd.iem.sh/objects/block~/>
- Faust architecture docs: a Faust program is pure DSP mapping inputs to
  outputs; architecture files connect that DSP to drivers and controllers.
  <https://faustdoc.grame.fr/manual/architectures/>
- Faust UI controls: `hslider`, `vslider`, `nentry`, `button`, and `checkbox`
  expose runtime controls; UI helper classes such as `MapUI` provide
  `setParamValue`/`getParamValue` style access by path or short name.
  <https://faustdoc.grame.fr/manual/architectures/>
- Faust signals library: buses, block termination, interpolation, repeat, and
  smoothing are first-class signal-composition tools; smooth control crossings
  instead of letting UI steps become audio artifacts.
  <https://faustlibraries.grame.fr/libs/signals/>
