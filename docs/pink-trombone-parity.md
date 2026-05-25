# Pink Trombone Parity Packet

## Objective

Use Pink Trombone as old-school speech parity pressure, not as a vague mascot
for "more vocal knobs." The current AquaSynth speech-loss surface is a
source/filter proxy. Pink Trombone is a moving vocal-tract waveguide. Those are
not the same machine.

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

## AquaSynth Capability Map

Current AquaSynth can express:

- runtime parameters with stable paths;
- oscillator/noise sources;
- ADSR and staged rate/level envelopes;
- low/high/band/notch filters;
- static and frame-scanned formant banks;
- patch-level and voice-local modulation;
- compiled Faust control sweeps through `/speech/output/N`.

Current AquaSynth cannot express:

- a 44-cell bidirectional tract waveguide;
- independent right/left traveling-wave state arrays;
- a 28-cell nasal waveguide branch;
- a three-way velum/nose junction;
- per-section diameter targets as synthesis authority;
- diameter-squared area reflection coefficients;
- a subgraph that steps twice per output sample;
- turbulence injected at a tract position into neighboring cells;
- obstruction-state release transients;
- exact Pink Trombone LF glottal coefficient updates.

## First Parity Rung

The first committed rung is structural, not audio:

- `PinkTromboneReference.ToReferencePatch()` records the source, tract features,
  and relevant controls.
- `ReferenceRebuildCatalog.PinkTromboneRebuilds` contains the current
  AquaSynth source-filter proxy as a deliberately non-passing rebuild.
- The proxy script parses and emits Faust, but its missing features explicitly
  name the waveguide authorities AquaSynth does not own.
- `PinkTromboneReferenceDeclaresMissingWaveguideAuthority` prevents the proxy
  from being mistaken for real tract parity.

That is the correct failure for now. A failed exactness claim is better than a
successful little lie with sliders.

## Next Cut

Do not add another formant workaround. The next coherent implementation is one
of these:

1. Add a dedicated tract-DSP model to AquaSynth.Faust/Core: typed tract sections,
   diameter curves, reflection calculation, glottal source, nasal branch, and a
   Faust emitter for that structure.
2. Add a test-only Pink Trombone oracle renderer and compare AquaSynth's first
   tract-DSP candidate against fixed vowel/constriction fixtures.

The new owner should be explainable as:

```text
TractPlan owns section diameters and source/injection events so tract acoustics
remain true during rendering.
```

If that sentence becomes awkward, the design is probably drifting back into
proxy mud.
