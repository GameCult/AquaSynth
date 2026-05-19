using System.Globalization;
using System.Text;

using AquaSynth.Dsl;

namespace AquaSynth.Faust;

public sealed record FaustRenderedSpeechLossOptions(
    int SampleRate = 22050,
    float DurationSeconds = 0.22f,
    int MelBandCount = 12,
    float MinFrequencyHz = 90,
    float MaxFrequencyHz = 7000,
    float FiniteDifferenceStep = 0.035f,
    IReadOnlyList<int>? GradientOutputIndices = null);

public sealed record FaustRenderedSpeechLoss(
    float Loss,
    float[] OutputGradient,
    FaustRender Reference,
    FaustRender Candidate,
    AudioComparison Comparison);

public sealed class FaustRenderedSpeechLossSurface(FaustRenderedSpeechLossOptions? options = null, string? faustPath = null)
{
    private readonly FaustRenderedSpeechLossOptions options = options ?? new FaustRenderedSpeechLossOptions();

    public async Task<FaustRender?> RenderAsync(
        VocalTractControlTarget target,
        CancellationToken cancellationToken = default) =>
        await FaustCompiler.RenderAsync(
            SourceFor(target),
            new FaustRenderOptions(options.SampleRate, options.DurationSeconds),
            faustPath,
            cancellationToken);

    public async Task<FaustRenderedSpeechLoss?> EvaluateAsync(
        VocalTractControlTarget reference,
        VocalTractControlTarget candidate,
        CancellationToken cancellationToken = default)
    {
        var referenceRender = await RenderAsync(reference, cancellationToken);
        if (referenceRender is null || referenceRender.Samples.Length == 0)
        {
            return null;
        }

        var candidateRender = await RenderAsync(candidate, cancellationToken);
        if (candidateRender is null || candidateRender.Samples.Length == 0)
        {
            return null;
        }

        var analyzer = Analyzer();
        var comparison = analyzer.Compare(referenceRender.Samples, candidateRender.Samples);
        var gradient = await EstimateGradientAsync(referenceRender.Samples, candidate, comparison.LogMelDistance, cancellationToken);
        return new FaustRenderedSpeechLoss(comparison.LogMelDistance, gradient, referenceRender, candidateRender, comparison);
    }

    public string SourceFor(VocalTractControlTarget target)
    {
        var mel = target.MelSpectralEnvelope ?? [];
        var source = new StringBuilder();
        source.AppendLine("patch gain=0.85");
        source.AppendLine(VoiceLine("saw", F(120 + target.GlottalTenseness * 110), F(0.08f + target.Pressure * 0.22f), target, target.Turbulence * 0.22f));
        if (target.Turbulence > 0.02f)
        {
            source.AppendLine(VoiceLine("noise", F(900 + target.Turbulence * 2600), F(target.Turbulence * 0.16f), target, 1));
        }

        for (var band = 0; band < options.MelBandCount; band++)
        {
            var value = band < mel.Count ? Math.Clamp(mel[band], 0, 1) : 0.5f;
            var hz = MelBandCenterHz(band);
            var gain = value * value * 0.08f;
            source.AppendLine(VoiceLine("sine", F(hz), F(gain), target, 0));
        }

        return FaustEmitter.EmitScript(source.ToString(), new FaustExportOptions("aquasynth_speech_loss")).Source;
    }

    private async Task<float[]> EstimateGradientAsync(
        IReadOnlyList<float> referenceSamples,
        VocalTractControlTarget candidate,
        float baselineLoss,
        CancellationToken cancellationToken)
    {
        var gradient = new float[14 + options.MelBandCount];
        foreach (var index in GradientIndices())
        {
            var perturbed = Perturb(candidate, index, options.FiniteDifferenceStep);
            var render = await RenderAsync(perturbed, cancellationToken);
            if (render is null || render.Samples.Length == 0)
            {
                continue;
            }

            var loss = Analyzer().Compare(referenceSamples.ToArray(), render.Samples).LogMelDistance;
            gradient[index] = (loss - baselineLoss) / options.FiniteDifferenceStep;
        }

        return gradient;
    }

    private IReadOnlyList<int> GradientIndices() =>
        options.GradientOutputIndices ?? Enumerable.Range(14, options.MelBandCount).ToArray();

    private AudioAnalyzer Analyzer() =>
        new(new AudioAnalysisConfig(
            SampleRate: options.SampleRate,
            FftSize: 256,
            HopSize: 96,
            MelBandCount: options.MelBandCount,
            MinFrequencyHz: options.MinFrequencyHz,
            MaxFrequencyHz: options.MaxFrequencyHz));

    private float MelBandCenterHz(int band)
    {
        var minMel = HzToMel(options.MinFrequencyHz);
        var maxMel = HzToMel(options.MaxFrequencyHz);
        var t = (band + 0.5f) / Math.Max(1, options.MelBandCount);
        return MelToHz(minMel + (maxMel - minMel) * t);
    }

    private static VocalTractControlTarget Perturb(VocalTractControlTarget target, int outputIndex, float delta)
    {
        var values = target.ToVector(Math.Max(0, outputIndex - 13));
        if (outputIndex >= values.Length)
        {
            Array.Resize(ref values, outputIndex + 1);
        }

        values[outputIndex] = Math.Clamp(values[outputIndex] + delta, 0, 1);
        return TargetFromVector(values);
    }

    private static VocalTractControlTarget TargetFromVector(IReadOnlyList<float> values)
    {
        var mel = new float[Math.Max(0, values.Count - 14)];
        for (var index = 0; index < mel.Length; index++)
        {
            mel[index] = values[14 + index];
        }

        return new VocalTractControlTarget(
            values.ElementAtOrDefault(0),
            values.ElementAtOrDefault(1),
            values.ElementAtOrDefault(2),
            values.ElementAtOrDefault(3),
            values.ElementAtOrDefault(4),
            values.ElementAtOrDefault(5),
            values.ElementAtOrDefault(6),
            values.ElementAtOrDefault(7),
            values.ElementAtOrDefault(8),
            values.ElementAtOrDefault(9),
            values.ElementAtOrDefault(10),
            values.ElementAtOrDefault(11),
            values.ElementAtOrDefault(12),
            values.ElementAtOrDefault(13),
            mel);
    }

    private static string VoiceLine(string wave, string freq, string gain, VocalTractControlTarget target, float noise) =>
        string.Create(CultureInfo.InvariantCulture, $"voice wave={wave} freq={freq} gain={gain} attack=0.004 sustain=0.16 decay=0.055 lpf={Math.Clamp(target.FilterCutoff, 0.04f, 1):0.######} lpf_q={Math.Clamp(0.2f + target.FilterResonance * 8, 0.2f, 12):0.######} noise={Math.Clamp(noise, 0, 1):0.######} fm=2.0 fm_index={Math.Clamp(target.FmDepth, 0, 1):0.######} tremolo={Math.Clamp(target.AmDepth, 0, 1):0.######} tremolo_hz={Math.Clamp(2 + target.LfoRate * 11, 0.1f, 20):0.######}");

    private static string F(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static float HzToMel(float hz) => 2595 * MathF.Log10(1 + hz / 700);
    private static float MelToHz(float mel) => 700 * (MathF.Pow(10, mel / 2595) - 1);
}
