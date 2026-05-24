namespace AquaSynth.Dsl.Tests;

public sealed class MelCepstralWarpEffectTests
{
    [Fact]
    public void MelCepstralWarpEffectPreservesLengthAndFiniteSamples()
    {
        var input = TestTone(44_100, 0.35f);
        var effect = new MelCepstralWarpEffect(new MelCepstralWarpEffectOptions(
            SampleRate: 44_100,
            FftSize: 1024,
            HopSize: 256,
            MelBandCount: 32,
            CepstralCoefficientCount: 14,
            WarpFrames: 1.25f,
            WarpCoefficients: 1.75f,
            BlurPasses: 1));

        var output = effect.Process(input);

        Assert.Equal(input.Length, output.Length);
        Assert.All(output, sample => Assert.True(float.IsFinite(sample)));
        Assert.True(Rms(output) > 0.01f);
    }

    [Fact]
    public void MelCepstralWarpEffectIsAudiblyParameterized()
    {
        var input = TestTone(44_100, 0.35f);
        var dryish = new MelCepstralWarpEffect(new MelCepstralWarpEffectOptions(
            SampleRate: 44_100,
            FftSize: 1024,
            HopSize: 256,
            MelBandCount: 32,
            CepstralCoefficientCount: 14,
            Wet: 0.15f)).Process(input);
        var wet = new MelCepstralWarpEffect(new MelCepstralWarpEffectOptions(
            SampleRate: 44_100,
            FftSize: 1024,
            HopSize: 256,
            MelBandCount: 32,
            CepstralCoefficientCount: 14,
            WarpFrames: 2.0f,
            WarpCoefficients: 2.5f,
            BlurPasses: 2,
            Wet: 1.0f)).Process(input);

        Assert.True(MeanAbsoluteDifference(dryish, wet) > 0.005f);
    }

    private static float[] TestTone(int sampleRate, float seconds)
    {
        var samples = new float[(int)(sampleRate * seconds)];
        for (var index = 0; index < samples.Length; index++)
        {
            var t = index / (double)sampleRate;
            samples[index] = (float)(
                0.35 * Math.Sin(2.0 * Math.PI * 330.0 * t) +
                0.20 * Math.Sin(2.0 * Math.PI * 660.0 * t + 0.3) +
                0.12 * Math.Sin(2.0 * Math.PI * 1410.0 * t + 0.7));
        }

        return samples;
    }

    private static float Rms(IReadOnlyList<float> samples) =>
        MathF.Sqrt(samples.Sum(sample => sample * sample) / Math.Max(1, samples.Count));

    private static float MeanAbsoluteDifference(IReadOnlyList<float> first, IReadOnlyList<float> second)
    {
        var count = Math.Min(first.Count, second.Count);
        var sum = 0.0f;
        for (var index = 0; index < count; index++)
        {
            sum += Math.Abs(first[index] - second[index]);
        }

        return sum / Math.Max(1, count);
    }
}
