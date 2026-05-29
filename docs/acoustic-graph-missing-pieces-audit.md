# Acoustic Graph Missing Pieces Audit

Date: 2026-05-29.

This pass compares the live graph lowering to the local physical-voice
reference packet after the graph closure release cut. The result is not "turn
up plosives." The probe evidence says contact release reaches the two-port
scatter and lip radiation. The utterance harness still fails accepted speech
parity, so the missing contracts are adjacent physical ownership, not gain.

Latest evidence:

- graph probe:
  `artifacts/parity/pink-trombone-graph-thrombosis-probes/20260529T021934812`
- utterance probe:
  `artifacts/parity/pink-trombone-utterance-logmel/20260529T022214016`

`thrombosis` RMS improved after the closure cut, but cosine stayed weak and
silence mismatch worsened. The graph can move release energy; it cannot yet
shape it into reference-like speech.

## Objective

Find the research-backed physical contracts still absent from AquaSynth's
graph-native vocal synthesis, using the current code path rather than Pink
Trombone nostalgia as the authority.

## Current Mechanism

`PatchScript.EnsureTractAcousticNetwork` lowers PT-shaped tract authoring into
neutral graph records:

1. an oral `AcousticPath` with live area controls;
2. a glottal tissue-valve source at the path start;
3. contact terminals at tract sections;
4. many turbulence source terminals weighted by constriction position;
5. an optional nasal branch with side-port admittance;
6. lip/nostril radiation terminals;
7. a fractional-linear wave clock.

`FaustExport` then emits a bidirectional delay graph:

1. terminals become graph nodes;
2. path intervals become right/left traveling-wave segment state;
3. same-path two-port nodes use the Kelly-Lochbaum area law;
4. declared non-bypass connections use one N-port area-scattering law;
5. unconnected multi-port nodes still fall back to a pressure-like node law;
6. source ports inject local flow into outgoing waves;
7. contact terminals can store a scalar reservoir and inject a directional
   release into two-port scattering;
8. radiation reads boundary flow and colors it as output.

## Invariants

- Acoustic graph records own vocal topology. Do not revive a legacy tract
  renderer or a Pink Trombone module.
- Sources, contacts, branch junctions, propagation, loss, and radiation must
  be explicit graph semantics, not output EQ or hidden gain.
- Manual gestures, scripted utterance curves, and native Faust controls must
  share the same graph lowering path.
- If a value is a consequence of pressure, aperture, tissue, loss, load, or
  morphology, it should not be owned by a generic `drive` or brightness knob.

## Reference Comparison

### Birkholz / VocalTractLab

Reference pressure: separate anatomy, gestures, vocal-fold model, acoustic
simulation, and copy-synthesis/optimization controls.

Aqua has the acoustic path graph and named controls, but it still lacks a
gesture owner inside the graph. `tract_motion` is parsed, yet live graph area
and delay changes do not use a first-class slew/passivity policy. Contact
timing is inferred from aperture frame differences instead of owned by
articulatory gesture plus constrained flow.

Missing: gesture-owned area/delay smoothing, contact velocity, and physical
control ranges that copy synthesis can optimize without finding fake gains.

### Story / TubeTalker

Reference pressure: continuous area functions and tract length are the bridge
between morphology and acoustics.

Aqua has area functions and fractional delay, but generated PT-style tract
sampling still emits many fixed contact/source terminals. Interior sources
attach to nearest graph nodes; there is no interpolated injection primitive.

Missing: continuous source/contact placement over the path, and a clearer rule
for changing length/area without changing the graph's acoustic convention.

### Smith / Digital Waveguides

Reference pressure: bidirectional wave variables, explicit scattering,
fractional delay, loss, radiation, and passivity.

Aqua now has the right skeleton. The main violation is that not every graph
node uses one wave convention. Declared connections use the N-port area law and
ordinary two-port path nodes use KL scattering, but unconnected multi-port nodes
still use a pressure-like fallback:

`nodePressure = 2 * sum(incoming) / portCount`

then outgoing waves mix that pressure with averaged node reflection. That is a
second truth. Source and contact energy are also injected after scattering, so
energy probes report total in/out but do not account for source work.

Missing: one explicit node-law family for every topology, plus source/contact
energy accounting so passivity checks can distinguish legal excitation from
unstable scattering.

### Fels / ArtiSynth

Reference pressure: body geometry, tissue/contact mechanics, airflow/acoustics,
and diagnostics are distinct layers.

Aqua's contact cut moved release into the graph, which was the right direction.
It is still not a constrained airflow/contact model. The reservoir is a scalar
charged from upstream-minus-downstream pressure and released by closure falloff.
It does not own side-specific upstream/downstream state, leakage through narrow
apertures, collision/contact damping, or flow resistance through the reopening
constriction.

Missing: contact as a flow constraint that participates in scattering while
closed and reopening, not only as a pulse injected at release.

### SndKit / Pink Trombone

Reference pressure: PT is a compact stress fixture for obstruction history,
nasal coupling, tract radiation, and tract-source interaction. It is not the
architecture.

Aqua now preserves the graph-native path and can produce contact release
signals. The utterance artifacts show the tract is still under-articulated and
bad at silence/release timing. PT-like output cannot be recovered honestly by a
global drive restore because the graph already proves release reaches radiation.

Missing: obstruction history with proper constricted flow, source/radiation
loading, and gesture timing. The PT fixture is exposing absent physics, not a
missing preset.

### Syrinx / Myoelastic-Aerodynamic Sources

Reference pressure: bird and mammal sources are pressure/tension/aperture
tissue-valve systems coupled to acoustic load. Pitch and harmonic richness are
outcomes, not standalone owners.

