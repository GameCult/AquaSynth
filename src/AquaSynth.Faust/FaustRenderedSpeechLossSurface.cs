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

public sealed record FaustRenderedSpeechCandidateLoss(
    VocalTractControlTarget Target,
    float Loss,
    float[] OutputGradient,
    FaustRender Render,
    AudioComparison Comparison);

public sealed record FaustRenderedSpeechBatchLoss(
    FaustRender Reference,
    IReadOnlyList<FaustRenderedSpeechCandidateLoss> Candidates);

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
        options.GradientOutputIndices ?? Enumerable.Range(0, 14 + options.MelBandCount).ToArray();

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

public sealed class CompiledFaustRenderedSpeechLossSurface : IDisposable
{
    private readonly AquaSynthPatchCompiler compiler;
    private readonly AquaSynthCompiledPatch patch;
    private readonly FaustRenderedSpeechLossOptions options;
    private bool disposed;

    private CompiledFaustRenderedSpeechLossSurface(
        AquaSynthPatchCompiler compiler,
        AquaSynthCompiledPatch patch,
        FaustRenderedSpeechLossOptions options)
    {
        this.compiler = compiler;
        this.patch = patch;
        this.options = options;
    }

    public IReadOnlyList<string> ControlPaths => patch.ControlPaths;

    public static bool TryCreate(
        out CompiledFaustRenderedSpeechLossSurface? surface,
        out string? error,
        FaustRenderedSpeechLossOptions? options = null,
        AquaSynthNativeOptions? nativeOptions = null)
    {
        options ??= new FaustRenderedSpeechLossOptions();
        var runtimeOptions = (nativeOptions ?? new AquaSynthNativeOptions()) with
        {
            SampleRate = options.SampleRate,
            MinRenderSeconds = options.DurationSeconds,
            MaxRenderSeconds = Math.Max(options.DurationSeconds, nativeOptions?.MaxRenderSeconds ?? options.DurationSeconds)
        };
        var compiler = new AquaSynthPatchCompiler(runtimeOptions);
        var source = ControllableSource(options);
        if (!compiler.TryCompileSource(
                new AquaSynthCompileIdentity("speech_loss_surface", "speech_loss_surface", source),
                source,
                options.DurationSeconds,
                out var patch,
                out error))
        {
            compiler.Dispose();
            surface = null;
            return false;
        }

        surface = new CompiledFaustRenderedSpeechLossSurface(compiler, patch!, options);
        error = null;
        return true;
    }

