# Physical Voice Modeling Talks

Captured on 2026-05-26.

## Julius O. Smith: Sound synthesis based on physical models

- Page: https://www.cirmmt.org/en/events/distinguished-lectures/Smith
- Local page: `pages/julius-smith-cirmmt-physical-modeling.html`
- YouTube ID: `dUcNzPhZdwk`
- Transcript: `transcripts/julius-smith-cirmmt-physical-modeling.txt`

Distilled notes:

- The talk explicitly walks from Kelly-Lochbaum vocal tract modeling to
  digital waveguides. It frames the tract as an idealized piecewise cylindrical
  tube with scattering at discontinuities and propagation delays between
  junctions.
- Delay lines are the cheap realtime core. Filters then decorate the model
  with damping, radiation, pickup, and other physical losses.
- For Aqua, this supports a Faust-friendly wave variable graph: bidirectional
  propagation edges, scattering junctions, loss filters, source/radiation
  ports, and fractional delay for continuous geometry.

## Brad Story: Vocal Tract Resonances in Vowel Production

- Page: https://ncvs.org/vocal-tract-resonances-in-vowel-production/
- Video: https://www.youtube.com/watch?v=q23bAG-b6OA
- Local page: `pages/brad-story-ncvs-vocal-tract-resonances.html`
- Transcript: `transcripts/brad-story-ncvs-vocal-tract-resonances.txt`

Distilled notes:

- The talk uses source-filter framing to explain how vocal-tract resonances
  enhance source spectrum components into formants.
- A closed-open tube is useful as a teaching model, but Story stresses that
  actual vowel production changes the tract shape continuously.
- The transcript repeatedly ties tract length to resonance frequency and shows
  that different vowel shapes can move the first resonances substantially while
  retaining standing-wave interpretation.
- For Aqua, formants are diagnostic outcomes of a continuous tract, not primary
  controls. The DSL should expose length, area curves, and constrictions; the
  renderer should produce resonances from those controls.

## Brad Story: From Car Mufflers to Human Voice Tracts

- Page: https://www.azpm.org/p/podcasts/2018/3/1/125000-episode-120-from-car-mufflers-to-human-voice-tracts/
- Local page: `pages/brad-story-azpm-mufflers-voice-tracts.html`
- Transcript: `transcripts/brad-story-azpm-mufflers-voice-tracts.transcript-unavailable.txt`

Distilled notes:

- The podcast page connects Story's acoustics background in muffler modeling to
  mathematical models of human sound production.
- The useful Aqua lesson is the same one, wearing work boots: vocal tracts are
  acoustic filters whose geometry determines spectral shaping.

## Peter Birkholz: Recent improvements of VocalTractLab

- Page: https://lpp.cnrs.fr/evenement/srpp-de-peter-birkholz/
- Local page: `pages/peter-birkholz-vtl-2.3-lpp-talk.html`
- Transcript: `transcripts/peter-birkholz-vtl-2.3-lpp-talk.transcript-unavailable.txt`

Distilled notes:

- The abstract presents VocalTractLab 2.3 as a practical articulatory
  synthesizer with multiple model components and multiple control levels.
- For Aqua, the important pressure is not VTL-shaped compatibility. It is the
  separation between tract model, vocal-fold/source model, control gestures,
  and higher-level speech control.

## Peter Birkholz: How physical models of the vocal apparatus help us understand speech production

- Page: https://brahms.ircam.fr/en/media/xfb9e0a_peter-birkholz-how-physical-models-of-th
- Local page: `pages/peter-birkholz-ircam-physical-models.html`
- Transcript: `transcripts/peter-birkholz-ircam-physical-models.transcript-unavailable.txt`

Distilled notes:

- The IRCAM page identifies a 39 minute talk by Birkholz in a voice acoustics
  modeling event.
- The captured public page did not expose transcript text. Keep it as a source
  anchor for future manual retrieval or audio transcription.

## Sidney Fels and John E. Lloyd: Developing Physically-Based, Dynamic Vocal Tract Models Using ArtiSynth

- Page: https://www.microsoft.com/en-us/research/video/developing-physically-based-dynamic-vocal-tract-models-using-artisynth/
- Local page: `pages/sidney-fels-msr-artisynth-vocal-tract.html`
- Transcript: `transcripts/sidney-fels-msr-artisynth-vocal-tract.transcript-unavailable.txt`

Distilled notes:

- The Microsoft Research abstract describes ArtiSynth as a Java platform that
  combines mass-spring, finite-element, and rigid-body anatomical components
  with source-filter and airflow acoustic models.
- It also emphasizes interactive editing, timeline control, and output logging.
- For Aqua, ArtiSynth is too heavy as a realtime patch target, but it is an
  excellent authority map: geometry/body mechanics, acoustic model, control
  timeline, and diagnostics are separate organs.
