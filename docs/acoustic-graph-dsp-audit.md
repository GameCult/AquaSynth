# Acoustic Graph DSP Audit

This audit asks the blunt question for each live graph-voice component:

- What is this simulating?
- Does the implementation still make sense?
- If not, what authority should own the correction?

The answer is mixed. The graph is no longer pure rubble, but several pieces are
still standing because they recently helped a metric rather than because they
have a clean physical contract.

## Pipeline Map

`PatchScript` builds neutral acoustic records:

1. `AcousticPath` owns tract or branch length, rest area, propagation speed,
   loss, and optional live area controls.
2. `AcousticTerminal` marks topology, source, branch, and radiation positions
   on paths.
3. `AcousticConnection` connects terminals into scatter groups.
4. `AcousticSourcePort` owns glottal, turbulence, reed, labial, click, or
   synthetic excitation.
5. `AcousticRadiationPort` owns output boundary behavior.
6. `AcousticPortNetwork` names which paths, terminals, connections, sources,
   radiation ports, and wave clock form one graph.

`FaustExport` lowers that graph into bidirectional delay state:

1. Terminals become graph nodes.
2. Adjacent nodes on each path become bidirectional segments.
3. Each segment stores right-going and left-going traveling waves.
4. Nodes scatter incoming waves into outgoing waves.
5. Source ports add energy at node context.
6. Radiation ports read boundary flow.
7. Segment delays propagate outgoing waves into the next sample state.

That is the right high-level architecture. The weak parts are inside the
lowering laws.

## 2026-05-28 Legacy Vocal Renderer Cut

Owner: `AcousticPortNetwork` and typed graph lowering own vocal acoustics.

Inputs: paths, terminals, connections, source ports, radiation ports, wave
clock, and ordinary Aqua parameter bindings.

Outputs: one Faust graph loop plus named source, scattering, segment delay, and
radiation signals.

Derived state: `tract`, `glottis`, `tract_injection`, and `nasal_branch` are
authoring conveniences that populate the acoustic graph records.

Forbidden writers: the old `Voice.Tract` resonator, the old `Voice.Tract`
waveguide backend, and the acoustic response proxy no longer produce vocal
audio. Legacy `propagation=resonator`, `propagation=waveguide`, and related
aliases now fail at parse time. Invalid graph topology emits silence with a
warning instead of inventing a fallback body.

Deletion line: do not restore a fake vocal renderer to make a test pass. If the
graph cannot express a vocal behavior, add or fix graph semantics.

## 2026-05-28 Source Load And Contact Cut

Owner: graph source ports own source impedance/load interaction; graph contact
terminals own closure pressure storage and release at constrictions.

Inputs: source pressure/tension/opening/noise/balance/impedance, node incident
pressure, contact terminal area, and upstream/downstream traveling waves.

Outputs: load-aware source flow terms, named `_load_pressure` locals for
non-turbulence sources, and named `graph_contact_*` closure/reservoir/release
signals for contact terminals.

Derived state: generated tract area terminals are topology junctions that
derive area from the live tract path geometry; generated contact terminals are
a sparse overlay that owns closure pressure storage and release. Area samples
are no longer contact owners just because they define tube geometry.

Forbidden writers: plosive release is not allowed to live only inside a
TurbulenceJet source expression. A source-local burst can remain as excitation,
but graph contact state now has the authority to store and release pressure at
severe constrictions.

Syrinx proof: `source_port kind=syrinx` is still the generic labial source
kind, now load-aware through the same source impedance path. The checked-in
`patches/advanced/bird-syrinx.aqua` patch uses two labial source ports, two
bronchial paths, one tracheal path, a three-terminal area-scattering merge,
and a beak radiation port. No species module was added.

## 2026-05-29 Sparse Contact Owner

Owner: generated `tract` authoring owns a sparse contact overlay; graph path
lowering owns contact release injection through ordinary two-port scattering.

Inputs: full area-junction grid for tube geometry, up to four generated contact
terminals spread across the tract interior, local terminal area, node incident
pressure, and upstream/downstream traveling waves.

Outputs: named `graph_contact_*` closure/reservoir/release signals only at the
sparse contact owners, while every generated `voices_0_area_*` terminal remains
a `Junction` for geometry/scattering.

