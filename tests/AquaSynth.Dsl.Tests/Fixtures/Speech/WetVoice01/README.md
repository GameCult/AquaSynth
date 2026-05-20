# wet-voice-01

`wet-voice-01` is the named audible witness for AquaSynth's utterance lane.
It is not a model dump, not a benchmark suite, and not a universal schema.
It is one tiny canary with enough pinned paperwork to keep the boundary and the
evidence visible while the machine changes around it.

## Boundary

- Weksa owns conversational intent, semantics, and lowering into the structured
  utterance in [`structured-utterance.json`](structured-utterance.json).
- AquaSynth owns the lowering from that structured utterance into tract/synth
  automation in [`automation-trace.json`](automation-trace.json) and into the
  audible render described under [`render/`](render/).

If a future stage needs meaning that is not present in the structured
utterance, the fix is to extend the utterance contract visibly. Do not smuggle
semantics into an opaque embedding blob and call it progress.

## File Layout

- `structured-utterance.schema.json`: pinned schema for the Weksa -> AquaSynth
  handoff artifact.
- `structured-utterance.json`: one inspectable utterance instance.
- `automation-trace.json`: the AquaSynth-owned lowering receipt from utterance
  to automation targets and gestures.
- `render/RENDER.md`: render slot and instructions while no committed witness
  audio exists yet.
- `notes.json`: timing, confidence, provenance, and current limitations.

This witness was introduced for Bifrost topic
`topic_040d9807-2efa-4f89-acce-ca05300bfc2a` so the speech lane has an address
instead of a rumor.
