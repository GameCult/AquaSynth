# Pink Trombone DSL Parity Plan

## Objective

Cover Pink Trombone's useful pipeline with AquaSynth language constructs, then
use Pink Trombone as an audio parity target. Do not build a PT clone with Aqua
paint on it. The primitives must compose with ordinary Aqua voices, envelopes,
LFOs, parameters, layers, learned speech controls, filters, and future
morphologies.

## Current Mechanism

`tract` currently lowers into `Voice.Tract`: a voice-local scalar control bundle
with a Faust source/filter proxy and an early waveguide backend. It is useful as
a playground surface and as a named failure against Pink Trombone, but the
remaining anatomy ownership is still too discrete:

- authored tract shapes now own physical length and continuous interpolation,
  and a tract voice may resample them to a chosen compiled grid;
- `sections`, `nose_sections`, and integer junction indices still leak lowering
  grid choices into the script surface;
- `substeps` still exists as compatibility clock intent, but it should be
  demoted in favor of acoustic length, propagation speed, and fractional delay;
- topology changes can recompile, but morphology motion must remain runtime
  control data.

## Invariants

- PT pressure must become Aqua DSL vocabulary, not a bespoke PT mode.
- A tract voice remains a voice. Tract primitives are treatments, sources,
  curves, junctions, and event lanes that can be combined with the rest of the
  patch graph.
- Diameter/area functions own continuous tract shape over normalized position,
  physical acoustic length, derived areas, reflection coefficients, and
  resampling. Tongue and constriction gestures deform that shape; they do not
  replace the owner.
- PT's 44 oral cells and 28 nasal cells are a reference discretization, not an
  Aqua morphology model.
- Reflection coefficients derive from adjacent areas. Caches and summaries may
  observe that derivation, but must not become independent truth.
- Waveguide clock derives from acoustic length, propagation speed, sample rate,
  and delay approximation. A user-facing `substeps` count is only legacy
  pressure until the fractional-delay waveguide owns this cleanly.
- Waveguide state owns delay-line propagation once it exists. Formant filters
  may remain cheap approximations, but they must be named as approximations.
- Expressive parity comes before audio golf. Audio parity claims require
  rendered PT fixtures, Aqua renders, and log-mel cosine similarity.

## Primitive Decomposition

1. `tract_shape`: reusable section diameter/area function.
   - Owns sampled diameters or areas over normalized continuous tract position.
   - Owns physical acoustic length through `length_cm`.
   - Emits interpolated diameters, resampled grids, areas, reflection
     coefficients, per-cell acoustic delay, and shape summaries.
   - Feeds `tract` voices and later morphology/gesture planning.

2. `glottis`: reusable excitation primitive.
   - Owns frequency, intensity, tenseness, aspiration, and reflection intent.
   - Can feed tract voices or other voice treatments.
   - Implemented as a named source primitive consumed by `tract` voices.

3. `tract_injection`: positioned noise/burst excitation.
   - Owns constriction position, diameter/opening, turbulence, and release
     transient behavior.
   - Can be driven by envelopes, LFOs, learned speech controls, or consonant
     gestures.
   - Implemented as a named injection primitive consumed by `tract` voices and
     distributed into waveguide section updates by position and width.

4. `nasal_junction`: velum-controlled branch primitive.
   - Owns nasal opening and branch shape.
   - Derives the three-way junction coefficients from local areas.
   - Implemented as `nasal_branch` plus generated three-way junction equations
     when a waveguide tract references the branch.

5. `waveguide_tract`: propagation primitive.
   - Owns right/left traveling-wave state, acoustic wave clock, fractional
     delay/loss approximation, and radiation.
   - Consumes area/reflection fields and source/injection events.
   - First oral-tube lowering exists: generated right/left section equations
     consume `tract_shape` reflection coefficients and boundary reflections.
   - Waveguide lowering now derives live per-section diameter targets, areas,
     and reflection coefficients from the tract shape plus tongue, constriction,
     and lip controls before scattering.
     Legacy substep clock intent is represented and lowering consumes it for
     drive/loss scaling, but exact fractional-delay propagation remains missing.

6. `tract_motion`: control-rate slew and obstruction history.
   - Owns diameter/constriction/velum slew rates and obstruction threshold.
   - Feeds smoothed target controls and release-transient detection.
   - Implemented as `tract_motion`; waveguide lowering uses it for control
     slew and stateful obstruction-opening burst pressure.

