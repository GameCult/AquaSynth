# Pink Trombone Parity Packet

## Objective

Use Pink Trombone as old-school speech parity pressure, not as a vague mascot
for "more vocal knobs." The current AquaSynth tract voice is a tract-shaped
voice treatment with Pink Trombone-style controls. Pink Trombone itself is still
a moving vocal-tract waveguide. Those are related machines, but not the same
machine.

This packet exists so the mismatch stays inspectable.

## Reference Source

Primary source for this packet:

- `chdh/pink-trombone-mod`: MIT TypeScript modularization of Neil Thapen's Pink
  Trombone, <https://github.com/chdh/pink-trombone-mod>
- Source files inspected:
  - `src/Synthesizer.ts`
  - `src/Glottis.ts`
  - `src/Tract.ts`
  - `src/TractShaper.ts`

The original interactive/programmatic Pink Trombone lineage is also visible in
`zakaton/Pink-Trombone`, but that package is GPL-3.0. Treat it as reference
pressure only unless a later license review says otherwise.

## Current Mechanism

Pink Trombone's source-readable graph:

```text
frequency/tenseness/intensity
  -> LF-style glottal source plus aspiration noise
  -> 44-section main tract diameter array
  -> squared diameters become section areas
  -> adjacent section areas become reflection coefficients
  -> right/left traveling-wave delay-line state
  -> 28-section nasal branch through a velum junction
  -> lip output + nose output
```

The synthesizer steps the tract twice per output sample. Tract shape changes are
not static formants: the shaper slews section diameters toward target diameters,
updates reflections at block boundaries, injects turbulence at constrictions,
and emits a transient when a closure opens.

The 44 oral and 28 nasal counts are Pink Trombone's implementation grid, not
the AquaSynth abstraction. At 44.1 kHz, a 17 cm tract split into 44 cells has an
acoustic travel time of roughly 0.5 samples per cell, so the twice-per-sample
update is better understood as a discretization/wave-clock choice. AquaSynth's
live target is continuous morphology plus fractional-delay waveguide lowering,
with the grid chosen by the backend.

## AquaSynth Capability Map

Current AquaSynth can express:

- runtime parameters with stable paths;
- reusable `tract_shape` declarations with authored diameter or area samples,
  physical length, normalized interpolation, resampling, and per-cell acoustic
  delay derivation;
- derived tract areas and adjacent-section reflection coefficients;
- reusable `glottis` declarations for tract excitation quality;
- reusable `tract_injection` declarations for positioned frication/burst
  pressure;
- reusable `nasal_branch` declarations with velum-controlled junctions;
- reusable `tract_motion` declarations for slew and obstruction thresholds;
- a `tract` voice command owned by `Voice.Tract`;
- Pink Trombone-shaped controls for intensity, tenseness, tongue body,
  constriction, velum/nasal opening, turbulence, lip opening, and end
  reflections;
- a Faust-lowered tract voice proxy with LF-style excitation, aspiration,
  frication, oral resonators, and a velum-controlled nasal lane;
- generated oral bidirectional waveguide equations over a `tract_shape` section
  field when `propagation=waveguide`;
- oscillator/noise sources;
- ADSR and staged rate/level envelopes;
- low/high/band/notch filters;
- static and frame-scanned formant banks;
- patch-level and voice-local modulation;
- compiled Faust control sweeps through `/speech/output/N`.

Current AquaSynth cannot exactly express:

- fractional-delay tract propagation whose clock derives from physical length,
  propagation speed, and sample rate;
- exact Pink Trombone LF glottal coefficient updates.

## Current Parity Rung

The current committed rung is an expressive tract voice, not exact Pink
Trombone anatomy:

- `PinkTromboneReference.ToReferencePatch()` records the source, tract features,
  and relevant controls.
- `ReferenceRebuildCatalog.PinkTromboneRebuilds` contains the current
  AquaSynth tract-voice proxy as a deliberately non-passing rebuild.
- The `tract` DSL command parses into an ordinary `Voice` with `Voice.Tract`,
  so this remains a voice with a very expressive tract treatment.
- `tract_shape` owns reusable continuous diameter/area functions. The current
  proxy consumes its shape summaries and derived reflection energy; waveguide
  lowering consumes a compiled grid sampled from that continuous owner, so
  authoring sample count and backend section count can differ.
- `glottis` and `tract_injection` own excitation and positioned noise/burst
  controls consumed by tract voices.
- `propagation=waveguide` emits an oral right/left tube from the derived
  reflection field. It is the first low-level propagation path, not full PT.
- `nasal_branch` adds a second diameter/area tube and generated three-way
  oral/nasal scattering junction when used by a waveguide tract.
- `tract_injection` pressure is distributed into waveguide section updates by
  position and width when waveguide propagation is active.
- `tract_motion` smooths tract controls and lets the waveguide derive burst
  pressure from obstruction history.
- waveguide lowering derives live per-section diameter targets, areas, and
  reflection coefficients from shape and gesture controls.
- waveguide lowering now defaults to an acoustic unit-delay compiled grid from
  physical tract length, currently turning a 17 cm oral tract into 22 compiled
  sections at the 44.1 kHz target instead of treating PT's 44 cells as anatomy.
- `substeps` is now legacy waveguide clock pressure. Current Faust lowering
  consumes it through drive/loss scaling, but the coherent target is
  fractional-delay propagation from physical tract length and wave speed.
- `PinkTromboneParityFixtures` defines fixed Aqua DSL workouts for open,
  front, nasal, sibilant, and closure cases using the reusable low-level
  primitives.
- `PinkTromboneReferenceRenderer` renders deterministic test-only traveling-wave
  references for those fixture controls.
- `PinkTromboneLogMelParityTests` renders Aqua waveguide candidates,
  writes listening WAV/report artifacts under
  `artifacts/parity/pink-trombone-logmel/`, and reports the current waveguide
  baseline: open vowel cosine 0.6143, front vowel 0.6349, nasal 0.5425,
  sibilant 0.3744, closure-release -0.0820.
- Waveguide lowering now uses one Faust feedback component
  `wg_loop ~ si.bus(...)` for the compiled oral tract and nasal branch. The
  previous scalar named-recursion form was cut because it was the wrong Faust
  shape, and the PT 44/28 grid is now reference pressure rather than the
  default emitted topology.
- The proxy script parses and emits Faust with live controls, but its missing
  features explicitly name the waveguide authorities AquaSynth does not own.
- `tools/vocal-tract-playground` exposes the same control surface for fast
  knob-twiddling through a small WebAudio witness.
- `PinkTromboneReferenceDeclaresMissingWaveguideAuthority` prevents the proxy
  from being mistaken for real tract parity.

That is the correct shape for now. It should be fun to touch, but still honest
about the parts of Pink Trombone it cannot claim.

## Next Cut

Do not add another formant workaround. The next coherent implementation is one
of these:

1. Promote `Voice.Tract` from tract-shaped proxy lowering to a dedicated tract
   DSP model in AquaSynth.Faust/Core: typed tract sections, diameter curves,
   reflection calculation, glottal source, nasal branch, and a Faust emitter for
   that structure.
2. Replace `substeps` as a musical surface with fractional-delay waveguide
   lowering. Physical length, propagation speed, and sample rate decide delay;
   runtime morphology controls move diameters and junction openings without
   recompiling unless topology changes.
3. Use the log-mel artifact loop to golf fixture-by-fixture now that the real
   tract path renders.

The new owner should be explainable as:

```text
TractPlan owns section diameters and source/injection events so tract acoustics
remain true during rendering.
```

If that sentence becomes awkward, the design is probably drifting back into
proxy mud.
