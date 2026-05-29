# IPA Vocal Tract Roadmap

## Current Objective

AquaSynth is currently focused on a speech-learning parity harness. The active
machine is:

```text
structured utterance metadata
  -> learned utterance embedding
  -> learned synth automation
  -> vocal tract simulation plus envelopes, LFOs, filters, and spectral controls
  -> rendered audio
  -> loss against a simple speech synthesizer reference
```

Every arrow in that chain is a trainable artifact. The immediate job is to make
small utterances cheap to render, score, and backpropagate through, so hundreds
of controller weights can be tested against generated ground truth. The older
project of eating DX7, ZynAddSubFX, and other synthesizers remains useful
reference pressure, but it is not the current center of gravity.

The curriculum is deliberate:

1. master basic IPA-level speech parity against a simple reference;
2. grow from vowels into CV/CVC contrasts and core pulmonic consonants;
3. only then add intonation, prosody, emphasis, emotional context, and
   personality;
4. finally let Weksa and nonhuman morphologies stress the machine without
   letting IPA pretend it owns alien anatomy.

AquaSynth can get close to a Pink Trombone-class tract model, then move past it
by treating IPA as an authoring input rather than the synth itself. The machine
we want is not a giant table from symbol to sound. It is a pipeline:

```text
IPA text
  -> phonetic tokens
  -> language phonology profile
  -> timed articulatory gesture plan
  -> morphology-specific tract and excitation curves
  -> Faust DSP
  -> rendered speech, song, chant, and nonhuman vocalization
```

The target user feeds Weksa text as IPA or IPA-like phonetic strings. AquaSynth
keeps the real authority below that surface: phonetic features, gestures,
anatomy, excitation, and acoustic simulation.

## Boundary Bones

Three handoffs must stay visible in code, docs, tests, and host reports:

1. `PhoneticIntent` is the inspectable Weksa-to-AquaSynth contract. It carries
   IPA tokens, feature bundles, timing, and prosody. It does not know tract
   section areas or Faust.
2. `ArticulatoryPlan` is the AquaSynth-owned realization layer. It carries
   gestures and tract targets after morphology constraints have had a chance to
   bite. It does not pretend an impossible phone was rendered honestly.
3. `ArticulatoryConstraintReport` is the host-visible failure surface. If a
   morphology cannot form a requested articulation, the report names the phone,
   source event, missing capability, and severity.

The first proof artifact is `VocalTractPlanResult`: it packages the intent,
morphology, constraint report, optional articulatory plan, and
`VocalTractHostReaction`. A host can reject `/pa/` for a beaked morphology by
reading `missing_bilabial_capability`, or it can accept `/a/` for a human
baseline and inspect the emitted glottal-source and vowel-area gestures. This is
not audio yet. It is the first visible branch where the machine refuses to lie.

Those records are the first defense against liar-slurry: no parser, planner,
renderer, or host should need to infer whether IPA intent, physical anatomy, or
DSP output owns a decision.

## Objective

Build a research-grounded vocal-tract instrument for human and alien speech:

- parse IPA into structured phonetic intent;
- lower phonetic intent through a language profile into articulatory gestures;
- render those gestures through human and nonhuman tract morphologies;
- keep coarticulation continuous rather than stepping between phoneme presets;
- make every layer testable with mocks, fixtures, and deterministic outputs;
- grow supported IPA exponentially as the tract model earns new physics.

The Weksa goal is conlang speech rendering for Zyphos worldbuilding. IPA gives
authors a familiar entry point. The tract model gives us the power to make Weksa
sound like Weksa instead of like English wearing ceremonial markup.

## Research Spine

The first implementation should stand on old, durable work:

- Kelly-Lochbaum / digital waveguide vocal tract models simulate 1D wave
  propagation through connected tube sections using reflection coefficients.
  This is the practical spine behind Pink Trombone-like realtime tract synthesis.
- Pink Trombone demonstrates that a simplified tract, glottis, turbulence, oral
  cavity, and nasal coupling can produce interactive intelligible speech-like
  sound in realtime.
- Liljencrants-Fant-style glottal source models give a better voiced excitation
  than a naive sawtooth or pulse train, and can parameterize voice quality.
