# Pink Trombone Parity Packet

## Objective

Use Pink Trombone as old-school speech parity pressure, not as a vague mascot
for "more vocal knobs." The active Aqua path is the typed acoustic graph:
continuous paths, source ports, branch terminals, radiation ports, connection
laws, and wave-clock policy. Pink Trombone's old waveguide discretization is
source-reference pressure, not an Aqua implementation lane to preserve.

This packet exists so the machine stays inspectable while the old one turns
into commit-history dust.

## Reference Source

Primary source for this packet:

- `chdh/pink-trombone-mod`: MIT TypeScript modularization of Neil Thapen's Pink
  Trombone, <https://github.com/chdh/pink-trombone-mod>
- Pinned local source: `external/pink-trombone-mod` at
  `359c2d3b42b10280404c1650dc601902112b4c90`
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
- typed acoustic paths, terminals, connections, source ports, branch/radiation
  ports, and wave-clock policies;
- optional acoustic path `AreaControl` fields that turn tongue, constriction,
  and lip gestures into live diameter/area expressions at graph sample points;
- graph lowering that splits paths at terminals, injects typed sources, scatters
  connection groups, and radiates from typed terminal ports;
- oscillator/noise sources;
- ADSR and staged rate/level envelopes;
- low/high/band/notch filters;
- static and frame-scanned formant banks;
- patch-level and voice-local modulation;
- compiled Faust control sweeps through `/speech/output/N`.

Current AquaSynth cannot exactly express:

- exact Pink Trombone wall update timing and obstruction history;
- fully continuous area modulation inside a delay segment between graph sample
  points. Generated tract graphs now sample live area along the path, but the
  delay segment between two sampled nodes remains uniform.
- exact Pink Trombone block timing and source/noise/transient behavior inside
  the Faust-lowered Aqua graph.

## Reference Renderer Authority

`PinkTromboneReferenceRenderer` is a source-port of the pinned MIT TypeScript
implementation, not a Pink-Trombone-flavored proxy. Its inner graph mirrors the
upstream DSP authorities:

- `Synthesizer`: 512-sample blocks, tract stepping twice per output sample;
- `Glottis`: LF waveform coefficient solve, aspiration noise, vibrato/wobble,
  and intensity/tenseness/frequency smoothing;
- `Tract`: 44 oral cells, 28 nasal cells, area-derived reflections, three-way
  nasal junction, lip/nose radiation sum, filtered frication, and closure
  transients;
- `TractShaper`: tongue/rest diameter targets, touch-style constriction
  reduction, wall slew, velum slew, and obstruction release detection.

The renderer keeps AquaSynth's fixture control record as the authoring surface,
but those controls are now translated into the source graph rather than into a
hand-rolled approximation. Reference WAVs written before
`20260526T211902375` were produced by the old approximation and are invalid for
parity golf.

The accepted utterance curriculum is intentionally small: `mama`, `papa`, and
`thrombosis`. `lulek` was cut after listening because its control sketch was
teaching a bad artifact rather than useful Pink Trombone capability.

## Current Parity Rung

The current committed rung is an expressive graph voice, not exact Pink
Trombone anatomy:

- `PinkTromboneReference.ToReferencePatch()` records the source, tract features,
  and relevant controls.
- The `tract` DSL command parses into an ordinary `Voice` with `Voice.Tract`,
  and generates the same acoustic graph records used by explicit graph
  authoring.
- `tract_shape` owns reusable continuous diameter/area functions. Generated
  graph paths now attach `AcousticAreaControl`, so tongue, constriction, and lip
  gestures alter live node areas and therefore scattering along the oral tract.
- `glottis` and `tract_injection` own excitation and positioned noise/burst
  controls consumed by tract voices.
- `nasal_branch` adds a second diameter/area tube and generated oral/nasal
  connection terminals. Velum owns branch coupling and nasal aperture; the first
  nasal tube sample is not allowed to masquerade as the velum opening.
- generated graph records mirror live `tract` parameter bindings into acoustic
  source, terminal, connection, radiation, and path-area fields so `/pink/...`
  controls exercise the actual graph.
- accepted utterance parity lowers the surviving PT control-point sketches into
  Faust `age` curves over ordinary `/pink/...` parameters and compares the
  Aqua graph render against the source-ported PT renderer.
- `PinkTromboneParityFixtures` defines fixed Aqua DSL workouts for open,
  front, nasal, sibilant, and closure cases using the reusable low-level
  primitives.
- `PinkTromboneReferenceRenderer` renders deterministic test-only traveling-wave
  references for those fixture controls.
- `PinkTromboneLogMelParityTests` renders Aqua graph candidates,
  writes listening WAV/report artifacts under
  `artifacts/parity/pink-trombone-logmel/`, and reports only the graph lane.
- Latest static graph fixture evidence after cutting the old parity lane:
  open vowel cosine 0.6252, front vowel 0.6576, nasal 0.6893,
  bilabial-nasal-ma 0.6550, sibilant 0.4166, closure-release 0.5554.
- Latest graph utterance smoke evidence:
  `mama` cosine 0.3354 / RMS 1.0698, `papa` 0.8162 / 0.1815,
  `thrombosis` 0.3584 / 1.9499 under
  `artifacts/parity/pink-trombone-utterance-logmel/20260526T232106458`.
  This is still not final speech parity; it is the first honest graph-only
  moving-utterance rung.
- `tools/vocal-tract-playground` exposes the same control surface for fast
  knob-twiddling through a small WebAudio witness.
- `PinkTromboneReferenceDeclaresMissingWaveguideAuthority` prevents graph
  coverage from being mistaken for exact PT parity.

That is the correct shape for now. It should be fun to touch, but still honest
about the parts of Pink Trombone it cannot claim. The next target is not to
revive the old waveguide. The next target is making moving graph morphology
sound like an actual mouth.

## Next Cut

Do not add another formant workaround. The next coherent implementation is one
of these:

1. Give plosive/closure release a graph-native transient owner instead of
   smearing burst pressure through the turbulence port.
2. Improve graph source/radiation level normalization so `papa` is not
   under-driven while `thrombosis` is hot.
3. Use the log-mel artifact loop to golf `mama`, `papa`, and `thrombosis`
   against the source-ported PT renderer without reintroducing a parallel
   PT-shaped backend.

The new owner should be explainable as:

```text
AcousticGraph owns paths, terminals, sources, connections, radiation, and live
control bindings so tract acoustics remain true during rendering.
```

If that sentence becomes awkward, the design is probably drifting back into
proxy mud.
