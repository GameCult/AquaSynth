# Pink Trombone DSL Parity Plan

## Objective

Cover Pink Trombone's useful pipeline with AquaSynth language constructs, then
use Pink Trombone as an audio parity target. Do not build a PT clone with Aqua
paint on it. The primitives must compose with ordinary Aqua voices, envelopes,
LFOs, parameters, layers, learned speech controls, filters, and future
morphologies.

## Current Mechanism

`tract` currently lowers into `Voice.Tract`: a voice-local scalar control bundle
with a Faust source/filter proxy. It is useful as a playground surface and as a
named failure against Pink Trombone, but it does not own the anatomy:

- no reusable tract area function;
- no derived area/reflection coefficient primitive;
- no traveling-wave state owner;
- no nasal junction primitive;
- no waveguide-applied positioned excitation/injection events;
- no substep/rate semantics;
- no parity renderer/score loop against PT output.

## Invariants

- PT pressure must become Aqua DSL vocabulary, not a bespoke PT mode.
- A tract voice remains a voice. Tract primitives are treatments, sources,
  curves, junctions, and event lanes that can be combined with the rest of the
  patch graph.
- Diameter/area functions own static or slowly moving tract shape. Tongue and
  constriction gestures deform that shape; they do not replace the owner.
- Reflection coefficients derive from adjacent areas. Caches and summaries may
  observe that derivation, but must not become independent truth.
- Waveguide state owns delay-line propagation once it exists. Formant filters
  may remain cheap approximations, but they must be named as approximations.
- Expressive parity comes before audio golf. Audio parity claims require
  rendered PT fixtures, Aqua renders, and log-mel cosine similarity.

## Primitive Decomposition

1. `tract_shape`: reusable section diameter/area function.
   - Owns sampled diameters or areas over normalized tract position.
   - Emits diameters, areas, reflection coefficients, and shape summaries.
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
   - Owns right/left traveling-wave state, substep rate, loss, and radiation.
   - Consumes area/reflection fields and source/injection events.
   - First oral-tube lowering exists: generated right/left section equations
     consume `tract_shape` reflection coefficients and boundary reflections.
   - Waveguide lowering now derives live per-section diameter targets, areas,
     and reflection coefficients from the tract shape plus tongue, constriction,
     and lip controls before scattering.
     Substep clock intent is represented and lowering consumes it for drive/loss
     scaling, but exact intra-sample recursive state updates remain missing.

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
The full 44-section oral tract plus 28-section nasal branch renders through
Faust. Current waveguide baseline: open vowel 0.5578, front vowel 0.5368,
nasal 0.4299, sibilant 0.2852, closure-release 0.1147. Those are pressure
readings, not parity, but closure-release is no longer anti-correlated.

Exact Pink Trombone timing still needs pressure: the loop currently performs
one feedback update per output sample while carrying `substeps` as drive/loss
intent. The next backend cut is true twice-per-sample state update inside the
Faust-friendly loop shape, not a return to named equation sprawl.

## Cut Line

The current `Voice.Tract` scalar proxy may survive only as an adapter over real
tract primitives. If a future change adds PT behavior that cannot be described
as a reusable Aqua primitive, cut it or keep it in the reference harness instead
of promoting it to the public DSL.