Derived state: `voices_0_contact_*` is no longer a second morphology grid. It is
a small collision/pressure-storage overlay derived from tract section count.

Forbidden writers: ordinary area terminals must not decide plosive contact
state. Inline contact occlusion in the scattering equation is not shipped; the
first attempt still overflowed the native Faust debug compiler at eight contact
owners, so contact count is capped at four until occlusion can be expressed as a
smaller compiled primitive or a lower-pressure diagnostic path.

Evidence: opt-in thrombosis probe
`artifacts/parity/pink-trombone-graph-thrombosis-probes/20260529T122217729`
passes with lip-end contact closure peak `0.936876`, contact output peak
`0.547741`, lip boundary load `0.516196`, lip flow `0.585704`, and modal source
out `1.235997`. Accepted utterance diagnostic
`artifacts/parity/pink-trombone-utterance-logmel/20260529T122409541` still fails
articulation: `mama/papa/thrombosis` cosine `0.8302/0.7865/0.3218`, with
thrombosis RMS ratio `0.0770`, active ratio `0.3151`, and silence mismatch
`0.7205`.

## 2026-05-29 Compact Generated Tract Graph

Owner: generated `tract` authoring owns the translation from high-resolution
tract morphology into a Faust-sized graph. `VocalTract.Sections` still owns
morphology and index semantics; generated acoustic terminals/source ports own
only the compiled approximation.

Inputs: full tract area function, tract section count, live tongue/constriction
and lip controls, nasal branch position, and injection position/width.

Outputs: at most ten generated oral area junctions, at most eight generated
injection source ports, and at most four generated contact terminals. Source
position weighting is widened to the compact source spacing so a moving
constriction remains represented without one source port per tract cell.

Derived state: generated `voices_0_area_*` and `voices_0_inj_*` records are no
longer a one-record-per-section discretization. They are backend lowering
samples of the continuous tract owner.

Forbidden writers: the Faust backend must not be forced to compile the full
authoring grid. If a future change needs more articulatory precision, it should
improve the continuous area/source/contact heuristics or add a named compact
primitive, not restore a 44-node/43-source generated graph by default.

Evidence: latest opt-in thrombosis debug DSP
`artifacts/parity/pink-trombone-graph-thrombosis-probes/20260529T125048965`
dropped from `1652` lines to `532`: source-related graph lines `687 -> 162`,
segment-area lines `211 -> 56`, next-state lines `89 -> 27`, and area-reflection
lines `74 -> 12`. The opt-in native graph probe passed in about `1m15s`.
Accepted utterance diagnostic
`artifacts/parity/pink-trombone-utterance-logmel/20260529T125227926` passed in
about `1m33s`; `mama/papa/thrombosis` cosine is `0.8324/0.7730/0.3176`.
Thrombosis RMS improved to `0.1715`, but active ratio fell to `0.1598` and
silence mismatch rose to `0.7811`, so articulation remains the live failure.

Follow-up: source placement now uses a raised-cosine kernel instead of a hard
triangular weight over the compact source bank. It keeps the same graph size
while making moving injection ownership continuous enough for DSP. Artifact
`artifacts/parity/pink-trombone-utterance-logmel/20260529T140038717` nudges
thrombosis active ratio to `0.1826`, silence mismatch to `0.7643`, and
speech-band ratio to `0.2704`; still not accepted, but the improvement came
without restoring discrete source density.

Follow-up: generated lip radiation now mirrors live tract lip opening into
`/acoustic/radiation/*/opening`. Before this, path-end area followed the mouth
gesture but the radiation aperture stayed at its default, splitting ownership
between geometry and output. Artifact
`artifacts/parity/pink-trombone-utterance-logmel/20260529T141822464` improves
`papa` RMS ratio `0.3242 -> 0.8369`, cosine `0.7725 -> 0.7996`, and motor-band
ratio `0.5238 -> 0.0555`, which matches the listening complaint that plosives
were drum hits. `mama` remains wrong and `thrombosis` remains mostly absent, so
the next vowel cut is tract/radiation color and vowel body, not more plosive
gain.

## Component Audit

### Topology Construction

What it simulates: fixed graph discretization of acoustic paths, with topology
terminals defining scattering locations.