Aqua's tissue-valve source is load-aware enough to be inspectable, and it has
one-mass/two-mass-ish state names. It is still partly a musical oscillator
wrapped in pressure gates: frequency can become a stiffness hint, modal
oscillator/ring output contributes directly to flow, and `drive` still lowers
into `flow_scale`.

Missing: a pressure-flow valve law where aperture, resistance/loss,
tissue state, and downstream/load pressure determine volume velocity. The
source should not need a hidden spectral rescue path.

### Birkholz / Haesner Losses

Reference pressure: area-dependent viscous and kinetic losses, including
partial pressure recovery at expansions, affect formant bandwidth, airflow, and
turbulence power.

Aqua has scalar area-dependent segment loss and source-local `flow_loss`, but
not a unified pressure-loss model across glottis, constrictions, expansions,
branches, and radiation.

Missing: cheap frequency/area-dependent wall and flow losses that feed back
into the graph, instead of only damping segment amplitude or coloring output.

## Missing Pieces

### P0: Boundary Impedance Is Not Really In The Graph

Radiation currently changes boundary reflection with aperture, then reads
boundary flow and applies admittance/high-pass coloring as output. That is
useful, but the frequency-dependent radiation load is not solved as part of
the scattering/load seen by the tract and source.

Why it matters: the references treat radiation/load as part of the acoustic
system. Aqua mostly treats radiation as a boundary tap with color. That can
move energy to the output without shaping the upstream pressure field enough.

### P0: Source Impedance Is Still A Heuristic Injection

Tissue-valve sources read local pressure and emit named flow locals, but the
output is still injected into outgoing waves after node scattering. The model
has pressure drive, aperture, modal tissue, flow resistance, and load pressure,
but it does not yet solve a clean source-flow/load relation at the graph port.

Why it matters: syrinx and vocal-fold references agree that source behavior
changes under acoustic load. Aqua can perturb source output with load pressure,
but the graph does not own source impedance as a port law.

### P0: Contact Is A Pulse, Not A Constricted-Flow State

Graph contact release now lives inside two-port path scattering. That was a
real cut. The remaining missing piece is the closed/reopening interval: leakage,
pressure storage on the upstream side, pressure recovery, and aperture-filtered
flow through the constriction.

Why it matters: the latest `thrombosis` probe shows release energy exists, but
the utterance still has poor timing and silence mismatch. A release pulse can
make sound; it cannot model the pressure-flow history that makes plosives and
frication behave.

### P0: One Node-Law Convention Is Still Not Total

Declared connections and same-path two-port nodes have explicit area laws. The
remaining unconnected multi-port fallback is pressure-like and uses averaged
node reflection. It may be rare in generated PT graphs, but it is still a
surviving second authority.

Why it matters: the graph should either emit an explicit law for a topology or
reject it. A quiet fallback will become a future compensator farm.

### P1: Loss Is Too Scalar

Segment loss is amplitude-only, and source `flow_loss` is local to the valve.
The references support area-dependent viscous/kinetic pressure losses and
frequency-dependent wall/radiation behavior.

Why it matters: current harmonic suppression and articulation weakness are
tempting to fix with drive/brightness constants. The supported owner is loss
and load physics.

### P1: Continuous Placement Is Missing

Generated tract graphs use many source terminals because source position is
nearest-node attachment. That is a compiled approximation, not a clean graph
primitive.

Why it matters: frication and release should move continuously with
constriction gestures. Creating many sources keeps parity pressure alive but
does not scale cleanly to alien morphologies.

### P1: Gesture Motion Is Not Graph-Owned

The parser knows `tract_motion`, but graph area/delay/contact transitions do
not yet share one motion primitive. Utterance fixtures smooth controls
externally.

Why it matters: if fixture curves repair graph behavior, manual/scripted/native
actions can diverge. The graph needs the same transition semantics for all
callers.

### P2: Diagnostics Need Source-Aware Energy

Current energy probes compare wave energy before and after scattering, but
source/contact injection is included in output energy without a separate work
term.

Why it matters: this makes passivity probes useful for gross bugs but weak for
distinguishing lawful excitation from unstable scattering.

## Do Not Do

- Do not restore global graph drive.
- Do not add output EQ as "speech articulation."
- Do not add a Pink Trombone renderer beside the graph.
- Do not make `drive` own spectral harshness, phonation threshold, and energy
  input at once.
- Do not add rules around utterance failures before the graph sees the physical
  state it needs.

## Next Coherent Cuts

1. Make boundary/source impedance a port law.
   Radiation and tissue valves should contribute load/admittance to scattering,
   not only observe or inject after it.

2. Replace contact release with a constricted-flow contact primitive.
   Contact should own upstream/downstream pressure state, leakage, aperture
   flow, and release through the same two-port law.

3. Eliminate the remaining node pressure fallback.
   Every node topology should lower through KL two-port, N-port area scatter,
   bypass, radiation/source/contact port law, or fail with a clear warning.

4. Add a cheap unified loss model.
   Start with named area/frequency-dependent loss terms that can be applied to
   constrictions, expansions, walls, sources, and radiation without becoming
   output coloration.

5. Move gesture slew into graph ownership.
   `tract_motion` should affect area, contact transition, and delay policy in
   the emitted graph, so all callers share the same physical transition.

The next implementation should choose one of those ownership cuts. The sharpest
first cut is probably source/radiation impedance as port law, because both the
human utterance and syrinx references say load coupling is central, and the
current probes already proved raw release transport is no longer the blocker.
