# Acoustic Graph Proprioception Pass

Date: 2026-05-29

This pass corrects the direction of the rebuild. The failure is not that the
graph lacks one more utterance target. The failure is that AquaSynth has been
letting a large discrete PT-shaped graph survive as the working body while
adding compensators around it. That is Jenga.

The live objective is to collapse the graph into a small set of physical
modeling primitives, instrument those primitives directly, and compare their
sample/flow behavior against serious reference implementations: especially
VocalTractLab, ArtiSynth, Story/TubeTalker, Smith-style waveguides, and only
then PT/SndKit as a compact stress fixture.

Baby words remain end-to-end witnesses. They are not the rebuild owner.

## Objective

Replace the monstrous discrete graph with as few faithful physical primitives
as possible:

- continuous tract geometry and area functions;
- source primitives coupled to tract load;
- branch/coupling primitives;
- radiation/load primitives;
- delay-line propagation and scattering primitives;
- contact/constriction primitives;
- graph instrumentation that exposes wave/sample flow at those owners.

The target is not a better audio golf loop. The target is an acoustic machine
whose internal behavior can be inspected and compared to reference simulators.

## Research Authority Map

### VocalTractLab / Birkholz

Owner lesson: separate anatomy, gestures, vocal-fold/source models, acoustic
simulation, and copy-synthesis controls.

Pressure on Aqua: `tract` should not lower into many little terminal records
that become the de facto anatomy. It should lower into continuous morphology
and explicit primitive controls that a gesture layer can drive and diagnostics
can observe.

### Story / TubeTalker

Owner lesson: continuous area functions and tract length are the compact bridge
between anatomy and acoustics. Formants are derived evidence.

Pressure on Aqua: emitted sections, sample delays, and source/contact sample
points are lowering choices. They must not become the authoring truth.

### Julius Smith / Digital Waveguides / Faust Primitives

Owner lesson: bidirectional delay lines, scattering junctions, loss filters,
fractional delay, and passive boundaries are the cheap realtime substrate.

Pressure on Aqua: generated Faust should use compact waveguide and delay
primitives where they preserve the owner contract. If custom emitted scattering
is needed for differentiable controls, it should still look like a small
physical primitive, not hundreds of section-local expressions.

### ArtiSynth / Fels and Lloyd

Owner lesson: body geometry, mechanical contacts, airflow/acoustics, timeline
controls, and diagnostics are separate organs in an integrated simulation.

Pressure on Aqua: instrumentation is not optional. We need probes that observe
geometry, contact state, pressure/flow, wave variables, source load, radiation,
and timeline transitions at the layer where the primitive claims authority.

### Pink Trombone / SndKit

Owner lesson: PT is a compact Kelly-Lochbaum stress case with oral tube, nasal
branch, source injection, obstruction history, and radiation.

Pressure on Aqua: use PT to compare behavior and catch regressions, but do not
let PT's cell grid or utterance sketches become the architecture.

## Current Mechanism

`tract` authoring is now a PT-shaped fixture adapter. It preserves the
high-resolution `VocalTract` authoring surface, but it no longer lowers into
generated `voices_0_area_*`, `voices_0_contact_*`, or `voices_0_inj_*` acoustic
banks.

The vocal DSP owner is the primitive network:

- `AreaFunction` owns continuous tract/nasal geometry, physical length, live
  deformation controls, and the resampling policy for emitted Faust.
- `WaveguidePath` owns propagation speed, delay strategy, loss, and semantic
  tube identity.
- `SourcePort` owns pressure/flow excitation and tract-load coupling.
- `ConstrictionContact` owns opening, resistance, stored pressure, and released
  flow.
- `BranchPort` owns side-port admittance and exchanged flow between paths.
- `RadiationLoad` owns aperture, impedance/reflection, boundary flow, flow, and
  radiated output.
- `ProbeTimeline` owns deterministic primitive telemetry for comparison before
  utterance metrics.
- `VocalNetwork` ties those records together as the speech-path authority.

Explicit `AcousticPortNetwork` scripts still exist for lower-level acoustic
graph experiments, but PT-style `tract`, `glottis`, `tract_injection`, and
`nasal_branch` no longer own vocal DSP by generating a banked graph. They adapt
fixtures into primitive records.

## Invariants

- `VocalNetwork` owns PT-shaped vocal acoustics. Explicit `AcousticPortNetwork`
  scripts remain a lower-level lab surface, not the default tract lowering.
- Continuous morphology owns tract shape and length. Generated section/terminal
  count is backend approximation, not anatomy.
- Faust output must be compact and inspectable enough to compile, probe, and
  compare.
- Primitives must state what physical quantity they own: area, pressure, flow,
  wave variable, delay, coupling, contact, source load, or radiation.
- Instrumentation must observe primitive internals, not only final audio or
  final metrics.
- PT utterances are witnesses after the primitive behavior is legible, not the
  steering authority.

## Jenga Findings

### The Discrete Graph Is Still Too Much Body

The graph was reduced enough for Faust to compile, but its shape is still
section-bank driven: area terminals, source terminals, contact terminals, and
node-local expressions. This keeps pushing us toward local fixes because every
generated component looks like a place to add one more rule.

### We Need Primitive Flow Probes, Not Just Audio Reports