7. PT parity harness.
   - Renders fixed PT references for vowels, nasals, fricatives, closures, and
     moving tongue/constriction gestures.
   - Renders Aqua DSL reconstructions.
   - Scores log-mel cosine similarity plus targeted feature probes.
   - First fixture catalog exists in `PinkTromboneParityFixtures`.
   - `PinkTromboneReferenceRenderer` now renders deterministic test-only
     traveling-wave references for the fixture controls.
   - `PinkTromboneLogMelParityTests` writes reference/candidate WAVs and report
     artifacts under `artifacts/parity/pink-trombone-logmel/`.

## First Cut

Implemented owners: `tract_shape`, `glottis`, `tract_injection`,
`nasal_branch`, `tract_motion`, and first-pass `propagation=waveguide`.
`tract_shape` owns section diameter/area fields and reflection derivation.
`glottis` owns excitation quality. `tract_injection` owns positioned
frication/burst pressure. The current Faust proxy consumes these primitives
through shape summaries, reflection energy, glottal shaping, and injection
pressure instead of silently inventing all of them inside one helper. The
waveguide lowering consumes the derived oral reflection field as right/left
section state equations.

The first log-mel rung is now real but not flattering. The first compileable
Aqua proxy baseline against the deterministic PT-style renderer landed around
cosine 0.51 open vowel, 0.54 front vowel, 0.39 nasal, 0.12 sibilant, and -0.18
closure-release. That negative closure score correctly exposed the wrong
machine.

The waveguide backend now uses a Faust-friendly state owner:
`wg_loop ~ si.bus(...)`. One feedback component owns the right/left oral and
nasal traveling-wave state instead of hundreds of named recursive equations.
The backend now resamples continuous geometry to an acoustic unit-delay grid
when a waveguide tract does not explicitly request `sections=...`; a 17 cm oral
tract renders as 22 compiled sections at the current 44.1 kHz target instead of
leaking PT's 44 half-sample cells into Aqua as anatomy. Current waveguide
baseline: open vowel 0.6143, front vowel 0.6349, nasal 0.5425, sibilant
0.3744, closure-release -0.0820. Those are pressure readings, not parity. The
vowel/nasal/sibilant cases now respond to the physical clock correction and
runtime index scaling; the closure fixture remains an exposed failure.

The continuous morphology cut has started. `tract_shape length_cm=...` is now
physical geometry, not just a list length. `TractAreaFunction` can interpolate
diameter over normalized position, resample to an arbitrary grid, and derive
per-cell acoustic delay. A `tract` voice may choose a different `sections=...`
lowering grid without requiring the named shape's authoring samples to match.
For a human-length 17 cm tract sampled at PT's 44 oral cells at 44.1 kHz, that
delay is about 0.5 samples per cell. That makes PT's two tract updates per
output sample look like a discretization strategy for a half-sample cell grid,
not a reusable Aqua language concept.

The acoustic graph cut has also started. Aqua now has neutral model records for
`AcousticPath`, `AcousticTerminal`, `AcousticConnection`,
`AcousticSourcePort`, `AcousticBranch`, `AcousticRadiationPort`,
`WaveClockPolicy`, and `AcousticPortNetwork`. Parser commands `path`,
`terminal`, `connect`, `source_port`, `branch`, `radiation_port`,
`wave_clock`, and `acoustic_network` can express a larynx, paired labial
syrinx-like sources, a nasal/oral junction, or stranger source/topology
combinations without adding a species mode. Existing PT-shaped commands now
populate those acoustic records as compatibility aliases: source ports and
radiation ports create typed terminals, while `branch` creates branch endpoint
terminals plus a connection. A first `acoustic` voice command now lowers an
`AcousticPortNetwork` to an audible Faust response proxy, so non-larynx
topologies can be heard before the full waveguide network owns them. Source,
branch, and radiation fields can be bound to parameters and survive into the
emitted Faust, which keeps playground/training knobs attached to declared
acoustic owners instead of dead UI state.

### Graph ownership decision

The topology choice is now explicit: Aqua will use a path graph with typed
terminals. Authoring sugar can hide some ceremony later, but the compiler-facing
shape is geometry plus terminals plus connections. `AcousticBranch` is no
longer the future topology law; it is a side-branch shorthand over the graph.

The first graph lowering cut now compiles typed graphs into bidirectional Faust
segment state. It splits each path at terminal positions, orders connection
ports deterministically, scatters connection groups with area-weighted pressure
continuity, injects source terminals, and sums radiation terminals. Faust
validation passes on a three-path trachea/oral/nasal graph.

