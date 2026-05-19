# Reference Synth Roadmap

AquaSynth needs external synthesis references for the same reason the
SFXR path was useful: a real target prevents the DSL from becoming a pile of
locally plausible knobs. The goal is not to clone every synth's internals. The
goal is to use known engines and patch formats as pressure tests, then rebuild
selected sounds through AquaSynth's own graph until the missing abstractions are
obvious.

## Objective

Build a reference-driven synth library workflow:

- Parse or describe external synth patches as structured reference models.
- Render references when legally and technically practical.
- Rebuild selected references in AquaSynth patch DSL.
- Compare audio with analysis metrics and listening notes.
- Promote only the abstractions that make repeated reference rebuilds simpler.
- Reject reference targets we cannot turn into parity tests. Parsing a format is
  inventory; proving AquaSynth can reproduce the behavior is the point.

The DSL should grow because a reference target demanded a missing invariant, not
because a field name seemed maybe useful at 2 AM.

## Live Invariants

- AquaSynth owns its patch graph. External synths are reference targets, not
runtime dependencies.
- Expressive parameters are part of the patch contract. Callers must be able to
  change exposed values at runtime through the hosted DSP parameter API instead
  of recompiling the patch.
- Reference importers must preserve source provenance, license status, and the
source format hash.
- Tests must compare against rendered fixtures, extracted parameter topology, or
known structural expectations that prove AquaSynth can express the same behavior.
Weak "it parsed" tests do not prove expressivity, and references without a
credible parity test path should not be consumed.
- AquaSynth Faust output can be rendered through Faust-generated C# for candidate
  audio comparison. Reference audio still needs a real source fixture or renderer;
  do not compare a rebuild only against itself and call that parity.
- DX7 reference audio uses `dexed-py` when available, with public-domain SysEx
  fixtures stored under the test project. These are development references, not
  package content.
- ZynAddSubFX source is pinned under `external/zynaddsubfx` as GPL
  test/development material. It may be compiled or wrapped only for parity
  tests; AquaSynth must not ship, link, or runtime-depend on it.
- Do not add a generic graph escape hatch until a specific family of references
forces it.
- When a reference target exceeds the current voice-centric DSL, document the
missing graph feature instead of smuggling it through unrelated parameters.

## Target Families

### New Lane: IPA Vocal Tract / Weksa

This is not merely another reference synth. It is a new authoring and rendering
lane: IPA text lowers into phonetic features, timed articulatory gestures,
morphology-specific tract/excitation curves, and Faust DSP. The roadmap lives in
[`docs/ipa-vocal-tract-roadmap.md`](ipa-vocal-tract-roadmap.md).

Pressure on AquaSynth DSL:

- IPA as an input surface, not an internal patch model.
- Phonology profiles separated from tract morphology.
- Gesture planning and coarticulation before audio rendering.
- Human and alien vocal anatomies driven by the same structured phonetic intent.
- Mockable tokenizer, feature mapper, gesture planner, morphology, emitter, and
  renderer seams.

First deliverable:

- Stage 0/1 IPA tokenization and vowel feature fixtures.
- Human baseline vowel tract-area planner.
- A small render proof that can say a few vowels before the supported IPA table
  expands.
- Optional eSpeak NG workout fixtures that render tiny text/IPA-adjacent speech
  references when `ESPEAK_NG` or an `espeak-ng` binary is available. These are
  intelligibility/timing references, not tract-anatomy truth.

### Tier 1: DX7/Dexed

Use this first. DX7 SysEx is compact, structured, and musically meaningful:
six operators, thirty-two algorithms, per-operator envelopes, ratios, feedback,
LFO modulation, pitch envelopes, key scaling, and output levels.

Dexed is a GPL-3.0 DX7-compatible open-source synth that is closely modeled on
the Yamaha DX7 and accepts SysEx cartridge data. Its `msfa` synth engine remains
Apache-2.0, which may matter if we use code as a reference rather than only
format notes.

Pressure on AquaSynth DSL:

- Operator graph representation.
- Multi-stage envelopes beyond ADSR.
- Algorithm presets as topology, not per-voice decoration.
- Feedback as a first-class edge.
- Ratio/fixed-frequency operator modes.
- Feature and audio parity tests against rendered references.

First deliverable:

- Add a `Dx7SysEx` parser for single voice and cartridge payloads.
- Convert one or two public-domain or project-authored DX7-style voices into
  `ReferencePatch` records.
- Build an AquaSynth DSL approximation using explicit FM templates.
- Add mel/envelope/spectral comparison against rendered Dexed output from the
  public-domain fixture. AquaSynth candidate rendering and `dexed-py` reference
  rendering exist, and `analog1.syx` voice 13 has the first thresholded parity
  rebuild. The next pressure is a harder voice that actually needs audible
  operator routing rather than a sine-like behavioral match. Staged operator
  envelopes now exist as readable `env=rl rates=... levels=...` declarations
  with per-segment curves. Direct DX7 rate/level lowering on `PRC SYNTH1`
  forced EG timing/gain calibration; the current applied-envelope lowering and
  stronger summed/cascaded route scale give PRC the same character with much
  closer stacked-modulation brightness. The hard PRC probe writes ignored listening artifacts under
  `artifacts/parity/dx7-prc-synth1` so metrics and ears can steer the next
  calibration pass together. Listening showed aggregate score was a weak judge
  for this target, so the hard PRC probe gates primarily on log-mel distance.
  The calibration ladder lives in
  [dx7-calibration-plan.md](dx7-calibration-plan.md) and should be followed
  before more PRC-specific tuning.

