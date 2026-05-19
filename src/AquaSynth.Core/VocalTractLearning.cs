namespace AquaSynth.Dsl;

public sealed record VocalTractSemanticEmbedding(IReadOnlyList<float> Values);

public sealed record VocalTractControlTarget(
    float TongueBody,
    float TongueTip,
    float LipAperture,
    float LipRounding,
    float Velum,
    float GlottalTenseness,
    float Turbulence,
    float Pressure,
    float AmDepth = 0,
    float FmDepth = 0,
    float LfoRate = 0,
    float LfoDepth = 0,
    float FilterCutoff = 0.5f,
    float FilterResonance = 0,
    IReadOnlyList<float>? MelSpectralEnvelope = null);

public sealed record VocalTractTrainingExample(
    PhoneticEvent Event,
    VocalTractSemanticEmbedding SemanticEmbedding,
    VocalTractControlTarget Target);

public sealed record VocalTractTrainingOptions(
    int Epochs = 200,
    float LearningRate = 0.01f,
    int Seed = 1337,
    VocalTractOptimizer Optimizer = VocalTractOptimizer.Adam,
    int BatchSize = 32,
    float Beta1 = 0.9f,
    float Beta2 = 0.999f,
    float Epsilon = 0.00000001f,
    bool Shuffle = true);

public enum VocalTractOptimizer
{
    Sgd,
    Adam
}

public sealed record VocalTractTrainingStep(int Epoch, float Loss);

public sealed record VocalTractTrainingResult(
    VocalTractNeuralMapper Mapper,
    IReadOnlyList<VocalTractTrainingStep> Steps);

public sealed class VocalTractNeuralMapper
{
    private const int PhoneticFeatureCount = 48;
    private const int BaseOutputCount = 14;
    private const float NeutralMelBand = 0.5f;
    private readonly PackedNeuralNetwork network;

    private VocalTractNeuralMapper(int semanticEmbeddingSize, int melBandCount, PackedNeuralNetwork network)
    {
        SemanticEmbeddingSize = semanticEmbeddingSize;
        MelBandCount = melBandCount;
        this.network = network;
    }

    public int SemanticEmbeddingSize { get; }

    public int MelBandCount { get; }

    public int InputSize => PhoneticFeatureCount + 4 + SemanticEmbeddingSize;

    public int OutputSize => BaseOutputCount + MelBandCount;

    public IReadOnlyList<int> HiddenLayerSizes => network.HiddenLayerSizes;

    public static VocalTractNeuralMapper Create(
        int semanticEmbeddingSize,
        int melBandCount = 32,
        IReadOnlyList<int>? hiddenLayerSizes = null,
        int seed = 1337)
    {
        if (semanticEmbeddingSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(semanticEmbeddingSize), "semantic embedding size must be positive");
        }

