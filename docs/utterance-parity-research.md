# Utterance Parity Research Notes

Research pass: 2026-05-20.

## Current Machine

AquaSynth is growing a speech curriculum, not a theatrical pile of IPA
decorations. The active pipeline is:

```text
structured utterance metadata
  -> learned utterance embedding
  -> learned synth automation
  -> vocal tract, envelopes, LFOs, filters, and spectral controls
  -> rendered audio
  -> loss against a speech reference
```

Each arrow is a machine-learning artifact. The first useful job is not
universal speech. It is a tight parity harness where small utterances can be
rendered against a simple reference synthesizer, scored quickly, and used to
probe hundreds of controller weights. The loss landscape comes first. Poetry
can wait outside and smoke.

## Prior Work Signals

### Physical And Articulatory Synthesis

Kelly-Lochbaum-style tube models remain the practical starting point for
realtime tract acoustics. Pink Trombone and related ports prove that a simplified
tract, glottis, noise source, and tract geometry can be interactive enough to
use as a control target. Recent Pink Trombone optimization work treats that
model as a realtime Kelly-Lochbaum articulatory synthesizer and fits physical
parameters, which is close to AquaSynth's black-box rendered-loss pressure.

VocalTractLab is the heavyweight reference for articulatory synthesis: a 3D
tract model, glottal source, aeroacoustic simulation, and articulatory control.
Its papers are a warning label: speech quality depends on control trajectories
and coarticulation, not only on static phoneme shapes.

Story's area-function model is especially useful for AquaSynth because it gives
a middle representation between symbolic phonetic intent and tract acoustics.
Vowels are shaped by tract modes; consonants can be expressed as constriction
location, area, and range imposed on that substrate. That matches the local
doctrine: area curves are the synthesis authority, formants are diagnostics.

### Gesture Planning

Articulatory phonology and task dynamics are the right control metaphor. Phones
should lower into overlapping gestures, not a row of isolated presets. Haskins
TADA is important because it treats an utterance as a gestural score feeding a
task-dynamic model. AquaSynth's utterance schema should eventually resemble a
score of targets, timings, coupling, emphasis, and speaking style; it should not
be a giant enum of phonemes with cute flags.

### Classical Formant References

Klatt-style and eSpeak-style formant synthesis are useful early references
because they are compact, deterministic, intelligible, and easy to render in
bulk. They are not anatomy truth. If AquaSynth learns only to imitate eSpeak's
formant shortcuts, it will build a loyal little mimic and call it a throat.

The correct use is narrow: use eSpeak NG for IPA/text coverage, timing pressure,
log-mel targets, and fast generated ground truth while the physical tract earns
basic vowels and consonants. Then move the curriculum toward tract-shaped
targets and listening fixtures.

### Differentiable DSP And Neural Controllers

DDSP is the strongest precedent for this shape of work: combine neural networks
with known signal models so the model learns control rather than reinventing
acoustics from raw samples. Magenta's DDSP work shows the general bargain:
smaller models, stronger inductive bias, and trainable synthesis controls.

More recent DDSP articulatory vocoder work synthesizes speech from articulatory
measurements, F0, and loudness. That is not AquaSynth's exact setup, because
AquaSynth starts from metadata and generated speech references rather than EMA
capture. The useful lesson is architectural: split source controls from tract or
filter controls, keep features interpretable, and train against rendered audio.

Neural source-filter models are also relevant. They show that neural pieces can
drive physically meaningful source/filter structure instead of replacing the
whole backend with a black box. AquaSynth should use that as permission to learn
automation and embeddings, not permission to throw away tract ownership.

### IPA And Feature References

PanPhon maps IPA segments to articulatory feature vectors and PHOIBLE provides
language inventories plus distinctive features. Both are reference material for
metadata and sanity checks. Neither should become the permanent internal truth.

The reason is simple: Weksa and alien morphologies need "human IPA-like intent"
without letting human phonetics own nonhuman anatomy. IPA features can seed
intent; morphology owns whether the requested articulation is possible.

## Implications For AquaSynth

Keep the curriculum brutally small:

1. Render tiny eSpeak fixtures and extract normalized log-mel evidence.
2. Train the utterance encoder and synth-driver chain from that rendered loss.
3. Add a compiled Faust candidate path that can vary controller outputs without
   recompiling topology.
4. Use vowels first, then CV/CVC contrasts, then broader IPA.
5. Only move to intonation, prosody, emotional context, and personality after
   the harness can show phoneme-level loss improvements without handwaving.

Keep training receipts as first-class evidence:

- when each stage decided;
- how much latency it spent;
- what latency budget it was trying to fit;
- how confident the mapping was;
- which request, result, checkpoint, artifact, and model version produced the
  evidence.

Time is not bookkeeping garnish here. If the mapping is correct only after an
unbounded wait, cache accident, or hidden retry, the system has learned a
beautiful falsehood.

