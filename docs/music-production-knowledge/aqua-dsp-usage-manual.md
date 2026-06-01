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
  register/root/scale for pitch lanes, and log-mel spectrogram structure for
  filter/register/lane balance. RMS and motion are warning lights, not goals to
  golf.
- Between patch attempts, compare target and candidate log-mel images/features
  directly: where is energy missing, smeared, too bright, too static, or
  entering at the wrong time? Change the owner lane that explains that visual
  mismatch before touching global gain.
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

Reusable production abstractions:

```aqua
sub_kick_grid name=kit kick=x...x..x snare=....x... hat=x.x.xx.x step=.125
dust_hat pattern=x..x.x.. step=.125 gain=.045 sustain=30
bass_response name=bass root=110 progression=0,5,3,4 pattern=x..x.x.. scene=0:.16,8:.24,16:.18
additive_wash name=pad root=@/chords/bass_prog/root partials=1:.12,2:.07,3:.04 scene=0:.05,8:.14,16:.08
section_rise name=lead start=0 peak=12 end=24 low=.04 high=.18
```

These are not hidden instruments. They lower into visible `sequence`, `texture`,
`chords`, `mix`, `voice`, `layer`, and `harmonics` owners. Use them when the
role is actually present in the reference; rewrite their patterns and scene
points from the target's log-mel/onset evidence.

Voice-like leads:

```aqua
voice name=vowel_lead wave=saw freq=@/seq/lead/freq gain=@/mix/lead/gain sustain=.18 decay=.22 fm=3 fmi=.25 lpf=.62
```

Use syrinx/acoustic ownership when the reference has singing, vocal chops,
creature calls, reed-like lead, or expressive vowel motion, but only if the
patch includes a complete, known-valid acoustic graph scaffold. A lone
`source_port` or `acoustic_network` line does not render. Start from
`patches/advanced/syrinx-voice.aqua` or `patches/advanced/bird-syrinx.aqua`
when using syrinx in the swarm; otherwise use a formant-filtered ordinary lead
for the attempt and write the missing syrinx scaffold as an Aqua gap. A static
syrinx badge is not a vocal line.

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

`sub_kick_grid` is the reusable drum shorthand. It owns the subtractive drum
family: kick body, kick skin, snare body, snare skin, and hat tick, plus
tempo-derived lane gates. Keep the owner names but rewrite `kick`, `snare`,
`hat`, and `step` per target; copied lane strings are template pressure.

Additive/PAD beds:

```aqua
layer name=pad engine=pad freq=@/chords/pad/root gain=@/mix/pad/gain
harmonics name=pad_bank source=pad partials=1:.7,2:.25,3:.18,5:.08
spectrum name=pad_air source=pad_bank centroid=1800 bandwidth=.35
```

Pads own harmonic field and emotional bed. Use harmonic banks and slow
brightness/gain motion. Do not let a pad occupy every band for the full target
unless the reference really does.

`additive_wash` is the reusable harmonic-bed shorthand. It owns an additive/PAD
layer, harmonic partials, and a mix scene. Use it when the log-mel target shows
stable harmonic bed energy or section swells; do not use it to wallpaper over a
missing bass/drum/lead owner.

Texture and recording color:

```aqua
texture name=dust_hat role=dust pattern=x..x..x. step=.125 gain=.08 sustain=30
texture name=air_wash role=air gain=.035 sustain=30
texture name=codec_bed role=codec gain=.04 sustain=30
```

Texture owns band-limited color, not arrangement. It should support a scene with
motion, gates, or slow modulation. A static broadband noise voice is failure
pressure.

`dust_hat` is the reusable high-band texture shorthand. It owns gated spectral
dust, not percussion by itself. If the target spectrogram shows high-band ticks,
use `dust_hat`; if it shows drum body, use `sub_kick_grid` first.

Bass and section motion:

```aqua
bass_response name=bass root=110 progression=0,5,3,4 pattern=x..x.x.. scene=0:.14,8:.24,16:.16
section_rise name=lead start=0 peak=16 end=30 low=.04 high=.2
```

`bass_response` owns low-mid call/answer motion: a bass gate, chord root lane,
mix scene, driven bass body, and short pulse layer. Use it when log-mel bands
show low/mid movement between downbeats. `section_rise` owns one lane's
macro-energy contour; use it to place entrances, lifts, drops, and endings
where the target spectrogram actually changes.

## Production Checklist

- Producer brief: target feel, energy contour, meter, tonal center, section map,
  role map, and mix priority.
- First attempt: prove parsing, duration coverage, and audible lane ownership.
- Later attempts: change one hypothesis at a time: timing, register, role
  assignment, filter band, envelope, or section/mix motion.
- Before chasing a scalar metric, inspect target-vs-candidate log-mel evidence.
  Ask which visible region is wrong, which owner controls that region, and what
  single owner edit would test the hypothesis.
- Listening journal: write what sounded alive, fake, static, missing, or too
  generic after every render.
- Gap ledger: name missing AquaSynth primitives or sugar only when a concrete
  production problem forced the workaround.

## Failure Modes

- Naming `kick`, `hat`, `pad`, or `syrinx` without audible role behavior.
- Copying one stock drum grid across unrelated targets.
- Golfing RMS or another scalar while the spectrogram says the wrong lane,
  register, or section is being edited.
- Letting texture keep the tail non-silent while composition dies.
- Hand-writing isolated frequencies when the reference suggests a tonal center
  or progression.
- Treating a metric bump as a lesson without a reusable owner sentence.
