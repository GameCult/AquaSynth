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

The ladder's lip termination is the open-end reflectance R(z) = -g (1-b)/(1-b z^-1) (Smith PASP; Valimaki and Karjalainen 1994; Levine and
Schwinger): g = 1 is the radiation load's `reflection` magnitude (sign stays negative), and b follows the lip cell's area, with a = sqrt(A/pi) cm,
q = (a fs / c)^2, c = 35000 cm/s, b/(1-b)^2 = q, b = ((2q+1) - sqrt(4q+1)) / (2q), clamped to [0, .9]. The low-frequency loss 1-|R|^2 is then
(ka)^2 and the DC group delay b/(1-b) samples is the end correction. A constant -.82 reflection, the earlier termination, had no F2 peak for
the rounded and back vowels and no end correction.

`tools/formant-check/ladder_formants.py --filtered` runs the same ladder in numpy (24 cells, 17.52 cm, 350 m/s, loss .998, glottal reflection .75).
Against the independent tube chain (`tube_formants.py`, 17.52 cm; baffled-piston R||L radiation) the numpy ladder reads at g = 1 (F1/F2 error %):
i +1.4/+0.4, ih +1.7/+1.5, e +2.3/+4.2, eh +4.0/+3.1, ae +2.9/+3.5, uh +5.6/+4.3, ah +5.1/+6.6, aw +8.6/+3.7, o +8.2/+5.0, oo_short +5.5/+8.2,
u +6.0/+8.2. Lowering g does not help (g = .95: aw F1 +9.6; g = .9: the aw F2 peak is lost). The ladder therefore reads systematically high:
F1 +4 to +9%, F2 +3 to +8% for the back vowels. That bias is diagnosed, not isolated: raising the glottal reflection to .9 moves only /aw/ F1
(8.6 to 6.2%); a 1.4 times larger end correction leaves a worst case of 6.9%; the remaining suspects (cell-length quantisation of a 24.03-cell
tract, and that the reference itself uses a different radiation model) are untested. It is the open item.

