# Render Slot

This directory is the audible slot for `wet-voice-01`.

Expected future artifact names:

- `wet-voice-01.reference.wav`: pinned external or oracle reference render.
- `wet-voice-01.aquasynth.wav`: AquaSynth-rendered candidate for comparison.
- `render-report.md`: short notes on toolchain, timing, confidence, and known
  lies.

No audio file is committed yet because the repo does not have a settled policy
for checked-in witness WAVs. Until that lands, this directory remains the named
render address and the witness stays honest about the gap instead of spraying
untracked artifacts into `artifacts/` and pretending that counts as a contract.