Current state: mostly coherent. Interior source terminals no longer create
topology nodes; they attach to existing graph nodes. That fixed a real authority
leak.

Smells:

- Interior sources attach to the nearest node, not an interpolated point inside
  a segment. Generated tract source banks compensate by creating many fixed
  source terminals. That is expressive but heavy.
- A node can effectively belong to one connection through `nodeConnection`.
  More complex graph topologies may need a first-class multi-connection node
  model instead of assuming one branch connection owns the node.
- `terminalConnection` is built but not used. That is small rot.

Verdict: keep the model, but source placement wants an interpolated injection
primitive before this becomes a clean alien-tract substrate.

### Area And Geometry

What it simulates: time-varying tract cross-sectional area derived from rest
diameter plus tongue, constriction, and lip controls.

Current state: useful but simplistic. Diameter is squared into area, which is
the correct broad convention. Generated lip opening now owns both path-end
geometry and radiation aperture.

Smells:

- Tongue/lip shaping is a hand-authored Gaussian blend, and constriction is a
  min-like clamp. This is a serviceable control surface, not a principled
  articulatory model.
- Graph propagation does not use `tract_motion` slew. Utterance fixtures smooth
  controls externally, but generic graph voices can move morphology abruptly.
- Node area sums co-located non-source terminal areas. That is right for some
  admittance joins and suspicious for ordinary labels sharing a location.

Verdict: acceptable as a compact morphology surface, but motion/smoothing must
move into graph area ownership instead of living only in fixture curves or the
old tract proxy.

### Wave Clock And Delay

What it simulates: travel time through each path segment.

Current state: conceptually correct. Segment delay follows physical length,
speed of sound, and the selected fractional-delay strategy.

Smells:

- Audio-rate delay modulation from live morphology can hit Faust fractional
  delay primitives directly. That is flexible, but the graph has no explicit
  policy for smoothing delay changes or preserving passivity during fast
  topology-scale motion.
- Minimum-delay policy is hidden in `WaveClockMinimumDelay`; it deserves clearer
  documentation per strategy.

Verdict: keep fractional delay. Add explicit delay-smoothing/passivity policy
before trusting violent morphology animation.

### Two-Port Path Scattering

What it simulates: Kelly-Lochbaum area-discontinuity scattering between two
adjacent tube sections.

Current state: coherent. The graph now uses the PT/Kelly-Lochbaum traveling-wave
convention:

`r = (A_left - A_right) / (A_left + A_right)`.

Smells:

- Source injection is added equally to both outgoing directions at a two-port
  node. That is plausible as a symmetric pressure/flow source only when the
  source is actually centered at the discontinuity. It is not yet a general
  source impedance law.

Verdict: keep the scatter law. Revisit source injection weighting separately.

### Three-Port Branch Scattering

What it simulates: PT-style oral/nasal side branch scattering.

Current state: replaced. The old recognized three-port branch path has been
collapsed into the same general N-port scatter law used for every area
scattering connection.

Smells:

- Source injection is added per port inside the branch law. That needs a clearer
  impedance story if sources can live at branch nodes.
- The new general law still uses scalar terminal area/admittance, not a
  frequency-dependent junction impedance.

Verdict: keep the generalization. It removes the PT-shaped topology shortcut,
but source impedance at junction nodes still needs a named model.

### Generic Connection Fallback

What it simulates: arbitrary connection scattering when a connection is not the
recognized three-port branch.

Current state: cut. The lowering no longer emits
`graph_connection_pressure_*`, no longer uses the handmade
`0.6 + 0.4 * sqrt(...)` port-admittance blend, and no longer keeps a
shape-specific three-port branch path. Area-scattering connections now emit one
passive N-port traveling-wave law:

`r_p = (2 * A_p - sum(A)) / sum(A)`

`out_p = r_p * in_p + (1 + r_p) * sum(other inputs)`

Connection coupling now crossfades between that scattered wave and the local
incoming wave. Bypass remains explicitly bypass.

Smells:

- Port area selection is still simple: ordinary isolated junction labels use
  adjacent segment area, while branch/radiation/source terminals use their
  declared terminal area. That is coherent enough for the present graph, but it
  should be documented as terminal admittance ownership before more exotic
  junctions rely on it.
