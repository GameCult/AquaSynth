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
index/diameter, turbulence, lip opening, and radiation reflection controls.

The browser synth runs a small 44-cell/28-cell waveguide-style witness so the
knobs are touchable while the Faust lowering keeps maturing toward exact Pink
Trombone parity.
