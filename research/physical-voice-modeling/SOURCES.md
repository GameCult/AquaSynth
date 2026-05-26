# Physical Voice Modeling Sources

Downloaded or captured on 2026-05-26.

## Core Sources

| File | Source | Notes | SHA-256 |
| --- | --- | --- | --- |
| `sources/faust-2.85.5-delays.lib` | Local Faust 2.85.5 install, `C:\Program Files\Faust\share\faust\delays.lib` | Fractional delay implementations: linear, Lagrange, Thiran/allpass, variable delay. | `3089afb6654e6a37105d910f8b58f5c3bc4384acd57c5644227d69cb62486a39` |
| `sources/faust-2.85.5-physmodels.lib` | Local Faust 2.85.5 install, `C:\Program Files\Faust\share\faust\physmodels.lib` | Bidirectional chains, length conversion, waveguides, tubes, terminations, wind/string physical models. | `4eaa76825372db0f79d0863912b46b21be73607dc99ac63dca3c` |
| `sources/mullen-howard-murphy-2006-waveguide-vocal-tract.pdf` | White Rose Research Online, `https://eprints.whiterose.ac.uk/id/document/715711` | Open PDF: waveguide vocal tract acoustics and increased-dimensionality discussion. | `cdb98482ea5dd2ac153e5097caa743f3691de0ddb747b0ae40466c2932ea106b` |
| `sources/mathur-story-rodriguez-2006-fractional-elongation.html` | Arizona Board of Regents page, `https://experts.azregents.edu/en/publications/vocal-tract-modeling-fractional-elongation-of-segment-lengths-in-/` | Metadata/abstract page for half-sample vocal-tract model with fractional-delay segment elongation. | `5ff7d56bb888b7dc7c7f402d38c51c31552edd0345ee7d5a972e9a784e6b236c` |
| `sources/frontiers-2023-ddsp-review.html` | Frontiers, `https://www.frontiersin.org/journals/signal-processing/articles/10.3389/frsip.2023.1284100/full` | Review covering differentiable DSP for music/speech, differentiable source-filter, and Pink Trombone-style articulatory estimation. | `6f4f8cdfb5741df8a5e5be41304914cdbe887a8d5345161a8a63b8842fa54678` |

## Practical Implementations

| File | Source | Notes | SHA-256 |
| --- | --- | --- | --- |
| `sources/implementations/vocaltractlab-backend-dev-main.zip` | GitHub, `https://github.com/TUD-STKS/VocalTractLabBackend-dev/archive/refs/heads/main.zip` | Open VocalTractLab backend source snapshot. | `073e99bb1dec00b4dbabbdc754fdfc9dd5c54c182e2972cb7dd09b9dbf9daaeb` |
| `sources/implementations/vocaltractlab-backend-readme.md` | GitHub raw README, `https://raw.githubusercontent.com/TUD-STKS/VocalTractLabBackend-dev/main/README.md` | Build/API orientation for VTL backend. | `c8c33655915553bb30c3e85fde8bbd25abbe3bfa3d8ee605ee66cffdd829c708` |
| `sources/implementations/vocaltractlab-api.h` | GitHub raw API header, `https://raw.githubusercontent.com/TUD-STKS/VocalTractLabBackend-dev/main/src/VocalTractLabApi.h` | Public backend API surface. | `2f7918335d06f6ce47ba046a26c8312d9b355cf2246c3525c18211bfda936ef3` |
| `sources/implementations/vocaltractlab-2.4-manual.pdf` | VocalTractLab, `https://vocaltractlab.de/download-vocaltractlab/VTL2.4-manual.pdf` | User manual for VTL model/control surface. | `fb1df5249b770a7cc9f756274a23c9261b680c029181d2979f6c77b6c255202f` |
| `sources/implementations/praat-articulatory-synthesis-manual.html` | Praat manual, `https://www.fon.hum.uva.nl/praat/manual/Articulatory_synthesis.html` | Small articulatory synthesis implementation/documentation anchor. | `d0dbee84fa99a912bfc13f9ddc4dc34a0a75b3dbbc2bb695720d2906ddf8663e` |
| `sources/implementations/praat-vocaltract.cpp` | Praat GitHub source, `https://raw.githubusercontent.com/praat/praat/master/fon/VocalTract.cpp` | VocalTract implementation excerpt. | `2fbc3fa64e7dc07dbc5cedd8167209aeb89ae6fb1ee75cd1833cb9cde9f254dd` |
| `sources/implementations/gnuspeech-tube-resonance-model.pdf` | GNUstep/Gnuspeech docs, `https://www.gnustep.org/experience/GnuSpeechTubeResonanceModel.pdf` | Tube-resonance model documentation. | `348f5dfb02d940510625fec33a861d06960c3c0912aa5fd8e37c0faeb72d9803` |
| `sources/implementations/sndkit-tract.html` | SndKit, `https://pbat.ch/sndkit/tract/` | Literate C implementation of Pink Trombone-style tract. | `21154b131221aef9fb41c413b2dc229f0ea01906cfbf097171336457d1bce74b` |
| `sources/implementations/sndkit-glottis.html` | SndKit, `https://pbat.ch/sndkit/glottis/` | Literate C implementation of Pink Trombone-style glottis. | `d9f5e7778bbb78f1ce755cc23a890607fb2f038b78a3a8458d1970314f5e134c` |

