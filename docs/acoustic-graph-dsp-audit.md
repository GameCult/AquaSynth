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
the correct broad convention. Generated lip opening now owns path-end geometry
instead of also owning radiation opening.

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

Current state: coherent for the one common case: two same-path ports plus one
side branch. Removing the hidden `0.15` velum damper fixed a real nasal failure.

Smells:

- The implementation recognizes one special topology shape. It is not yet a
  general N-port passive junction.
- Source injection is added per port inside the branch law. That needs a clearer
  impedance story if sources can live at branch nodes.

Verdict: good enough for nasal branches, not enough for arbitrary alien tract
graphs. General N-port scattering should replace the shape-specific path.

### Generic Connection Fallback

What it simulates: arbitrary connection scattering when a connection is not the
recognized three-port branch.

Current state: suspect. It computes a pressure-like expression, applies a
handmade port admittance blend, mixes with bypassed incoming waves, and injects
sources. This is exactly the kind of helper that survives because it is useful,
not because it has a single physical invariant.

Smells:

- `0.6 + 0.4 * sqrt(...)` is an unexplained compensator.
- It mixes pressure-continuity language into a graph whose main convention is
  KL/PT traveling waves.
- It has no passivity proof.

Verdict: highest-priority Jenga. Replace with a general passive scattering law
or restrict supported connections until that exists.

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

Smells:

- The aperture gate constants `0.85`, `0.04`, source gains `0.62`, `1.2`,
  `0.58`, and pressure-drive `0.50 + 1.50 * pressure` are not owned by a named
  physical model.
- Release still detects local control opening, not stored upstream pressure
  behind a sealed constriction.
- There is no graph-native reservoir state.

Verdict: keep as a pressure-informed source, but do not call this plosive
modeling yet. The next real primitive is closure reservoir state connected to
the scattering graph.

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

Current state: less wrong after cutting generated lip double gates. Radiation
now reads boundary flow and generated tract lip opening is path geometry, not
also output aperture.

Smells:

- The 30/70 raw/high-pass blend is a voicing scar. It approximates radiation
  color, but it is not a clear radiation impedance model.
- The admittance expression `sqrt(area / (area + 1.0))` contains an unexplained
  unit constant.
- `RadiationBoundaryReflectionExpression` is useful for standalone radiation
  openings, but dangerous when the path end area already represents aperture.

Verdict: keep boundary-flow readout. Replace color/admittance constants with a
named radiation impedance primitive.

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

1. Generic connection fallback pressure/admittance blend.
2. Unowned radiation constants: 30/70 high-pass blend and `area + 1.0`.
3. Unowned segment contact-loss constant `0.02`.
4. Turbulence/release scalar constants without a named pressure-flow model.
5. Nearest-node source attachment as the only interior source placement model.

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

The next implementation should not be another gain pass. Build a general
passive N-port junction and a closure reservoir primitive:

- N-port junction: one law for any connection group, using each connected port's
  admittance/area, with a passivity probe and no shape-specific fallback.
- Closure reservoir: a stateful pressure store attached to a constriction owner,
  charged while local area is sealed and released through the graph when area
  opens.
- Radiation impedance: replace output coloring constants with a named local
  radiation model.

Until those exist, the graph can imitate some Pink Trombone pressure, but it is
not yet a clean general vocal-acoustic machine.
