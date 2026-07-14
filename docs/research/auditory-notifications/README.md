# Auditory Notification Research Distillation

This ledger grounds AquaSynth Dings in auditory-interface research without
pretending that a desktop agent notification is a medical or aviation safety
alarm. The local PDFs are research inputs, not redistributed product assets.

## Findings that constrain the implementation

- **Use decaying, percussive envelopes.** Foley et al. found temporally varying,
  percussive tones less annoying and more detectable than flat tones under
  concurrent speech load. Every curated instrument therefore has a short attack,
  brief sustain, and decaying tail. Flat beeps and broadband noise are rejected.
- **Map urgency through timing deliberately.** Haas and Casali compared 0, 150,
  and 300 ms inter-pulse intervals; shorter intervals increased perceived
  urgency without improving detection time. Routine AquaSynth motifs therefore
  keep onset gaps in the 180–320 ms range. We do not make ordinary progress
  notifications sound urgent merely to make them noticeable.
- **Keep pitch and spectral agitation restrained.** Hellier, Edworthy, and
  Dennis found speed, fundamental frequency, repetition, and inharmonicity all
  affect perceived urgency. The catalog stays in a calm mid register, uses
  consonant C-major intervals, limits brightness, and forbids noisy timbres.
- **Do not rely on arbitrary melodies alone.** Sanderson, Wee, and Lacherez
  found poor learning and persistent confusion in a standardized melodic alarm
  set. Here the motif grammar is small and systematic: landing pitch and motion
  carry event class, while timbre independently identifies the speaking agent.
- **Expect masking when sounds overlap.** Bolton et al. experimentally showed
  that masking a tonal alarm's primary harmonic can make it indistinguishable.
  The service mixes concurrent sounds, but the curated catalog intentionally
  separates timbre families and exposes `stop_dings`; a later field test should
  measure identification under realistic overlap before expanding the catalog.
- **Prefer a small, tested vocabulary.** Studies of IEC melodic alarms and
  overlapping alarms show that nominal distinctness is not reliable human
  discriminability. New instruments require listening/identification trials;
  parameter validation is only admission hygiene.

## Implemented policy

- Event selects motif; instrument selects agent identity.
- Instrument source is catalog-only. MCP callers cannot inject patch scripts.
- Root frequency: 196–392 Hz.
- Brightness curation score: 0.20–0.78.
- Decay: 0.25–1.50 seconds; attacks remain short and nonzero where practical.
- No broadband-noise instruments.
- Multi-note onset gaps: 180–320 ms.
- One persistent playback mixer owns the output device.
- Aqua daemon remains the sole synthesis authority.

## Sources

1. Foley, L. et al. (2022), *More detectable, less annoying: Temporal
   variation in amplitude envelope and spectral content improves auditory
   interface efficacy*, JASA 151(5), DOI `10.1121/10.0010447`. Saved as
   `foley-et-al-2022-more-detectable-less-annoying.pdf`.
2. Hellier, E. J., Edworthy, J., & Dennis, I. (1993), *Improving auditory
   warning design: quantifying and predicting the effects of different warning
   parameters on perceived urgency*, Human Factors 35(4), DOI
   `10.1177/001872089303500408`.
3. Haas, E. C., & Casali, J. G. (1993), *The Perceived Urgency and Detection
   Time of Multi-Tone and Frequency-Modulated Warning Signals*, HFES Annual
   Meeting, DOI `10.1177/154193129303700906`.
4. Sanderson, P. M., Wee, A., & Lacherez, P. (2006), *Learnability and
   discriminability of melodic medical equipment alarms*, Anaesthesia 61(2),
   DOI `10.1111/j.1365-2044.2005.04502.x`.
5. Lacherez, P., Seah, E. L., & Sanderson, P. M. (2007), *Overlapping Melodic
   Alarms Are Almost Indiscriminable*, Human Factors 49(4), DOI
   `10.1518/001872007X215719`.
6. Bolton, M. L. et al. (2020), *An Experimental Validation of Masking in IEC
   60601-1-8:2006-Compliant Alarm Sounds*, Human Factors 62(6), DOI
   `10.1177/0018720819862911`.
7. NIST Technical Note 1950, *Auditory Warning Signals*. Saved as
   `nist-tn-1950-auditory-warning-signals.pdf`.

The source README for the event vocabulary is
<https://github.com/iain/minimal-dings/blob/main/minimal-dings/README.md>.