- Articulatory phonology and task dynamics provide the right control model:
  phonetic units become overlapping gestures, not isolated frames.
- Vocal-tract area-function work is the middle layer between anatomy and DSP:
  vowels and constrictions can be expressed as cross-sectional area over tract
  distance before being lowered to reflection coefficients.
- IPA feature databases and phoneme-table implementations are reference
  material for metadata, not authorities over the sound. They help keep the
  parser and feature mapper honest while the gesture and morphology layers own
  synthesis.
- Source-filter speech theory still matters, but this lane should not collapse
  back into static formant filters. Formants are useful diagnostics; tract shape
  is the synthesis authority.

Useful starting sources:

- Pink Trombone / Neil Thapen interactive tract model:
  <https://dood.al/pinktrombone/>
- Pink Trombone overview at IMAGINARY:
  <https://www.imaginary.org/program/pink-trombone>
- Recent Pink Trombone optimization paper describing PT as a realtime
  Kelly-Lochbaum tract model:
  <https://link.springer.com/article/10.1186/s13636-025-00414-5>
- Story 2005 vocal-tract area-function model:
  <https://bpb-us-e2.wpmucdn.com/sites.arizona.edu/dist/f/80/files/2023/10/story_jasa2005-1.pdf>
- Kelly-Lochbaum / time-domain articulatory synthesis summary:
  <https://www.isca-archive.org/eurospeech_1989/owens89b_eurospeech.html>
- Waveguide vocal tract acoustics:
  <https://eprints.whiterose.ac.uk/id/eprint/3713/>
- Voice source model comparison including LF:
  <https://pmc.ncbi.nlm.nih.gov/articles/PMC4491021/>
- LF model fitting for speech synthesis:
  <https://www.cs.cmu.edu/~awb/papers/is2013/is2013_lfmodel.pdf>
- Haskins TADA gestural planning model:
  <https://www.haskinslaboratories.org/tada>
- PanPhon IPA feature vectors:
  <https://pypi.org/project/panphon/>
- PHOIBLE phoneme inventories and distinctive features:
  <https://phoible.org/>
- eSpeak NG phoneme-table implementation reference:
  <https://deepwiki.com/espeak-ng/espeak-ng/4.2-phoneme-model>
- Utterance parity research notes:
  <utterance-parity-research.md>

## Architecture

### 1. IPA Parser

Owns Unicode IPA tokenization and notation, not sound.

Inputs:

- IPA string;
- optional language tag;
- optional inline stress, length, tone, syllable, and boundary marks.

Outputs:

- ordered `PhoneticToken` records;
- diacritic attachments;
- suprasegmental marks;
- source spans for diagnostics.

The parser should be table-driven and deterministic. It should reject unknown
clusters with useful diagnostics instead of guessing heroically and making a new
little bureaucracy out of Unicode.

### 2. Phonetic Feature Mapper

Owns IPA-to-feature interpretation.

Examples:

- `/p/` -> voiceless bilabial stop;
- `/ɬ/` -> voiceless alveolar lateral fricative;
- `/qʼ/` -> uvular ejective stop;
- `/aː/` -> long open vowel, front/central depending on profile defaults.

Outputs:

- `PhoneticSegment` with manner, place, voicing, airstream, length, stress,
  vowel height/backness/rounding, and diacritics.

This layer should support language profiles. IPA has broad and narrow uses, and
Weksa may assign phonemic categories to articulatory targets that are only
human-adjacent. PanPhon, PHOIBLE, and eSpeak NG are useful references for feature
schemas and inventory sanity checks, but AquaSynth should own its feature record
instead of importing a third-party schema as the permanent internal model.

### 3. Gesture Planner

Owns time and coarticulation.

Inputs:

- phonetic segments;
- language timing profile;
- prosody vector;
- speaking style;
- morphology capability map.

Outputs:

- overlapping gestures for tract constriction, tongue body, tongue tip, lips,
  velum, glottis, pressure, turbulence, burst release, and auxiliary organs.

This is where consonants and vowels smear into each other. Stops close before
their release. Vowels pull neighboring consonants. Fricatives hold a narrow
constriction and noise source. Nasals open a branch. Ejectives and clicks become
pressure/airstream events rather than cute punctuation.

