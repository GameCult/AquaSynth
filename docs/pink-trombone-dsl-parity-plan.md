# Pink Trombone DSL Parity Plan

## Objective

Cover Pink Trombone's useful pipeline with AquaSynth language constructs, then
use Pink Trombone as an audio parity target. Do not build a PT clone with Aqua
paint on it. The primitives must compose with ordinary Aqua voices, envelopes,
LFOs, parameters, layers, learned speech controls, filters, and future
morphologies.

## Current Mechanism

`tract` currently lowers into a voice-local scalar control bundle plus a typed
`AcousticPortNetwork`. The graph is the only vocal-tract audio authority. The
old source/filter proxy and old tract waveguide backend are removed; legacy
propagation names now fail fast instead of quietly rendering a fake voice. The
remaining anatomy ownership is still too discrete:

- authored tract shapes now own physical length and continuous interpolation,
  and a tract voice may resample them to a chosen compiled grid;
- `sections`, `nose_sections`, and integer junction indices still leak lowering
  grid choices into the script surface;
- `substeps` is no longer a live lowering contract; acoustic length,
  propagation speed, and fractional delay own timing;
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
- Wave clock derives from acoustic length, propagation speed, sample rate, and
  delay approximation. User-facing substeps must not own morphology timing.
- The typed acoustic graph owns delay-line propagation. Formant filters and
  tract proxies are not valid fallbacks for voice parity.
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
   - Implemented as named graph source ports consumed by `tract` voices and
     distributed over cell-centered source terminals by position and width.

4. `nasal_junction`: velum-controlled branch primitive.
   - Owns nasal opening and branch shape.
   - Derives the three-way junction coefficients from local areas.
   - Implemented as `nasal_branch` sugar over acoustic path terminals and a
     graph connection.

5. `acoustic_graph`: propagation primitive.
   - Owns right/left traveling-wave state, acoustic wave clock, fractional
     delay/loss approximation, and radiation.
   - Consumes area/reflection fields and source/injection events.
   - Graph lowering splits paths at typed terminals, emits bidirectional
     segment state, and scatters two-port and N-port junctions from live area
     and admittance.
   - Tract graph generation derives live per-terminal area from the tract shape
     plus tongue, constriction, and lip controls before scattering.

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
`nasal_branch`, `tract_motion`, and graph-native `propagation=graph`.
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

The old `wg_loop ~ si.bus(...)` backend is now commit history, not a live
lowering. Graph output uses `graph_loop ~ si.bus(...)`, physical segment
lengths, and fractional delay. Generated tract graphs preserve declared
morphology unless the author explicitly asks for another compiled grid.

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
combinations without adding a species mode. Existing PT-shaped commands
populate those acoustic records as authoring aliases: source ports and
radiation ports create typed terminals, while `branch` creates branch endpoint
terminals plus a connection. `acoustic` and `tract` voices lower through the
same compiled graph; missing or invalid graph topology renders silence with a
warning rather than falling back to a pretend tract.

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

- Owner: the acoustic network lowering owns an ordered scattering graph, not
  `Voice.Tract`, not the removed tract waveguide, and not a response proxy.
- Inputs: continuous paths, typed terminals, connections, source/radiation
  ports, branch sugar, wave-clock policy, and live parameter bindings.
- Outputs: Faust state for delay-line segments, source injection points,
  junction scattering, radiation, and diagnostics.
- Derived state: `tract`/`nasal_branch` are larynx/PT-shaped authoring
  conveniences over the same acoustic graph.
- Forbidden writers: `Voice.Tract`, legacy propagation modes, and response
  proxies must not produce alternate vocal audio; branch/radiation/source
  controls must not be repaired by a separate playground model.
- Shared paths: direct DSL authoring, PT compatibility commands, future
  syrinx/reed/alien voices, and training/playground parameter bindings must all
  commit through the same acoustic graph.
- Deletion line: do not expand the proxy or revive the old waveguide into a
  second fake synthesizer. `AcousticTerminal`/`AcousticConnection` records own
  graph lowering; `branch` can remain only as shorthand that emits those
  records.

Exact Pink Trombone timing still needs pressure, but the next backend cut is no
longer "add more substeps." The graph renderer now has fractional-delay clock
semantics and a `tract propagation=graph` entry point; the remaining parity
question is whether graph-authored PT compatibility can beat the current weak
`Voice.Tract` baseline.

The parity report now renders the graph lane only. The latest static fixture
evidence is useful instead of decorative: open/front/nasal/ma/sibilant/closure
cosine is 0.6067/0.6573/0.6871/0.6476/0.4502/0.6168. The useful levers so far
are source semantics, radiation impedance, terminal-specific admittance,
frication injection, cutting stale first-frame morphology from generated graph
paths, and live path-area control at graph sample points.

The nasal branch pressure exposed the next graph-law issue. The graph now uses
terminal-specific admittance in connection pressure instead of collapsing every
co-located terminal into one node area, and branch output uses a floored
area-ratio pressure normalization so weak side ports no longer receive full
junction pressure. The generated tract graph also stopped treating the first
nasal tube diameter as the velum aperture; velum now owns branch entrance area
and nasal aperture, while connection coupling remains a structural admittance
limit. Two tempting compensators were cut: raw per-port outgoing coupling
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