Current probes can show selected source/contact/radiation values, but they do
not yet provide a full primitive timeline: incoming/outgoing wave variables,
segment delay states, scattering inputs/outputs, source load pressure/flow,
contact opening/pressure, branch admittance, and radiation impedance over time.

Without that, comparing to VTL/ArtiSynth/PT is mostly listening plus aggregate
spectral evidence. That is not enough.

### Motion And Contact Are In The Wrong Shape

`tract_motion` parses gesture intent, but graph area/delay lowering does not
own motion/passivity as a primitive. Contact release exists, but it is still a
scalar reservoir pulse instead of a contact/constriction flow primitive that
can be inspected as pressure, opening, resistance, and released flow.

### Faust Compile Pressure Is Architectural Evidence

When Faust refuses to compile or becomes slow enough that three toy utterances
are painful, that is not tooling bad luck. It means the lowering is asking
Faust to carry too many discrete local expressions. The correct response is to
collapse into primitives, not to tune around the generated graph.

## Primitive Collapse Plan

### 1. Define The Minimal Primitive Set

Create or document the target primitives before adding another acoustic law:

- `AreaFunction`: continuous length/area geometry with live deformation.
- `WaveguidePath`: compact bidirectional propagation over that area function.
- `ScatterJunction`: two-port and N-port passive scattering over admittance.
- `SourcePort`: pressure/flow source with load coupling.
- `ConstrictionContact`: opening, resistance, stored pressure, and released
  flow.
- `BranchPort`: side-port admittance and coupling.
- `RadiationLoad`: boundary reflection, aperture, impedance, and radiated flow.
- `ProbeTimeline`: dev/test-only sampling of the primitive state above.

If an existing record cannot be mapped onto one of these owners, it should be
deleted, demoted to authoring sugar, or kept only as a compatibility shim that
delegates to a primitive.

### 2. Compare Primitive Behavior Against References

Reference comparison should look at internal behavior, not only waveform
similarity:

- VTL/Birkholz: gesture-to-tract/source separation and copy-synthesis control
  surfaces.
- ArtiSynth: body/contact/acoustic/timeline diagnostic separation.
- Story/TubeTalker: continuous area-function parameterization and formant
  consequences.
- Smith/Faust: delay-line/scatter/loss/radiation primitive economics and
  passivity.
- PT/SndKit: compact oral/nasal KL behavior, obstruction history, and radiation
  under known fixtures.

### 3. Instrument Sample Flow Through The Graph

Add a dev/test probe path that can write per-block or per-window timelines:

- path area and delay samples;
- incoming/outgoing wave variables at selected scatter sites;
- source load pressure, source flow, and injected wave contribution;
- contact opening/resistance/reservoir/released flow;
- branch admittance and exchanged flow;
- radiation boundary reflection, flow, and emitted output;
- derived energy/passivity checks.

This is the layer that lets us say "the tract behaves like the reference" or
"this primitive is lying" before touching the final utterance.

### 4. Collapse Generated PT Lowering

Generated PT-style tract lowering is now an authoring adapter over the
primitive set. It may preserve PT-shaped controls for fixtures, but it cannot
decide the physical model by emitting banks of terminals where a continuous
primitive can own the same behavior.

## Cut Line

Cut any new feature whose main evidence is that it improves `mama`, `papa`, or
`thrombosis` without making the primitive flow more legible.

Cut any generated component that cannot explain which primitive owner it
serves.

Cut any probe that observes final audio while leaving source/load/contact/path
flow hidden.

## Next Work

The first primitive cut is in place: parser/model records, primitive Faust
lowering, tract fixture adaptation, and deterministic `ProbeTimeline` CSV
support. The second cut adds explicit path wave/passivity probes and a
PT/SndKit comparison report:

- `ProbeTimelineReport` now reports path `incoming_wave`, `outgoing_wave`,
  `energy_in`, `energy_out`, and `passivity_ratio`, plus contact resistance.
- Primitive Faust debug probes expose the same path wave and passivity fields
  under `/debug/...`.
- `PrimitiveReferenceReport.ComparePinkTrombone` compares primitive geometry,
  delay, branch admittance, contact, and radiation fields against local
  PT/SndKit fixture controls without adding a native reference dependency to CI.
- `PinkTromboneReferenceRenderer.RenderTimeline` exposes PT internal oral/nasal
  path, branch, contact, and radiation timeline fields from its own arrays, and
  `PrimitiveReferenceReport.ComparePinkTromboneTimeline` role-normalizes those
  fields against Aqua primitive timelines.
- `PinkTrombonePrimitiveTimelineComparisonWritesArtifacts` writes per-fixture
  Aqua timelines, PT internal timelines, comparison CSVs, and a summary under
  `artifacts/parity/primitive-vocal-timeline`.
- The first comparison-driven correction demoted hidden source unit gain into
  explicit `SourcePort.FlowScale`, stopped aperture from rewriting
  `RadiationLoad.Reflection`, and fixed `ProbeTimelineReport` radiation flow to
  observe path wave flow rather than `aperture / impedance` alone.

The next implementation pass should harden the primitives further:

- decide where gesture slew lives: inside primitive ports or in a separate
  gesture/timeline owner;
- add optional VTL/ArtiSynth adapters only as artifact comparators, not default
  CI dependencies;
- keep `mama`, `papa`, and `thrombosis` as downstream witnesses until primitive
  flow evidence improves.
