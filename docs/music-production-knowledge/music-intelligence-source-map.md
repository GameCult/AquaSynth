# Music Intelligence Source Map For AquaSynth

kind: source-map
tier: current-source-doctrine
topic: external music intelligence signals for song practice targets

## Owner

This source map owns how AquaSynth should use external genre, mood, tempo, key,
and audio-feature services while training song agents. External metadata is
context and contrast, not authority over the local reference audio.

## Current Landscape

- Every Noise at Once is reachable as a genre/listening-map snapshot, but it is
  no longer a maintained live discovery organ. Use it for genre vocabulary,
  neighborhood clues, and artist/genre relation hypotheses, not current-release
  truth.
- Echo Nest is gone. Its public API was shut down years ago after Spotify
  acquired it; do not design new infrastructure around Echo Nest endpoints.
- Spotify audio features and audio analysis are deprecated/restricted for new
  Web API use cases. Spotify also warns that Spotify content may not be used to
  train or ingest into ML/AI models. Do not feed Spotify content or proprietary
  feature payloads into CultCache training memory.
- AcousticBrainz stopped collecting new data in 2022, but its public dataset
  and API remain useful for historical MusicBrainz-linked acoustic descriptors
  when a recording MBID is available.
- Essentia is the strongest local/open path: it provides feature extractors and
  models for genre/style, mood, instrumentation, tonality, pitch, tempo, source
  separation, and tagging. Prefer local Essentia-style extraction over remote
  black-box labels when we have the audio files.
- Paid or hosted APIs such as Soundcharts, Tunebat, Beatlyze, SoundStat,
  MetaMagican, Kapiko, and RapidAPI-hosted track-analysis services can provide
  BPM/key/mood/genre/energy hints. Treat them as optional adapters with license,
  privacy, quota, and provenance checks.

## Transfer Rules

- Local audio analysis owns primary truth: waveform, log-mel bands, RMS,
  autocorrelation, key/register estimates, sections, loudness, and rendered
  comparison metrics.
- External metadata owns vocabulary and priors: likely genre family, mood tags,
  related styles, energy/danceability labels, and production reference words.
- If an external service disagrees with the local audio, preserve both and let
  the producer brief state the conflict.
- Never promote a genre label unless it changes a concrete AquaSynth decision:
  tempo grid, drum pattern, bass role, harmonic language, section plan, timbre,
  mix priority, or failure check.
- Every external feature should carry provider, timestamp, source track id or
  file hash, confidence if available, and licensing/usage note.

## AquaSynth Integration Pattern

Recommended per-target `intelligence.md` shape:

```markdown
# Target Music Intelligence

- local_file_hash:
- source_title:
- local_features: bpm, key/root, register, active duty, spectral centroid,
  flux, section-energy hints
- external_labels:
  - provider:
    query:
    result:
    confidence:
    license_or_policy_note:
- production_translation:
  - genre/mood clue:
  - AquaSynth consequence:
  - rejected assumption:
```

Recommended retrieval query additions:

```text
genre style mood energy danceability key tempo section instrumentation
EveryNoise AcousticBrainz Essentia local audio analysis production reference
```

## Failure Modes

- Treating Spotify/Echo-Nest-derived features as available public training
  infrastructure in 2026.
- Copying a genre label into the prompt without translating it into composition
  or production choices.
- Letting remote labels override local audio artifacts.
- Forgetting provenance and later being unable to explain why the agent believed
  a track was halftime, dark, danceable, acoustic, aggressive, or vocal-heavy.
- Using black-box API output as curriculum doctrine instead of bounded,
  provider-labeled evidence.

## Source Anchors

- Every Noise at Once: https://everynoise.com/
- Spotify Web API changes, 2024-11-27:
  https://developer.spotify.com/blog/2024-11-27-changes-to-the-web-api
- Spotify Get Track's Audio Features reference:
  https://developer.spotify.com/documentation/web-api/reference/get-audio-features
- AcousticBrainz: https://acousticbrainz.org/
- Essentia models: https://essentia.upf.edu/models.html