- Source injection is still added as node pressure energy after scattering,
  divided by local node port count. That is not a full source impedance law.

Verdict: the highest-priority fallback Jenga is gone. Keep passivity probes on
this law while cutting source impedance and closure storage next.

### Glottal Source

What it simulates: voiced excitation at a source boundary.

Current state: better after local `tanh` normalization. Before that, the source
could dominate the tract as an overdriven buzzer.

Smells:

- The waveform is still a hand-shaped proxy, not an LF/Rosenberg model with
  named open quotient, speed quotient, and return phase.
- `opening` is used as waveform skew in generated glottis records. That field
  name is too generic for what it does here.
- Source injection bypasses boundary impedance; it is simply added into node
  outgoing waves.

Verdict: acceptable as a bounded placeholder. Rename or split glottal shape
fields, then implement a real glottal flow source with impedance coupling.

### Turbulence And Release Source

What it simulates: pressure-driven constriction noise and a short opening
transient.

Current state: partially coherent. Sustained turbulence now has a local
pressure-drive term, and release pressure can observe node incident pressure.
Graph turbulence lowering now emits named closure, release, reservoir,
pressure-drive, gate, burst, and release-pulse locals instead of hiding the
whole pressure-flow model inside one source expression.

Smells:

- The aperture gate constants `0.85`, `0.04`, source gains `0.62`, `1.2`,
  `0.58`, and pressure-drive `0.50 + 1.50 * pressure` are not owned by a named
  physical model. They are now at least localized in graph source lowering.
- Release still detects local control opening and stores pressure at the source
  owner, not as a true two-sided constriction/contact state inside the scatter
  graph.
- There is no explicit upstream/downstream reservoir split across a sealed
  constriction.

Verdict: keep as a named pressure-informed source. Do not call this plosive
modeling yet. The next real primitive is a closure/contact owner that can store
pressure on one side of a severe constriction and release it through the
scattering graph.

### Segment Loss

What it simulates: propagation loss plus extra loss in narrow/contact-like
sections.

Current state: directionally correct and audibly important. It stopped closed
sections from preserving wave energy as immortal buzz.

Smells:

- The `0.02` area constant is an exposed scar. It says "area-dependent loss"
  but not whose units or morphology it belongs to.
- Loss is amplitude-only. There is no frequency-dependent wall/radiation loss
  model.

Verdict: keep the authority, parameterize it. Path or material records should
own contact loss scale and frequency-dependent wall loss.

### Radiation

What it simulates: boundary flow emitted from lip, nostril, beak, or other
openings.

Current state: less wrong after cutting generated lip double gates and moving
the graph output constants into one named radiation impedance expression.
Radiation now reads boundary flow, generated tract lip opening is path
geometry, and emitted graph Faust names per-port reference area,
differentiation, high-pass cutoff, admittance, and flow.

Smells:

- The graph no longer has inline `30/70` raw/high-pass blend or `area + 1.0`
  admittance denominator, but the helper still uses compact empirical kind
  constants. Those are now in one organ, not scattered through graph lowering.
- `RadiationBoundaryReflectionExpression` is useful for standalone radiation
  openings, but dangerous when the path end area already represents aperture.
- Radiation still has no frequency-dependent load matched to tube radius and
  characteristic impedance.

Verdict: keep boundary-flow readout and the named primitive. Replace the
empirical primitive with a radius/characteristic-impedance model once source
and closure storage stop dominating the audible failure.

### Generated PT-Style Tract

What it simulates: a compact human vocal tract morphology expressed through
neutral graph records.

Current state: much healthier. It no longer relies on PT-shaped runtime modules,
and the accepted utterance smoke now passes without cross-word collapse.

Smells:

- It still generates many area terminals and many source terminals. That is a
  compiled approximation of continuous morphology, not a beautiful abstraction.
- Nasal and lip reflection both bind to `lip_reflection`, matching PT pressure
  but not a general anatomy model.
- `tract_motion` is not applied to graph area controls.

Verdict: good pressure fixture, not final architecture. Continuous/interpolated
source and area sampling would make it less PT-grid-shaped.

### Tests And Metrics

What they simulate: falsification pressure, not hearing.

Current state: better. Cross-utterance separability caught the metric lie.
Static closure-release no longer pretends a sealed static constriction has
time-history release energy.

