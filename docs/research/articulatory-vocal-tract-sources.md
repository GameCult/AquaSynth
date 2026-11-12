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
u +6.0/+8.2. Lowering g does not help (g = .95: aw F1 +9.6; g = .9: the aw F2 peak is lost). The numpy ladder therefore reads systematically high
against the tube chain: F1 +4 to +9%, F2 +3 to +8% for the back vowels. That bias is diagnosed, not isolated: raising the glottal reflection to .9 moves only /aw/ F1
(8.6 to 6.2%); a 1.4 times larger end correction leaves a worst case of 6.9%; the remaining suspects (cell-length quantisation of a 24.03-cell
tract, and that the reference itself uses a different radiation model) are untested. It is the open item.

Held tolerances (`VocalTractFormantTests`, harmonic-line reading of the 10 Hz render): the front vowels i ih e eh ae within 6% on F1 and F2;
the back and rounded vowels within 10%, which is the measured disagreement between two lip radiation models (the ladder's one-pole
Levine-Schwinger-form reflectance, the tube chain's baffled piston R||L) plus the ladder's cell quantisation. All eleven meet their bands. For
a while uh, ah, aw, o and oo_short read F1 +14.8, +10.8, +12.4, +25.8 and +13.0% against the tube chain and the tests were skipped with those
numbers; the numpy ladder read those F1 20-85 Hz lower than the render. The cause was the velopharyngeal junction in `FaustExport.VocalLadder.cs`:
it summed the areas of oral cells 11 and 12 and used their reflection coefficients for the junction that sits between cells 10 and 11, so the
pharynx (cells 10-12 of /o/: .52 .33 .52 cm^2) carried the next step's reflection with the wrong sign. Isolated by running the lip wave of the rendered
ladder under an impulse against the numpy copy (the first echo already differed: -0.30 against -0.23), with the velum shut. Fixed, the render agrees
with the numpy ladder to 10 Hz (o 520/830, uh 580/840, ah 690/980, aw 550/720, oo_short 500/920, u 290/820 at the harmonic lines) and the five tests
run. Read by Burg LPC on the renders (Soul pass of 2026-11-09, vowel window about 100-200 ms), F1 error against the tube chain fell from 5c32bf6 to the fix 05efdc9 as uh +5.3 to -0.5, ah +2.5 to -4.3,
aw +4.2 to +0.7, o +16.7 to +5.7, oo_short +12.7 to +4.9 percent (a reconcile rerun on the same renders, 2026-11-10, reproduces these to within 1 point in the 100-200 ms window, aw least: +3.6 to -0.1).
The figures depend on the window: in the 70-150 ms window of the table below the same renders read uh +4.4 to -2.1, ah -0.6 to -6.6, aw +1.3 to -3.4, o +16.3 to +2.3, oo_short +8.8 to +1.7, and the two vowels
the fix did not repair, u and i, read +10.7 to +13.8 and +15.6 to +17.1 (u +4.6 to +6.4 and i +10.8 to +12.2 at 100-200 ms). So the junction fix removed the large positive bias of o and oo_short in either window (ah turns from near zero or slightly high to 4-7 percent low),
and the earlier "-4.3..+5.9 after" range is the five repaired vowels plus u read at one window: it does not cover i and u, which stay 5-17 percent high by Burg and are the LPC's resolution limit at 140 Hz
(see the table note); ah undershoots by 4 to 7 percent after the fix. The earlier "12-26 percent high becomes 1.5-9 percent high" came from the harmonic-line picker, not from LPC. What remains against the tube chain
(F1 +1.5 to +9%, F2 +2 to +8% at the harmonic lines) is the two radiation models' disagreement above.

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
| i | 294/2080 | 251/2063 | +17.0/+0.8 | 343/2323 | -14.3/-10.4 |
| ɪ | 375/2024 | 346/1987 | +8.5/+1.8 | 429/2034 | -12.5/-0.5 |
| e | 448/1925 | 432/1827 | +3.7/+5.4 | 476/2090 | -5.9/-7.9 |
| ɛ | 551/1603 | 527/1571 | +4.5/+2.0 | 588/1803 | -6.3/-11.1 |
| æ | 739/1550 | 729/1523 | +1.4/+1.8 | 591/1930 | +25.0/-19.7 |
| ʌ | 538/822 | 549/809 | -2.1/+1.6 | 621/1181 | -13.4/-30.4 |
| ɑ | 607/950 | 650/913 | -6.6/+4.0 | 757/1309 | -19.8/-27.4 |
| ɔ | 499/715 | 516/692 | -3.4/+3.4 | 656/1023 | -24.0/-30.1 |
| o | 488/806 | 477/787 | +2.3/+2.4 | 498/910 | -2.0/-11.4 |
| ʊ | 486/894 | 478/850 | +1.7/+5.2 | 469/1123 | +3.7/-20.4 |
| u | 308/811 | 271/761 | +13.8/+6.6 | 380/992 | -18.9/-18.2 |

(Table re-rendered 2026-11-09 after the junction fix, the nasal and stress changes below; the first table, 2026-11-08, had o, ʊ and u 9-16% high.)

Against its own area function the render sits within 6.6 percent on F1 for every vowel but /i/ (+17%) and /u/ (+13.8%), whose F1 near 270-300 Hz is under the LPC's resolution at a
140 Hz line spacing (the 10 Hz harmonic-line reading in `VocalTractFormantTests` puts them at 260 and 290 Hz, +3.6 and +7.0%); F2 of all eleven is within 6.6 percent. Against the population
means the render is 8-30 percent off for the back vowels' F2, which is the one-speaker gap described above, not a defect test.

Nasal (steady parts: the consonant's middle 60 percent against the mean power of the flanking vowels, 70 ms before and 90 ms from 30 ms after): /ɑmɑ/ -11.8 dB, /ɑnɑ/ -9.6 dB, Burg N1 295 and 332 Hz,
inside Fujimura's low-N1 range. The target band is 8 to 16 dB under the vowels, a DESIGN value: no primary level was retrieved (secondary accounts of Fujimura 1962 and later put nasal murmurs
well under their vowels). Before: -13.2 and -3.8 dB by the same measure (the old whole-segment rms read -2.8 and +1.8 dB). Three causes, in order: the /n/ oral closure began to open at half the gesture,
a third before the velum and the contact (held to 0.8 now); then, with the junction on its own interface the nose took its share and both murmurs rose to -4.2 and -2.2 dB; the nasal path now carries
the damping of the nasal cavity (soft walls, turbinates, sinuses): loss .975 per cell, a bandwidth of about 380 Hz at this sample rate (`IpaGestureExperiment.NasalPathLoss`; the bandwidth is a design value,
none retrieved). /n/ stays 2 dB louder than /m/: the cause is not isolated (a shorter oral side branch is the physical candidate).

Stress (cue values in `WeksaWordPlan.CueFor`): primary stress renders longer by 75 ms for the vowel (design 68 ms), f0 148.1 against 139.9 Hz (ratio 1.059; design 1.06). Level: +2.0 dB mean over the five vowels
i a u e o (`PrimaryStressRendersAboutTwoDecibelsLouderOnAverageOverVowels`, 2.05 dB in the last run) with the amplitude factor at 1.30 (was 1.26: +1.7 dB). The f0 rise alone moves the harmonics against F1 and
changes the level by -1.3 to +1.8 dB by vowel, so a single vowel is a noisy measure of the amplitude cue: /i/ in /nibu/ (steady window 150-250 ms) reads +0.21 dB in the last render (+0.99 before), isolated /i/
+0.79 dB, /e/ +4.1 dB. The mean is what is held; the scatter between vowels is the harmonic/formant interaction of a 140 Hz voice, not the cue.

Plosive: word-initial /b/ closure is the tract wall's voice bar, -29.2 dB re the following vowel, /p/ -40.1 dB (`PlosiveClosureCarriesTheVoiceBarOnlyWhenVoiced`); /bɪn/ closure peak 0.0036 (was 0.0000, below 16-bit
resolution), release 60-95 ms peak 0.0695, vowel 0.129. The wall radiation reads the first-cell pressure of the ladder (R0 + L0), band-limits it to 90-300 Hz and adds it at gain 0.7 (a DESIGN value for a voice-bar
level of -30 dB; the primary level was not retrieved, and the 2.4 manual's skin radiation, Birkholz & Zhang 2025, is the reference to read). In a vowel the term moves the f0 line by under 1 dB; the 200 and 300 Hz harmonics move up to +1.5 dB on one open vowel (wall-only measurement, Soul pass). The band is two second-order sections, not a brick wall: with the mouth and the velum shut the bar sits about 25 dB down above 300 Hz. The burst
itself (difference of renders with and without `release_flow`), before the #47 fix (c0e6b98; see the re-measure below for the post-fix figures), peaked at about 0.006-0.008, -24 dB re the vowel peak; the release peak in the table above is the voicing breaking through as the contact opens. Burst gain 0.06 is a DESIGN VALUE.

Weak spots found in the renders, 2026-11-08 (not re-measured on 2026-11-12 unless named in the next paragraph): (1) the vowel level is not steady, it rises for about 100 ms after onset (ɑ: rms 0.028 at 80 ms to 0.079 at 160 ms) so any window early in a vowel
reads low; the cause is not isolated (surface smoothing and the pressure ramp are the suspects). (3) The voice bar's level is a design value and its band
(90-300 Hz) assumes f0 of 90 Hz and up. (4) Only f0 and level of the first vowel are checked per cue; the cues' perceptual strength is unknown. Everything here is a measurement, nothing was heard.

Re-measure 2026-11-12. The stress, nasal and lax-vowel figures were taken on main c0e6b98, before the #47 burst fix (which does not touch them); the plosive-release bullet gives the c0e6b98 figures as "before" and the main af0e44f figures, after the #47 fix, as "after" (`tools/formant-check/weak_spots.py` over renders from `tools/aqua-listen/kits/tract-weak-spots/render.sh`; the renders reproduce the table above: /ɪ/ 375/2024 Hz, /ʊ/ 486/894 Hz, murmurs -11.81 and -9.64 dB, /i/ in /nibu/ +0.21 dB). Unheard.
- Stress level (100-170 ms of each vowel, stressed against unstressed, f0 rise split from the rest by a render of the unstressed vowel at 148.4 Hz): total i +1.22, ɑ +1.67, u +2.07, e +2.99, o +1.69 dB (mean 1.93, sd 0.60);
  the f0 rise alone -0.80, +0.03, +0.17, +1.23, -0.07 dB; the rest (the amplitude factor 1.30 = 2.28 dB nominal, with the longer vowel) +2.02, +1.64, +1.90, +1.76, +1.76 dB (sd 0.14). The scatter between vowels is the f0 rise; the amplitude cue itself is even.
  No level the f0 rise cannot move was found cheap, so the five-vowel-mean basis stands (`aqua-body:question:stress-amplitude-calibration-basis`); the rest sits 0.46 dB under the nominal 2.28 dB, cause not isolated.
- Plosive release: a defect was found and fixed. `release_flow` was held at the burst gain from the plosive's gesture on (a surface holds its first value before its first point and its last after its last), so every later contact state made noise:
  the difference of the renders with and without `release_flow` peaked at 0.0302 in the final /n/ and tail of /bɪn/ (above its 0.0226 at the release, 81 ms), 0.0126 in /bʊn/; in /nibu/ (flat) the leading /n/ carried it backward: peak 0.0688, rms 0.0067 against the /n/'s own 0.0058.
  After the fix (`BurstGainAroundGesture`: zero before and after the plosive, the gain through it; figures from here to the end of the bullet are post-fix, main af0e44f) the same difference is 0.0 from 120 ms after the release and the burst itself is unchanged (0.0226 at 81 ms in /bɪn/). Burst alone, after the fix: /bɪn/ 0.0226 (-15.1 dB re its vowel peak 0.129), /ba/ 0.0142 (-24.0 dB re 0.225), /bʊn/ 0.0130 (-24.3 dB re 0.213),
  each falling to a tenth in 15-17 ms; the pre-fix "0.006-0.008, -24 dB" figure above does not hold for /bɪn/. The release peak of the full render (voicing breaking through) is -5.4, -6.3, -5.9 dB re the vowel peak. Test `PlosiveBurstExistsOnlyAroundItsOwnRelease`.
- Nasals: /m/ -11.81 dB, /n/ -9.64 dB re the flanking vowels, Burg N1 295 and 332 Hz. The 2 dB is not harmonic sampling: /n/ minus /m/ is 2.01, 2.17, 2.05, 1.61 dB at f0 110, 140, 170, 200 Hz. Band levels of the murmur (0-400, 400-800, 800-1600, 1600-3200 Hz): /m/ 36.3, 23.6, 21.3, 2.7; /n/ 38.4, 28.2, 16.8, 0.9 dB.
  The cause (the shorter oral side branch, 7.1 cm against 9.6 cm from the velum junction) is a candidate, not shown. Open.
- Lax vowels: /ɪ/ and /ʊ/ are in the DSL table (`StoryVowelAreas`) and render in `WordRender` (/bɪn/, /bʊn/, isolated). Burg F1/F2: /ɪ/ 375/2024 against the tube chain 346/1987 (+8.5/+1.8 %), Hillenbrand 429/2034 (-12.5/-0.5 %), Peterson & Barney 392/1993 (-4.3/+1.5 %);
  /ʊ/ 486/894 against 478/850 (+1.7/+5.2 %), 469/1123 (+3.7/-20.4 %), 437/1023 (+11.3/-12.6 %). No defect found.
- Phones the tract cannot make: `WordRender` refuses /t/, /d/, /k/, /l/ with exit 4 (`UNSUPPORTED PHONES (the tract has no gesture for them; nothing rendered): t`); the table holds p b m n s z ʃ ʒ ʍ w and the Story vowels. A direct `phoneme_gesture` on an unknown IPA symbol still falls back to an alveolar approximant (`GestureDescriptor`); not changed here.

Burst-window mutants (hand-applied in a scratch worktree on af0e44f plus the cut, never committed, 2026-11-12; `BurstGainAroundGesture`). The rendered test `PlosiveBurstExistsOnlyAroundItsOwnRelease` did not kill any of them, so `PlosiveBurstGainWindowIsOneRampEitherSide` reads the window off the spline (opens one ramp, 0.15 of the gesture, before the plosive; closed one ramp after it):

| Mutant | Rendered test | Window test |
|---|---|---|
| ramp 0.15 x duration to 0 | survived | killed |
| ramp 0.15 x duration to 1.0 x duration | survived | killed |
| last point at start+duration+ramp moved to start+duration+3*ramp | survived | killed |

Mutations (hand-applied, restored, each run against the named tests): pitch ratio ignored, burst removed, burst not shaped by opening, burst gain x20 (0.06 to 1.2), stress duration ignored,
stress pitch ignored, stress amplitude ignored, secondary equal to primary, probe timeline sample rate re-hardcoded to 48000: nine of nine killed. Script: `work/mut-b3.sh` in the aqua workspace.
