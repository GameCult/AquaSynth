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

The practical implementation literature reinforces the same split:

- Birkholz/VocalTractLab: continuous articulatory geometry plus multiple
  control levels. Gestures and targets deform a physical model; the model is
  not just a fixed list of tube cells.
- Story/TubeTalker: area functions and tract length are the useful compact
  bridge from anatomy to acoustic response. Formants are derived resonance
  evidence, not the control authority.
- Smith/digital waveguides: bidirectional delay-line wave variables, scattering
  junctions, loss filters, and fractional delay are the efficient realtime
  substrate.
- Fels/ArtiSynth: heavy biomechanical models are valuable truth/calibration
  sources, but realtime Aqua lowering should keep their authority split rather
  than inherit their computational weight.
- Praat/Gnuspeech/SndKit/PT: small implementations are excellent parity and
  contract pressure, but none should become the public DSL shape.

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

The live DSL now points at the realtime-core version of this map:

- `area_curve`: named continuous tract morphology, resampled into graph
  segments during lowering.
- `AcousticPath`: name, area function or `area_curve` reference, physical
  length, wave speed, live area-control deformation, and named loss model.
- `AcousticSourcePort`: path, normalized position, source kind, `model`, valve
  `law`, pressure reservoir/downstream pressure, tension, aperture/opening,
  mass/damping/stiffness, saturation, load coupling, and layered upper/lower
  parameters for two-mass/body-cover approximations.
- `AcousticBranch`: source path/position, target path/position, opening,
  coupling, passivity constraints.
- `AcousticRadiationPort`: path/position, opening, reflection, radiation
  model, filter behavior, and loss.
- `WaveClockPolicy`: delay strategy, fractional-delay order, max delay, and
  smoothing mode.
- `gesture`: a group of parameter-targeting control curves. It is an authoring
  and controller surface only; the stable `param path=` remains the single
  realtime authority that Faust exposes to hosts.

Pink Trombone then becomes one larynx-shaped preset over these primitives:
one glottal source port, one oral path, one nasal branch, and two radiation
ports. A syrinx becomes two source ports plus bronchial/tracheal topology. Alien
voices become more ports, branches, nonlinear junctions, and radiation surfaces.

## Research Inventory

Expanded source notes live beside this summary:

- `PEOPLE.md`: practical scientists/implementers and Aqua implications.
- `talks/TALKS.md`: captured talks, available transcripts, and transcript gaps.
- `SOURCES.md`: downloadable source manifest with local hashes.

Two public talk transcripts were captured: Julius Smith's CIRMMT physical
modeling lecture and Brad Story's NCVS vocal-tract resonance tutorial. Peter
Birkholz, Sidney Fels/John Lloyd, and Brad Story AZPM talk pages were preserved,
but public transcripts were not exposed by their pages as of 2026-05-26.

## Open Questions

- Whether the primary training renderer should be the full waveguide network or
  a differentiable response proxy periodically checked against waveguide audio.
- Which fractional-delay family is best for realtime moving morphology:
  first-order Thiran/allpass for magnitude preservation, Lagrange for FIR-like
  behavior, or crossfaded variable delay for fast geometry motion.
- How strict the default passivity clamp should be once the new loss/radiation
  probes are used by training. The default graph should stay bounded, but
  high-drive research patches need explicit room to destabilize.
- How far to push `law=body_cover` inside Faust before the model should defer
  to an offline calibration renderer. The runtime version should remain a
  layered valve, not a hidden finite-element throat.
