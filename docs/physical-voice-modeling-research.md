# Physical Voice Modeling Research Pass

Canonical research packet: `research/physical-voice-modeling/`.

That folder contains downloaded/captured sources where possible, a source
manifest with hashes, talks/transcripts where available, a practical
researcher map, and the distilled implementation summary. This document remains
the docs-facing copy of the same architectural direction.

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
- Peter Birkholz / VocalTractLab sources and talk pages:
  https://www.vocaltractlab.de/ ,
  https://github.com/TUD-STKS/VocalTractLabBackend-dev ,
  https://lpp.cnrs.fr/evenement/srpp-de-peter-birkholz/ ,
  https://brahms.ircam.fr/en/media/xfb9e0a_peter-birkholz-how-physical-models-of-th
- Brad Story / TubeTalker and resonance tutorial:
  https://ncvs.org/vocal-tract-resonances-in-vowel-production/ ,
  https://www.youtube.com/watch?v=q23bAG-b6OA ,
  https://bpb-us-e2.wpmucdn.com/sites.arizona.edu/dist/f/80/files/2023/10/Story-2011_0-1.pdf
- Julius Smith CIRMMT physical-modeling talk:
  https://www.cirmmt.org/en/events/distinguished-lectures/Smith
- Sidney Fels / John Lloyd ArtiSynth talk:
  https://www.microsoft.com/en-us/research/video/developing-physically-based-dynamic-vocal-tract-models-using-artisynth/

The expanded packet now includes `PEOPLE.md` and `talks/TALKS.md`. Two public
YouTube transcripts were downloaded: Julius Smith's physical-modeling lecture
and Brad Story's NCVS vocal-tract resonance tutorial. Birkholz and Fels/Lloyd
pages were preserved with transcript-unavailable notes because their public
pages did not expose transcript text.

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

First implementation cut is now underway in `src/AquaSynth.Core/Model.cs` and
`src/AquaSynth.Core/PatchScript.cs`. The new neutral records are the ownership
surface:

- `AcousticPath`: name, length, area curve, propagation speed, loss profile.
- `AcousticSourcePort`: path/position, source model, pressure/tension/noise
  controls.
- `AcousticBranch`: from path/position to path/position, opening/coupling,
  branch kind.
- `AcousticRadiationPort`: path/position, opening/reflection/radiation filter.
- `WaveClockPolicy`: unit grid, half-sample grid, fractional delay order, max
  delay, and smoothing mode.
- `AcousticPortNetwork`: names one primary path and the source, branch,
  radiation, and wave-clock records that make it renderable.

Parser support exists for `path`, `source_port`, `branch`, `radiation_port`,
`wave_clock`, and `acoustic_network`. Existing PT-pressure commands now also
feed the acoustic record lists: `tract_shape` demotes to an `AcousticPath`,
`glottis`/`tract_injection` demote to `AcousticSourcePort`, `nasal_branch`
demotes to an `AcousticPath` plus `AcousticBranch`, and each `tract` voice
gets an `AcousticPortNetwork` over generated oral/source/branch/radiation
records.

The second cut gives those records an audible Faust proxy. An `acoustic` /
`acoustic_voice` command can reference an `acoustic_network`; Faust lowering
builds a compact source/body/radiation model from the declared source ports,
primary path, branches, and radiation ports. This is a response-proxy renderer,
not the final waveguide truth renderer: it derives modal resonances from path
length and area summaries, then mixes branch and radiation responses. It exists
so syrinx-like and alien topologies can be heard and tested before the full
bidirectional network lowering is complete. Source-port pressure/tension/opening
and branch/radiation opening-style controls can bind through normal Aqua
parameters, so the knobs used by training and playground surfaces are attached
to acoustic records instead of side-channel state.

A syrinx can now be authored as two labial `source_port` records plus
bronchial/tract `branch` topology without a species mode. The next deeper cut
is to make `AcousticPortNetwork` own the waveguide renderer directly, at which
point `Voice.Tract` should become a larynx-shaped authoring convenience over
the same network rather than a separate audio authority.

The topology decision is now a path graph with typed terminals. `AcousticPath`
owns tube geometry. `AcousticTerminal` owns a named position on a path and its
role (`junction`, `source`, `radiation`, boundary, or diagnostic probe).
`AcousticConnection` owns the set of terminals that scatter together and the
connection law. `source_port`, `radiation_port`, and `branch` remain ergonomic
commands, but they populate typed graph records rather than becoming the graph
law themselves.

The first graph compiler cut now exists. It splits paths at terminal positions,
creates bidirectional segment state in Faust, scatters connected terminals by
area-weighted pressure continuity, injects declared source ports at source
terminals, and radiates declared radiation ports. Faust validation passes for a
three-path trachea/oral/nasal graph. The next cut added wave-clock lowering and
same-node aggregation: segment delay now follows the network's `wave_clock`
strategy (`unit`, half-sample, linear fractional, Lagrange, or Thiran/allpass),
and co-located terminals on one path become a shared graph node so source,
junction, boundary, and radiation roles can inhabit the same anatomical point.
The remaining limits are now parity and law depth rather than graph ownership:
connection laws beyond area/pressure/bypass need sharper physical semantics,
runtime morphology changes still require care around recompilation boundaries,
and PT-shaped `Voice.Tract` has not yet been demoted to graph sugar.
