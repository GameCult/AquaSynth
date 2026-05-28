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

    public static ReferenceRebuild CurrentAquaSynthGraph() =>
        new(
            "pink-trombone/current-aquasynth-graph",
            "pink-trombone",
            "Current AquaSynth typed acoustic graph",
            ReferenceId,
            BuiltInScripts.PinkTromboneProxy,
            [
                new("acoustic_graph_authority", "AcousticPortNetwork", "Pink Trombone pressure now lowers through the typed acoustic graph on an ordinary AquaSynth voice."),
                new("tract_area_function", "tract_shape diameters", "AquaSynth can now author reusable section diameter/area functions for tract voices."),
                new("diameter_to_reflection_coefficients", "TractAreaFunction.ReflectionCoefficients", "AquaSynth derives adjacent-section reflection coefficients from squared diameters as a reusable primitive."),
                new("glottal_source_primitive", "glottis", "AquaSynth can author reusable glottal excitation controls for tract voices."),
                new("positioned_injection_primitive", "tract_injection", "AquaSynth can author reusable constriction-position noise/burst injection controls."),
                new("main_tract_waveguide_cells", "graph path sections", "AquaSynth can emit an oral bidirectional graph path from derived section geometry."),
                new("reflection_coefficients_applied_to_waveguide", "graph scattering junctions", "Derived areas feed generated right/left wave scattering equations."),
                new("nose_waveguide_cells", "nasal_branch", "AquaSynth can author a nasal branch as its own diameter/area tube."),
                new("nose_junction", "generated graph connection", "Graph lowering can emit a velum-controlled oral/nasal junction from local areas."),
                new("positioned_turbulence_applied_to_waveguide_cells", "position-weighted graph source bank", "Frication pressure can be distributed into oral graph source ports by injection position and width."),
                new("tract_shape_motion", "tract_motion slew semantics", "AquaSynth can author tract control slew and obstruction thresholds for moving diameter targets."),
                new("diameter_authority", "dynamic graph section areas", "Graph lowering derives live per-terminal area targets before scattering."),
                new("closure_transients", "graph source release pulse", "Graph source lowering derives burst pressure from prior obstruction state and current opening."),
                new("tract_sample_rate", "physical-length acoustic grid", "Graph lowering derives segment timing from tract length and maps runtime index controls onto the emitted graph; `substeps` is no longer a DSL owner."),
                new("runtime_controls", "/pink/frequency,/pink/intensity,/pink/tenseness,/pink/tongue/index,/pink/tongue/diameter,/pink/velum,/pink/turbulence", "AquaSynth can expose stable runtime parameter paths."),
                new("glottal_pitch_pressure", "bounded graph glottal source", "AquaSynth can drive pitch/loudness-like controls without recompiling."),
                new("turbulence_source", "position-aware frication source ports", "The tract voice has constriction index/diameter and turbulence controls."),
                new("nasal_branch", "velum-controlled nasal graph branch", "The tract voice has a velum-controlled nasal branch.")
            ],
            [
                new("contact_aware_closure_state", "graph-owned upstream/downstream pressure storage", "The graph still needs a contact primitive that participates in scattering and flow."),
                new("lf_glottal_exactness", "Pink Trombone LF-style coefficients", "The bounded source does not yet match Pink Trombone coefficient updates exactly.")
            ],
            "This is a deliberately non-passing rebuild: it proves Pink Trombone pressure lowers through the typed acoustic graph while naming the remaining closure/source exactness debt.");
}
