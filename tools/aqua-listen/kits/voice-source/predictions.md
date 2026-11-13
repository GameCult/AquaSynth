# voice-source-1: predictions, written before any clip was rendered

Phrase: /ɑmi/ (WordRender, 140 Hz, 48 kHz). Meta's verdict on tract-weak-spots-1 heard "a kazoo with a two pack a day smoking habit", "reedy ... pink trombone", "a basketball buzzer" for /m/ and /n/; the voice source (a band-limited saw through a one-pole lowpass, plus 0.08 x noise x white) is the shared suspect. Each pair below changes ONE source parameter, everything else is the scaffold's.

Source-side numbers (computed from the two source formulas alone, harmonics of 140 Hz after the same tilt lowpass at 1131 Hz; the tract is not in them): Rosenberg minus saw, per harmonic: H1 +1.2 dB, H2 +0.3, H3 -6.3, H5 -10.5, H8 -17.2, H10 -17.8, H15 -22.0, H20 -24.5, H28 -27.4. Source rms differs +0.4 dB.

| pair | A | B | what changes | prediction | falsified if |
|---|---|---|---|---|---|
| pulse | saw | rosenberg | `pulse=` | B is darker and rounder: less buzz, less edge, F2 of /i/ weaker (the harmonics near 2 kHz are ~22 dB lower). Loudness the same (rms within 0.5 dB). | no difference heard; or B heard as buzzier/brighter; or B heard as the same "kazoo" quality. |
| noise level | saw, noise at scaffold (.04) | saw, white noise raised to HNR 20 dB | `noise=` | B has audible breath/hiss under the voice; a listener who called A a "kazoo" or "buzzer" hears B as less pure-tone, but still a machine. | B is not noisier; or the noise is heard as a separate hiss stream not fused with the voice; or A and B are both still a kazoo to the same degree (then purity of the periodicity is not what makes it a kazoo). |
| noise colour | white noise, HNR 20 dB | noise high-passed at 2 kHz, HNR 20 dB | `noise_hp=` (noise level re-set to keep HNR 20) | B reads as hiss/"s"-like air, more separable from the voice than A; A reads as roughness/breathy voice. | no difference; or B heard as less hissy than A. |
| aspiration level | high-passed noise, HNR 25 dB | high-passed noise, HNR 15 dB | `noise=` | B is clearly hissy; A is at most faintly airy and may be called "no difference" from a clean voice. | A is already clearly hissy, or B is not hissier than A. |

Measured HNR (hnr.py, Boersma autocorrelation, median over voiced frames) is reported next to each clip after rendering; a target missed by more than 2 dB is stated as missed, and the noise value is not retuned to the answer.

What this kit cannot say: LF (the Liljencrants-Fant model) is not tested; the pulse is Rosenberg C with fixed open phase 0.4 and return 0.16. One listener, one phrase, one f0, one device.

## Added after the calibration renders, before the kit renders

Eight calibration renders of the same phrase were made to turn noise settings into dB (nobody listened to them). They gave: noise 0 -> HNR 30.8 dB (the method's ceiling on this phrase), white noise .1/.16/.25 -> 27.7/25.2/22.2, noise high-passed at 2 kHz .2/.4/.7/1.0 -> 24.5/18.4/13.6/10.6, and Rosenberg at the scaffold's noise -> 29.1 dB. They also showed the Rosenberg render is 2.4 dB lower in output rms and 6.8 dB lower in peak than the saw, although the SOURCE rms is matched: the tract passes the saw's strong upper harmonics and the pulse has none. The first prediction row's "loudness the same" is therefore already known to be false at the output; it stays in the table as written.

A fifth pair is added so loudness cannot carry the answer: the Rosenberg render scaled up by its measured rms deficit against the saw (a gain on the clip, applied by the kit's render script), against the same saw render. Prediction: the same darker, rounder B as the pulse pair, and B is no longer the quieter one. FALSIFIED if B at matched rms is no longer heard as darker/rounder (then loudness, not shape, carried the first pair), or if it is heard as louder or brighter.

Targets chosen from the calibration fit: white .33 -> HNR 20 dB; high-passed .33 -> 20 dB, .165 -> 25 dB, .6 -> 15 dB.
