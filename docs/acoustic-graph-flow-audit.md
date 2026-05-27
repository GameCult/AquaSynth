# Acoustic Graph Flow Audit

This note compares AquaSynth's current acoustic graph lowering with the
research-backed vocal-tract implementations we are using as pressure:
VocalTractLab, Story/TubeTalker, Julius Smith digital waveguides, ArtiSynth,
Praat/Gnuspeech, and the Pink Trombone/SndKit lineage.

Pink Trombone is a fixture. It is not the architecture.

## Objective

Make Aqua's graph model a reusable physical-acoustic language:

- continuous area functions and path lengths;
- source ports, branch ports, and radiation ports;
- bidirectional wave variables through delay/scatter sections;
- bounded, differentiable controls over geometry, pressure, coupling, and
  openings;
- Faust lowering that preserves sample flow instead of repairing it with output
  gain.

## Research Shape

The research implementations agree on the important split.

- Birkholz/VocalTractLab separates anatomy, gestures, vocal-fold models,
  acoustic simulation, and copy-synthesis controls.
- Story/TubeTalker treats tract length and area functions as the compact bridge
  between anatomy and acoustics. Formants are derived evidence.
- Smith's waveguide work makes the runtime substrate explicit: bidirectional
  delay lines, scattering junctions, loss, fractional delay, and passive
  boundaries.
- ArtiSynth shows the heavier truth-model split: biomechanical body state,
  contact, airflow/acoustics, and rendered sound are separate authorities.
- PT/SndKit is a compact Kelly-Lochbaum-style stress case: oral tube, nasal
  branch, source injection, obstruction history, and radiation.

The reusable owner is therefore not `pink_trombone`. It is an acoustic port
network over continuous morphology.

## What Aqua Can Express

The current DSL can already describe the right objects.

- `AcousticPath` owns tube geometry, length, wave speed, loss, and area control.
- `AcousticTerminal` places typed ports on paths.
- `AcousticConnection` connects terminals into scatter groups.
- `AcousticSourcePort` owns glottal, labial, reed, turbulence, click, and
  synthetic source expressions.
- `AcousticRadiationPort` owns opening-dependent boundary reflection and
  radiated flow.
- `WaveClockPolicy` owns unit, half-sample, and fractional delay lowering.
- `tract_shape`, `glottis`, `tract_injection`, `nasal_branch`, and `tract` are
  authoring conveniences over those neutral records.

That is enough vocabulary to express a larynx, a syrinx, lateral channels, or
stranger branch graphs without adding species-shaped modules.

## Where Lowering Is Struggling

### Multi-Port Branch Junctions

The record shape can express an oral/nasal junction, but the Faust lowering is
still too blunt.

For a research-backed waveguide junction, nasal opening should affect the side
port admittance. The two oral traveling-wave ports should keep clean continuity
through the main tube while the nasal port steals energy according to its area.

Generated PT graphs now move the nasal limit onto the nasal terminal and keep
the connection itself fully coupled. This preserves the physical ownership:
velopharyngeal opening is side-port admittance, not a main-airway clamp.

That change makes the broken downstream law louder. Current utterance fixtures
over-radiate by roughly 3-5x RMS and articulation worsens. This is not evidence
that side-port admittance is wrong; it is evidence that connection scattering
and radiation normalization were relying on the old global damper.

Required cut: connection scattering needs passivity checks and radiation needs
normalization against local port admittance/boundary aperture. Generated nasal
branches must not rely on global connection coupling as a gain patch.

### Transformed Control Bindings

Several physical controls need transforms before reaching graph fields:

- velum diameter to nasal entrance area;
- lip opening to radiation aperture;
- constriction diameter to closure area;
- branch opening to side-port admittance;
- normalized articulator position to emitted graph position.

`ParameterBinding` now carries a small typed transform and scale. The first
kept transform is `Square`, used by generated tract graphs to map live velum
diameter into nasal side-port admittance (`area = k * velum^2 / referenceArea`)
without also damping the whole branch connection.

That is enough to stop one class of sample-flow corruption: a scalar gesture no
longer has to pretend that branch admittance, radiation aperture, and raw
control value have the same dimension. Future transforms should remain typed
and local to field ownership. If a transform cannot state the physical quantity
it converts from and to, it is probably a compensator.

