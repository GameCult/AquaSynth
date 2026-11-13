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
