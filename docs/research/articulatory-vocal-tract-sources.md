# Articulatory vocal tract: sources and what AquaSynth takes from them

Read 2026-11-08 for follow-up `aqua-body:follow_up:chat-s4456-4456`. The full fact sheet with quoted values and the list of
what could not be retrieved is the Eyes pass `tract-literature-20261108` (research notes in the aqua mind;
working copy `work/tract-literature-facts.md` in the aqua workspace). This page keeps only what the tract design rests on.
Nothing built from it has been heard by anyone; every number below is a measurement or a published value, not a listening result.

## Sources and what each decides

| Source | What we take | What we do not take |
|---|---|---|
| Kelly & Lochbaum 1962; Smith, *Physical Audio Signal Processing* (KL junctions); Liljencrants 1985 / Story 1995 (section length `c / 2Fs`, i.e. one sample per section); Mullen, Howard & Murphy 2006 (boundary reflections, wall loss 0.90-0.98 sets bandwidth) | The ladder: one cell per sample, junction `r = (A[i-1]-A[i])/(A[i-1]+A[i])`, glottal reflection about +0.75, lip reflection about -0.85, a per-pass loss factor for bandwidth | Fractional-delay cells (Välimäki & Karjalainen, Mathur & Story): needed for continuous length change, not built; the tract length is fixed per patch |
| Pink Trombone (Neil Thapen 2017, MIT; read in chdh/pink-trombone-mod `Tract.ts`) | Constants (glottal 0.75, lip -0.85, 0.999 per pass), the 3-port velum junction `rL=(2A_k-S)/S`, `rR`, `rN`, with `S = A_k + A_(k+1) + A_velum` | Its 44-cell tract at twice the sample rate and its Gaussian tongue model as the vowel source: the vowel shapes now come from measured area functions |
| Story, Titze & Hoffman 1996/1998 (MRI area functions; numeric tables paywalled) and **Story 2008, JASA 123(1):327-335, PMC2377017, Table I** (44-section area functions of 11 vowels incl. /ɪ/ /ʊ/, one adult male, 2002 scan) | The 11 vowel area functions, as data: `docs/research/data/story2008-area-functions.csv` | Story's 1994 set (plot only). The sizes are one speaker's, not a population mean (see the measured gap below) |
| Story 2005, JASA 117(5):3231-3254 (area function model: vowel substrate `(pi/4)[Omega + q1 phi1 + q2 phi2]^2`, consonant constriction `C(i) = 1 - exp(-ln16((i-lc)/rc)^2)`, nasal port at about section 22) | The principle: a vowel is a point in a low-dimensional space of whole area functions, blended in diameter space, with a consonant constriction laid on top of it. We blend the 11 measured shapes by weights instead of PCA modes (Omega, phi1, phi2 are in the paper's Table III; not transcribed) | Its 14-parameter control set |
| VocalTractLab (Birkholz; GPL; 2.4 manual, 2005 dissertation) | Nasal cavity area function (Dang & Honda subject, 19 sections x 0.6 cm = 11.4 cm, 0.76-4.24 cm^2); the observation that nasal antiresonances come from the oral cavity as a side branch of the open velum; c = 350 m/s; the wall-loss and radiation (R||L) constants as the reference for the independent check | Its 3D articulators and glottis models; the four paranasal sinus Helmholtz resonators (zeros at 552-1682 Hz) and piriform fossae are not modelled |
| Maeda 1979/1990 (seven jaw/tongue/lip/larynx parameters from cineradiographs; `A = alpha d^beta`) | Structure only: the argument for articulator-level parameters over formant knobs | Per-vowel parameter values (not retrievable); no Maeda parameterisation is built |
| Nasal coupling: Fujimura 1962 (nasal murmur: very low N1 near 250-400 Hz, zeros), Dang & Honda 1994/1995, Story 2005 tier IV | A velum that opens the 3-port junction; the murmur's low first formant and the oral side-branch zero as the measured behaviour to look for | Per-consonant zero ranges for /m n ŋ/: no readable primary source |
| LF glottal flow (Fant, Liljencrants & Lin 1985; Fant 1995 `Rd`; Gobl 2021; Wang & Gobl 2023 regressions `Rkp = 0.118 Rd + 0.224`, `Rap = 0.048 Rd - 0.01`) | Reference for the source spectrum: Rd is a tension axis (low = tense), tilt cutoff falls as Rd rises. The current source is a band-limited saw through a one-pole lowpass whose cutoff is the `tilt` control: it matches the LF pulse's spectral tilt only qualitatively | An LF pulse generator: not built. If the source is moved to LF, `Rd` replaces `tilt` |
| Hillenbrand et al. 1995, JASA 97(5):3099-3111 (male means computed from the public data of github.com/santiagobarreda/hillenbrand_et_al_1995, MIT) and Peterson & Barney 1952 (means from phonTools `pb52`) | The reference formants F1/F2/F3 per vowel for the verification, table below | Table V itself was not read; P&B vowel labels are inferred from the data codes |
| Stress: Sluijter & van Heuven 1996 (ICSLP table + JASA abstract); Fry 1955/58, Lieberman 1960 (secondary reports only) | Stressed syllables are longer (stressed 220 ms against 160 ms unstressed in the initial-stress pairs; a stressed/unstressed ratio of 1.68 in a native-speaker study), 1-3 dB louder overall, f0 higher by about 6 percent when unaccented; duration is the most reliable cue, f0 the weakest in unaccented words | Fry's and Lieberman's own magnitudes (not retrieved) |
| Plosives: Lisker & Abramson 1964 (VOT, secondary table), Chodroff & Wilson 2014 (burst spectra, /b/ COG 2833 Hz, peak 1643 Hz), Stevens & Blumstein (labial onset spectrum diffuse-falling) | A /b/ burst is a short low-frequency-weighted transient with a short voice-onset lag (0-5 ms mean 1 ms for English /b/) | A /b/ voice-bar level in dB and burst duration norms: no source retrieved, so the AquaSynth levels for them are design choices, labelled as such |

## Male reference formants used for verification (Hz)

Hillenbrand 1995 male means (45 men, steady state; computed from the public data): /i/ 343/2323/3001, /ɪ/ 429/2034/2687,
/e/ 476/2090/2692, /ɛ/ 588/1803/2604, /æ/ 591/1930/2595, /ɑ/ 757/1309/2535, /ɔ/ 656/1023/2521, /o/ 498/910/2459,
/ʊ/ 469/1123/2435, /u/ 380/992/2355, /ʌ/ 621/1181/2548. Peterson & Barney 1952 male means (33 men): /i/ 267/2294/2937,
/ɪ/ 392/1993/2569, /ɛ/ 526/1854/2481, /æ/ 664/1727/2420, /ʌ/ 631/1192/2377, /ɑ/ 718/1091/2442, /ɔ/ 568/836/2403,
/ʊ/ 437/1023/2245, /u/ 307/876/2239.

## The gap the area functions must be read against

Story's shapes belong to one speaker. Computed independently (frequency-domain tube chain, `tools/formant-check/tube_formants.py`,
no AquaSynth code), the tabulated area functions at their own lengths give for example /i/ 261/2139, /ʌ/ 561/827, /ɑ/ 666/933,
/u/ 243/684 Hz, against the population means above (/i/ 343/2323, /ʌ/ 621/1181, /ɑ/ 757/1309, /u/ 380/992). So a render that
follows Story's area functions can be correct and still sit 10-30 percent from a population mean. The verification therefore
reports two distances: render against the area function's own predicted formants (does the ladder implement the tube), and
render against the population means (is the speaker typical). Only the first is a defect test.