### Closure Pressure Storage

PT's audible plosives are not just noise bursts. They come from obstruction
history and a short pressure injection when a previously closed tract opens.
VTL/ArtiSynth-style thinking pushes the same direction: closures are contact
and pressure events, not decorative source clicks.

Current `AcousticSourcePort.Transient` can emit a short pressure-shaped pulse,
but it is still a local source-port expression. It does not own upstream
pressure storage in the graph state. Graph source evaluation has started moving
into node context: each node now names incident pressure and a node-local source
sum, and generated tract injection source banks are placed at cell centers
rather than directly on graph/radiation nodes. That matches the waveguide-cell
mental model better than terminal-born clicks.

This still is not full pressure storage. A first node-pressure-informed
turbulence release only moved metrics slightly, which says the release source is
now closer to the right layer but still lacks an actual upstream reservoir.

Required cut: graph lowering needs a closure/reservoir primitive that can
store pressure on one side of a severe constriction and release it through the
same scattering path when the constriction opens.

### Radiation Normalization

Radiation now reads boundary flow (`incoming - outgoing`), which is the right
direction, but output level remains sensitive to whether a branch junction is
being damped by the global connection scalar. That means radiation gain is
partly compensating for connection-law behavior.

The current graph now names each radiation terminal's boundary flow and applies
a bounded local admittance term derived from that terminal area. This reduces
the worst over-radiation exposed by the side-branch fix, but it also makes the
remaining failure clearer: speech-band articulation is still weak, especially
for utterances. The radiation blend was softened away from a mostly
differentiated/high-passed flow because the graph was over-bright relative to
its weak vowel body. That helps some closure/thrombosis evidence but does not
fix the missing tract resonance.

Required cut: keep moving radiation toward local port admittance and aperture
physics, but the next audible articulation work probably belongs in pressure
storage/release and passive multi-port scattering rather than output gain.

## Current Invariants

- Path geometry owns area and length.
- Wave clock owns delay floor and fractional-delay family.
- Source ports inject energy; they do not secretly decide topology.
- Radiation ports read boundary flow; they do not own tract filtering.
- Branch opening belongs on side-port admittance, not on the whole connection.
- Live parameter bindings may transform dimensions, but only at field owners
  that can explain the conversion.
- Metrics can expose regressions, but listening and physical plausibility are
  the authority.

## Next Cut

The live implementation now has a named three-port branch law for connection
groups with two same-path ports plus one side-branch port. It is intentionally
kept even though it exposes over-radiation, because it moves ownership to the
physically correct port. The emitted Faust now also names branch
`energy_in`/`energy_out` probes for that junction, so the next pass can inspect
whether the scatter law itself is passive or whether downstream radiation is
the only loudness fault. Ordinary two-port area discontinuities now expose the
same area-weighted energy probes, so vowel-body scattering can be inspected at
the layer where tract resonances should actually form.

By default those probes remain ordinary named Faust locals. When
`FaustExportOptions.DebugProbeUi` is enabled, the same signals are wrapped in
Faust `vbargraph` UI probes under `/debug/...`, exposing passivity and radiation
telemetry without changing the patch's audio output arity. Native streaming
patches can read those bargraph zones after processing blocks, so passivity
checks can sample probe values over time without turning probes into audio
channels.

The first native probe report on the nasal-vowel graph showed same-path and
nasal branch energy ratios essentially at 1.0. That was useful but incomplete:
the original two-port same-path area law was passive while still carrying the
wrong pressure relation for unequal areas. It could conserve a local energy
proxy and still phase-mangle the tract.

Same-path two-port area discontinuities and three-port nasal/side branches now
use one wave convention: Kelly-Lochbaum/PT-style traveling-wave scattering over
area reflection coefficients. The previous pressure-continuity rewrite was a
mistake because the graph's wave variables were not power-normalized pressure
variables. It could look locally conservative and still collapse articulation
into the phone-vibration failure the user heard.

For two-port path nodes the emitted law is now the classic form:
`r = (A_left - A_right) / (A_left + A_right)`,
`right_out = left_in - r * (left_in + right_in)`, and
`left_out = right_in + r * (left_in + right_in)`. Three-port branch nodes use
the matching multi-port form from PT's nasal junction:
`r_p = (2 * A_p - sum(A)) / sum(A)` and
`out_p = r_p * in_p + (1 + r_p) * sum(other inputs)`.

