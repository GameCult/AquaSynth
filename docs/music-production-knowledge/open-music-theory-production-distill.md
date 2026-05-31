# Open Music Theory And Production Textbook Distill

kind: source-distillation
tier: core-doctrine
topic: open music theory and digital production lessons for AquaSynth

## Sources

- Open Music Theory, https://viva.pressbooks.pub/openmusictheory/
- Music Theory for the 21st-Century Classroom, https://musictheory.pugetsound.edu/mt21c/
- Miller Puckette, The Theory and Technique of Electronic Music, https://msp.ucsd.edu/techniques.htm
- Hack Audio, https://www.hackaudio.com/

## Owner

This distillation owns source-grounded general music and production knowledge
for AquaSynth agents, so the music generator can reason from composition and
signal-production fundamentals instead of imitating previous weak candidates.

## Transfer Rules

- Meter is a promise about expectation. Use it to place accents, repetition,
  syncopation, fills, and section boundaries, not just to calculate step
  seconds.
- Melody needs contour, register, motive, and variation. A lead line should have
  a recognizable shape, then repeat, answer, invert, truncate, sequence, or
  intensify.
- Harmony is motion plus gravity. Even simple progressions should imply
  departure and return. Use roots, chord tones, pedal tones, and bass movement
  deliberately.
- Rhythm works through pattern, contrast, and hierarchy. Kick/snare/hat lanes
  should form a groove with strong and weak positions, not independent blinking
  lights.
- Timbre is spectrum over time. Envelopes, filters, modulation, and saturation
  should move the sound from attack to body to tail.
- Mixing is arrangement made audible. Gain lanes, frequency separation, and
  automation decide which role owns attention at each section.

## AquaSynth Patterns

Melodic motive:

```aqua
meter bpm=132 beats=4
scale name=lead path=/seq/lead/freq root=220 scale=minor degrees=0,2,3,5,3,2,0,-2 step=.25
sequence name=lead_gate pattern=x.x.xx.. step=.25 high=.75 low=0
mix name=lead points=0:.00,2:.25,6:.18,10:.32,14:.12
```

Harmonic field:

```aqua
chords name=prog root=110 scale=minor progression=0,6,3,5 voicing=0,2,4 paths=/chords/prog/root,/chords/prog/third,/chords/prog/fifth step=bar
mix name=pad points=0:.08,4:.18,8:.11,12:.24,16:.08
```

Groove hierarchy:

```aqua
sequence name=kick pattern=x...x..x....x... step=.125 high=1 low=0
sequence name=snare pattern=....x.......x... step=.125 high=.8 low=0
sequence name=hat pattern=x.x.x.xx.x.x.xxx step=.125 high=.18 low=.03
```

Production automation:

```aqua
mix name=bass points=0:.20,4:.26,8:.14,12:.31,16:.18
mix name=air points=0:.02,4:.05,8:.03,12:.07,16:.02
```

## Composition Moves

- Repeat with change: keep one parameter stable while another evolves. Examples:
  same rhythm with different degrees, same degrees with a denser gate, same pad
  chord with brighter filter motion.
- Call and response: a lead phrase should leave space for bass, percussion,
  chord stab, or texture answer.
- Register contrast: keep bass low, lead in a bounded melodic register, and pads
  wide but not louder than the role carrying attention.
- Section contrast: intro, main groove, break, rise, drop, and outro can be
  represented by lane gain curves before timbre gets fancy.
- Energy contour: density, brightness, loudness, and rhythmic subdivision are
  separate levers. Move one or two deliberately.

## Digital Production Moves

- Attack/body/tail: split percussive sounds into transient click/skin and tonal
  body; filter each component differently.
- Subtractive shaping: start rich or noisy, then use filters/envelopes to carve
  the useful band. Do not confuse raw oscillator choice with final timbre.
- Additive shaping: build sustained tones from harmonic relationships, then
  automate gain/brightness slowly.
- Modulation: use LFO/tremolo/FM/AM for motion, but keep rate and depth tied to
  the song grid or expressive gesture.
- Space and air: model ambience as low-gain shaped texture or delayed motion,
  not as a full-band noise floor.

## Failure Modes

- A loop with no phrase contour is not melody.
- A progression with no role using its chord tones is metadata, not harmony.
- A drum grid with no body/skin separation becomes weak noisy ticking.
- A pad with no entrance, exit, or brightness motion becomes wallpaper.
- A mix with every lane always on has no foreground.
- A source citation is not a lesson unless it lowers to a current AquaSynth
  action.