The gesture planner should resemble a small task-dynamics compiler: phone
features become overlapping constriction tasks and source events. It should not
linearly interpolate phone presets unless a test proves that shortcut works for
the current stage.

The first learned controllers live below the handoff and above rendering. Weksa
or another language layer may provide speech text embeddings, prosody/emphasis
hints, and Ghostlight/Epiphany-shaped character state vectors, but AquaSynth owns
the gradient descent. `UtteranceEmbeddingNeuralEncoder` trains a tiny model to
compress those ingredients into one utterance embedding. `VocalTractNeuralMapper`
then takes phonetic features plus that embedding and predicts tract controls.
Both controllers use the same native C# packed-network path: row-major buffers,
unsafe hot loops, batched gradients, and Adam or SGD updates. For chained speech
training, `SpeechBackpropagationPipeline` runs the synth-driver loss backward
through the utterance embedding, slices out the embedding gradient from the
synth-driver input gradient, and updates the utterance encoder from that upstream
loss. The synth-driver output includes core tract parameters plus expressive
synthesis lanes: AM depth, FM depth, LFO rate/depth, filter cutoff, filter
resonance, and a full mel-frequency spectral envelope vector for PAD-style
wavetable/formant coloration. Those lanes are not license to bypass tract
anatomy; they are renderable expression controls the later synth can choose to
honor.

The first Weksa handoff target is `weksa.utterance_embedding_handoff.v0.1`.
AquaSynth should treat it as the training input contract:

- 1024-float `bge-m3:latest` speech text embedding for semantic text evidence;
- variable-length PanPhon feature sequence for IPA, phones, pronunciation, and
  alien speech-shape evidence;
- 256-float AquaSynth-owned phonetic realization embedding produced by
  `aquasynth.panphon_sequence_encoder.v0.1`;
- 32-float deterministic prosody/emphasis hint vector;
- 64-float projected Ghostlight/Epiphany-shaped character-state vector;
- 64-float AquaSynth-owned learned utterance embedding output.

The first Weksa v0.1 training artifact now exists at
`E:\Projects\weksa\examples\speech-training\tiny-panphon-v0.1\batch.json`.
It covers the crawl-stage IPA set `a`, `pa`, `ta`, `ka`, `sa`, and `ma` with
fixed vector widths, zeroed semantic text embeddings, inline PanPhon 22-feature
sequence evidence, deterministic prosody hints, and an Epiphany-compatible
neutral character-state vector. Toy vectors are allowed for plumbing tests only.
Do not feed IPA strings into the semantic text embedding channel and pretend
that it means speech shape; English semantics and PanPhon sequence evidence are
separate inputs. AquaSynth owns the learned compression from PanPhon sequence to
256D phonetic realization embedding.

#### Gesture Score

Gesture score measures the control/timeline layer before final audio. It is not
a log-mel proxy and not a reward for decorative noise layers. It answers one
question: did the IPA/phonetic intent become a plausible anatomical gesture over
the public `ControlSurface`/`ControlSpline` API?

The first scoring contract:

- `coverage` (`0.25`): required organs and surfaces are touched, forbidden
  surfaces remain quiet, and optional helpers are weighted lightly. A voiceless
  labial-velar fricative should touch source voicing/noise, lip aperture, velar
  constriction, constriction turbulence/contact, and should not open the velum
  like a nasal.
- `direction` (`0.25`): each touched surface moves the correct way. Plosives
  close then release; nasals open the velum; voiceless segments reduce voicing
  and may raise noise; vowels move tongue/lip surfaces toward
  height/backness/rounding targets.
- `contour_timing` (`0.20`): sampled spline shapes match the expected gesture
  morphology. Stops need closure hold and release, fricatives need a steady
  narrow constriction, vowels need smooth target movement, and nasals need an
  open velum plateau.
- `primitive_timeline` (`0.20`): `ProbeTimelineReport` shows the expected
  physical consequences: contact opening/reservoir/released flow for stops,
  branch admittance for nasals, source flow/noise for fricatives, and radiation
  aperture for lip/vowel gestures.