The generated graph clock also now defaults to continuous fractional delay for
`propagation=graph`. Cell-centered source terminals had doubled the segment
count; keeping a half-sample floor silently lengthened the tube. Fractional
delay keeps terminal density from changing morphology, which is the invariant
we need for differentiable tract graphs.

A per-segment Faust low-pass loss experiment was cut. Frequency-dependent wall
loss remains a real missing feature, but one `fi.lowpass` per tiny segment made
parity runs slow and collapsed body energy without improving articulation. A
softer radiation-color experiment was also cut after it worsened `papa`. Those
lessons belong here, not as dead compensators in the emitter.

The latest wave-convention/fractional-clock evidence is:

- static PT graph artifact
  `artifacts/parity/pink-trombone-logmel/20260527T223816035`;
- utterance artifact
  `artifacts/parity/pink-trombone-utterance-logmel/20260527T223931573`;
- probe artifact
  `artifacts/parity/pink-trombone-graph-probes/20260527T204610846`;
- plosive source probe artifact
  `artifacts/parity/pink-trombone-graph-source-probes/20260527T204918624`.

The remaining audible failure is still speech-band articulation: `mama` and
`papa` have weak 500-2500 Hz energy and plosive motion, while closure/release
still lacks a true upstream pressure reservoir. The first `papa` source probe
showed the lip-adjacent plosive source at only about `0.09` peak while the
glottal source was about `3.7`. A temporary release-reservoir boost moved that
plosive source peak to about `0.43`, but after the wave-convention fix it
overdrove `papa` and was cut. The next failure is how stored pressure is shaped
by source impedance, constriction filtering, and radiation. After the
wave-convention fix, `papa` rose to cosine `0.8828` with near-unity RMS, while
`thrombosis` still fails because it goes too silent even though speech-band
ratio is finally present.

The user-listening report after that pass overruled the flattering metric:
`mama` and `papa` are still nearly the same vibrating-phone sound. The harness
now writes utterance separability evidence and fails when candidate utterances
are more mutually similar than the PT references by a wide log-mel margin.
Latest collapse artifact:
`artifacts/parity/pink-trombone-utterance-logmel/20260527T225833392`.
It reports `mama/papa` reference log-mel similarity `0.3791` versus candidate
similarity `0.6069`, with candidate envelope similarity `0.8885`. That is not
speech parity; it is spectral collapse with a nice report.

Source terminals are also no longer topology owners unless they sit on a path
boundary. Interior source ports now attach to the nearest existing graph node
and inject energy there, while non-source terminals own node area/reflection.
This is the right ownership cut: geometry creates junctions; source ports add
energy to traveling waves. The change worsened some output levels because it
removed fake source-created impedance slices, but that is a downstream source,
closure, and radiation problem, not grounds to let source placement mutate the
tract.

Generated lip radiation terminals no longer bake the first frame's lip opening
into terminal `area_scale`. Lip opening already owns live path area and
radiation aperture; letting the terminal keep the initial opening made a word
that starts closed, such as `papa`, carry closed-lip admittance through the
whole utterance. Static artifact
`artifacts/parity/pink-trombone-logmel/20260527T231611586` still passes. The
utterance artifact
`artifacts/parity/pink-trombone-utterance-logmel/20260527T231056242` still
fails: `thrombosis` is too silent, and `mama/papa` candidate similarity is
`0.6343` versus reference `0.3791`. The fix is kept because it removes stale
authority; the remaining failure is now more plainly source/reservoir/radiation
coupling.

The next coherent implementation target is source/boundary coupling, not more
global gain:

1. Add graph-native closure/reservoir state so a sealed constriction stores
   upstream pressure and releases through the same path when opening returns.
2. Audit glottal/source impedance against the corrected pressure law so source
   ports inject flow/pressure into the correct wave variables.
3. Revisit frequency-dependent tract loss with a Faust-friendly formulation
   that does not cascade heavyweight filters across every tiny segment.
4. Keep radiation local to aperture/admittance and boundary flow; do not
   restore a global output scalar as a speech-band patch.

If the graph remains under-articulated after closure storage exists, inspect
the source and boundary probes at the same layer. Do not fix this with another
global gain.