Keep scoring layered. The patch under test may use all of AquaSynth: FM, AM,
modulators, envelopes, filters, auxiliary animated voices, breath/noise/room
emulation, and post-processing around both the vocal output and its gesture
inputs. That is necessary for matching real IPA references, which carry speaker
and recording conditions. But the reports must not let a noise layer impersonate
articulation:

- `gesture_score` measures descriptor-to-surface coverage, motion direction,
  spline contour/timing, primitive timeline consequences, and optional external
  articulation landmarks.
- `clean_vocal_score` measures broad phoneme identity from the vocal primitive
  path before heavy dressing.
- `full_parity_score` measures the whole patch against the reference clip.

Only the third score is allowed to reward recording-condition helpers. The first
score is where IPA labels and anatomical descriptors prove that the right organs
were asked to move.

The first scalable IPA training loop starts with frozen gesture rounds, not a
free-running optimizer. `IpaGestureExperiment.WriteRound` produces candidate
scripts, primitive timelines, gesture metrics, a manifest, and evidence JSONL
for a whole batch. `IpaGestureExperiment.AnalyzeRound` then writes metric
summaries, score-band clusters, and a science brief. A science worker can
inspect that immutable bundle for clusters, outliers, loss surfaces, and
promising descriptor/variant changes while the foreground process writes the
next batch. This keeps hypothesis generation, primitive evidence, and
statistical analysis separate enough to argue with each other. Good. Machines
get less stupid when their witnesses are allowed to disagree.

Do not start serious gradient descent from toy embedding packets. The first
training contract coordinated with Weksa is
`weksa.utterance_embedding_handoff.v0.1`: 1024 speech-text floats from
`bge-m3:latest`, variable-length PanPhon sequence evidence, a 256-float
AquaSynth-owned phonetic realization embedding from
`aquasynth.panphon_sequence_encoder.v0.1`, 32 deterministic prosody/emphasis
floats, 64 projected character-state floats, and a 64-float AquaSynth-owned
utterance embedding output. Anything smaller is a plumbing fixture, not a
training corpus.

Keep semantic text and phonetic realization separate. English text embeddings
may help preserve meaning and phrasing, but an IPA string is not English prose
with a spicy alphabet. Alien phones belong in PanPhon sequence evidence and the
AquaSynth-trained phonetic sequence encoder.

The universal utterance schema should stay boring at first:

- segment inventory and source spans;
- duration, stress, tone, and boundary hints;
- speaker/morphology identity;
- pitch, loudness, speaking-rate, and breath controls;
- emphasis and emotional/context vectors as named side inputs;
- diagnostics when a field is not yet consumed.

Do not encode every future expressive dimension before the parity harness can
make `/a pa ta ka sa ma/` less embarrassing. Grand schemas are where bugs go to
earn tenure.

## Source Ledger

- Pink Trombone interactive vocal tract:
  <https://dood.al/pinktrombone/>
- Pink Trombone optimization / Kelly-Lochbaum parameter fitting:
  <https://link.springer.com/article/10.1186/s13636-025-00414-5>
- Sndkit tract notes, Pink Trombone/Kelly-Lochbaum lineage:
  <https://pbat.ch/sndkit/tract/>
- VocalTractLab project:
  <https://www.vocaltractlab.de/>
- VocalTractLab features:
  <https://www.vocaltractlab.de/index.php?page=vocaltractlab-features>
- VocalTractLab coarticulation model paper:
  <https://pmc.ncbi.nlm.nih.gov/articles/PMC3628899/>
- Story 2005 area-function model:
  <https://bpb-us-e2.wpmucdn.com/sites.arizona.edu/dist/f/80/files/2023/10/story_jasa2005-1.pdf>
- Stop consonants and Story area-function model discussion:
  <https://pmc.ncbi.nlm.nih.gov/articles/PMC3145491/>
- Haskins TADA:
  <https://www.haskinslaboratories.org/tada>
- eSpeak NG repository and formant-synthesis reference:
  <https://github.com/espeak-ng/espeak-ng>
- PanPhon package:
  <https://pypi.org/project/panphon/>
- PanPhon paper:
  <https://aclanthology.org/C16-1328.pdf>
- PHOIBLE:
  <https://phoible.org/>
- PHOIBLE FAQ:
  <https://phoible.org/faq>
- Magenta DDSP:
  <https://magenta.withgoogle.com/ddsp>
- DDSP paper:
  <https://arxiv.org/abs/2001.04643>
- DDSP review for music and speech synthesis:
  <https://www.frontiersin.org/journals/signal-processing/articles/10.3389/frsip.2023.1284100/full>
- Introduction to differentiable synthesizer programming:
  <https://intro2ddsp.github.io/>
- DDSP articulatory vocoder:
  <https://arxiv.org/abs/2409.02451>
- Neural source-filter waveform model:
  <https://arxiv.org/abs/1810.11946>
- LF/voice source model comparison:
  <https://pmc.ncbi.nlm.nih.gov/articles/PMC4491021/>
- LF model fitting for statistical parametric speech synthesis:
  <https://www.cs.cmu.edu/~awb/papers/is2013/is2013_lfmodel.pdf>