## Ladder against the tube chain (measured 2026-11-08, unheard)

`tools/formant-check/ladder_formants.py` runs the ladder's update equations in numpy on the 24-cell resampling of each Story shape
(17.52 cm, 350 m/s, loss .998, glottal reflection .75). With the lip reflection at the scaffold's -.82 the rounded and back vowels
show no F2 spectral peak, only a damped shoulder (/ʌ/ 624/3064, /ɔ/ 615/3173, /o/ 674/2546, /ʊ/ 533/2774, /u/ 278/2575 against the tube chain's
549/809, 516/692, 477/787, 478/850, 271/761). Burg LPC (`burg_formants.py`) on a 140 Hz render of the same ladder still places a pole near the
tube chain (/u/ 301/744, /ʊ/ 526/848), so F2 is heavily damped rather than absent; the 10 Hz harmonic-line reading and LPC order 12 do not see it. With the lip reflection at -1 F2 returns but every back vowel reads 6-13 percent above the
tube chain (/ɔ/ 568/741, /o/ 524/844, /ɑ/ 688/1031, /u/ 290/835); 48 cells instead of 24 change nothing, so cell quantisation is not
the cause. The tube chain uses a frequency-dependent piston radiation (end correction, narrow lips reflect almost totally); the ladder's
lip is a constant real reflection. The front vowels (/i ɪ e ɛ æ/) are within 2-7 percent at -.82. Which radiation model the ladder
should have is open (ruling owed); the vowel tests of `VocalTractFormantTests` skip the nine vowels outside 6 percent until then.

## Not retrieved (from the fact sheet)

Story-Titze-Hoffman 1996/1998 numeric tables; Fant 1960, Stevens-House, Wakita, Ladefoged tables; Maeda per-vowel values;
Dang & Honda 1994 text; per-consonant nasal zero ranges; Fry and Lieberman magnitudes; Klatt 1975; burst duration norms and the
/b/ voice-bar level; Liljencrants 1985; the JOS book tract chapters. The Story 2008 table came through a page summariser, not a PDF
text extraction; every column was checked by computing its formants (all eleven give vowel-typical F1/F2 orderings), which
catches a gross transcription error, not a small one.