- `external_articulation` (`0.10`, optional): when rtMRI/video/manual landmark
  evidence exists, compare coarse normalized trajectories such as lip aperture,
  tongue body/front/back proxy, constriction location/opening, velum state, and
  voicing state. When only audio exists, this term is weak evidence and should
  mostly yield to clean/full audio scores.

The weights are defaults, not scripture. They should be recorded in reports and
made tunable per training run, but changing them is a scorer-version change.

`phoneme_gesture` and future phrase templates are only spline emitters. They
may seed the expected surface set from IPA descriptors such as
`voiceless_labial-velar_fricative`, but local reference datasets are allowed to
golf the numeric targets, timings, and patch code. The score must report the
expanded splines so a passing gesture cannot hide behind a later audio helper.

### 4. Morphology Model

Owns anatomy.

Human baseline:

- tract length;
- section count;
- palate/tongue mapping;
- lip opening/protrusion;
- velum and nasal branch;
- one glottal source;
- oral tract radiation.

Alien/Weksa extensions:

- longer or shorter tract families;
- asymmetric or split oral branches;
- additional resonant sacs;
- secondary glottal or membrane exciters;
- nonhuman nasal/side-channel coupling;
- constrained articulator ranges by species or caste;
- morphology-specific mappings from human IPA-like features to physical organs.

IPA does not get to own alien anatomy. IPA names intent; morphology realizes it.

### 5. Tract DSP

Owns sound.

First spine:

- 1D Kelly-Lochbaum-style tube sections;
- reflection coefficients from section area;
- glottal excitation at the source;
- lip radiation;
- nasal branch switch/coupling;
- turbulence injection at constriction points;
- closure and burst primitives for stops.

Use area functions as the stable middle representation: a morphology and gesture
plan produce tract-area curves over time; the DSP compiler turns those curves
into reflection coefficients, source injection points, and coupling events.

Later spine:

- losses and dispersion;
- lip/larynx impedance refinements;
- side branches and sacs;
- multiple sources;
- differentiable or search-assisted parameter fitting for vowel targets;
- morphologies that are not merely scaled human throats.

Generated Faust should remain boring enough to inspect. If tract DSP becomes
large, generate a specialized Faust module from a typed tract plan instead of
stuffing a hundred magic controls into ordinary `voice`.

## Supported IPA Roadmap

Do not support all IPA first. That is the trap where the parser looks mighty and
the synth says five ugly vowels. Expand by doubling the useful surface each time
the physical model earns another category.

### Stage 0: Token Spine

Goal: lossless-enough IPA tokenization and diagnostics.

Support:

- whitespace and word boundaries;
- `/.../` and `[...]` wrappers;
- primary and secondary stress;
- length marks `ː` and `ˑ`;
- common tie mark for affricates;
- combining diacritic storage without full interpretation.

Tests:

- round-trip token spans;
- unknown-symbol diagnostics;
- diacritic attachment fixtures.

### Stage 1: Vowels And Simple Voice

Goal: intelligible sustained vowel targets through a human tract.

Support:

- monophthong vowels: `i y ɨ ʉ ɯ u ɪ ʏ ʊ e ø ɘ ɵ ɤ o ə ɛ œ ɜ ɞ ʌ ɔ æ ɐ a ɶ ɑ ɒ`;
- length and stress as duration/prosody modifiers;
- modal voiced glottal source;
- simple pitch, intensity, and breathiness controls.

Machine growth:

- vowel feature space;
- tract area targets informed by area-function references;
- glottal source model;
- vowel fixture renderer.

Validation:

- check approximate formant regions for `/i e a o u ə/`;
- compare tract-area curves against known vowel-shape expectations;
- keep listening fixtures because a formant table can still lie with confidence.

### Stage 2: Core Pulmonic Consonants

Goal: CV, VC, and CVC syllables with recognizably different places and manners.

Support:

- stops: `p b t d k g ʔ`;
- nasals: `m n ŋ`;
- fricatives: `f v s z ʃ ʒ h`;
- approximants: `w j l ɹ`;
- basic aspiration `ʰ`;
- syllable and word timing.

Machine growth:

- closure/release events;
- plosive burst noise;
- fricative turbulence source;
- nasal branch;
- simple coarticulation curves.

Validation:

- verify voice onset timing for stops;
- verify burst timing and broad noise bands;
- verify nasal/oral contrast;
- reject fricative support that is only a filtered noise preset with no
  constriction/source location.

### Stage 3: Expanded Human IPA

Goal: useful conlang coverage without pretending to be a universal phonetician.

Support:

- places: dental, alveolar, postalveolar, retroflex, palatal, velar, uvular,
  pharyngeal, glottal;
- manners: trills, taps/flaps, lateral fricatives, lateral approximants,
  affricates;
- symbols: `θ ð ɕ ʑ ç ʝ x ɣ χ ʁ ħ ʕ ɸ β ʂ ʐ ɲ ɳ ɴ ɾ r ʀ ɭ ʎ ʟ ɰ`;
- affricate tie sequences such as `t͡s`, `d͡ʒ`, `t͡ɬ`;
- voiceless/voiced and dentalized/palatalized/labialized/velarized diacritics
  where they map to real gestures.

Machine growth:

- tongue tip/body split;
- lateral constriction;
- trill/tap event generators;
- profile-specific place maps.

This is the point where a language profile starts earning its keep. The same IPA
symbol may need different timing, place bias, or allophonic behavior by language;
that belongs in the profile, not in the global token parser.

### Stage 4: Airstream And Phonation

Goal: Weksa can use sounds that feel physically intentional instead of being
ornamental apostrophes.

Support:

- ejectives: `ʼ`;
- implosives: `ɓ ɗ ʄ ɠ ʛ`;
- clicks: `ʘ ǀ ǃ ǂ ǁ`;
- breathy, creaky, slack/stiff voice diacritics;
- voiceless vowels and sonorants;
- tone marks if Weksa needs lexical or prosodic tone.

Machine growth:

- pressure reservoirs;
- glottal closure timing;
- ingressive/egressive event modeling;
- richer glottal source parameterization;
- non-pulmonic test fixtures.

### Stage 5: Weksa And Alien Morphologies

Goal: IPA can drive nonhuman vocal anatomy without reducing aliens to exotic
human accents.

Support:

- morphology profiles with declared organ inventory;
- feature remapping per morphology;
- unsupported-feature diagnostics that explain the missing organ or gesture;
- dual-source phonation;
- resonant sacs and side branches;
- nonhuman lateral or split-tract continuants;
- authored Weksa phoneme inventories and allophones.

Machine growth:

- morphology capability negotiation;
- tract graph beyond a single tube plus nasal branch;
- multi-source DSP;
- Weksa fixture corpus;
- Zyphos scene hooks for speaker, body, place, and emotional pressure.

## Testing And Mocking

The implementation should be built as a set of injectable services:

- `IIpaTokenizer`
- `IPhoneticFeatureMapper`
- `IPhonologyProfile`
- `IGesturePlanner`
- `ITractMorphology`
- `ITractPlanCompiler`
- `ITractFaustEmitter`
- `IAudioRenderer`
- `IReferenceFixtureStore`

Mock seams are not ceremony here. They protect the machine:

- tokenizer tests do not need DSP;
- feature tests do not need coarticulation;
- gesture tests can snapshot control curves;
- morphology tests can assert capabilities and remaps;
- Faust emitter tests can verify generated structure without rendering;
- renderer tests can compare short deterministic buffers;
- Weksa profile tests can prove unsupported IPA fails clearly.

Golden fixtures:

- IPA tokenization fixtures;
- feature bundle fixtures;
- gesture-plan JSON fixtures;
- tract-area curve fixtures;
- rendered vowel and CVC WAV artifacts;
- Weksa phrase fixtures once the language profile exists.

Metrics:

- formant target distance for vowels;
- envelope and burst timing for stops;
- noise-band distance for fricatives;
- nasal/oral spectral contrast;
- intelligibility/listening notes for complete syllables;
- script/IPA terseness only after audio is credible.

## Speech Parity Harness

The first harness must stay humble and fast:

- render a tiny utterance set from eSpeak NG or another compact reference;
- extract normalized log-mel, timing, and envelope evidence;
- train `SpeechBackpropagationPipeline` through the utterance encoder and synth
  driver;
