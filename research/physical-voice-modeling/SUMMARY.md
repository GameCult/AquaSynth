# Physical Voice Modeling Research Summary

## Objective

Build AquaSynth voice synthesis around reusable physical-acoustic primitives,
not a Pink Trombone-shaped module. The same language should express a human
larynx, a bird-like syrinx, and alien anatomies by changing source ports,
branches, geometry, and radiation, not by adding special-case engines.

## Distilled Model

The right abstraction is an acoustic port network:

- `AcousticMorphology` owns continuous geometry: named paths, lengths,
  area/diameter curves, branch locations, and radiation openings.
- `AcousticSourcePort` owns excitation at a physical location: glottal folds,
  paired syrinx labia, reed-like oscillators, turbulence jets, click cavities,
  or synthetic/alien pressure sources.
- `AcousticBranch` owns topology and coupling: nasal coupling, bronchial split,
  lateral channels, side vents, secondary resonators, and alien branch graphs.
- `AcousticRadiationPort` owns boundary behavior: lips, nostrils, beak exits,
  membranes, vents, or other apertures.
- `WaveClockPolicy` owns physical length to delay conversion and the lowering
  strategy: unit grid, half-sample grid, fractional delay, and smoothing.
- Differentiable controls should be normalized physical controls: path position,
  area/opening, pressure, tension, coupling, loss, and source balance. Raw
  emitted section indices are a backend detail.

## Faust Implications

Faust already has useful primitives:

- `physmodels.lib`
  - `pm.l2s`: length in meters to samples using sample rate and speed of sound.
  - `pm.chain`: bidirectional physical-model chain composition.
  - `pm.waveguideUd`, `pm.waveguideFd`, `pm.waveguideFd2`, `pm.waveguideFd4`:
    unit and fractional bidirectional waveguides.
- `delays.lib`
  - `de.fdelay`: linear fractional delay.
  - `de.fdelayltv` and `de.fdelay1..5`: Lagrange fractional delays.
  - `de.fdelay1a..4a`: Thiran/allpass delays, useful when magnitude-flat
    interpolation matters and delay constraints are respected.

The clean Aqua approach is hybrid:

- Use Faust delay/waveguide primitives for tube segments where they match the
  required delay behavior.
- Continue generating custom scattering equations where Aqua needs live area
  controls, multi-port branch junctions, inspectable parameter paths, and
  training-friendly graph structure.
- Avoid exposing `substeps` as a musical primitive. It is a historical clue
  about PT's half-sample wave clock, not the owner of voice physics.

## Implementation Direction

Next model records should likely be:

- `AcousticPath`: name, length, area curve, wave speed, loss.
- `AcousticSourcePort`: path, normalized position, source type, pressure,
  tension/opening/noise controls.
- `AcousticBranch`: source path/position, target path/position, opening,
  coupling, passivity constraints.
- `AcousticRadiationPort`: path/position, opening, reflection, radiation
  filter/loss.
- `WaveClockPolicy`: delay strategy, fractional-delay order, max delay, and
  smoothing mode.

Pink Trombone then becomes one larynx-shaped preset over these primitives:
one glottal source port, one oral path, one nasal branch, and two radiation
ports. A syrinx becomes two source ports plus bronchial/tracheal topology. Alien
voices become more ports, branches, nonlinear junctions, and radiation surfaces.

## Open Questions

- Whether the primary training renderer should be the full waveguide network or
  a differentiable response proxy periodically checked against waveguide audio.
- Which fractional-delay family is best for realtime moving morphology:
  first-order Thiran/allpass for magnitude preservation, Lagrange for FIR-like
  behavior, or crossfaded variable delay for fast geometry motion.
- How to constrain branch/source/radiation controls so learned parameters remain
  passive or bounded enough to avoid explosive acoustic networks.
- How to expose syrinx-specific controls without hardcoding "bird mode":
  paired source balance, independent tension/pressure, bronchial coupling, and
  shared tract loading seem like source-port/topology controls.