## Papers

| File | Source | Notes | SHA-256 |
| --- | --- | --- | --- |
| `sources/papers/story-2011-tubetalker.pdf` | University of Arizona hosted PDF, `https://bpb-us-e2.wpmucdn.com/sites.arizona.edu/dist/f/80/files/2023/10/Story-2011_0-1.pdf` | TubeTalker airway modulation model. | `bddc842a8e38b67331802bf2b216ca1fc073085bfd5c3d0f88badbfbae4c423e` |
| `sources/papers/story-2005-parametric-area-function.pdf` | `https://bpb-us-e2.wpmucdn.com/sites.arizona.edu/dist/f/80/files/2023/10/story_jasa2005-1.pdf` | Parametric vocal tract area function work. | `55f05d31c97009a61215211a5730f6fb15425834940da74ed706c4af0b24541b` |
| `sources/papers/story-2013-phrase-level-airway-modulation.html` | PubMed/PMC page | Phrase-level airway modulation metadata/full text page. | `d5bcc0bf17e63bbb60a9efa06a5deb3507d6317da4f52cc474ebd59063f4926d` |
| `sources/papers/birkholz-2013-coarticulation.html` | PubMed/PMC page | Birkholz coarticulation/control material. | `64edd5e30d23282b78dc9635f88915d12f13974a0f373cf78bb43cbbb4319bd1` |
| `sources/papers/gao-stone-birkholz-2019-copy-synthesis-ga.pdf` | `https://www.isca-archive.org/interspeech_2019/gao19e_interspeech.pdf` | Copy synthesis by genetic algorithm with VocalTractLab pressure. | `37077e8aee9fdcb8555b68c8c377cd3e15d3024cb5826bf36f1743b43035fb7e` |
| `sources/papers/promon-birkholz-xu-2013-training-continuous-acoustic-data.pdf` | `https://www.isca-archive.org/interspeech_2013/promon13_interspeech.pdf` | Training continuous acoustic data/control material. | `99118d54804ca570b453157461e956696b533cc021bff21def40866bba7522cc` |
| `sources/papers/weitz-steiner-birkholz-2017-gesture-tts.pdf` | `https://vocaltractlab.de/publications/weitz-2017-essv.pdf` | Gesture-based text-to-speech with articulatory control pressure. | `8e2bf9aa0b06ea173d73e5e252e934ab1416f9f28e86e92b6e48dbdc8fa37361` |
| `sources/papers/fels-2006-artisynth-vocal-tract.pdf` | `https://www.cefala.org/issp2006/camera-ready/fels.pdf` | ArtiSynth dynamic vocal tract modeling paper. | `d938bb16953ab9e80f78a5a1340233f0bb52c520820d983674630b45999f0061` |
| `sources/papers/haskins-asy-animal-acoustic-communication.pdf` | Haskins/ASY source | Animal acoustic communication and articulatory synthesis pressure. | `15fd7eca917d0d01b000b01085775e53058cc3270d4982137fef6c6cfb2264c5` |

## Talk Pages And Transcripts