    public FaustRender Render(VocalTractControlTarget target)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var samples = patch.Render(ControlsFor(target));
        return new FaustRender(samples, options.SampleRate, "native-faust", "", "");
    }

    public FaustRenderedSpeechLoss Evaluate(VocalTractControlTarget reference, VocalTractControlTarget candidate)
    {
        var batch = EvaluateBatch(reference, [candidate]);
        var loss = batch.Candidates[0];
        return new FaustRenderedSpeechLoss(loss.Loss, loss.OutputGradient, batch.Reference, loss.Render, loss.Comparison);
    }

    public FaustRenderedSpeechBatchLoss EvaluateBatch(
        VocalTractControlTarget reference,
        IReadOnlyList<VocalTractControlTarget> candidates)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var referenceRender = Render(reference);
        var results = new List<FaustRenderedSpeechCandidateLoss>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var candidateRender = Render(candidate);
            var comparison = Analyzer().Compare(referenceRender.Samples, candidateRender.Samples);
            var gradient = EstimateGradient(referenceRender.Samples, candidate, comparison.LogMelDistance);
            results.Add(new FaustRenderedSpeechCandidateLoss(
                candidate,
                comparison.LogMelDistance,
                gradient,
                candidateRender,
                comparison));
        }

        return new FaustRenderedSpeechBatchLoss(referenceRender, results);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        patch.Dispose();
        compiler.Dispose();
        disposed = true;
    }

    public static string ControllableSource(FaustRenderedSpeechLossOptions? options = null)
    {
        options ??= new FaustRenderedSpeechLossOptions();
        var source = new StringBuilder();
        source.AppendLine("import(\"stdfaust.lib\");");
        source.AppendLine("declare name \"aquasynth_speech_loss_surface\";");
        source.AppendLine("time = ba.time / ma.SR;");
        source.AppendLine("clip01(x) = min(1.0, max(0.0, x));");
        source.AppendLine("sine(hz) = os.osc(hz);");
        for (var index = 0; index < 14 + options.MelBandCount; index++)
        {
            source.AppendLine($"o{index} = hslider(\"{OutputPath(index)}\", 0.5, 0.0, 1.0, 0.0001) : si.smoo;");
        }

        source.AppendLine("fm = sine((120.0 + o5 * 110.0) * 2.0) * o9 * 0.18;");
        source.AppendLine("glottis = sine((120.0 + o5 * 110.0) * (1.0 + fm)) * (0.08 + o7 * 0.22);");
        source.AppendLine("breath = no.noise * o6 * 0.18;");
        for (var band = 0; band < options.MelBandCount; band++)
        {
            source.AppendLine($"mel{band} = sine({F(MelBandCenterHz(options, band))}) * o{14 + band} * o{14 + band} * 0.08;");
        }

        var melMix = options.MelBandCount == 0
            ? "0.0"
            : string.Join(" + ", Enumerable.Range(0, options.MelBandCount).Select(index => $"mel{index}"));
        source.AppendLine($"raw = glottis + breath + {melMix};");
        source.AppendLine("trem = 1.0 - o8 * (0.5 + 0.5 * sine(2.0 + o10 * 11.0));");
        source.AppendLine("filtered = raw * trem : fi.lowpass(2, min(ma.SR * 0.45, max(20.0, o12 * 18000.0)));");
        var keepControls = string.Join(" + ", Enumerable.Range(0, 14 + options.MelBandCount).Select(index => $"o{index}"));
        source.AppendLine($"process = filtered * 0.85 + 0.000000001 * ({keepControls});");
        return source.ToString();
    }

    public static string OutputPath(int outputIndex) => $"speech/output/{outputIndex}";

    private float[] EstimateGradient(
        IReadOnlyList<float> referenceSamples,
        VocalTractControlTarget candidate,
        float baselineLoss)
    {
        var gradient = new float[14 + options.MelBandCount];
        foreach (var index in GradientIndices())
        {
            var perturbed = Perturb(candidate, index, options.FiniteDifferenceStep);
            var render = Render(perturbed);
            var loss = Analyzer().Compare(referenceSamples.ToArray(), render.Samples).LogMelDistance;
            gradient[index] = (loss - baselineLoss) / options.FiniteDifferenceStep;
        }

        return gradient;
    }

    private IReadOnlyList<int> GradientIndices() =>
        options.GradientOutputIndices ?? Enumerable.Range(0, 14 + options.MelBandCount).ToArray();

    private IReadOnlyDictionary<string, float> ControlsFor(VocalTractControlTarget target)
    {
        var values = target.ToVector(options.MelBandCount);
        var controls = new Dictionary<string, float>(values.Length, StringComparer.Ordinal);
        for (var index = 0; index < values.Length; index++)
        {
            controls[OutputPath(index)] = Math.Clamp(values[index], 0, 1);
        }

        return controls;
    }

    private AudioAnalyzer Analyzer() =>
        new(new AudioAnalysisConfig(
            SampleRate: options.SampleRate,
            FftSize: 256,
            HopSize: 96,
            MelBandCount: options.MelBandCount,
            MinFrequencyHz: options.MinFrequencyHz,
            MaxFrequencyHz: options.MaxFrequencyHz));

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

    private static float MelBandCenterHz(FaustRenderedSpeechLossOptions options, int band)
    {
        var minMel = HzToMel(options.MinFrequencyHz);
        var maxMel = HzToMel(options.MaxFrequencyHz);
        var t = (band + 0.5f) / Math.Max(1, options.MelBandCount);
        return MelToHz(minMel + (maxMel - minMel) * t);
    }

    private static string F(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static float HzToMel(float hz) => 2595 * MathF.Log10(1 + hz / 700);
    private static float MelToHz(float mel) => 700 * (MathF.Pow(10, mel / 2595) - 1);
}
