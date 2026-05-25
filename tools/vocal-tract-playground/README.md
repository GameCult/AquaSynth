# AquaSynth Vocal Tract Playground

Touchable WebAudio playground for the current `VocalTractControlTarget` surface.

Run it from this directory:

```powershell
node .\serve.mjs
```

Then open:

```text
http://localhost:5125/
```

This is a control-surface witness, not a Pink Trombone-class tract simulation.
It exposes the real AquaSynth speech-driver controls, draws a tract-shaped
visualization, and uses a small WebAudio source/filter proxy so the knobs are
audible while the proper tract renderer continues to mature.