Smells:

- Smoke floors are still survival rails, not acceptance criteria.
- Artifact WAVs are generated, but the harness has no durable listening verdict.
- Speech-band/articulation metrics are still weak proxies.

Verdict: keep the new separability check. Add windowed phone/gesture reports as
first-class artifacts because they exposed the nasal and rounded-vowel failures
faster than global scores.

## Cut List

Cut or replace these before piling on more tuning:

1. Unowned segment contact-loss constant `0.02`.
2. Turbulence/release scalar constants without a named pressure-flow model.
3. Nearest-node source attachment as the only interior source placement model.
4. Any new vocal feature added before the primitive-collapse and flow-probe
   plan in `docs/acoustic-graph-proprioception.md`.

Cut in the 2026-05-28 N-port pass:

- Generic connection fallback pressure/admittance blend.
- Shape-specific three-port branch scattering path.

Cut in the 2026-05-28 radiation pass:

- Inline graph radiation `30/70` raw/high-pass blend.
- Inline graph radiation `area + 1.0` admittance denominator.

## Keep List

These foundations deserve to stay:

1. Path graph with typed terminals.
2. KL/PT traveling-wave convention for two-port and nasal branch scatter.
3. Source terminals not owning topology.
4. Velum as side-port area, not global branch damping.
5. Generated lip opening as path-end geometry, not output double gate.
6. Area-dependent segment loss as a named owner, once parameterized.
7. Cross-utterance separability as a hard guardrail.

## Next Coherent Build

The next implementation should not be another gain pass. The general passive
N-port junction now exists; the remaining build targets are closure storage and
radiation impedance:

- Closure reservoir: a stateful pressure store attached to a constriction owner,
  charged while local area is sealed and released through the graph when area
  opens.
- Radiation impedance: upgrade the named empirical local model to a
  radius/characteristic-impedance model.

Until those exist, the graph can imitate some Pink Trombone pressure, but it is
not yet a clean general vocal-acoustic machine.

## 2026-05-28 N-Port Connection Cut

The first audit cut replaced connection fallback lowering with one general
area-scattering law for all non-bypass acoustic connections. This removes two
old authorities at once: the pressure-like fallback expression and the
PT-shaped three-port branch recognizer.

Latest utterance artifact:
`artifacts/parity/pink-trombone-utterance-logmel/20260528T113334390`.
The change keeps smoke tests passing. `thrombosis` improves slightly to cosine
`0.4555`, while `mama/papa` remain differentiated (`candidateLogMel=0.2469`
versus reference `0.3791`) but still under-articulated and quiet.

Latest static artifact:
`artifacts/parity/pink-trombone-logmel/20260528T113334415`.
Static fixtures still pass; `closure-release` remains sealed-static evidence
with peak `0.000009`.

This is not a victory lap. It is a floor repair. The next real smells are still
radiation impedance constants, graph-native closure reservoir state, and source
impedance at junction nodes.

## 2026-05-28 Radiation Primitive Cut

The second audit cut moved graph radiation coloring and admittance into a named
local primitive. The emitter now names per-radiation-terminal:

- `graph_radiation_reference_area_*`
- `graph_radiation_differentiation_*`
- `graph_radiation_highpass_*`
- `graph_radiation_admittance_*`
- `graph_radiation_flow_*`

This removes the inline `flow * 0.30 + highpass(flow) * 0.70` expression and
the inline `area + 1.0` admittance denominator from graph lowering. The current
primitive is still empirical, but it has one owner.

Latest utterance artifact:
`artifacts/parity/pink-trombone-utterance-logmel/20260528T113949841`.
`mama/papa/thrombosis` cosine is `0.7648/0.8550/0.4556`. `mama/papa`
candidate separability is `0.2044` versus reference `0.3791`, so this cleanup
does not fix articulation.

Latest static artifact:
`artifacts/parity/pink-trombone-logmel/20260528T113949813`.
Static fixtures still pass; `closure-release` remains sealed-static evidence
with peak `0.000011`.

The conclusion is plain: radiation needed an owner, but the voice still needs
graph-native closure pressure storage and source impedance before it will stop
leaning on boundary color.

## 2026-05-28 Graph Source Reservoir Naming

