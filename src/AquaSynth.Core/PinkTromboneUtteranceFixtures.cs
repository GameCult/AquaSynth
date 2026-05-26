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
        Frequency = 128,
        TongueIndex = 12,
        TongueDiameter = 2.9f,
        ConstrictionIndex = 38,
        ConstrictionDiameter = 1.45f,
        LipOpening = 0.55f,
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
        Frequency = 132,
        TongueIndex = 12.5f,
        TongueDiameter = 2.65f,
        ConstrictionIndex = 37,
        ConstrictionDiameter = 1.35f,
        LipOpening = 0.72f,
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
        Intensity = 0.04f,
        Tenseness = 0.22f,
        ConstrictionIndex = 41,
        ConstrictionDiameter = 0.02f,
        Turbulence = 0.08f,
        Velum = 0.01f,
        LipOpening = 0.35f,
        Burst = 0.9f,
        Gain = 0.7f
    };

    private static readonly PinkTromboneFixtureControls PRelease = PClosure with
    {
        Intensity = 0.28f,
        Tenseness = 0.36f,
        ConstrictionDiameter = 0.52f,
        Turbulence = 0.75f,
        Burst = 1.0f
    };

    private static readonly PinkTromboneFixtureControls L = E with
    {
        Frequency = 144,
        TongueIndex = 28,
        TongueDiameter = 1.2f,
        ConstrictionIndex = 30,
        ConstrictionDiameter = 0.85f,
        Turbulence = 0.02f,
        LipOpening = 1.25f
    };

    private static readonly PinkTromboneFixtureControls KClosure = A with
    {
        Frequency = 130,
        Intensity = 0.08f,
        Tenseness = 0.26f,
        TongueIndex = 19,
        TongueDiameter = 1.1f,
        ConstrictionIndex = 18,
        ConstrictionDiameter = 0.02f,
        Turbulence = 0.1f,
        Burst = 0.9f
    };

    private static readonly PinkTromboneFixtureControls KRelease = KClosure with
    {
        Intensity = 0.26f,
        ConstrictionDiameter = 0.62f,
        Turbulence = 0.58f,
        Burst = 1.0f
    };

    private static readonly PinkTromboneFixtureControls Th = A with
    {
        Frequency = 118,
        Intensity = 0.18f,
        Tenseness = 0.2f,
        TongueIndex = 31,
        TongueDiameter = 0.72f,
        ConstrictionIndex = 32,
        ConstrictionDiameter = 0.32f,
        Turbulence = 0.86f,
        LipOpening = 1.05f
    };

    private static readonly PinkTromboneFixtureControls R = O with
    {
        Frequency = 136,
        TongueIndex = 22,
        TongueDiameter = 1.0f,
        ConstrictionIndex = 24,
        ConstrictionDiameter = 0.95f,
        Turbulence = 0.02f,
        LipOpening = 0.9f
    };

    private static readonly PinkTromboneFixtureControls BClosure = M with
    {
        Velum = 0.01f,
        Intensity = 0.2f,
        Burst = 0.85f
    };

    private static readonly PinkTromboneFixtureControls BRelease = BClosure with
    {
        Intensity = 0.58f,
        ConstrictionDiameter = 0.62f,
        Turbulence = 0.35f,
        Burst = 0.9f
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
                (0.12f, PClosure, "p pressure"),
                (0.16f, PRelease, "p release"),
                (0.25f, A, "a"),
                (0.45f, A, "a hold"),
                (0.52f, PClosure, "p closure"),
                (0.64f, PRelease, "p release"),
                (0.73f, A with { Frequency = 142 }, "a"),
                (1.02f, A with { Intensity = 0.46f }, "fade")),
            ["p", "a", "p", "a"]),
        new(
            "lulek",
            "lulek",
            1.22f,
            Points(
                (0.00f, L, "l"),
                (0.13f, U, "u"),
                (0.39f, U, "u hold"),
                (0.48f, L with { Frequency = 148 }, "l"),
                (0.61f, E, "e"),
                (0.86f, E, "e hold"),
                (0.95f, KClosure, "k closure"),
                (1.07f, KRelease, "k release"),
                (1.22f, KRelease with { Intensity = 0.02f, Turbulence = 0.12f }, "stop")),
            ["l", "u", "l", "e", "k"]),
        new(
            "thrombosis",
            "thrombosis",
            1.72f,
            Points(
                (0.00f, Th, "th"),
                (0.17f, Th, "th hold"),
                (0.25f, R, "r"),
                (0.39f, O, "o"),
                (0.59f, O, "o hold"),
                (0.68f, M, "m"),
                (0.82f, M, "m hold"),
                (0.90f, BClosure, "b closure"),
                (0.98f, BRelease, "b release"),
                (1.07f, O with { Frequency = 140 }, "o"),
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