The graph source law now separates sustained aperture noise from closure
release. `AcousticSourcePort.Transient` owns release energy, while sustained
`TurbulenceJet` noise is gated by the intersection of the old upper constriction
cap and a narrow-open aperture window. Closed constrictions no longer hiss just
because they are closed. Latest accepted utterance graph smoke is `mama` 0.3339,
`papa` 0.8320, and `thrombosis` 0.3957 under
`artifacts/parity/pink-trombone-utterance-logmel/20260527T110608640`.

Moving source position is now graph-native too. `AcousticSourcePositionControl`
weights fixed source terminals from a live index/width/scale expression, and
generated `tract propagation=graph` injection emits an oral source-port bank
instead of one stale terminal. This keeps the graph topology Faust-friendly
while letting frication and release energy follow the articulator. Latest
accepted utterance graph smoke is `mama` 0.3418, `papa` 0.8318, and
`thrombosis` 0.4521 under
`artifacts/parity/pink-trombone-utterance-logmel/20260527T111349174`.

The first listening-led anti-buzz cut changed the glottal source, not the tract
topology. A direct LF coefficient expression improved some cosine scores but
overdrove the recursive graph and broke the static open-vowel fixture, so it
was cut. The kept graph source adds a mild cubic waveshaping component to the
existing glottal primitive. Latest accepted utterance graph smoke is `mama`
0.4822, `papa` 0.8602, and `thrombosis` 0.5202 under
`artifacts/parity/pink-trombone-utterance-logmel/20260527T114901634`. The band
probe still shows weak 500-1000 Hz vowel-body energy, so this is not the final
answer to the smartphone-vibration complaint. It only proves source excitation
was part of the failure; tract scattering/radiation remains the live pressure.

The next successful anti-buzz cut moved ordinary same-path interior nodes from
transparent pressure averaging to signed two-port area-discontinuity scattering.
The first version used a midpoint admittance pressure law and collapsed `mama`;
the kept version uses adjacent segment areas with the reflection sign that
matches the graph's right/left state convention. Latest accepted utterance
graph smoke is `mama` 0.3262, `papa` 0.8567, and `thrombosis` 0.5762 under
`artifacts/parity/pink-trombone-utterance-logmel/20260527T120741887`. The band
probe now shows `papa` with roughly half its energy in the 500-1000 Hz body
band instead of mostly 80-200 Hz vibration. `mama` still lacks enough
500-1000 Hz vowel body, so nasal/oral coupling and radiation remain live
pressure.

The next radiation cut moved aperture out of the output-only lane. Lip/beak
opening now blends graph boundary reflection between a near-closed termination
and the declared open-end reflection. Radiation reads boundary flow
(`incoming - outgoing`) rather than raw incoming pressure, then applies a
stronger radiation slope. Latest accepted utterance smoke is `mama` 0.5650,
`papa` 0.8613, and `thrombosis` 0.4347 under
`artifacts/parity/pink-trombone-utterance-logmel/20260527T122757617`; latest
static smoke is open/front/nasal/ma/sibilant/closure
0.6088/0.6826/0.6468/0.6153/0.4158/0.6198 under
`artifacts/parity/pink-trombone-logmel/20260527T123141625`. This is a real
authority fix, not the final mouth. The remaining articulation failure points
at pressure storage/release around severe constrictions and better nasal/oral
coupling, not another output color patch.

A small glottal-brightness pass then added higher modal harmonics to the
reusable glottal source rather than adding a PT-only exciter. The post-flow
radiation normalization now uses `4.0`, a middle point that improves utterance
RMS without pushing static closure into the soft-clip guardrail. Latest
accepted utterance smoke is `mama` 0.5712, `papa` 0.8659, and `thrombosis`
0.4379 under
`artifacts/parity/pink-trombone-utterance-logmel/20260527T124230804`; static
smoke remains passing under
`artifacts/parity/pink-trombone-logmel/20260527T124311305`. This helps the
voiced fixtures a little, but it is not the missing articulation mechanism.

Log-mel cosine is no longer allowed to impersonate a speech-parity verdict.
It rewards spectral overlap even when the candidate is mostly silence, filtered
motor buzz, or a few breathy/squeaky bursts. `AudioComparison` now includes an
`AudioArticulationComparison` with envelope cosine, active-frame ratio, silence
mismatch, envelope/spectral flux ratios, motor-band ratio, speech-band ratio,
and an articulation score. The utterance report prints a verdict. Latest run:
`mama`, `papa`, and `thrombosis` are all
`not-accepted-articulation` under
`artifacts/parity/pink-trombone-utterance-logmel/20260527T130026316`, despite
their old cosine values. That is the harness telling the truth again.

The next physically grounded cut fixed graph tract timing. Segment delay is no
longer clamped to a full sample before wave-clock lowering; the wave-clock
strategy owns the minimum legal delay. Generated `tract propagation=graph`
patches now use `HalfSampleGrid`, matching the half-sample Kelly-Lochbaum
regime that PT approximates by updating the tract twice per audio sample. This
removed an accidental overlong-tract crutch and improved several witnesses,
especially `thrombosis` motor-band ratio. Latest accepted utterance smoke is
`mama` 0.5982 / articulation 0.1952, `papa` 0.8615 / 0.1979, and `thrombosis`
0.5217 / 0.2640 under
`artifacts/parity/pink-trombone-utterance-logmel/20260527T133539554`. The
voice is still not accepted; the remaining failure is body/radiation and
pressure-release behavior, not tract length.

## Cut Line

The current `Voice.Tract` scalar proxy may survive only as an adapter over real
tract primitives. If a future change adds PT behavior that cannot be described
as a reusable Aqua primitive, cut it or keep it in the reference harness instead
of promoting it to the public DSL.