The third audit cut did not change the intended source math. It moved graph
turbulence/release lowering out of one dense expression and into emitted
per-source locals:

- `_closure`
- `_release`
- `_reservoir`
- `_pressure_drive`
- `_release_pressure`
- `_noise_gate`
- `_release_gate`
- `_sustained`
- `_burst_noise`
- `_release_pulse`

Latest utterance artifact:
`artifacts/parity/pink-trombone-utterance-logmel/20260528T114618213`.
Latest static artifact:
`artifacts/parity/pink-trombone-logmel/20260528T114618202`.
Metrics match the prior radiation-primitive pass, which is the expected result:
this was an ownership/observability cut, not a gain change.

The next change can now target the reservoir/source-impedance law directly.
The remaining bad smell is not that the source code is unreadable; it is that
the model still has only a source-local reservoir instead of a contact-aware
upstream/downstream pressure state.

## 2026-05-28 Reservoir Charging Sign

The source-local closure reservoir now charges from positive local pressure
only:

`max(0, incident_pressure) * closure`

The previous `abs(incident_pressure) * closure` let alternating buzzer pressure
charge a plosive reservoir equally in both phases. That is not pressure storage;
it is rectified vibration. The new law is more physical, but current parity
metrics did not move:

- utterance artifact
  `artifacts/parity/pink-trombone-utterance-logmel/20260528T114903720`;
- static artifact
  `artifacts/parity/pink-trombone-logmel/20260528T114953503`.

Keep the sign fix. The non-result strengthens the diagnosis: the real missing
primitive is still a contact-aware upstream/downstream closure state, not a
source-local scalar reservoir.

## 2026-05-28 Directed Reservoir Drive

For turbulence/release sources attached to ordinary two-port path nodes, the
reservoir now charges from directed upstream-minus-downstream pressure:

`max(0, upstream_incoming - downstream_incoming)`

Boundary or non-two-port source nodes fall back to positive local incident
pressure. This lets a source-local plosive at least read the graph's traveling
wave direction instead of only a node-average pressure.

Latest utterance artifact:
`artifacts/parity/pink-trombone-utterance-logmel/20260528T115452268`.
Latest static artifact:
`artifacts/parity/pink-trombone-logmel/20260528T115719882`.

Metrics still do not move. That is useful and damning: the source-local
reservoir sign/direction is no longer the main missing piece. The graph needs a
contact/closure primitive that participates in scattering and flow, not a
separate source pulse hoping the node will turn it into articulation.

## 2026-05-29 Radiation Boundary Load And Node-Law Cut

Radiation terminals are no longer output-only observers at one-port boundaries.
For a boundary radiation node, graph lowering now emits a named
`graph_radiation_boundary_load_*` value from the radiation admittance,
differentiation, and loss. The reflected boundary wave is filtered before it
returns to the delay line:

`reflected = raw * (1 - load) + lowpass(raw, radiation_cutoff) * load`

The output still reads boundary flow as `incoming - outgoing`, but the outgoing
wave now carries radiation load back into the tract. This is not a full
frequency-dependent radiation impedance model. It is the first graph-owned
load term that changes the pressure field instead of only coloring the output.

The remaining unconnected multi-port node fallback was also cut. Multi-port
nodes not owned by an explicit connection now use the same area-scattering
wave convention and emit node area-energy probes. The old
`graph_node_pressure_*` expression should not reappear as a quiet fallback.

Latest graph probe:
`artifacts/parity/pink-trombone-graph-thrombosis-probes/20260529T112805836`.
The new lip boundary load peaks at `0.516196`, lip flow remains alive at
`0.586863`, modal source out is `1.234403`, and the sibilant area-34 source
out is `0.099318`.

Latest utterance artifact:
`artifacts/parity/pink-trombone-utterance-logmel/20260529T112806745`.
The accepted-utterance diagnostic still fails articulation. `thrombosis`
cosine is `0.3287`, RMS ratio is `0.0770`, active ratio is `0.3151`, and
silence mismatch is `0.7205`. This confirms the boundary-load cut is a
coherence repair, not the missing speech primitive.

Next pressure remains contact as constrained-flow history and source impedance
as a port law. Do not interpret this pass as permission to restore global
drive or output EQ.
