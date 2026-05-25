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
   - Implemented as a named injection primitive consumed by `tract` voices; it
     is not yet injected into waveguide cell state.

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
     True substep execution and cell-local injection remain missing.

6. PT parity harness.
   - Renders fixed PT references for vowels, nasals, fricatives, closures, and
     moving tongue/constriction gestures.
   - Renders Aqua DSL reconstructions.
   - Scores log-mel cosine similarity plus targeted feature probes.

## First Cut

Implemented owners: `tract_shape`, `glottis`, `tract_injection`,
`nasal_branch`, and first-pass `propagation=waveguide`.
`tract_shape` owns section diameter/area fields and reflection derivation.
`glottis` owns excitation quality. `tract_injection` owns positioned
frication/burst pressure. The current Faust proxy consumes these primitives
through shape summaries, reflection energy, glottal shaping, and injection
pressure instead of silently inventing all of them inside one helper. The
waveguide lowering consumes the derived oral reflection field as right/left
section state equations.

The next cut is cell-local events and timing: the oral/nasal tube now consumes
the full reflection coefficient fields, but PT's two tract steps per output
sample, cell-positioned turbulence injection, and obstruction-state transients
still need low-level owners.

## Cut Line

The current `Voice.Tract` scalar proxy may survive only as an adapter over real
tract primitives. If a future change adds PT behavior that cannot be described
as a reusable Aqua primitive, cut it or keep it in the reference harness instead
of promoting it to the public DSL.