- drive a compiled Faust candidate by changing `/speech/output/N` controls, not
  by recompiling the patch for every weight probe;
- write reports under ignored `artifacts/parity/...` so metrics and listening
  receipts stay visible.

Ground truth means "reference pressure," not "the truth about throats." eSpeak
can teach early intelligibility, timing, and rough spectral targets. It cannot
decide AquaSynth's anatomy, coarticulation, or Weksa morphology.

Training reports must keep three scores separate:

- `gesture_score`: descriptor/spline/primitive-timeline evidence for whether
  the intended phoneme became the right anatomical motion. This is measured
  before final audio.
- `clean_vocal_score`: broad phoneme identity from the vocal primitive path
  with minimal dressing. This checks whether the tract machine is doing the
  work.
- `full_parity_score`: the whole AquaSynth patch against the target reference,
  including FM, AM, modulators, envelopes, filters, added animated voices,
  breath/noise/room/mic emulation, and post-processing.

The full parity script may use all normal AquaSynth synthesis tools around both
the voice output and the gesture input controls. That flexibility is necessary:
IPA reference clips include speaker anatomy, microphone color, room tone,
loudness, pitch, and background noise. The harness must let patches model those
conditions without forcing the tract graph to lie. The price is accounting:
full parity improvements are not accepted as articulation improvements unless
`gesture_score` and `clean_vocal_score` move coherently too.

Training receipts must keep time as a witness. Every request, result, and
checkpoint should record when each stage made its decision, how much latency it
spent, what latency budget it was meant to respect, and how confident the
mapping was. A tiny audible witness that sounds right but arrives from a
clockless swamp is already teaching the model to lie with a lovely voice.

### Frozen IPA Gesture Rounds

`IpaGestureExperiment.WriteRound` is the first batch surface for IPA hypothesis
work. It writes a frozen round bundle under a caller-provided artifact root:

- `manifest.yaml`: target IPA descriptors, variant knobs, candidate script
  paths, primitive timeline paths, and the authority boundary for the round.
- `candidates/*.aqua`: deterministic primitive vocal scripts using
  `phoneme_gesture` plus the public `ControlSurface`/`ControlSpline` path.
- `timelines/*.csv`: `ProbeTimelineReport` output for each candidate before
  audio parity.
- `metrics.csv`: first-layer `gesture` metrics only:
  `surface_coverage`, `motion_direction`, `contour_timing`,
  `primitive_timeline`, and `gesture_score`.
- `evidence.jsonl`: one short machine-readable receipt per candidate for later
  science passes.

`IpaGestureExperiment.AnalyzeRound` adds the deterministic science packet:

- `analysis/metric-summary.csv`: per-target metric mean, min, max, spread, and
  best/worst candidate IDs.
- `analysis/candidate-clusters.csv`: `strong`, `workable`, and `weak`
  candidate bands by `gesture_score`.
- `analysis/science-brief.md`: a compact handoff for a human or sub-agent to
  inspect score surfaces, clusters, and next hypothesis pressure.

This round writer owns frozen candidate evidence. It does not own clean vocal
identity, full spectrogram parity, optimizer checkpoints, or distributed worker
orchestration. Those stages may consume the bundle later, but they must add
their own scores instead of rewriting gesture evidence after the fact.

The intended scale-out loop is brutally simple:

1. Generate a round of descriptor/variant candidates and primitive timelines.
2. Hand the frozen bundle to a science worker for loss-landscape summaries,
   clustering, outlier detection, and next-hypothesis recommendations.
3. While that worker runs, generate another round with new DSL or patch
   variations.
4. Merge only conclusions that improve the layered evidence ledger:
   `gesture_score` first, then `clean_vocal_score`, then
   `full_parity_score`.

### IPA Trial Render Workers

`IpaTrialOrchestrator.RunAsync` is the first local render/scoring worker for
that loop. It takes trial target sets, asks `IpaGestureExperiment` to generate
candidate patches, renders each candidate through Faust, compares it against a
local PT fixture reference with `AudioAnalyzer`, writes WAV/report artifacts,
and stores typed `IpaTrialResult` records in `ipa-trial-results.cc`.

