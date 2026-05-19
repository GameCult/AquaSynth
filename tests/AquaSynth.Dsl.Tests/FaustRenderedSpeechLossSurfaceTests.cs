using AquaSynth.Dsl;
using AquaSynth.Faust;

namespace AquaSynth.Dsl.Tests;

public sealed class FaustRenderedSpeechLossSurfaceTests
{
    [Fact]
    public async Task FaustRenderedSpeechLossSurfaceReturnsFiniteDifferenceGradientWhenToolchainIsAvailable()
    {
        if (FaustCompiler.FindFaust() is null ||
            Environment.GetEnvironmentVariable("AQUASYNTH_RUN_FAUST_SPEECH_LOSS") != "1")
        {
            return;
        }

        var surface = new FaustRenderedSpeechLossSurface(new FaustRenderedSpeechLossOptions(
            SampleRate: 16000,
            DurationSeconds: 0.12f,
            MelBandCount: 4,
            GradientOutputIndices: [14]));
        var reference = Target([0.90f, 0.78f, 0.28f, 0.12f], turbulence: 0.08f, cutoff: 0.46f);
        var candidate = Target([0.18f, 0.25f, 0.76f, 0.88f], turbulence: 0.40f, cutoff: 0.80f);

        var loss = await surface.EvaluateAsync(reference, candidate);

        Assert.NotNull(loss);
        Assert.True(loss.Loss > 0.01f, $"rendered log-mel loss should see the mismatch; loss={loss.Loss}");
        Assert.Contains(loss.OutputGradient, value => Math.Abs(value) > 0.0001f);
        Assert.Contains("process =", surface.SourceFor(candidate));
    }

    private static VocalTractControlTarget Target(IReadOnlyList<float> mel, float turbulence, float cutoff) =>
        new(
            TongueBody: 0.55f,
            TongueTip: 0.45f,
            LipAperture: 0.70f,
            LipRounding: 0.08f,
            Velum: 0.05f,
            GlottalTenseness: 0.48f,
            Turbulence: turbulence,
            Pressure: 0.62f,
            AmDepth: 0.10f,
            FmDepth: 0.12f,
            LfoRate: 0.25f,
            LfoDepth: 0.08f,
            FilterCutoff: cutoff,
            FilterResonance: 0.20f,
            MelSpectralEnvelope: mel);
}
