# voice-source-1: predictions, written before any clip was rendered

Phrase: /ɑmi/ (WordRender, 140 Hz, 48 kHz). Meta's verdict on tract-weak-spots-1 heard "a kazoo with a two pack a day smoking habit", "reedy ... pink trombone", "a basketball buzzer" for /m/ and /n/; the voice source (a band-limited saw through a one-pole lowpass, plus 0.08 x noise x white) is the shared suspect. Each pair below changes ONE source parameter, everything else is the scaffold's.

Source-side numbers (computed from the two source formulas alone, harmonics of 140 Hz after the same tilt lowpass at 1131 Hz; the tract is not in them): Rosenberg minus saw, per harmonic: H1 +1.2 dB, H2 +0.3, H3 -6.3, H5 -10.5, H8 -17.2, H10 -17.8, H15 -22.0, H20 -24.5, H28 -27.4. Source rms differs +0.4 dB.

| pair | A | B | what changes | prediction | falsified if |
|---|---|---|---|---|---|
| pulse | saw | rosenberg | `pulse=` | B is darker and rounder: less buzz, less edge, F2 of /i/ weaker (the harmonics near 2 kHz are ~22 dB lower). Loudness the same (rms within 0.5 dB). | no difference heard; or B heard as buzzier/brighter; or B heard as the same "kazoo" quality. |
| noise level | saw, noise at scaffold (effective level .05: the gesture splines hold noise at .05 and win over the slider's base value) | saw, white noise raised to HNR 20 dB | `noise=` | B has audible breath/hiss under the voice; a listener who called A a "kazoo" or "buzzer" hears B as less pure-tone, but still a machine. | B is not noisier; or the noise is heard as a separate hiss stream not fused with the voice; or A and B are both still a kazoo to the same degree (then purity of the periodicity is not what makes it a kazoo). |
| noise colour | white noise, HNR 20 dB | noise high-passed at 2 kHz, HNR 20 dB | `noise_hp=` (noise level re-set to keep HNR 20) | B reads as hiss/"s"-like air, more separable from the voice than A; A reads as roughness/breathy voice. | no difference; or B heard as less hissy than A. |
| aspiration level | high-passed noise, HNR 25 dB | high-passed noise, HNR 15 dB | `noise=` | B is clearly hissy; A is at most faintly airy and may be called "no difference" from a clean voice. | A is already clearly hissy, or B is not hissier than A. |

Measured HNR (hnr.py, Boersma autocorrelation, median over voiced frames) is reported next to each clip after rendering; a target missed by more than 2 dB is stated as missed, and the noise value is not retuned to the answer.

What this kit cannot say: LF (the Liljencrants-Fant model) is not tested; the pulse is Rosenberg C with fixed open phase 0.4 and return 0.16. One listener, one phrase, one f0, one device.

## Added after the calibration renders, before the kit renders

Sixteen calibration renders of the same phrase were made in two rounds of eight (14:32 and 14:43 on 2026-11-13; nobody listened to them). The figures below are from the second round plus the Rosenberg render of the first; the first round's other five renders were made and their results are not reported here. They gave: noise 0 -> HNR 30.8 dB (the method's ceiling on this phrase), white noise .1/.16/.25 -> 27.7/25.2/22.2, noise high-passed at 2 kHz .2/.4/.7/1.0 -> 24.5/18.4/13.6/10.6, and Rosenberg at the scaffold's noise -> 29.1 dB. They also showed the Rosenberg render is 2.5 dB lower in output rms (measured -2.54 dB on the final render) and 6.8 dB lower in peak than the saw, although the SOURCE rms is matched: the tract passes the saw's strong upper harmonics and the pulse has none. The first prediction row's "loudness the same" is therefore already known to be false at the output; it stays in the table as written.

A fifth pair is added so loudness cannot carry the answer: the Rosenberg render scaled up by its measured rms deficit against the saw (a gain on the clip, applied by the kit's render script), against the same saw render. Prediction: the same darker, rounder B as the pulse pair, and B is no longer the quieter one. FALSIFIED if B at matched rms is no longer heard as darker/rounder (then loudness, not shape, carried the first pair), or if it is heard as louder or brighter.

Targets chosen from the calibration fit: white .33 -> HNR 20 dB; high-passed .33 -> 20 dB, .165 -> 25 dB, .6 -> 15 dB.

## Measured on main ca6af1d

Figures for the ten kit clips, taken on main `ca6af1d` (PR #52) with `hnr.py` and `clips.py` (`measures.txt`). Removed from the clip titles, which the listener can see through the export.

| clip id | file | HNR dB | output rms | peak | note |
|---|---|---|---|---|---|
| pulse-saw | saw_a.wav | 30.3 | 0.0537 | 0.227 | scaffold: band-limited saw through the tilt lowpass |
| pulse-rosenberg | ros.wav | 29.5 | 0.0401 (-2.5 dB re saw) | 0.102 (-6.9 dB) | rms deficit is a fact at the output; the prediction row's "loudness the same" is already falsified there |
| pulsematch-saw | saw_b.wav | 30.3 | 0.0537 | 0.227 | byte copy of pulse-saw's render |
| pulsematch-rosenberg | ros_matched.wav | 29.5 | 0.0537 (matched to the saw) | 0.137 | ros times 1.341 (+2.55 dB) |
| noise-clean | clean.wav | 30.3 | 0.0537 | 0.227 | the scaffold's noise floor, 0.05; noise 0 measured 30.8 before the tract fix, the method's ceiling here |
| noise-hnr20 | w20.wav | 19.9 | 0.0539 | 0.242 | white noise raised to level 0.33 |
| colour-white20 | w20_b.wav | 19.9 | 0.0539 | 0.242 | byte copy of noise-hnr20's render |
| colour-hp20 | h20.wav | 19.9 | 0.0540 | 0.238 | noise high-passed at 2 kHz (2nd order), level 0.33 |
| aspiration-hp25 | h25.wav | 25.8 | 0.0538 | 0.231 | high-passed noise, level 0.165 |
| aspiration-hp15 | h15.wav | 14.9 | 0.0546 | 0.250 | high-passed noise, level 0.6 |

Band energy re total, 0-0.5 / 0.5-1.5 / 1.5-3 / 3-6 kHz, from the render before the tract fix of PR #52: saw -2.3 / -4.0 / -24.0 / -24.9 dB, Rosenberg -0.3 / -12.4 / -43.7 / -40.3 dB. These were not re-taken with the original method. A whole-file Hann periodogram on the new render gives saw -2.2 / -4.0 / -24.0 / -25.0 and Rosenberg -0.3 / -12.5 / -43.2 / -40.2, within 0.5 dB; a different FFT estimate of the new render differs by under 1 dB, and an independent check found the figures depend on the estimator (Welch differs by up to 1.6 dB, a raw periodogram by up to 3.1 dB). Read them as estimates to about 1 to 3 dB. The HNR figure is the Boersma autocorrelation method of `hnr.py` on the rendered output; Praat's autocorrelation HNR is within 0.5 dB for the noisy clips, and the clean clip sits at this method's ceiling.

Record corrections after the Soul pass (2026-11-14): the noise pair's table cell said the scaffold's noise was .04, the effective level is .05 (the cell was edited; the pair, the prediction and the falsifier are unchanged); the calibration addendum's count of renders was eight and is sixteen.