The graph compiler now also honors `wave_clock` delay strategy. Segment delay
comes from physical segment length, propagation speed, and `ma.SR`, then lowers
through unit-grid, half-sample, linear fractional, Lagrange, or Thiran/allpass
Faust delay primitives. Co-located terminals on one path now aggregate into a
single graph node, so a source can share an anatomical point with a junction or
boundary without disappearing.

The remaining implementation pressure is now narrower: broaden connection laws
with sharper physical semantics and keep the log-mel parity loop on the graph
renderer. PT-shaped `tract` authoring now uses `propagation=graph` for accepted
parity. The old waveguide lane is not a baseline, pressure lane, or fallback.

Authority map for the required rebuild:

- Owner: the acoustic network lowering must own an ordered scattering graph,
  not `Voice.Tract` and not the response proxy.
- Inputs: continuous paths, typed terminals, connections, source/radiation
  ports, branch sugar, wave-clock policy, and live parameter bindings.
- Outputs: Faust state for delay-line segments, source injection points,
  junction scattering, radiation, and diagnostics.
- Derived state: `tract`/`nasal_branch` become larynx/PT-shaped authoring
  conveniences over the same acoustic graph; the response proxy remains a
  preview renderer only.
- Forbidden writers: `Voice.Tract` must stop being a parallel audio authority
  once graph lowering exists; branch/radiation/source controls must not be
  repaired by a separate playground model.
- Shared paths: direct DSL authoring, PT compatibility commands, future
  syrinx/reed/alien voices, and training/playground parameter bindings must all
  commit through the same acoustic graph.
- Deletion line: do not expand the proxy into a second fake synthesizer.
  `AcousticTerminal`/`AcousticConnection` records now own graph lowering;
  `branch` can remain only as shorthand that emits those records.

Exact Pink Trombone timing still needs pressure, but the next backend cut is no
longer "add more substeps." The graph renderer now has fractional-delay clock
semantics and a `tract propagation=graph` entry point; the remaining parity
question is whether graph-authored PT compatibility can beat the current weak
`Voice.Tract` baseline.

The parity report now renders the graph lane only. The latest static fixture
evidence is useful instead of decorative: open/front/nasal/ma/sibilant/closure
cosine is 0.6252/0.6576/0.6893/0.6550/0.4166/0.5554. The useful levers so far
are source semantics, radiation impedance, terminal-specific admittance,
frication injection, cutting stale first-frame morphology from generated graph
paths, and live path-area control at graph sample points.

The nasal branch pressure exposed the next graph-law issue. The graph now uses
terminal-specific admittance in connection pressure instead of collapsing every
co-located terminal into one node area, and branch output uses a floored
area-ratio pressure normalization so weak side ports no longer receive full
junction pressure. The generated tract graph also stopped treating the first
nasal tube diameter as the velum aperture; velum now owns branch coupling and
aperture. Two tempting compensators were cut: raw per-port outgoing coupling
made supposedly oral fixtures nearly silent, and lip/beak radiation gain did
not restore oral vowel RMS. The internal-node scatter fix made unconnected
injection/source terminals transmit pressure between adjacent segments instead
of acting as dead boundaries; latest utterance cosine is `mama` 0.3365, `papa`
0.8264, and `thrombosis` 0.3874.

Live graph morphology now has a path-level owner. `AcousticPath.AreaControl`
turns tongue, constriction, and lip controls into live diameter/area expressions
at generated graph terminals, and generated `tract propagation=graph` paths
insert neutral area terminals along the oral tract so those live areas actually
scatter waves. This is still sampled continuous morphology, not a resurrection
of PT's fixed cell module.

Frication also had a low-level ownership bug: generated graph `TurbulenceJet`
ports were using burst as source pressure, so sustained sibilants were choked
by a transient control. They now use `max(turbulence, burst)` as pressure while
keeping turbulence as the noise amount. This only nudges the current sibilant
fixture (0.2302 cosine), but the primitive semantics are cleaner: turbulence
owns continuous noisy pressure, burst owns transient pressure.

## Cut Line

The current `Voice.Tract` scalar proxy may survive only as an adapter over real
tract primitives. If a future change adds PT behavior that cannot be described
as a reusable Aqua primitive, cut it or keep it in the reference harness instead
of promoting it to the public DSL.