| File | Source | Notes | SHA-256 |
| --- | --- | --- | --- |
| `talks/pages/julius-smith-cirmmt-physical-modeling.html` | `https://www.cirmmt.org/en/events/distinguished-lectures/Smith` | CIRMMT talk page with YouTube archive. | `486c3ba00b0a5812ac33f3d3cfe926efa8c0530dea39c6654ca30877d52fcadf` |
| `talks/transcripts/julius-smith-cirmmt-physical-modeling.txt` | YouTube transcript for `dUcNzPhZdwk` | Plain text transcript. | `1828ac1ec9677a5ef26d930c772ee18c0739156a75e5162f35f45ba543bdfa55` |
| `talks/transcripts/julius-smith-cirmmt-physical-modeling.vtt` | YouTube transcript for `dUcNzPhZdwk` | WebVTT transcript. | `867b0278c50daca8b9c50321b2395e108d8ef733bf9724b0afa44b6950e93cb1` |
| `talks/pages/brad-story-ncvs-vocal-tract-resonances.html` | `https://ncvs.org/vocal-tract-resonances-in-vowel-production/` | NCVS page with YouTube link. | `c6b2e4f9c34aad63d6824f7f57c70598ea40d6a41996f6b0022701c1d4f3b86a` |
| `talks/transcripts/brad-story-ncvs-vocal-tract-resonances.txt` | YouTube transcript for `q23bAG-b6OA` | Plain text transcript. | `5ae4483199924a74d15583319ee666eb169275026b4199dc6de9e4e609727c90` |
| `talks/transcripts/brad-story-ncvs-vocal-tract-resonances.vtt` | YouTube transcript for `q23bAG-b6OA` | WebVTT transcript. | `448ad12bf17c7a5114f42d36683956a951b5dfdbf550591ab3de6362e4865f32` |
| `talks/pages/brad-story-azpm-mufflers-voice-tracts.html` | `https://www.azpm.org/p/podcasts/2018/3/1/125000-episode-120-from-car-mufflers-to-human-voice-tracts/` | Podcast page; transcript unavailable. | `342eca9bd21c98acbc01d655c4e80827688c3a15f2703f3517c5cacc5db49236` |
| `talks/transcripts/brad-story-azpm-mufflers-voice-tracts.transcript-unavailable.txt` | Local availability note | No public transcript found in captured page. | `b2004c158d05b710961d5c450b16db7ba8ab8b7a3e1d0d70a6cc4b9d1c36a782` |
| `talks/pages/peter-birkholz-vtl-2.3-lpp-talk.html` | `https://lpp.cnrs.fr/evenement/srpp-de-peter-birkholz/` | LPP event page; transcript unavailable. | `8e15aab67d864f1208675c71abd9e475def0d58151209c63d63ed01a23313dea` |
| `talks/transcripts/peter-birkholz-vtl-2.3-lpp-talk.transcript-unavailable.txt` | Local availability note | No public transcript or embedded video found in captured page. | `d106ca0e2fc011d7d0d4e633ffeeeca4a752935d41de47f1fa51467eaab1e7db` |
| `talks/pages/peter-birkholz-ircam-physical-models.html` | `https://brahms.ircam.fr/en/media/xfb9e0a_peter-birkholz-how-physical-models-of-th` | IRCAM event media page; transcript unavailable. | `e132b85bc5d020e1d1d0fa086a4a809307519766c83d093857f60db4dde20121` |
| `talks/transcripts/peter-birkholz-ircam-physical-models.transcript-unavailable.txt` | Local availability note | Public page exposes video metadata but not a transcript. | `9b9cf086f7674cebfa4df7b8bf9e1c89cdd8d6d813a0e6e97b4dc444548463f2` |
| `talks/pages/sidney-fels-msr-artisynth-vocal-tract.html` | `https://www.microsoft.com/en-us/research/video/developing-physically-based-dynamic-vocal-tract-models-using-artisynth/` | Microsoft Research video page; transcript unavailable. | `f1b2136dda7758419bc5db01d292d87feec7c13cb42e02aac0e00b41ba6347da` |
| `talks/transcripts/sidney-fels-msr-artisynth-vocal-tract.transcript-unavailable.txt` | Local availability note | Captured page did not expose public transcript text. | `db56923f537ebdacc73164518a343b2dc37076e343ae58d5fd74bf39991be9b0` |

## Sources Not Fully Downloaded

- Mathur, Story, Rodriguez, "Vocal-tract modeling: Fractional elongation of
  segment lengths in a waveguide model with half-sample delays", IEEE TASLP
  2006, DOI `10.1109/TSA.2005.858550`. The local file is the accessible
  metadata/abstract page, not the full IEEE article PDF.
- Several talk pages did not expose transcripts in public page text. Each has a
  `transcript-unavailable.txt` marker under `talks/transcripts/`.
- Attempted Praat source download:
  `https://raw.githubusercontent.com/praat/praat/master/fon/Artword.cpp`
  returned 404 on 2026-05-26; `VocalTract.cpp` was captured instead.

## Key Local Search Anchors

- `PEOPLE.md`
- `talks/TALKS.md`
- `pm.l2s`
- `pm.chain`
- `pm.waveguideUd`
- `pm.waveguideFd`
- `pm.waveguideFd2`
- `pm.waveguideFd4`
- `de.fdelay`
- `de.fdelayltv`
- `de.fdelay1a`
- `de.fdelay2a`
- `VocalTractLabApi`
- `TubeTalker`
