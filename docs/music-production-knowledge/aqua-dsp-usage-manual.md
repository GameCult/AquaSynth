# AquaSynth DSP Usage Manual For Music Agents

kind: manual
tier: core-doctrine
topic: AquaSynth DSP usage for composition and production

## Owner

This manual owns the minimum practical DSP and DSL knowledge a musician agent
needs before writing a song candidate, so retrieval starts from usable
AquaSynth technique instead of copying weak prior patches.

## Transfer Rules

- Start with form, not timbre. Declare `meter`, harmonic center or progression,
  lane gates, section-level mix motion, and target duration before building
  voices.
- Treat every sound as a role owner. Leads, bass, drums, pads, and texture need
  separate control surfaces so the next agent can adjust them without surgery.
- Use reference analysis as constraints: tempo/autocorrelation for gates,
  register/root/scale for pitch lanes, band stats for filtering, and RMS/motion
  coverage for arrangement density.
- Write the simplest patch that makes ownership inspectable. Do not hide a song
  in one giant voice or one full-duration noise bed.

## Current AquaSynth Authoring Surface

Composition sugar:

```aqua
meter bpm=128 beats=4
sequence name=kick pattern=x...x...x...x... step=.125 high=.9 low=0
scale name=hook path=/seq/lead/freq root=220 scale=minor degrees=0,2,3,5,3,2 step=.25
chords name=pad root=220 scale=minor progression=0,5,3,4 voicing=0,2,4 paths=/chords/pad/root,/chords/pad/third,/chords/pad/fifth step=bar
mix name=pad points=0:.10,4:.22,8:.15,12:.30,16:.12
```

The sugar lowers into visible `param` and `curve` owners. Bind those paths from
voices with `gain=@/seq/kick`, `freq=@/seq/lead/freq`, or
`gain=@/mix/pad/gain`.

Voice-like leads:

```aqua
source_port name=lead_src kind=syrinx pressure=.75 opening=.42
acoustic_network name=lead_body source=lead_src radiation=beak
acoustic_voice name=lead network=lead_body freq=@/seq/lead/freq gain=@/mix/lead/gain
```

Use syrinx/acoustic ownership when the reference has singing, vocal chops,
creature calls, reed-like lead, or expressive vowel motion. Move pressure,
opening, filter/formant color, and pitch. A static syrinx badge is not a vocal
line.

Subtractive drums:

```aqua
sequence name=kick pattern=x...x...x...x... step=.125 high=1 low=0
voice name=kick_body wave=sine freq=54 gain=@/seq/kick env=ad attack=.002 decay=.14
voice name=kick_click wave=noise gain=@/seq/kick hpf=2500 lpf=7000 env=ad attack=.001 decay=.035
```

Drums need body plus skin. Use sine/triangle bodies for kick/snare weight,
filtered noise only for click, skin, dust, or air, and gates from target tempo.
Noise alone is a placeholder unless it has band limits, a gate, and a musical
job.

Additive/PAD beds:

```aqua
layer name=pad engine=pad freq=@/chords/pad/root gain=@/mix/pad/gain
harmonics name=pad_bank source=pad partials=1:.7,2:.25,3:.18,5:.08
spectrum name=pad_air source=pad_bank centroid=1800 bandwidth=.35
```

Pads own harmonic field and emotional bed. Use harmonic banks and slow
brightness/gain motion. Do not let a pad occupy every band for the full target
unless the reference really does.

Texture and recording color:

```aqua
texture name=dust_hat role=dust pattern=x..x..x. step=.125 gain=.08 sustain=30
texture name=air_wash role=air gain=.035 sustain=30
texture name=codec_bed role=codec gain=.04 sustain=30
```

Texture owns band-limited color, not arrangement. It should support a scene with
motion, gates, or slow modulation. A static broadband noise voice is failure
pressure.

## Production Checklist

- Producer brief: target feel, energy contour, meter, tonal center, section map,
  role map, and mix priority.
- First attempt: prove parsing, duration coverage, and audible lane ownership.
- Later attempts: change one hypothesis at a time: timing, register, role
  assignment, filter band, envelope, or section/mix motion.
- Listening journal: write what sounded alive, fake, static, missing, or too
  generic after every render.
- Gap ledger: name missing AquaSynth primitives or sugar only when a concrete
  production problem forced the workaround.

## Failure Modes

- Naming `kick`, `hat`, `pad`, or `syrinx` without audible role behavior.
- Copying one stock drum grid across unrelated targets.
- Letting texture keep the tail non-silent while composition dies.
- Hand-writing isolated frequencies when the reference suggests a tonal center
  or progression.
- Treating a metric bump as a lesson without a reusable owner sentence.

