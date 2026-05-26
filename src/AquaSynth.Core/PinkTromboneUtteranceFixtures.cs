namespace AquaSynth.Dsl;

public sealed record PinkTromboneUtteranceFixture(
    string Id,
    string Text,
    float DurationSeconds,
    IReadOnlyList<PinkTromboneControlPoint> ControlPoints,
    IReadOnlyList<string> IntendedPhones);

public static class PinkTromboneUtteranceFixtures
{
    private static readonly PinkTromboneFixtureControls A = new(
        Frequency: 138,
        Intensity: 0.72f,
        Tenseness: 0.56f,
        TongueIndex: 13,
        TongueDiameter: 2.7f,
        ConstrictionIndex: 32,
        ConstrictionDiameter: 1.45f,
        Turbulence: 0.03f,
        Velum: 0.01f,
        LipOpening: 1.7f,
        Gain: 0.72f);

    private static readonly PinkTromboneFixtureControls U = A with
    {
        Frequency = 124,
        Tenseness = 0.5f,
        TongueIndex = 11.5f,
        TongueDiameter = 3.1f,
        ConstrictionIndex = 38,
        ConstrictionDiameter = 1.65f,
        LipOpening = 0.32f,
        LipReflection = -0.9f
    };

    private static readonly PinkTromboneFixtureControls E = A with
    {
        Frequency = 150,
        Tenseness = 0.64f,
        TongueIndex = 27,
        TongueDiameter = 1.05f,
        ConstrictionIndex = 34,
        ConstrictionDiameter = 1.18f,
        LipOpening = 1.08f
    };

    private static readonly PinkTromboneFixtureControls I = E with
    {
        Frequency = 156,
        TongueIndex = 29,
        TongueDiameter = 0.88f,
        LipOpening = 0.95f
    };

    private static readonly PinkTromboneFixtureControls O = A with
    {
        Frequency = 128,
        Tenseness = 0.5f,
        TongueIndex = 11.8f,
        TongueDiameter = 2.85f,
        ConstrictionIndex = 38,
        ConstrictionDiameter = 1.55f,
        LipOpening = 0.46f,
        LipReflection = -0.9f
    };

    private static readonly PinkTromboneFixtureControls M = A with
    {
        Frequency = 132,
        Intensity = 0.66f,
        Tenseness = 0.52f,
        TongueIndex = 14,
        TongueDiameter = 2.2f,
        ConstrictionIndex = 41,
        ConstrictionDiameter = 0.04f,
        Turbulence = 0.03f,
        Velum = 0.34f,
        LipOpening = 0.35f,
        GlottalReflection = 0.78f,
        LipReflection = -0.84f
    };

    private static readonly PinkTromboneFixtureControls PClosure = A with
    {
        Frequency = 126,
        Intensity = 0.01f,
        Tenseness = 0.22f,
        TongueIndex = 13,
        TongueDiameter = 2.7f,
        ConstrictionIndex = 43.2f,
        ConstrictionDiameter = 0.02f,
        Turbulence = 0.02f,
        Velum = 0.01f,
        LipOpening = 0.02f,
        Burst = 1.35f,
        Gain = 0.7f
    };

    private static readonly PinkTromboneFixtureControls PRelease = PClosure with
    {
        Intensity = 0.34f,
        Tenseness = 0.3f,
        ConstrictionDiameter = 1.9f,
        Turbulence = 0.9f,
        LipOpening = 1.65f,
        Burst = 1.55f
    };

    private static readonly PinkTromboneFixtureControls L = E with
    {
        Frequency = 134,
        Tenseness = 0.48f,
        TongueIndex = 24,
        TongueDiameter = 1.75f,
        ConstrictionIndex = 30,
        ConstrictionDiameter = 1.18f,
        Turbulence = 0.01f,
        LipOpening = 1.25f
    };

    private static readonly PinkTromboneFixtureControls KClosure = A with
    {
        Frequency = 130,
        Intensity = 0.04f,
        Tenseness = 0.26f,
        TongueIndex = 19,
        TongueDiameter = 1.35f,
        ConstrictionIndex = 18,
        ConstrictionDiameter = 0.02f,
        Turbulence = 0.04f,
        Burst = 1.1f
    };

    private static readonly PinkTromboneFixtureControls KRelease = KClosure with
    {
        Intensity = 0.18f,
        ConstrictionDiameter = 1.35f,
        Turbulence = 0.45f,
        Burst = 1.2f
    };

    private static readonly PinkTromboneFixtureControls Th = A with
    {
        Frequency = 118,
        Intensity = 0.03f,
        Tenseness = 0.14f,
        TongueIndex = 30,
        TongueDiameter = 0.95f,
        ConstrictionIndex = 31.5f,
        ConstrictionDiameter = 0.48f,
        Turbulence = 0.62f,
        LipOpening = 1.0f,
        Gain = 0.58f
    };

