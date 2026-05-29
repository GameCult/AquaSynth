# Acoustic Graph Proprioception Pass

Date: 2026-05-29

The failure is not lack of research. The failure is that research vocabulary
outpaced owner contracts. The graph can name sources, contacts, radiation,
area, losses, branches, and wave clocks, but the baby-word witness still says
the machine does not own an open vowel, a nasal vowel transition, or a shaped
plosive as audible speech.

## Objective

Make AquaSynth's graph voice able to say first words before adding more vocal
machinery. `mama` and `papa` are not toy cases; they are the smallest useful
truth test for vowel body, lip closure/opening, nasal/oral coupling, source
continuity, and radiation color.

## Current Mechanism

`tract` authoring builds a high-resolution morphology surface:

- `VocalTract.Sections` owns section/index semantics.
- `AcousticPath.AreaControl` owns live area expressions from tongue,
  constriction, lip, and velum controls.
- `tract_motion` parses motor-intent slew and obstruction parameters, but graph
  area and delay lowering do not yet own those slew laws.

Generated tract lowering then compacts that morphology into a Faust-sized graph:

- up to ten oral area terminals,
- up to eight moving injection source ports,
- up to four contact terminals,
- one lip radiation port and optional nasal branch/radiation.

`FaustExport` lowers the typed graph into bidirectional segment state:

- segment delay and area reflection own propagation,
- sources inject pressure/flow into nodes,
- contact terminals store and release scalar pressure,
- radiation ports read boundary flow and emit output,
- probes observe selected source/contact/radiation internals.

The latest artifact is
`artifacts/parity/pink-trombone-utterance-logmel/20260529T141822464`.
It improved the `papa` drum-hit failure after lip radiation started following
live lip opening, but `mama` still reads as closed-mouth humming and
`thrombosis` remains mostly absent.

## Invariants

- Typed `AcousticPortNetwork` is the only vocal audio authority. Do not restore
  the old proxy tract renderer.
- Declared morphology resolution and compiled Faust graph density are separate
  truths. More tract sections must not imply more Faust terminals.
- The mouth has one owner. Lip opening owns path-end geometry and radiation
  aperture together.
- Source placement over a compact graph must be continuous enough for moving
  articulation. Do not restore one source terminal per tract cell by default.
- Contact state must describe a pressure/flow event in the tract, not an output
  click or plosive gain rescue.
- Listening verdicts override proxy metrics. A high cosine that cannot say
  `a` is evidence against the witness, not evidence for the voice.

## Jenga Findings

### Research Names Outpaced Owner Tests

The code has acquired real terms from the literature: source impedance, contact
pressure, radiation load, fractional delay, area scattering. Those names are
useful only when each has a falsifiable owner test. Right now the graph can
pass structural checks while failing the smallest speech task.

### The Model Has Too Many Local Truths

Source, contact, area, loss, radiation, and output balance each implement part
of a pressure/flow story. They are not all wrong, but they can each be locally
reasonable while the whole graph says "buzz", "hum", or silence. The lip
radiation bug was the clean example: path geometry knew the mouth opened, while
radiation aperture did not.

### The Vowel Body Has No Gate

The current parity lane can render utterance WAVs and report log-mel,
articulation, band ratios, and silence mismatch. It does not yet have a hard
vowel-body gate that says: before plosives, before fricatives, before
`thrombosis`, the compact graph must sustain an open `a` and a nasal/open
`ma` transition with audible oral vowel body.

That missing gate let complexity accumulate around the wrong failure surface.

### Discrete Components Are Carrying Continuous Work

The compacting pass fixed the worst Faust compile pressure, but the graph still
uses sparse banks of area terminals, source ports, and contact terminals as a
compiled approximation of continuous articulation. That may be acceptable as a
backend lowering choice, but the source/contact laws must become continuous
heuristics over that compact bank. Adding density is the expensive answer and
already proved toxic.

### Contact Release Is Not Yet Speech Mechanics

Graph contact release now reaches lip radiation. That was a real ownership cut.
It is still a scalar closure/reservoir/release pulse, with directional weights
and decay constants. A baby's `p` is a constrained-flow history through a
changing tract aperture, not a drum trigger. The latest `papa` improvement came
from fixing mouth ownership, not from adding release force.

### Motion Is Authored Outside The Acoustic Owner

`tract_motion` exists, and fixture curves smooth controls, but graph area and
delay semantics do not own motor smoothing/passivity. Abrupt morphology changes
can still enter fractional delay and area reflection directly. That leaves
gesture realism outside the organ that emits sound.

### The Probes Do Not Yet Watch The Missing Vowel

Source/contact/radiation probes proved the graph is alive upstream. They do not
yet report a vowel-body witness: formant/body energy, open-mouth radiation
state, nasal/oral balance, and per-phone windows for `mama`/`papa`. We are
observing the machine's organs, but not the baby-word invariant at the layer
where the user hears the failure.

### Verification Health Is Part Of The Machine

The native Faust render path is slow and has needed process cleanup and larger
timeouts. That pressure is not incidental. A graph that takes minutes to render
three toy words and previously overflowed debug compilation is already telling
us the implementation surface is too discrete and too costly.

## Cut Line

Stop adding acoustic features until the graph passes a vowel-body proof.

Before the next physics addition, add a focused witness for:

1. sustained open `a`,
2. `ma` as nasal closure into open vowel,
3. `pa` as lip closure/release into open vowel,
4. the same windows in the accepted utterance artifacts.

The witness should write listening WAVs plus a short report of active vowel
body, speech-band energy, mouth/radiation opening, and nasal/oral balance. It
should fail loudly when the result is closed-mouth humming, a drum hit, or
silence.

Candidate simplifications after that witness exists:

- demote contact release constants into a simpler aperture-shaped constrained
  flow primitive, or cut the scalar reservoir if it cannot prove speech value;
- move `tract_motion` slew into graph-owned area/delay expressions;
- replace nearest-node source attachment pressure with a continuous
  interpolated injection primitive over adjacent segment nodes;
- use Faust physical-modeling/delay primitives where they collapse generated
  graph bulk without hiding owner contracts;
- keep compact generated terminal counts unless a failed witness proves a
  specific missing sample point.

## Next Work

The next implementation pass should be a baby-word gate, not another graph
feature. Once sustained `a`, `ma`, and `pa` are visibly and audibly wrong or
right, the graph will have a clean target for cuts. Without that gate, every
new coefficient is just another little brick in the stack.