Held tolerances (`VocalTractFormantTests`, harmonic-line reading of the 10 Hz render): the front vowels i ih e eh ae within 6% on F1 and F2;
the back and rounded vowels within 10%, which is the measured disagreement between two lip radiation models (the ladder's one-pole
Levine-Schwinger-form reflectance, the tube chain's baffled piston R||L) plus the ladder's cell quantisation. Only /u/ meets its 10% band in the
render; uh, ah, aw, o and oo_short read F1 +14.8, +10.8, +12.4, +25.8 and +13.0% against the tube chain (render 630, 720, 580, 600, 540 Hz; LPC agrees
with the harmonic lines) and stay skipped with those numbers. The numpy ladder reads those F1 20-85 Hz lower than the render, so that excess sits in
the rendered chain (untested suspect: the glottal tissue-valve source's spectral shape; `load_coupling=0` changed nothing), not in the lip reflectance.

Burg LPC (`burg_formants.py`) on the WordRender renders at 140 Hz (held vowels): /i/ in nibu 279/2105/2924, /u/ in nibu 283/836/2637, /ih/ in bin
359/2012/2536, /oo_short/ (bʊn) in bun 543/899/2781, against the tube chain i 251/2063/2877, u 271/761/2574, ih 346/1987/2491, oo_short 478/850/2760 and
Hillenbrand male means i 343/2323/3001, u 380/992/2355, ih 429/2034/2687, oo_short 469/1123/2435.

## Not retrieved (from the fact sheet)

Story-Titze-Hoffman 1996/1998 numeric tables; Fant 1960, Stevens-House, Wakita, Ladefoged tables; Maeda per-vowel values;
Dang & Honda 1994 text; per-consonant nasal zero ranges; Fry and Lieberman magnitudes; Klatt 1975; burst duration norms and the
/b/ voice-bar level; Liljencrants 1985; the JOS book tract chapters. The Story 2008 table came through a page summariser, not a PDF
text extraction; every column was checked by computing its formants (all eleven give vowel-typical F1/F2 orderings), which
catches a gross transcription error, not a small one.

## Render verification, stress cue and plosive burst (2026-11-08, unheard)

Renders: `WordRender` at f0 140 Hz, one 180 ms vowel per file (`v_<id>`), plus /ɑmɑ/, /ɑnɑ/, /ˈnibu/ with and without primary stress, /bɪn/, /bʊn/.
Estimator: `tools/formant-check/verify_renders.py` over `burg_formants.py` (Burg LPC order 12 at 10 kHz, 30 ms frames, vowel window 70-150 ms), a separate
Python process that shares no code with the C# tests or the ladder; references are Hillenbrand 1995 and Peterson & Barney 1952 male means (above) and the
tube chain at 17.52 cm (`tube_formants.py`). Files: `/opt/commons/handoff/aqua-tract-articulatory-20261108` (wav/, scripts/, manifest.json, verify.txt, verify.json).

| vowel | Burg F1/F2 | tube chain F1/F2 | render vs tube F1/F2 % | Hillenbrand F1/F2 | render vs Hillenbrand % |
|---|---|---|---|---|---|
| i | 290/2111 | 251/2063 | +15.5/+2.3 | 343/2323 | -15.4/-9.1 |
| ɪ | 374/2032 | 346/1987 | +8.2/+2.3 | 429/2034 | -12.7/-0.1 |
| e | 439/1929 | 432/1827 | +1.6/+5.6 | 476/2090 | -7.8/-7.7 |
| ɛ | 553/1602 | 527/1571 | +4.9/+2.0 | 588/1803 | -6.0/-11.1 |
| æ | 718/1558 | 729/1523 | -1.5/+2.3 | 591/1930 | +21.5/-19.3 |
| ʌ | 573/811 | 549/809 | +4.4/+0.3 | 621/1181 | -7.7/-31.3 |
| ɑ | 646/949 | 650/913 | -0.6/+3.9 | 757/1309 | -14.7/-27.5 |
| ɔ | 523/718 | 516/692 | +1.4/+3.8 | 656/1023 | -20.3/-29.8 |
| o | 555/819 | 477/787 | +16.4/+4.1 | 498/910 | +11.4/-10.0 |
| ʊ | 520/897 | 478/850 | +8.8/+5.5 | 469/1123 | +10.9/-20.1 |
| u | 300/816 | 271/761 | +10.7/+7.2 | 380/992 | -21.1/-17.7 |

Against its own area function the render sits within 6 percent for e ɛ æ ʌ ɑ ɔ and F2 of all eleven within 7.2 percent; F1 of i, o, u and ʊ reads 9-16 percent high.
The F1 excess is the ladder's known high bias (above) plus the LPC's resolution with a 140 Hz line spacing for F1 near 270-300 Hz: not separated. Against the population
means the render is 8-30 percent off for the back vowels' F2, which is the one-speaker gap described above, not a defect test.

Nasal: /ɑmɑ/ murmur Burg N1 220 Hz (F2 989), /ɑnɑ/ murmur 285 Hz (F2 1041), inside Fujimura's low-N1 range; murmur rms re the second vowel -7.4 dB (m) and -2.7 dB (n). A real murmur
is weaker than that (no source retrieved for the level); the /n/ is too loud. Open.

Stress (cue values in `WeksaWordPlan.CueFor`): primary stress renders longer by 75 ms for the vowel (design 68 ms), f0 148.1 against 139.9 Hz (ratio 1.059; design 1.06), level +0.93 dB at the end of the vowel
(design +2 dB). The shortfall is not the amplitude path: `IntensityIsAnAmplitudeFactorOnTheRenderedLevel` renders /ɑ/ at intensity 1.26 against 1 and finds the 2 dB; in /i/ with F1 near 290 Hz,
raising f0 by 6 percent moves the harmonic nearest F1 and costs about one dB of the gain.

Plosive: /bɪn/ closure peak 0.0000 (voice bar below 16-bit resolution here), release 60-95 ms peak 0.0625, vowel 0.126 (-6 dB). The burst itself (difference of renders with and without `release_flow`)
peaks at about 0.0076, -24 dB re the vowel peak; the release peak in the table above is the voicing breaking through as the contact opens. Burst gain 0.06 is a DESIGN VALUE.

Weak spots found in the renders: (1) the vowel level is not steady, it rises for about 100 ms after onset (ɑ: rms 0.028 at 80 ms to 0.079 at 160 ms) so any window early in a vowel
reads low; the cause is not isolated (surface smoothing and the pressure ramp are the suspects). (2) /n/ murmur louder than a nasal murmur should be. (3) Closure is silent, not a voice bar.
(4) Only f0 and level of the first vowel are checked per cue; the cues' perceptual strength is unknown. Everything here is a measurement, nothing was heard.

Mutations (hand-applied, restored, each run against the named tests): pitch ratio ignored, burst removed, burst not shaped by opening, burst gain x20 (0.06 to 1.2), stress duration ignored,
stress pitch ignored, stress amplitude ignored, secondary equal to primary, probe timeline sample rate re-hardcoded to 48000: nine of nine killed. Script: `work/mut-b3.sh` in the aqua workspace.
