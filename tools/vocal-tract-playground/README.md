# AquaSynth Tract Voice Playground

Touchable WebAudio playground for the Aqua DSL `tract` voice surface.

Run it from this directory:

```powershell
node .\serve.mjs
```

Then open:

```text
http://localhost:5125/
```

This is a tract-voice control witness. It exposes the same Pink
Trombone-shaped controls that Aqua DSL now lowers through `tract`: frequency,
intensity, tenseness, tongue index/diameter, velum, constriction
index/diameter, turbulence, burst, lip opening, and radiation reflection
controls.

The browser synth runs a stable source/filter witness so the knobs are
touchable while the Faust graph lowering keeps maturing toward Pink Trombone
parity. It is a control playground, not the canonical DSP owner; the canonical
audio path is still the Aqua DSL -> Faust graph lowering.
