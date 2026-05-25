namespace AquaSynth.Dsl;

public sealed record PinkTromboneParityFixture(
    string Id,
    string Description,
    string AquaScript,
    IReadOnlyList<string> ReferenceFeatures);

public static class PinkTromboneParityFixtures
{
    public static IReadOnlyList<PinkTromboneParityFixture> All { get; } =
    [
        Fixture(
            "open-vowel",
            "Sustained open oral vowel with modal glottis and quiet constriction.",
            "tongue_index=13 tongue_diameter=2.7 constriction_index=32 constriction_diameter=1.4 turbulence=0.02 velum=0.01 lip=1.7",
            ["glottal_source", "main_tract_waveguide_cells", "diameter_to_reflection_coefficients"]),
        Fixture(
            "front-vowel",
            "Front constricted vowel pressure for tongue-body movement.",
            "tongue_index=27 tongue_diameter=1.05 constriction_index=34 constriction_diameter=1.2 turbulence=0.03 velum=0.01 lip=1.1",
            ["tract_shape_motion", "diameter_authority"]),
        Fixture(
            "nasal-vowel",
            "Velum-open nasalized vowel through oral and nasal waveguides.",
            "tongue_index=14 tongue_diameter=2.2 constriction_index=18 constriction_diameter=0.8 turbulence=0.08 velum=0.33 lip=1.35",
            ["nose_waveguide_cells", "nose_junction"]),
        Fixture(
            "sibilant",
            "High-turbulence front constriction with positioned waveguide injection.",
            "tongue_index=28 tongue_diameter=0.75 constriction_index=34 constriction_diameter=0.35 turbulence=0.95 velum=0.01 lip=1.2",
            ["positioned_turbulence_applied_to_waveguide_cells"]),
        Fixture(
            "closure-release",
            "Closed constriction opening pressure for obstruction-history transient tests.",
            "tongue_index=18 tongue_diameter=1.2 constriction_index=24 constriction_diameter=0.02 turbulence=0.25 velum=0.01 lip=1.45 burst=0.8",
            ["closure_transients"])
    ];

    public static PinkTromboneParityFixture ById(string id) =>
        All.First(fixture => fixture.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static PinkTromboneParityFixture Fixture(
        string id,
        string description,
        string tractFields,
        IReadOnlyList<string> referenceFeatures) =>
        new(id, description, Script(tractFields), referenceFeatures);

    private static string Script(string tractFields) =>
        $$"""
        patch gain=0.82 soft_clip=true

        tract_shape
            name=human
            diameters=0.6,0.6,0.6,0.6,0.6,0.7,0.8,1.0,1.1,1.1,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.4,1.3,1.2,1.15,1.5

        glottis name=modal intensity=.72 tenseness=.6 aspiration=.12 reflection=.75 skew=.42
        tract_injection name=inj position=32 diameter=1 turbulence=.1 burst=.25 width=1
        nasal_branch name=nose junction=17 velum=.01 reflection=-.85 loss=.999 diameters=0.01,0.35,0.5,0.65,0.8,0.95,1.1,1.25,1.4,1.55,1.7,1.8,1.9,1.9,1.85,1.75,1.65,1.55,1.45,1.35,1.25,1.15,1.05,0.95,0.85,0.75,0.65,0.55
        tract_motion name=motion diameter_slew=18 shape_return=8 constriction_slew=24 velum_slew=16 obstruction_threshold=.05

        tract shape=human glottis=modal injection=inj nasal_branch=nose motion=motion propagation=waveguide substeps=2 waveguide_loss=.999 freq=140 gain=.7 sustain=.45 decay=.12 {{tractFields}}
        """;
}