    private static readonly PinkTromboneFixtureControls R = O with
    {
        Frequency = 124,
        TongueIndex = 18,
        TongueDiameter = 1.35f,
        ConstrictionIndex = 24,
        ConstrictionDiameter = 1.15f,
        Turbulence = 0.02f,
        LipOpening = 0.62f
    };

    private static readonly PinkTromboneFixtureControls BClosure = M with
    {
        Velum = 0.01f,
        TongueIndex = 13,
        TongueDiameter = 2.7f,
        ConstrictionIndex = 43.2f,
        ConstrictionDiameter = 0.02f,
        Intensity = 0.04f,
        LipOpening = 0.02f,
        Burst = 1.25f
    };

    private static readonly PinkTromboneFixtureControls BRelease = BClosure with
    {
        Intensity = 0.5f,
        ConstrictionDiameter = 1.85f,
        LipOpening = 1.5f,
        Turbulence = 0.45f,
        Burst = 1.35f
    };

    private static readonly PinkTromboneFixtureControls S = I with
    {
        Frequency = 130,
        Intensity = 0.42f,
        Tenseness = 0.22f,
        ConstrictionIndex = 34,
        ConstrictionDiameter = 0.32f,
        Turbulence = 0.98f,
        LipOpening = 1.15f,
        Gain = 0.76f
    };

    public static IReadOnlyList<PinkTromboneUtteranceFixture> All { get; } =
    [
        new(
            "mama",
            "mama",
            1.05f,
            Points(
                (0.00f, M, "m"),
                (0.15f, M, "m hold"),
                (0.24f, A, "a"),
                (0.46f, A, "a hold"),
                (0.54f, M, "m"),
                (0.68f, M, "m hold"),
                (0.77f, A with { Frequency = 142 }, "a"),
                (1.05f, A with { Intensity = 0.48f }, "fade")),
            ["m", "a", "m", "a"]),
        new(
            "papa",
            "papa",
            1.02f,
            Points(
                (0.00f, PClosure, "p closure"),
                (0.145f, PClosure, "p pressure"),
                (0.157f, PRelease, "p release"),
                (0.185f, PRelease, "p burst tail"),
                (0.25f, A, "a"),
                (0.45f, A, "a hold"),
                (0.52f, PClosure, "p closure"),
                (0.645f, PClosure, "p pressure"),
                (0.657f, PRelease, "p release"),
                (0.685f, PRelease, "p burst tail"),
                (0.74f, A with { Frequency = 142 }, "a"),
                (1.02f, A with { Intensity = 0.46f }, "fade")),
            ["p", "a", "p", "a"]),
        new(
            "lulek",
            "lulek",
            1.22f,
            Points(
                (0.00f, U with { Intensity = 0.58f }, "u lead-in"),
                (0.08f, L, "l onset"),
                (0.14f, U, "u"),
                (0.42f, U, "u hold"),
                (0.50f, L with { Frequency = 138 }, "l"),
                (0.58f, E with { Frequency = 142, TongueDiameter = 1.2f }, "e"),
                (0.88f, E with { Frequency = 142, TongueDiameter = 1.2f }, "e hold"),
                (0.99f, KClosure, "k closure"),
                (1.055f, KClosure, "k pressure"),
                (1.067f, KRelease, "k release"),
                (1.22f, KRelease with { Intensity = 0.02f, Turbulence = 0.08f }, "stop")),
            ["l", "u", "l", "e", "k"]),
        new(
            "thrombosis",
            "thrombosis",
            1.72f,
            Points(
                (0.00f, Th, "th"),
                (0.09f, Th, "th hold"),
                (0.15f, R, "r"),
                (0.28f, O, "o"),
                (0.56f, O, "o hold"),
                (0.66f, M with { Frequency = 126 }, "m"),
                (0.82f, M, "m hold"),
                (0.90f, BClosure, "b closure"),
                (0.975f, BClosure, "b pressure"),
                (0.987f, BRelease, "b release"),
                (1.07f, O with { Frequency = 136 }, "o"),
                (1.25f, S, "s"),
                (1.42f, I, "i"),
                (1.55f, S with { Intensity = 0.32f }, "s"),
                (1.72f, S with { Intensity = 0.04f, Turbulence = 0.2f }, "fade")),
            ["th", "r", "o", "m", "b", "o", "s", "i", "s"])
    ];

    public static PinkTromboneUtteranceFixture ById(string id) =>
        All.First(fixture => fixture.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<PinkTromboneControlPoint> Points(
        params (float time, PinkTromboneFixtureControls controls, string label)[] points) =>
        points.Select(point => new PinkTromboneControlPoint(point.time, point.controls, point.label)).ToArray();
}
