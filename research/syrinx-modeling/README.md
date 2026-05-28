# Syrinx Modeling Research

This folder persists public syrinx-modeling references for Aqua's graph-native bird voice work. Downloaded PDFs and HTML snapshots live in `sources/`; `sources.json` records provenance and notes when only metadata was reachable.

## Distilled Takeaways

### The Syrinx Is A Nonlinear Source, Not A Pitch Knob

The useful public model family starts with the Mindlin/Laje low-dimensional syrinx oscillator. It does not treat birdsong as an oscillator whose frequency is directly commanded. It treats the labia as a nonlinear biomechanical oscillator driven by physiological controls, mainly subsyringeal or bronchial pressure and syringeal muscle tension. Pitch, amplitude, register changes, onset, and some complex acoustic events emerge from the oscillator and its coupling to the tract.

Aqua implication: current `freq=` on the acoustic source is acceptable only as a smoke-test carrier. The reusable DSL primitive should be a labial oscillator with pressure, tension/stiffness, aperture/rest position, mass, damping, saturation, and coupling/load controls. Fitted pitch should become an observation or target loss term, not the primary command.

### Smooth Motor Gestures Can Produce Complex Song

Laje, Gardner, and Mindlin model birdsong syllables as paths through a low-dimensional parameter space. The strongest practical lesson is not the exact coefficient set; it is that smooth pressure/tension/gating gestures can produce rich syllables without a massive symbolic score. Later prosthetic and neural-synthesis work reinforces this: real-time synthesis can be driven by low-dimensional physiological trajectories.

Aqua implication: fit time-varying curves before expanding the static parameter grid. The next bird-golf pass should infer/control pressure envelopes, labial tension curves, active opening/gating, and left/right phase or alternation. Static candidates repeating across species are expected failure, not a mystery.

### Bilateral Source Coupling Matters

Oscine birds have two sound sources at the bronchial/tracheal junction. The sources can interact with each other and with the upper tract; source-filter separation is often an approximation, not law. Public models explicitly study source-source and source-filter/source-tract acoustic interaction, and recent reviews treat nonlinear interactions as first-class explanations for jumps, subharmonics, biphonation, and register transitions.

Aqua implication: the graph architecture is the right lane. A syrinx patch should keep left and right labial oscillators as graph-coupled sources feeding left/right bronchi into a merge junction, then through trachea, glottis/OEC-like cavities when modeled, and beak radiation. Do not lower this to two independent oscillators mixed after the fact.

### Pressure And Tension Are Physically Valid Control Axes

Elemans' mechanical syrinx model separately controls bronchial pressure and membrane tension and produces high-frequency self-sustained oscillations and syllable-like sweeps. It also shows the distal tube resonance strongly affects membrane vibration, which supports graph-native source loading instead of a one-way post-filter.

Aqua implication: pressure and tension need real semantics in the DSL and lowering. Source impedance/load should affect oscillator behavior, not merely output color. Tract resonances should be allowed to pull or destabilize the source.

### Myoelastic-Aerodynamic Mechanism Is Shared With Mammals

Universal-mechanism work argues birds and mammals use the same broad myoelastic-aerodynamic principle despite anatomical differences. Bird voice is not a weird exception; it is another morphology using airflow, tissue elasticity, and tract coupling.

Aqua implication: the same reusable source family can serve larynx-like, syrinx-like, and alien morphologies if the primitive is expressed as tissue-valve dynamics plus graph coupling rather than as named human/bird organs.

### Nonlinear Phenomena Are Features, Not Bugs

Fee et al. and later reviews show period doubling, mode locking, chaos, frequency jumps, biphonation, and other nonlinear transitions in bird vocal production. These are part of the target space, especially for expressive or alien voices.

Aqua implication: the oscillator should expose controllable nonlinear regimes. A too-polished harmonic oscillator will fail exactly where birds sound bird-like. Metrics should include onset timing, modulation, subharmonics, envelope, and sideband structure, not only log-mel similarity.

## Architecture Intention

Owner: graph-native acoustic lowering owns how pressure waves, source loading, junctions, tract delays, and radiation interact. `AcousticSourcePort` owns local source dynamics at a terminal; `kind=syrinx` and `kind=labial` are now aliases onto `model=tissue_valve`, not independent renderers.

Inputs: morphology graph, tissue-valve source parameters, pressure/tension/gating control curves, tract/radiation controls, and optional fitting targets. The implemented v1 valve surface exposes mass, damping, stiffness, saturation, drive, load coupling, and rest opening in addition to pressure, tension, opening, noise, balance, and impedance.

Outputs: emitted Faust DSP in which source dynamics and acoustic network are mutually coupled enough to express source-source and source-tract interaction. The current Faust lowering emits named pressure-drive, force, velocity, displacement, aperture, load-pressure, and flow locals so the source law remains inspectable.

Derived state: direct frequency controls are authoring hints or fit initializers for default stiffness, not the physical owner of pitch.

Forbidden writers: no bespoke bird renderer, no two-oscillator mixdown path, no species-specific hardcoded syrinx module, no hidden phasor-owned labial excitation, and no static spectral-grid compensator pretending to be a model.

Shared paths: human larynx, bird syrinx, and alien valve morphologies should use the same tissue-valve/source-port semantics where possible, differing through morphology, coupling, and control curves.

Deletion line: any future `kind=syrinx` lowering that cannot be explained as pressure/tension/aperture tissue dynamics coupled to graph pressure should be demoted to a smoke-test source and replaced.

## Recommended Next Build Slice

1. Extend control curves from single-parameter authored gestures into reusable gesture sets that can be fitted, stored, and driven by learned controllers.
2. Move glottal graph lowering onto `model=tissue_valve` defaults once the syrinx valve has enough listening evidence.
3. Fit clean bird references by segmented time-varying controls, keeping morphology parameters slower and gesture parameters faster.
4. Score onset timing, amplitude envelope, pitch contour, sidebands/subharmonics, and log-mel distance separately.
5. Add a larynx-style graph patch using the same tissue-valve primitive.

## Source Notes

- Mindlin/Laje are the best implementation guide for low-dimensional physiological control.
- Fee et al. are the warning that nonlinear regimes are not optional decoration.
- Elemans is the best mechanical validation that pressure/tension/tube resonance are the right control axes.
- Arneodo/Perl/Mindlin source-tract papers justify Aqua's graph-native coupling direction.
- Prosthetic and neural-synthesis papers show these models can run in real time and can be connected to learned control signals.
