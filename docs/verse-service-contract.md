# AquaSynth Verse Service Contract

AquaSynth is the synth daemon. It accepts `.aqua` DSL and speech/utterance
control documents, compiles Faust-native instruments, returns live instrument
handles, and publishes controllable audio/render state through CultMesh and Eve.

## Authority

Owner: AquaSynth.

Inputs:

- `.aqua` patch DSL
- `weksa.utterance_handoff.v0`
- `weksa.utterance_embedding_handoff.v0.1`
- learned phonetic realization and synth-driver model state
- render, sampling, and live-control requests

Outputs:

- `aquasynth.patch_graph.v0`
- `aquasynth.faust_source.v0`
- `aquasynth.compiled_instrument.v0`
- `aquasynth.instrument_handle.v0`
- `aquasynth.control_session.v0`
- `aquasynth.render_sample.v0`
- Eve/CultUI surfaces for patch graphs, compile products, live controls, and
  speech-training/render status

Weksa owns conversational intent and utterance lowering. AquaSynth owns the
learned utterance embedding, synth-driver automation, Faust compilation, native
binary lifetime, live instrument handles, control sessions, and sampled outputs.

## CultCache Witnesses

- `.aquasynth/provider-advertisement.cc`: read-only provider advertisement
  witness.
- `.aquasynth/patches/{patchId}.cc`: parsed `.aqua` patch graphs and semantic
  control catalogs.
- `.aquasynth/faust/{compileId}.cc`: emitted Faust source, compile options,
  diagnostics, and artifact references.
- `.aquasynth/instruments/{instrumentId}.cc`: compiled native binary, ABI,
  control catalog, architecture target, and lifecycle receipts.
- `.aquasynth/sessions/{sessionId}.cc`: live instrument handle, lease, control
  values, transport state, and teardown receipt.
- `.aquasynth/renders/{renderId}.cc`: offline/live sample outputs, feature
  measurements, and scoring receipts.
- `.aquasynth/speech/{utteranceId}.cc`: Weksa handoff consumption, packed
  utterance embeddings, synth-driver outputs, and render receipts.

## CultMesh Verses

- `aquasynth.service`: service identity, version, schema catalog, and health.
- `aquasynth.patch`: `.aqua` parse/validation/graph state.
- `aquasynth.compile`: Faust emission, native compilation, diagnostics, and
  artifact handles.
- `aquasynth.instrument`: compiled instrument handles, control catalogs, and
  lifecycle state.
- `aquasynth.speech`: utterance embedding, speech-driver, and render-training
  state.
- `aquasynth.operator`: daemon status, queue pressure, toolchain readiness,
  witness freshness, and degraded-mode visibility.

## Eve Surfaces

- `aquasynth.eve.patch_graph.v0`: inspect parsed `.aqua` graphs and semantic
  control catalogs.
- `aquasynth.eve.compile_queue.v0`: inspect Faust compile jobs, diagnostics,
  native binary handles, and failures.
- `aquasynth.eve.instrument_control.v0`: control a live instrument handle like
  an instrument, with stable parameters, ranges, and session leases.
- `aquasynth.eve.speech_training.v0`: inspect Weksa handoffs, packed utterance
  embeddings, synth-driver output, render loss, and sample receipts.
- `aquasynth.eve.operator.v0`: compact daemon state, toolchain status, queues,
  and witness freshness.

## Commands

- `patch.compile`: parse `.aqua`, emit Faust, compile a native target, and
  return an instrument handle.
- `instrument.open`: allocate or attach to a compiled instrument session.
- `instrument.control`: set one or more stable semantic controls.
- `instrument.sample`: sample a live or offline instrument output.
- `speech.realize`: consume a Weksa utterance handoff and produce synth-driver
  controls plus render receipts.
- `provider.advertise`: publish `gamecult.eve.provider_advertisement.v1`.

Every command must commit typed receipts through the same derivation path used
by any CLI or worker. A dashboard, REPL, worker loop, or native host must not
invent a separate truth for compiled products or control state.

## Runnable Daemon Slice

`tools/AquaSynthDaemon` is the current local daemon body. It exposes:

- `once`: accepts inline `.aqua` or `--script-file`, compiles through the native
  Faust boundary, renders samples, writes `.f32` and `.wav` artifacts, and
  commits typed `.cc` witnesses under the configured store root.
- `daemon`: reads JSON-lines command envelopes from stdin and writes JSON-lines
  receipts to stdout. The JSON-lines stream is an edge transport for local
  smoke and xenos callers; command/session/render authority lives in
  `AquaSynthDaemonService` and its CultCache witnesses.

Current witness roots:

- `.aquasynth/compile/{receiptId}.cc`
- `.aquasynth/sessions/{sessionId}.cc`
- `.aquasynth/renders/{renderId}.cc`
- `.aquasynth/samples/{renderId}.f32`
- `.aquasynth/samples/{renderId}.wav`
- `.aquasynth/operator/operator-state.cc`

Example:

```powershell
dotnet run --project tools\AquaSynthDaemon -- once `
  --script-file patches\808\kick.aqua `
  --patch-id patches.808.kick `
  --faust-name patch_808_kick `
  --duration 0.25
```

The service now makes `instrument.sample` duration authoritative for the render
window. Patch-estimated envelope duration remains useful for direct compiler
calls, but a daemon sample request owns the number of output frames.

## Forbidden Writers

- Weksa does not compile Faust or own instrument controls.
- Eve lowerings do not own compiled binary lifetime or control truth.
- Faust source is an emitted artifact, not the semantic patch owner.
- Native host handles cannot silently override CultMesh session state.
- Render samples are evidence, not patch identity.