Source: [Dexed](https://github.com/asb2m10/dexed)

### Tier 2: ZynAddSubFX

ZynAddSubFX is a GPL-2.0-or-later synth with additive, subtractive, and PAD
synthesis engines. It ships a large instrument library and exposes formant
filters, state-variable filters, free envelope shapes, layered kits, modulation,
and effects routing.

Pressure on AquaSynth DSL:

- Additive harmonic banks.
- Non-ADSR envelope curves.
- Layered instruments and kits.
- Formant and state-variable filter intent.
- PAD-style spectral/wavetable generation as a distinct source type.
- Part effects versus voice-local effects.

First deliverable:

- Parse a small set of Zyn instrument XML files into a neutral reference model.
  The first parser/classifier slice exists for project-authored `.xiz`
  fixtures and supports plain XML plus gzip-compressed XML.
- Classify features used by each instrument before trying to synthesize them.
  The current classifier identifies active ADD/SUB/PAD engines, enabled kit
  items, layering, envelopes, free envelopes, LFOs, filters, formants, and
  effects.
- Rebuild one additive lead, one PAD texture, and one formant/vocal patch. The
  first rebuild catalog entries now parse/export and record the first Zyn
  missing abstractions. Named layers, additive harmonic-bank syntax, PAD
  spectral-cloud syntax, and staged rate/level envelopes for normal voices now
  exist; exact PADsynth wavetable generation, arbitrary Zyn free-envelope point
  curves, fuller kit/effect routing, and richer formant/vowel morphing remain
  pressure.

The upstream ZynAddSubFX engine source is pinned as `external/zynaddsubfx`
for parity tests, currently at `3ab608c432996ba4d582176572c0b0f82328c825`.
That checkout is GPL reference machinery, not AquaSynth implementation. Do not
vendor the upstream Zyn instrument-bank submodule until its preset
license/provenance is explicit enough for this repo. Use project-authored
fixtures for parser shape and only promote external fixtures with clean source
notes.

The current external-bank pressure survey is in
[zynaddsubfx-pressure-survey.md](zynaddsubfx-pressure-survey.md). Its strongest
signal is that named kit/layer routing should probably precede syntax golf for
PAD, additive banks, or formant motion.

The first language-golf response is `layer`: a named layer declaration owns
engine tags, optional MIDI key range metadata, default gain, and an effect-send
label while lowering to ordinary AquaSynth voices. It is intentionally only an
ownership/routing scaffold for now; it does not add PAD synthesis, free
envelopes, or effect buses by stealth.

The second response is `harmonics`: additive layers can declare ratio/gain
partial banks such as `harmonics layer=body root=220
partials=1:.16,2:.075`. The parser preserves the bank as authored structure
and lowers it to ordinary voices. This gives additive lead, drawbar, and future
partial-bank targets a reusable surface without pretending to implement Zyn
ADDsynth phase, bandwidth, or free-envelope details.

The third response is staged voice envelopes. Normal voices and layer defaults
can now use the existing readable rate/level surface:

```text
layer name=pad engine=pad env=rl rates=.5,.7,1.0,1.4 levels=1,.85,.7,0 gate=2.2
voice layer=pad freq=220
```

This promotes the envelope authority already used by operator graphs into the
voice path. It is enough to express layered PAD/vocal onset and release shapes,
but it is not yet a Zyn free-envelope point editor.

The fourth response is `spectrum`: PAD layers can declare a source-level partial
cloud that emits as a Faust wavetable source before normal voice treatment:

```text
layer name=pad_low engine=pad
spectrum layer=pad_low root=130.8128 spread=.012 partials=1:.07,1.5:.052
```

The source table is generated by an authoring-time PADsynth-style path:
harmonics are spread into a frequency-domain amplitude profile, deterministic
random phase is applied, one inverse FFT produces the looped table, and Faust
only reads that baked table. This gives PAD texture targets a reusable source
authority without inflating the voice graph. It still does not claim full Zyn
PAD engine parity across all harmonic profiles, randomness controls, or
multi-table pitch-zone behavior. `root`, `spread`, and `partials` are
authoring-time table shape, not runtime controls.

The canonical AquaSynth spelling for richer spectral-table shaping is neutral:
`pad_mode`, `pad_bandwidth`, `pad_bwscale`, `pad_profile`, and `pad_position`.
The Zyn importer may populate those fields from Zyn's PAD controls, and the
parser still accepts older `zyn_*` names in generated parity artifacts, but
authored or newly generated `.aqua` should not use reference-synth names for
general DSP intent.

Sources:

- [ZynAddSubFX](https://github.com/zynaddsubfx/zynaddsubfx)
- [ZynAddSubFX feature list](https://github.com/zynaddsubfx/zynaddsubfx)
- [Paul Nasca's PADsynth algorithm](https://zynaddsubfx.sourceforge.io/doc/PADsynth/PADsynth.htm)

### Tier 3: Faust Examples And Libraries

Faust remains the best reference for general DSP graph topology. Its official
repo contains categorized examples, and the standard libraries include
oscillators, filters, delays, reverbs, physical models, DX7, virtual analog
effects, wave-digital models, spatialization, and more.

Pressure on AquaSynth DSL:

- General signal graph expression.
- Multichannel routing.
- Delay networks and feedback delay networks.
- Reverb topologies.
- Physical modeling: waveguides, modal resonators, mass interaction, exciters.
- Wave-digital circuit trees.

First deliverable:

- Add an `ExternalFaustReference` catalog entry type that stores source path,
  license/provenance, graph family, and expected compile behavior.
- Select small fixtures from official examples first: comb string, additive
  drum, stereo reverb, physical model, WDF filter.
- Use Faust references to decide whether AquaSynth needs a lower-level graph DSL
  beneath the current instrument patch surface.

Sources:

- [Faust examples](https://github.com/grame-cncm/faust)
- [Faust library docs](https://faustlibraries.grame.fr/)
- [Faust physical models](https://faustlibraries.grame.fr/libs/physmodels/)
- [Faust reverbs](https://faustlibraries.grame.fr/libs/reverbs/)
- [Faust wave-digital models](https://faustlibraries.grame.fr/libs/wdmodels/)

### Tier 4: Surge XT

Surge XT is a modern GPL-3.0 hybrid synth with a large real-world patch corpus.
It is a strong long-term target, but it is too broad for the first import pass.

Pressure on AquaSynth DSL:

- Multi-scene patch structure.
- Multiple oscillator algorithms.
- Wavetables.
- Deep modulation routing.
- Filter/fx chains.
- Macro controls and performance mapping.

First deliverable:

- Inspect factory patch format and licensing.
- Build a feature extractor before attempting translation.
- Choose one simple subtractive patch, one wavetable patch, and one modulation
  patch as future targets.

Source: [Surge XT](https://github.com/surge-synthesizer/surge)

### Tier 5: Cardinal/VCV-Style Modular Patches

Cardinal is a GPL-3.0 self-contained open-source modular synth based on VCV Rack
code. It is the right target once AquaSynth needs to prove arbitrary modular graph
expression.

Pressure on AquaSynth DSL:

- Module graph representation.
- Cables as first-class edges.
- Control voltage semantics.
- Audio-rate versus control-rate modulation.
- Feedback and clocking.
- Polyphonic lanes.

First deliverable:

- Do not start here.
- When the lower-level graph layer exists, parse a small Cardinal patch and
  classify modules, ports, cables, and rates.

Source: [Cardinal](https://github.com/DISTRHO/Cardinal)

## Data Model Slice

Add reference models before importers grow teeth:

```csharp
public sealed record ReferencePatch(
    string Id,
    string Family,
    string Name,
    ReferenceSource Source,
    IReadOnlyList<ReferenceFeature> Features,
    IReadOnlyList<PatchParameter> Parameters,
    string? AquaSynthScript);

public sealed record ReferenceSource(
    string Kind,
    string Uri,
    string License,
    string Hash,
    string Notes);

public sealed record PatchParameter(
    string Path,
    string Label,
    float Default,
    float Min,
    float Max,
    float Step,
    string Unit,
    string AutomationRate,
    string Notes);
```

This keeps source authority and runtime control authority visible. A reference
can be "known target, not yet translated" without pretending it is an AquaSynth
patch, and an AquaSynth patch can say which values callers may vary without
changing graph shape.

## Runtime Parameter Surface

Faust supports this directly through UI controls such as `hslider`, `vslider`,
`nentry`, `button`, and `checkbox`. Hosted DSP code can expose a parameter map
and update control zones with `setParamValue`/`getParamValue` style APIs, so
AquaSynth should compile once for a graph shape and then push parameter values at
runtime.

AquaSynth DSL needs an explicit parameter declaration surface, likely separate
from modulation buses:

```text
param name=brightness path=/macro/brightness default=0.45 min=0 max=1 step=0.001
param name=strike path=/exciter/strike default=0.2 min=0 max=1 step=0.001
```

Lowering rules:

- Parameter paths must be stable across recompiles unless the script changes the
  declared path.
- Parameters lower to Faust controls with the declared default, range, and step.
- Audio-sensitive parameters should be smoothed at the signal crossing.
- Modulators and exposed parameters may target the same semantic value, but the
  combined authority must be explicit.
- Recompile only when the graph, routing, oscillator type, operator topology, or
  other structural shape changes.

## Work Order

1. Create reference model contracts, parameter contracts, and tests.
2. Add explicit patch parameter declarations and Faust parameter emission.
3. Add DX7 SysEx parser and feature extraction.
4. Add two DX7-derived AquaSynth DSL rebuilds with exposed macro controls.
5. Add audio fixture workflow for rendered references.
6. Add Zyn XML feature extraction.
7. Add Faust reference catalog entries and compile checks.
8. Reassess DSL shape: keep voice DSL, split lower-level graph DSL, or add
   explicit operator/routing sublanguage.
9. Only then touch Surge or Cardinal.

## Rejected Shortcuts

- Do not vendor giant preset banks before license and provenance are explicit.
- Do not translate everything into the current `voice` command by adding dozens
  of fields.
- Do not compare only parse success. That proves the parser is lenient, not that
  the DSL can express the sound.
- Do not use Faust as a hidden escape hatch for AquaSynth patches. If a reference
  requires raw Faust, the DSL is missing an abstraction or the reference belongs
  in a lower-level graph layer.

## Success Criteria

- A new agent can pick one target family and know where to start.
- Every reference fixture says what feature pressure it applies.
- The DSL roadmap is driven by reference failures, not taste fumes.
- AquaSynth can keep using readable reference patches while the synth library
  grows toward serious DSP graph expression.
