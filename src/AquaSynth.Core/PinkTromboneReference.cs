namespace AquaSynth.Dsl;

public static class PinkTromboneReference
{
    public const string ReferenceId = "pink-trombone/modular-waveguide";

    public static ReferencePatch ToReferencePatch() =>
        new(
            ReferenceId,
            "pink-trombone",
            "Pink Trombone modular waveguide",
            Source,
            Features,
            Parameters);

    public static ReferenceSource Source { get; } = new(
        "source",
        "https://github.com/chdh/pink-trombone-mod",
        "MIT",
        "",
        "MIT TypeScript modularization of Neil Thapen's Pink Trombone, used as source-readable parity pressure.");

    public static IReadOnlyList<ReferenceFeature> Features { get; } =
    [
        new("main_tract_waveguide_cells", "44", "Tract.n owns the main vocal tract section count."),
        new("nose_waveguide_cells", "28", "Tract.noseLength owns the nasal branch section count."),
        new("tract_sample_rate", "2x-audio-sample-rate", "Synthesizer steps the tract twice for each output sample."),
        new("glottal_source", "LF-style-normalized-waveform", "Glottis derives an LF-style waveform from frequency and tenseness."),
        new("glottal_reflection", "0.75", "The source end reflects the left-going tract wave while adding glottal output."),
        new("lip_reflection", "-0.85", "The lip end reflects the right-going tract wave and radiates output."),
        new("diameter_authority", "section-diameter-array", "A 44-element diameter array owns tract shape before reflection calculation."),
        new("reflection_formula", "(area[i-1]-area[i])/(area[i-1]+area[i])", "Reflection coefficients derive from adjacent squared diameters."),
        new("nose_junction", "three-way-velum-reflection", "The tract/nose junction uses left, right, and nose reflection coefficients from local areas."),
        new("turbulence_injection", "positioned-constriction-noise", "Frication noise is injected into neighboring tract cells based on constriction position and diameter."),
        new("closure_transients", "obstruction-release-impulse", "Opening a closure can add a decaying transient at the obstruction position."),
        new("tract_shape_motion", "diameter-target-slew", "TractShaper moves diameters toward target shape with position-dependent return speeds.")
    ];

    public static IReadOnlyList<PatchParameter> Parameters { get; } =
    [
        new("/pink/frequency", "Frequency", 140, 10, 600, 0.01f, "Hz", "audio", "Glottal target frequency."),
        new("/pink/intensity", "Intensity", 0.7f, 0, 1, 0.001f, "normalized", "control", "Glottal excitation intensity."),
        new("/pink/tenseness", "Tenseness", 0.6f, 0, 1, 0.001f, "normalized", "control", "LF waveform/turbulence voice-quality control."),
        new("/pink/tongue/index", "Tongue index", 12.9f, 0, 44, 0.001f, "cell", "control", "Tongue-body constriction center in tract-cell coordinates."),
        new("/pink/tongue/diameter", "Tongue diameter", 2.43f, 0, 4, 0.001f, "diameter", "control", "Tongue-body diameter target."),
        new("/pink/velum", "Velum", 0.01f, 0.01f, 0.4f, 0.001f, "diameter", "control", "Nasal branch opening diameter."),
        new("/pink/constriction/index", "Constriction index", 32, 0, 44, 0.001f, "cell", "control", "Fricative or closure constriction position."),
        new("/pink/constriction/diameter", "Constriction diameter", 1, -1, 4, 0.001f, "diameter", "control", "Fricative or closure constriction diameter."),
        new("/pink/turbulence", "Turbulence", 0.18f, 0, 1, 0.001f, "normalized", "control", "Breath and frication noise pressure."),
        new("/pink/lip/opening", "Lip opening", 1.5f, 0.35f, 2.5f, 0.001f, "diameter", "control", "Mouth radiation opening proxy."),
        new("/pink/glottal/reflection", "Glottal reflection", 0.75f, -0.95f, 0.95f, 0.001f, "coefficient", "control", "Source-end reflection proxy."),
        new("/pink/lip/reflection", "Lip reflection", -0.85f, -0.98f, 0.1f, 0.001f, "coefficient", "control", "Lip-end reflection proxy.")
    ];

    public static ReferenceRebuild CurrentAquaSynthProxy() =>
        new(
            "pink-trombone/current-aquasynth-proxy",
            "pink-trombone",
            "Current AquaSynth source-filter proxy",
            ReferenceId,
            BuiltInScripts.PinkTromboneProxy,
            [
                new("tract_voice_authority", "voice.Tract", "Pink Trombone pressure now lowers through a tract treatment on an ordinary AquaSynth voice."),
                new("runtime_controls", "/pink/frequency,/pink/intensity,/pink/tenseness,/pink/tongue/index,/pink/tongue/diameter,/pink/velum,/pink/turbulence", "AquaSynth can expose stable runtime parameter paths."),
                new("glottal_pitch_pressure_proxy", "tract LF-style source", "AquaSynth can drive pitch/loudness-like controls without recompiling."),
                new("turbulence_proxy", "position-aware frication controls", "The tract voice has constriction index/diameter and turbulence controls."),
                new("nasal_proxy", "velum-controlled nasal resonator", "The tract voice has a velum/nasal output lane.")
            ],
            [
                new("main_tract_waveguide_cells", "44 bidirectional sections", "No AquaSynth DSL authority owns arrays of right/left traveling wave state per tract section."),
                new("nose_waveguide_cells", "28 bidirectional sections", "The current DSL has no nasal branch delay-line graph or velum-controlled three-way junction."),
                new("diameter_to_reflection_coefficients", "per-block area reflection update", "Formant filters do not preserve the section-area ownership or reflection math."),
                new("twice_rate_tract_stepping", "two tract steps per audio sample", "AquaSynth voice graphs do not express a subgraph with its own stepping rate."),
                new("positioned_turbulence_injection", "noise injected into neighboring cells", "The current noise field is source/color pressure, not a positioned tract injection event."),
                new("closure_release_transients", "obstruction release impulse", "AquaSynth envelopes can shape amplitude, but they do not observe obstruction state and emit release transients."),
                new("lf_glottal_exactness", "Pink Trombone LF-style coefficients", "The proxy uses ordinary oscillators/noise, not the LF coefficient path from tenseness.")
            ],
            "This is a deliberately non-passing rebuild: it keeps the current source-filter proxy parseable while naming the tract-DSP authority AquaSynth lacks.");
}