The `.cc` store is per-record CultCache MessagePack data, not a hand-written
JSON ledger. `IpaTrialResult` records include hypothesis text, candidate patch
URI, reference/candidate artifacts, primitive timeline URI, metrics, evaluator
summary, verdict, known contamination, and timing receipts. This is the trial
result database that later hypothesis workers consume.

The first five seed trials were run locally on 2026-05-29 under
`artifacts/parity/ipa-trials/20260529T214127955/five-seed-trials`. The first
attempt proved the pipeline but showed vowels/nasals were nearly silent. The
accepted refinement moved voiced source excitation into primitive `SourcePort`
lowering instead of relying on radiation to high-pass DC flow. After that cut:

- open `a`: log-mel cosine `0.5693`, RMS ratio `0.3033`;
- front `i`: log-mel cosine `0.3536`, RMS ratio `0.5721`;
- bilabial nasal `m`: log-mel cosine `0.5199`, RMS ratio `0.2688`;
- alveolar fricative `s`: log-mel cosine `0.5498`, articulation `0.4111`;
- bilabial plosive `p`: still weak, log-mel cosine `-0.1024` despite RMS ratio
  `0.6481`.

The evidence says the source-carrier cut fixed the voiced-air failure. The next
pressure is plosive closure/release ownership and stronger vowel/nasal tract
color, not decorative full-patch dressing.

### External Codex Trial Loop

`tools/IpaTrialWorker` is the command-line trial organ for external agents. It
keeps measurement and CultCache writes inside AquaSynth instead of asking Codex
workers to hand-edit evidence:

- `seed` renders the baseline five trial sets into a shared
  `ipa-trial-results.cc` store.
- `score` reads agent-authored `.aqua` candidates, renders and scores them
  against the PT fixtures, and upserts typed `IpaTrialResult` records.
- `search` is the semantic retrieval surface for the `.cc` store. It expands
  speech-control vocabulary such as vowel/nasal/fricative/stop, ranks records
  with metric-aware evidence bias, and writes a compact markdown result set.
- `show` drills into one trial or candidate with full metrics, artifacts,
  known contamination, hypothesis, and evaluator summary.
- `dump` remains an audit escape hatch, not the normal agent memory path.

`tools/run-ipa-trial-loop.ps1` orchestrates the outer loop with external
`codex exec` workers. Each round searches accumulated trial memory, asks a
hypothesis worker to write a batch of candidates, scores those candidates
through `IpaTrialWorker`, searches the updated store, then asks a science
evaluator to judge which hypotheses held up.

The trial shape is five target sets with five phoneme lanes each:

- vowels: `a`, `i`, `u`, `e`, `o`;
- nasals/approximants: `m`, `n`, `ng`, `l`, `r`;
- fricatives: `s`, `z`, `f`, `v`, `th`;
- stops: `p`, `b`, `t`, `d`, `k`;
- mixed generalization: `mix-a`, `mix-m`, `mix-s`, `mix-p`, `mix-u`.

Agent-authored candidates must be named
`<targetId>__<family>__<hypothesis-name>.aqua`, where `family` is lowercase
kebab-case and is the canonical hypothesis-family id for evaluator grouping.
Each round writes exactly 25 candidates: one per target lane. The point is not
one-patch golf. A useful round tests whether a contour, source, tract,
radiation, or DSL-lowering idea generalizes across the target sets. Full-patch
FM/AM/noise dressing is allowed as parity pressure, but the evaluator must
label it as dressing when primitive or gesture evidence does not improve.
The loop also judges evidence quality directly: retrieval receipts should be
specific, comparable, falsifiable, and reusable by the next round. A pretty
report with vague receipts is a failed science artifact even if it obeys the
file-count contract.

## CultMesh Render/Scoring Work

CultMesh is the transport, admission, and worker-orchestration layer for speech
render/scoring work. It is not a magical distributed trainer where every
compatible device mutates model weights directly. The authority trainer owns
the active `SpeechTrainingCheckpoint`; workers may produce gradients and witness
receipts, but they do not apply optimizer steps or commit new checkpoints.

