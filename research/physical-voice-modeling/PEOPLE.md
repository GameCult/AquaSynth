# Influential Physical Voice Modeling People

This note maps practical physical vocal-modeling researchers to AquaSynth
language pressure. The point is not hero worship. It is to steal the useful
organs cleanly.

## Peter Birkholz

Primary practical artifact: VocalTractLab, especially the open backend API and
2.4 manual. The captured LPP and IRCAM talk pages frame VTL as a multi-level
articulatory synthesizer with geometric tract modeling, gestures, vocal-fold
models, acoustic simulation, and higher-level control.

Useful pressure for Aqua:

- Separate anatomy/morphology from control gestures. A vocal tract shape is not
  a bag of section knobs; it is a geometry that gestures deform.
- Keep multiple control levels: low-level physical controls for parity and
  differentiability, plus higher-level gestures and utterance controls that
  can drive those controls.
- Treat copy synthesis and optimization as expected workflows. The DSL should
  preserve named controls and bounded physical ranges so log-mel parity golf
  has meaningful axes instead of anonymous index soup.
- VTL is a reference implementation, not a runtime dependency. Aqua should
  capture the reusable semantics: continuous area curves, source ports,
  radiation ports, branch coupling, gestures, and articulatory targets.

Downloaded/captured sources:

- `sources/implementations/vocaltractlab-backend-dev-main.zip`
- `sources/implementations/vocaltractlab-backend-readme.md`
- `sources/implementations/vocaltractlab-api.h`
- `sources/implementations/vocaltractlab-2.4-manual.pdf`
- `sources/papers/birkholz-2013-coarticulation.html`
- `sources/papers/gao-stone-birkholz-2019-copy-synthesis-ga.pdf`
- `sources/papers/promon-birkholz-xu-2013-training-continuous-acoustic-data.pdf`
- `sources/papers/weitz-steiner-birkholz-2017-gesture-tts.pdf`
- `talks/pages/peter-birkholz-vtl-2.3-lpp-talk.html`
- `talks/pages/peter-birkholz-ircam-physical-models.html`

## Brad H. Story

Primary practical artifact: TubeTalker and parametric vocal-tract area
functions. Story's work is especially useful because it compresses tract shape
into interpretable low-dimensional parameters while still grounding sound in
airway acoustics.

Useful pressure for Aqua:

- Area functions are the right bridge between anatomy and waveguide lowering.
  Aqua controls should deform continuous length/area curves and let lowering
  choose emitted sections or fractional delays.
- Tube length and constriction shape should be continuous runtime parameters.
  The Mathur/Story/Rodriguez fractional-elongation paper is direct evidence
  that changing morphology should not require changing sample rate or
  recompiling a fixed section count.
- Resonance/formant behavior should be derived evidence, not the owner of the
  voice. Story's NCVS talk makes the source-filter teaching model legible, but
  the synthesis layer should own the tube and its standing-wave conditions.

Downloaded/captured sources:

- `sources/papers/story-2011-tubetalker.pdf`
- `sources/papers/story-2005-parametric-area-function.pdf`
- `sources/papers/story-2013-phrase-level-airway-modulation.html`
- `sources/mathur-story-rodriguez-2006-fractional-elongation.html`
- `talks/pages/brad-story-ncvs-vocal-tract-resonances.html`
- `talks/transcripts/brad-story-ncvs-vocal-tract-resonances.txt`
- `talks/pages/brad-story-azpm-mufflers-voice-tracts.html`

## Julius O. Smith

Primary practical artifact: digital waveguide physical modeling, including the
Stanford lineage that Faust's physical-modeling library clearly reflects.

Useful pressure for Aqua:

- Bidirectional delay-line wave variables are the efficient owner for 1D
  acoustic propagation. Scattering, reflection, loss, and radiation should be
  explicit graph semantics.
- Physical models earn their keep when they are cheap enough for realtime
  interaction. Faust-friendly lowering is not a compromise here; it is the
  performant version of the idea.
- Fractional delay and delay-line filtering are not afterthoughts. They are how
  continuous length, tuning, loss, and moving boundaries become audio-rate
  machinery.
- Passivity and bounded nonlinear junctions matter. Aqua controls should make
  explosive acoustic networks difficult to express accidentally.

Downloaded/captured sources:

- `sources/faust-2.85.5-delays.lib`
- `sources/faust-2.85.5-physmodels.lib`
- `talks/pages/julius-smith-cirmmt-physical-modeling.html`
- `talks/transcripts/julius-smith-cirmmt-physical-modeling.txt`

## Sidney Fels and John E. Lloyd

Primary practical artifact: ArtiSynth, a 3D biomechanical simulation platform
for dynamic vocal tract and upper-airway modeling.

Useful pressure for Aqua:

- Full biomechanical models prove that anatomy, tissue mechanics, contacts,
  and acoustic loading can be one integrated simulation. Aqua should not copy
  that weight into realtime Faust, but it should preserve the authority split:
  body geometry, mechanical gesture, airflow/acoustics, and rendered sound are
  distinct layers.
- 3D/finite-element truth models are good calibration and constraint sources
  for lower-dimensional waveguide renderers.
- Interactive timelines and logged model outputs are a reminder that Aqua's
  playground should expose physical controls and diagnostics, not only final
  sound.

Downloaded/captured sources:

- `sources/papers/fels-2006-artisynth-vocal-tract.pdf`
- `talks/pages/sidney-fels-msr-artisynth-vocal-tract.html`

## Haskins, ASY, Gnuspeech, Praat, and SndKit

These are practical implementation pressure rather than a single-person lane.

Useful pressure for Aqua:

- Gnuspeech and ASY keep the older tube-resonance/articulatory lineage visible:
  source-filter language is useful, but the better owner is still an acoustic
  body with excitation.
- Praat's articulatory synthesis implementation is small and inspectable,
  useful as a sanity check for model records and tract object boundaries.
- SndKit's Pink Trombone material is a compact implementation reference for
  glottis and tract behavior. It should feed parity tests, not define the DSL.
- Animal acoustic communication keeps the syrinx/alien objective honest:
  source ports must be plural and topology-driven, not glottis-shaped forever.

Downloaded/captured sources:

- `sources/implementations/gnuspeech-tube-resonance-model.pdf`
- `sources/papers/haskins-asy-animal-acoustic-communication.pdf`
- `sources/implementations/praat-articulatory-synthesis-manual.html`
- `sources/implementations/praat-vocaltract.cpp`
- `sources/implementations/sndkit-tract.html`
- `sources/implementations/sndkit-glottis.html`
