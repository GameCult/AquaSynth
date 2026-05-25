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

## AquaSynth Capability Map

Current AquaSynth can express:

- runtime parameters with stable paths;
- reusable `tract_shape` declarations with authored diameter or area samples;
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

- exact two recursive tract state updates inside one output sample;
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
- `tract_shape` owns reusable section diameter/area functions. The current
  proxy consumes its shape summaries and derived reflection energy; future
  waveguide lowering should consume the full coefficients.
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
- `substeps` expresses waveguide clock intent; current Faust lowering consumes
  it through drive/loss scaling but does not yet prove exact intra-sample state
  updates.
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
2. Add a test-only Pink Trombone oracle renderer and compare AquaSynth's first
   tract-DSP candidate against fixed vowel/constriction fixtures.

The new owner should be explainable as:

```text
TractPlan owns section diameters and source/injection events so tract acoustics
remain true during rendering.
```

If that sentence becomes awkward, the design is probably drifting back into
proxy mud.