The clean path is:

1. AquaSynth emits typed `SpeechRenderRequest` documents.
2. CultMesh transfers the required worker payloads: compiled Faust renderer,
   scoring code, small model or encoder assembly, batch/artifact references.
3. Compatible runtimes admit the payload locally under a worker lease.
4. Workers render and score examples, then return `SpeechRenderResult`
   documents with loss, gradients, artifacts, and timing receipts.
5. The authority trainer aggregates/applies gradients through
   `SpeechBackpropagationPipeline`.
6. New checkpoints are committed through normal CultCache/CultMesh shard
   authority.

The first implemented layer in AquaSynth now covers payload manifests, CultMesh
peer/lease admission, worker assignment, result collection, and central
application through `SpeechBackpropagationPipeline`. This is enough to run a
distributed training step against leased CultMesh workers that return
`SpeechRenderResult` gradients. Real network artifact chunk transfer and remote
process supervision still belong to CultMesh infrastructure; AquaSynth's
authority boundary is ready for that transport instead of inventing its own.
Federated averaging can be explored later only after the simple authority path
is boring.

## Optional eSpeak NG Workout

The first IPA-adjacent parity lane uses eSpeak NG as a development reference.
It is not an anatomy oracle. eSpeak NG is a compact formant-synthesis TTS system
that can render speech to WAV and emit IPA transcriptions from text. That makes
it useful for early intelligibility, phoneme timing, and broad coverage pressure
while AquaSynth grows its own tract model.

The optional test `EspeakNgReferenceRendererWritesTinyIpaWorkoutWhenInstalled`
looks for `ESPEAK_NG` or `espeak-ng`/`espeak` on `PATH`. When present, it renders
a tiny workout under ignored artifacts:

```text
artifacts/parity/espeak-ng-ipa-workout/<timestamp>-tiny/
  open-vowel.wav
  bilabial-open.wav
  alveolar-open.wav
  velar-open.wav
  sibilant-open.wav
  nasal-open.wav
  report.md
```

The report records the source text, eSpeak IPA transcription, sample counts, and
basic audio features. It also records normalized log-mel spectrogram summaries
per fixture and a compact log-mel distance matrix across the workout. This is
the first external speech judge for the vocal tract lane. It should pressure
AquaSynth's future IPA/gesture/render path, not replace it.

The optional test `EspeakNgGroundTruthTrainsUtteranceEmbeddingAndSynthDriverWhenInstalled`
turns the same idea into a gradient-descent fixture. When eSpeak is present it
renders the tiny syllable set, extracts normalized log-mel means, and runs
`SpeechBackpropagationPipeline`: the eSpeak-seeded synth-driver target loss
updates `VocalTractNeuralMapper` and backpropagates through the utterance
embedding into `UtteranceEmbeddingNeuralEncoder`. It writes WAVs plus a training
report under:

```text
artifacts/parity/espeak-ng-gradient-descent/<timestamp>-tiny/
  *.wav
  training-report.md
```

This is supervised pressure from generated speech evidence. It is not yet
waveform-loss backprop through an AquaSynth renderer.

## First Work Packets

1. Add `docs/ipa-vocal-tract-roadmap.md` and preserve the boundary doctrine.
2. Add an IPA tokenizer and feature model with Stage 0/1 fixture tests.
3. Add a human baseline vowel planner that emits tract-area targets, not audio.
4. Add a tiny tract renderer prototype in AquaSynth.Faust or a test-only lab
   lane, then decide the package boundary once the prototype proves itself.
5. Connect the eSpeak/log-mel gradient fixture to the first audible AquaSynth
   vowel/tract renderer once the renderer can produce comparable buffers.
6. Render five vowels and three CVC syllables through fixtures.
7. Add the first Weksa phonology profile with explicit unsupported-feature
   diagnostics.
8. Only then add Stage 2 consonant synthesis to the shipping path.

## Non-Goals

- Full CFD, finite-element airflow, or anatomical medical simulation.
- A black-box neural TTS voice that hides anatomy and cannot explain itself.
- A complete IPA parser before the tract can render basic syllables.
- Alien sounds as random effects. Nonhuman output needs morphology, not glitter.
