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

The current generated PT graph still uses `AcousticConnection.Coupling` as a
global connection scalar. That scalar damps every port in the connection,
including the main oral path. It accidentally prevents flooding, but it also
means velopharyngeal coupling is partly acting as a main-airway clamp.

A test cut moved the limit onto the nasal terminal and restored full connection
coupling. That is closer to the research shape, but the current connection law
then over-radiated the body by roughly 3-5x RMS on the utterance fixtures. Even
shrinking nasal admittance did not fix it. The failure is therefore in the
multi-port lowering/radiation normalization, not in the DSL's ability to name
the anatomy.

Required cut: connection scattering needs per-port admittance authority and
passivity checks. Generated nasal branches should not rely on global connection
coupling as a gain patch.

### Transformed Control Bindings

Several physical controls need transforms before reaching graph fields:

- velum diameter to nasal entrance area;
- lip opening to radiation aperture;
- constriction diameter to closure area;
- branch opening to side-port admittance;
- normalized articulator position to emitted graph position.

`ParameterBinding` currently mirrors a parameter path into a field path without
a transform. That works for simple scalar fields, but it cannot express
`area = velum^2 / referenceArea` or a bounded softplus-style admittance. Some
generated records therefore bake a useful initial scale and then lose that
scale when a live parameter binding replaces it.

Required cut: either add typed transformed controls for graph fields, or move
these transforms into the lowering functions that already know terminal kind
and generated role.

### Closure Pressure Storage

PT's audible plosives are not just noise bursts. They come from obstruction
history and a short pressure injection when a previously closed tract opens.
VTL/ArtiSynth-style thinking pushes the same direction: closures are contact
and pressure events, not decorative source clicks.

Current `AcousticSourcePort.Transient` can emit a short pressure-shaped pulse,
but it is still a local source-port expression. It does not own upstream
pressure storage in the graph state. A post-commit reservoir experiment was
stable but inaudible because it lived outside the actual wave variables and did
not change the local boundary state.

Required cut: graph lowering needs a closure/reservoir primitive that can
store pressure on one side of a severe constriction and release it through the
same scattering path when the constriction opens.

### Radiation Normalization

Radiation now reads boundary flow (`incoming - outgoing`), which is the right
direction, but output level remains sensitive to whether a branch junction is
being damped by the global connection scalar. That means radiation gain is
partly compensating for connection-law behavior.

Required cut: radiation should be normalized against local port admittance and
boundary aperture, not against whatever gain happens to survive connection
scattering.

## Current Invariants

- Path geometry owns area and length.
- Wave clock owns delay floor and fractional-delay family.
- Source ports inject energy; they do not secretly decide topology.
- Radiation ports read boundary flow; they do not own tract filtering.
- Branch opening belongs on side-port admittance, not on the whole connection.
- Metrics can expose regressions, but listening and physical plausibility are
  the authority.

## Next Cut

The next coherent implementation target is a passive three-port branch law:

1. Build a named lowering path for connection groups with two same-path ports
   plus one branch port.
2. Use segment areas for the two through-path ports and terminal area for the
   side port.
3. Apply branch opening/coupling to the side-port admittance only.
4. Keep the main path continuous when side admittance approaches zero.
5. Assert this with generated Faust structure tests before re-golfing PT
   utterances.

If that law still floods, radiation normalization should be audited at the same
layer. Do not fix this with another global gain.
