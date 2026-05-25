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
- no positioned excitation/injection events;
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

3. `tract_injection`: positioned noise/burst excitation.
   - Owns constriction position, diameter/opening, turbulence, and release
     transient behavior.
   - Can be driven by envelopes, LFOs, learned speech controls, or consonant
     gestures.

4. `nasal_junction`: velum-controlled branch primitive.
   - Owns nasal opening and branch shape.
   - Derives the three-way junction coefficients from local areas.

5. `waveguide_tract`: propagation primitive.
   - Owns right/left traveling-wave state, substep rate, loss, and radiation.
   - Consumes area/reflection fields and source/injection events.

6. PT parity harness.
   - Renders fixed PT references for vowels, nasals, fricatives, closures, and
     moving tongue/constriction gestures.
   - Renders Aqua DSL reconstructions.
   - Scores log-mel cosine similarity plus targeted feature probes.

## First Cut

First owner implemented: `tract_shape`. It is the smallest useful owner:
reflection, waveguide, constriction, nasal junction, and morphology all depend
on a section diameter/area field. The current Faust proxy now consumes this
primitive through shape summaries and derived reflection energy instead of
silently inventing one universal tube. The next cut is to make a propagation
primitive consume the full reflection coefficient field.

## Cut Line

The current `Voice.Tract` scalar proxy may survive only as an adapter over real
tract primitives. If a future change adds PT behavior that cannot be described
as a reusable Aqua primitive, cut it or keep it in the reference harness instead
of promoting it to the public DSL.
