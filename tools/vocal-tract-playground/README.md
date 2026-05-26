# AquaSynth Tract Voice Playground

Touchable audition playground for the Aqua DSL `tract` voice surface.

Run it from this directory:

```powershell
node .\serve.mjs
```

Then open:

```text
http://localhost:5125/
```

This is an actual-machine audition surface. The browser sends the visible
controls to `tools/TractGraphRenderer`, which emits an Aqua DSL patch with
`tract propagation=graph`, lowers it through the normal Faust emitter, renders
the compiled DSP, and returns a WAV for playback.

That makes rendering slower than a browser-native oscillator, but it removes
the fake witness from the feedback loop. The sound you hear is the Aqua DSL ->
Faust graph path that the compiler owns.

Faust must be available through `PATH` or `FAUST_HOME`, and the .NET SDK must be
available to run the renderer.
