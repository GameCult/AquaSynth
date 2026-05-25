namespace AquaSynth.Dsl;

public sealed record PinkTromboneParityFixture(
    string Id,
    string Description,
    PinkTromboneFixtureControls Controls,
    string AquaScript,
    IReadOnlyList<string> ReferenceFeatures);

public sealed record PinkTromboneFixtureControls(
    float Frequency = 140,
    float Intensity = 0.72f,
    float Tenseness = 0.6f,
    float TongueIndex = 13,
    float TongueDiameter = 2.7f,
    float ConstrictionIndex = 32,
    float ConstrictionDiameter = 1,
    float Turbulence = 0.1f,
    float Velum = 0.01f,
    float LipOpening = 1.5f,
    float GlottalReflection = 0.75f,
    float LipReflection = -0.85f,
    float Gain = 0.7f,
    float Burst = 0.25f);

public static class PinkTromboneParityFixtures
{
    public static IReadOnlyList<PinkTromboneParityFixture> All { get; } =
    [
        Fixture(
            "open-vowel",
            "Sustained open oral vowel with modal glottis and quiet constriction.",
            new PinkTromboneFixtureControls(
                TongueIndex: 13,
                TongueDiameter: 2.7f,
                ConstrictionIndex: 32,
                ConstrictionDiameter: 1.4f,
                Turbulence: 0.02f,
                Velum: 0.01f,
                LipOpening: 1.7f),
            ["glottal_source", "main_tract_waveguide_cells", "diameter_to_reflection_coefficients"]),
        Fixture(
            "front-vowel",
            "Front constricted vowel pressure for tongue-body movement.",
            new PinkTromboneFixtureControls(
                TongueIndex: 27,
                TongueDiameter: 1.05f,
                ConstrictionIndex: 34,
                ConstrictionDiameter: 1.2f,
                Turbulence: 0.03f,
                Velum: 0.01f,
                LipOpening: 1.1f),
            ["tract_shape_motion", "diameter_authority"]),
        Fixture(
            "nasal-vowel",
            "Velum-open nasalized vowel through oral and nasal waveguides.",
            new PinkTromboneFixtureControls(
                TongueIndex: 14,
                TongueDiameter: 2.2f,
                ConstrictionIndex: 18,
                ConstrictionDiameter: 0.8f,
                Turbulence: 0.08f,
                Velum: 0.33f,
                LipOpening: 1.35f),
            ["nose_waveguide_cells", "nose_junction"]),
        Fixture(
            "sibilant",
            "High-turbulence front constriction with positioned waveguide injection.",
            new PinkTromboneFixtureControls(
                TongueIndex: 28,
                TongueDiameter: 0.75f,
                ConstrictionIndex: 34,
                ConstrictionDiameter: 0.35f,
                Turbulence: 0.95f,
                Velum: 0.01f,
                LipOpening: 1.2f),
            ["positioned_turbulence_applied_to_waveguide_cells"]),
        Fixture(
            "closure-release",
            "Closed constriction opening pressure for obstruction-history transient tests.",
            new PinkTromboneFixtureControls(
                TongueIndex: 18,
                TongueDiameter: 1.2f,
                ConstrictionIndex: 24,
                ConstrictionDiameter: 0.02f,
                Turbulence: 0.25f,
                Velum: 0.01f,
                LipOpening: 1.45f,
                Burst: 0.8f),
            ["closure_transients"])
    ];

    public static PinkTromboneParityFixture ById(string id) =>
        All.First(fixture => fixture.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static PinkTromboneParityFixture Fixture(
        string id,
        string description,
        PinkTromboneFixtureControls controls,
        IReadOnlyList<string> referenceFeatures) =>
        new(id, description, controls, Script(controls), referenceFeatures);

    private static string Script(PinkTromboneFixtureControls controls) =>
        $$"""
        patch gain=0.82 soft_clip=true

        tract_shape
            name=human
            diameters=0.6,0.6,0.6,0.6,0.6,0.7,0.8,1.0,1.1,1.1,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.4,1.3,1.2,1.15,1.5

        glottis name=modal intensity=.72 tenseness=.6 aspiration=.12 reflection=.75 skew=.42
        tract_injection name=inj position=32 diameter=1 turbulence=.1 burst=.25 width=1
        nasal_branch name=nose junction=17 velum=.01 reflection=-.85 loss=.999 diameters=0.01,0.35,0.5,0.65,0.8,0.95,1.1,1.25,1.4,1.55,1.7,1.8,1.9,1.9,1.85,1.75,1.65,1.55,1.45,1.35,1.25,1.15,1.05,0.95,0.85,0.75,0.65,0.55
        tract_motion name=motion diameter_slew=18 shape_return=8 constriction_slew=24 velum_slew=16 obstruction_threshold=.05

        tract shape=human glottis=modal injection=inj nasal_branch=nose motion=motion propagation=waveguide substeps=2 waveguide_loss=.999 freq={{F(controls.Frequency)}} gain={{F(controls.Gain)}} sustain=.45 decay=.12 tongue_index={{F(controls.TongueIndex)}} tongue_diameter={{F(controls.TongueDiameter)}} constriction_index={{F(controls.ConstrictionIndex)}} constriction_diameter={{F(controls.ConstrictionDiameter)}} turbulence={{F(controls.Turbulence)}} velum={{F(controls.Velum)}} lip={{F(controls.LipOpening)}} burst={{F(controls.Burst)}} glottal_reflection={{F(controls.GlottalReflection)}} lip_reflection={{F(controls.LipReflection)}}
        """;

    private static string F(float value) =>
        value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
}
