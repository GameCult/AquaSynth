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

This is a voice-patch control witness, not a Pink Trombone-class tract
simulation. It exposes the knobs the learned speech driver is meant to control:
phonetic and utterance input go into the model, the model predicts this
`VocalTractControlTarget` vector, and the voice patch renders sound from those
values. The WebAudio source/filter proxy only makes that target vector touchable
while the proper tract renderer continues to mature.
