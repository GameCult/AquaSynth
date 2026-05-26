# Physical Voice Modeling Research Pass

## Objective

Generalize AquaSynth voice modeling beyond Pink Trombone. The target is a
performant, Faust-friendly acoustic modeling layer that can express a larynx,
syrinx, or stranger anatomy from the same primitives:

```text
continuous morphology + acoustic source ports + propagation network
  -> generated Faust
  -> differentiable controls and log-mel/audio parity
```

Pink Trombone remains useful pressure, but its 44 oral cells, 28 nasal cells,
and twice-per-sample step loop are implementation evidence, not the language.

## Research Grounding

The useful physical model is still a wave/acoustic network, not a formant knob
pile.

- Digital waveguides are a standard efficient representation for 1D acoustic
  systems. Faust's `physmodels.lib` is built around this exact class of
  structures: bidirectional chains, waveguides, terminations, tubes, reeds,
  lips, bows, and other port interactions.
- Faust `physmodels.lib` exposes `pm.chain` for bidirectional physical-model
  blocks, `pm.l2s` for length-to-samples conversion using wave speed and sample
  rate, and `pm.waveguideUd`, `pm.waveguideFd`, `pm.waveguideFd2`, and
  `pm.waveguideFd4` for unit and fractional delay waveguides.
- Faust `delays.lib` exposes several delay families relevant to this work:
  `de.fdelay` for simple linear fractional delay, Lagrange fractional delays
  through `de.fdelayltv` and `de.fdelay1..5`, and Thiran/allpass delays through
  `de.fdelay1a..4a`. The allpass family preserves magnitude better but has
  explicit lower-delay stability limits; first-order Thiran bottoms out around
  a half sample.
- Vocal-tract waveguide literature treats half-sample Kelly-Lochbaum models as
  useful for speech research and uses fractional-delay filters to vary segment
  lengths continuously without changing sampling rate.
- Recent differentiable speech work is converging on the same division:
  physically meaningful low-dimensional articulatory controls feed differentiable
  acoustic models or differentiable approximations of them. The important point
  is not whether the model is literally PT; it is whether the controls are
  smooth, bounded, interpretable, and connected to the rendered loss.

Primary sources consulted:

- Faust libraries, `physmodels.lib`: https://faustlibraries.grame.fr/libs/physmodels/
- Local Faust 2.85.5 `physmodels.lib` and `delays.lib` under
  `C:\Program Files\Faust\share\faust\`.
- Mathur, Story, Rodriguez, "Vocal-tract modeling: Fractional elongation of
  segment lengths in a waveguide model with half-sample delays":
  https://doi.org/10.1109/TSA.2005.858550
- Mullen, Howard, Murphy, "Waveguide Physical Modeling of Vocal Tract
  Acoustics":
  https://eprints.whiterose.ac.uk/3713/
- Frontiers DDSP review section on differentiable source-filter and
  differentiable Pink Trombone-style articulatory estimation:
  https://www.frontiersin.org/journals/signal-processing/articles/10.3389/frsip.2023.1284100/full

## Authority Map

Owner: `AcousticMorphology`

- Owns continuous geometry: length, cross-sectional area/diameter curves,
  branch curves, radiation openings, and source-port positions.
- Does not own emitted grid size, PT section count, or Faust delay mechanics.

Owner: `AcousticPortNetwork`

- Owns topology: ports, directed wave variables, tube segments, scattering
  junctions, source couplings, branch junctions, and terminations.
- A larynx and a syrinx differ here mostly by source topology:
  - larynx: one primary glottal source feeding one tract;
  - syrinx: two independently controllable labial/bronchial source ports that
    can couple into a shared tracheal/oral path.
- Alien voices are additional source ports, branch graphs, nonlinear junctions,
  and radiation ports, not new special-case voice modules.

Owner: `WaveClock`

- Owns conversion from physical length to delay samples:
  `delaySamples = lengthMeters * sampleRate / propagationSpeed`.
- Chooses a lowering strategy per segment:
  - unit-delay compiled grid for cheap static geometry;
  - first-order Thiran/allpass for near-half-sample stable fractional delays;
  - Lagrange/linear delay for FIR-like interpolation where magnitude error is
    acceptable;
  - smoothed crossfaded variable delay when length changes rapidly.
- `substeps` is no longer an owner. It is a historical clue.

Owner: `DifferentiableControls`

- Owns the trainable/control surface:
  - normalized positions along named paths;
  - positive radii/areas via bounded or softplus-like transforms;
  - source pressure/tension/open quotient/noise as bounded continuous controls;
  - coupling/opening coefficients constrained into passive ranges;
  - slew rates and gesture envelopes as smooth controls.
- Does not expose raw emitted section indices as the primary interface.

## Faust Lowering Choices

### Keep Custom Generated Scattering For Articulatory Tracts

Faust `pm.chain` is attractive for generic bidirectional blocks, but AquaSynth
still needs generated named equations for tract-like acoustic networks because:

- the number of junctions and branch topology are compile-time graph shape;
- live controls must update areas, reflection coefficients, and source weights;
- generated code needs inspectable parameter names for training/playground use;
- nasal, lateral, syrinx, and alien branch points need multi-port scattering
  rather than a fixed string/tube model.

The current `wg_loop ~ si.bus(...)` shape remains plausible as the low-level
state owner. It should learn from Faust's physical-modeling library instead of
pretending the library does not exist.

### Use Faust Library Delay Blocks Where They Match The Segment

For future non-uniform segments, generate delay elements from the library:

- `de.fdelay(max, delay)` for simple fractional delay and quick parity probes.
- `de.fdelay1a(max, delay)` or related Thiran/allpass delays for magnitude-flat
  near-half-sample interpolation when the delay is within the documented stable
  range.
- `de.fdelayltv(order, max, delay)` or `de.fdelay1..5` when smooth variable
  segment length matters more than allpass phase behavior.
- `pm.waveguideFd4(max, delay)` for larger bidirectional tube segments that can
  live as chain blocks.

Do not blindly place arbitrary fractional delay inside every scattering edge.
For very short sections below a stable allpass delay bound, either:

- choose a coarser emitted scattering grid and let the area curve resample onto
  it; or
- use a half-sample KL structure where the delay model is explicitly designed
  for that regime.

### Separate Fast Training From Final Rendering

Aqua should support at least two lowerings from the same acoustic morphology:

1. `waveguide-grid`: realtime Faust waveguide network for listening, gameplay,
   and high-fidelity parity.
2. `response-proxy`: differentiable transfer-function or modal/SOS proxy for
   fast optimization, initialized from the same geometry and periodically
   checked against the waveguide render.

That is not a cheat if the proxy is named and falsified by audio parity. It is
how the machine avoids making every gradient step pay for a full acoustic body.

## DSL Direction

Replace PT-shaped source words with physical voice words.

Preferred concepts:

- `morphology`: named continuous acoustic body.
- `path`: oral, nasal, bronchial, lateral, resonator, alien named tube/path.
- `branch`: topology edge between paths, with opening/coupling control.
- `source_port`: acoustic excitation port; can be glottal, labial, reed,
  turbulent jet, click cavity, or synthetic/alien.
- `radiation_port`: lip, nostril, beak, membrane aperture, side vent.
- `gesture`: smooth control over areas, openings, source parameters, and
  branch couplings.
- `wave_clock`: propagation speed, delay strategy, and emitted-grid policy.

Avoid concepts as primary public abstractions:

- `substeps`;
- `44 sections`;
- `glottis` as the only source kind;
- `nose` as the only branch kind;
- integer cell index controls as the trainable interface.

## Next Implementation Cut

Add model records before more audio golf:

- `AcousticPath`: name, length, area curve, propagation speed, loss profile.
- `AcousticBranch`: from path/position to path/position, opening/coupling,
  branch kind.
- `AcousticSourcePort`: path/position, source model, pressure/tension/noise
  controls.
- `AcousticRadiationPort`: path/position, opening/reflection/radiation filter.
- `WaveClockPolicy`: unit grid, half-sample grid, fractional delay order, max
  delay, and smoothing mode.

Then lower the current PT fixtures through those records as one larynx-shaped
configuration. A syrinx should be expressible by adding two source ports and a
bronchial/tracheal branch topology, not by forking the tract voice.