        if (melBandCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(melBandCount), "mel band count must be positive");
        }

        hiddenLayerSizes ??= [64, 64, 32];
        var network = PackedNeuralNetwork.Create(
            PhoneticFeatureCount + 4 + semanticEmbeddingSize,
            BaseOutputCount + melBandCount,
            hiddenLayerSizes,
            seed);

        return new VocalTractNeuralMapper(semanticEmbeddingSize, melBandCount, network);
    }

    public VocalTractControlTarget Predict(PhoneticEvent phoneticEvent, VocalTractSemanticEmbedding semanticEmbedding)
    {
        return TargetFromVector(network.Predict(EncodeInput(phoneticEvent, semanticEmbedding)));
    }

    public VocalTractTrainingResult Train(
        IReadOnlyList<VocalTractTrainingExample> examples,
        VocalTractTrainingOptions? options = null)
    {
        if (examples.Count == 0)
        {
            throw new ArgumentException("at least one training example is required", nameof(examples));
        }

        options ??= new VocalTractTrainingOptions();
        var packedExamples = new PackedNeuralTrainingExample[examples.Count];
        for (var index = 0; index < examples.Count; index++)
        {
            var example = examples[index];
            packedExamples[index] = new PackedNeuralTrainingExample(
                EncodeInput(example.Event, example.SemanticEmbedding),
                example.Target.ToVector(MelBandCount, NeutralMelBand));
        }

        var result = network.Train(packedExamples, new PackedNeuralTrainingOptions(
            options.Epochs,
            options.LearningRate,
            options.Seed,
            options.Optimizer == VocalTractOptimizer.Adam ? PackedNeuralOptimizer.Adam : PackedNeuralOptimizer.Sgd,
            options.BatchSize,
            options.Beta1,
            options.Beta2,
            options.Epsilon,
            options.Shuffle));
        var steps = result.Steps.Select(step => new VocalTractTrainingStep(step.Epoch, step.Loss)).ToArray();
        return new VocalTractTrainingResult(this, steps);
    }

    public float[] EncodeInput(PhoneticEvent phoneticEvent, VocalTractSemanticEmbedding semanticEmbedding)
    {
        var input = new float[InputSize];
        EncodeInput(phoneticEvent, semanticEmbedding, input);
        return input;
    }

    private void EncodeInput(PhoneticEvent phoneticEvent, VocalTractSemanticEmbedding semanticEmbedding, float[] input)
    {
        if (semanticEmbedding.Values.Count != SemanticEmbeddingSize)
        {
            throw new ArgumentException($"semantic embedding must have {SemanticEmbeddingSize} values", nameof(semanticEmbedding));
        }

        Array.Clear(input);
        var offset = 0;
        offset = OneHot(input, offset, (int)phoneticEvent.Features.Manner, Enum.GetValues<PhoneticManner>().Length);
        offset = OneHot(input, offset, (int)phoneticEvent.Features.Place, Enum.GetValues<PhoneticPlace>().Length);
        offset = OneHot(input, offset, (int)phoneticEvent.Features.Phonation, Enum.GetValues<Phonation>().Length);
        offset = OneHot(input, offset, (int)phoneticEvent.Features.Airstream, Enum.GetValues<AirstreamMechanism>().Length);
        offset = OneHot(input, offset, (int)phoneticEvent.Features.Height, Enum.GetValues<VowelHeight>().Length);
        offset = OneHot(input, offset, (int)phoneticEvent.Features.Backness, Enum.GetValues<VowelBackness>().Length);
        input[offset++] = phoneticEvent.Features.Rounded ? 1 : 0;
        input[offset++] = phoneticEvent.Features.Long ? 1 : 0;
        input[offset++] = phoneticEvent.Features.Nasalized ? 1 : 0;
        input[offset++] = phoneticEvent.Features.Lateral ? 1 : 0;
        input[offset++] = Clamp01(phoneticEvent.Prosody.Stress);
        input[offset++] = Clamp01((phoneticEvent.Prosody.PitchTarget + 1) * 0.5f);
        input[offset++] = Clamp01(phoneticEvent.Prosody.Intensity);
        input[offset++] = Clamp01((float)phoneticEvent.DurationSeconds);
        foreach (var value in semanticEmbedding.Values)
        {
            input[offset++] = Math.Clamp(value, -1, 1);
        }
    }

    private static int OneHot(float[] input, int offset, int value, int count)
    {
        input[offset + Math.Clamp(value, 0, count - 1)] = 1;
        return offset + count;
    }

    private static VocalTractControlTarget TargetFromVector(IReadOnlyList<float> values)
    {
        var mel = new float[Math.Max(0, values.Count - BaseOutputCount)];
        for (var index = 0; index < mel.Length; index++)
        {
            mel[index] = values[BaseOutputCount + index];
        }

        return new(
            values[0],
            values[1],
            values[2],
            values[3],
            values[4],
            values[5],
            values[6],
            values[7],
            values[8],
            values[9],
            values[10],
            values[11],
            values[12],
            values[13],
            mel);
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0, 1);
}

public static class VocalTractControlTargetExtensions
{
    public static float[] ToVector(this VocalTractControlTarget target, int melBandCount = 0, float neutralMelBand = 0.5f)
    {
        var output = new float[14 + melBandCount];
        target.ToVector(output, melBandCount, neutralMelBand);
        return output;
    }

    public static void ToVector(this VocalTractControlTarget target, float[] output, int melBandCount = 0, float neutralMelBand = 0.5f)
    {
        if (output.Length < 14 + melBandCount)
        {
            throw new ArgumentException($"output must have at least {14 + melBandCount} values", nameof(output));
        }

        output[0] = Clamp01(target.TongueBody);
        output[1] = Clamp01(target.TongueTip);
        output[2] = Clamp01(target.LipAperture);
        output[3] = Clamp01(target.LipRounding);
        output[4] = Clamp01(target.Velum);
        output[5] = Clamp01(target.GlottalTenseness);
        output[6] = Clamp01(target.Turbulence);
        output[7] = Clamp01(target.Pressure);
        output[8] = Clamp01(target.AmDepth);
        output[9] = Clamp01(target.FmDepth);
        output[10] = Clamp01(target.LfoRate);
        output[11] = Clamp01(target.LfoDepth);
        output[12] = Clamp01(target.FilterCutoff);
        output[13] = Clamp01(target.FilterResonance);

        for (var index = 0; index < melBandCount; index++)
        {
            var band = target.MelSpectralEnvelope is not null && index < target.MelSpectralEnvelope.Count
                ? target.MelSpectralEnvelope[index]
                : neutralMelBand;
            output[14 + index] = Clamp01(band);
        }
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0, 1);
}
