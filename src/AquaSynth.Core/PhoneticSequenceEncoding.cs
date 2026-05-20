namespace AquaSynth.Dsl;

public static class PanPhonFeatureSet
{
    public static readonly string[] FeatureNames =
    [
        "syl",
        "son",
        "cons",
        "cont",
        "delrel",
        "lat",
        "nas",
        "strid",
        "voi",
        "sg",
        "cg",
        "ant",
        "cor",
        "distr",
        "lab",
        "hi",
        "lo",
        "back",
        "round",
        "velaric",
        "tense",
        "long"
    ];

    public const int FeatureCount = 22;
}

public sealed record PanPhonSequenceFrame(
    string Segment,
    IReadOnlyList<float> Features,
    float Stress = 0,
    float Length = 0,
    float Tone = 0,
    float Boundary = 0,
    float DurationSeconds = 0)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Segment))
        {
            throw new ArgumentException("segment must be present", nameof(Segment));
        }

        if (Features.Count != PanPhonFeatureSet.FeatureCount)
        {
            throw new ArgumentException($"PanPhon frame must have {PanPhonFeatureSet.FeatureCount} feature values", nameof(Features));
        }
    }
}

public sealed record PanPhonSequence(IReadOnlyList<PanPhonSequenceFrame> Frames)
{
    public PhoneticSequenceSketch ToSketch()
    {
        if (Frames.Count == 0)
        {
            throw new ArgumentException("at least one PanPhon frame is required", nameof(Frames));
        }

        foreach (var frame in Frames)
        {
            frame.Validate();
        }

        var values = new float[PhoneticSequenceSketch.Size];
        values[0] = Math.Clamp(Frames.Count / 16f, 0f, 1f);
        var offset = 1;

        for (var featureIndex = 0; featureIndex < PanPhonFeatureSet.FeatureCount; featureIndex++)
        {
            var sum = 0f;
            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            for (var frameIndex = 0; frameIndex < Frames.Count; frameIndex++)
            {
                var value = ClampFeature(Frames[frameIndex].Features[featureIndex]);
                sum += value;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }

            values[offset++] = sum / Frames.Count;
            values[offset++] = minimum;
            values[offset++] = maximum;
            values[offset++] = ClampFeature(Frames[0].Features[featureIndex]);
            values[offset++] = ClampFeature(Frames[^1].Features[featureIndex]);
        }

        WriteMarkerStats(values, ref offset, Frames, frame => frame.Stress);
        WriteMarkerStats(values, ref offset, Frames, frame => frame.Length);
        WriteMarkerStats(values, ref offset, Frames, frame => frame.Tone);
        WriteMarkerStats(values, ref offset, Frames, frame => frame.Boundary);
        WriteMarkerStats(values, ref offset, Frames, frame => Math.Clamp(frame.DurationSeconds, 0f, 1f));

        return new PhoneticSequenceSketch(values);
    }

    private static void WriteMarkerStats(
        float[] values,
        ref int offset,
        IReadOnlyList<PanPhonSequenceFrame> frames,
        Func<PanPhonSequenceFrame, float> selector)
    {
        var sum = 0f;
        var maximum = 0f;
        foreach (var frame in frames)
        {
            var value = Math.Clamp(selector(frame), -1f, 1f);
            sum += value;
            maximum = Math.Max(maximum, Math.Abs(value));
        }

        values[offset++] = sum / frames.Count;
        values[offset++] = maximum;
        values[offset++] = Math.Clamp(selector(frames[0]), -1f, 1f);
        values[offset++] = Math.Clamp(selector(frames[^1]), -1f, 1f);
    }

    private static float ClampFeature(float value) => Math.Clamp(value, -1f, 1f);
}

public sealed record PhoneticSequenceSketch(IReadOnlyList<float> Values)
{
    public const int MarkerCount = 5;
    public const int Size = 1 + (PanPhonFeatureSet.FeatureCount * 5) + (MarkerCount * 4);
}

public sealed record PhoneticRealizationEmbedding(IReadOnlyList<float> Values);

public sealed record PhoneticSequenceTrainingExample(
    PanPhonSequence Sequence,
    PhoneticRealizationEmbedding Target);

public sealed record PhoneticSequenceTrainingResult(
    PhoneticSequenceNeuralEncoder Encoder,
    IReadOnlyList<PackedNeuralTrainingStep> Steps);

public sealed class PhoneticSequenceNeuralEncoder
{
    private readonly PackedNeuralNetwork network;

    private PhoneticSequenceNeuralEncoder(PackedNeuralNetwork network)
    {
        this.network = network;
    }

    public int InputSize => network.InputSize;

    public int EmbeddingSize => network.OutputSize;

    public IReadOnlyList<int> HiddenLayerSizes => network.HiddenLayerSizes;

    public static PhoneticSequenceNeuralEncoder Create(
        IReadOnlyList<int>? hiddenLayerSizes = null,
        int seed = 7331)
    {
        var network = PackedNeuralNetwork.Create(
            PhoneticSequenceSketch.Size,
            WeksaUtteranceEmbeddingHandoff.PhoneticRealizationEmbeddingSize,
            hiddenLayerSizes ?? [96, 96, 64],
            seed);
        return new PhoneticSequenceNeuralEncoder(network);
    }

    public PhoneticRealizationEmbedding Encode(PanPhonSequence sequence) =>
        Encode(sequence.ToSketch());

    public PhoneticRealizationEmbedding Encode(PhoneticSequenceSketch sketch)
    {
        if (sketch.Values.Count != InputSize)
        {
            throw new ArgumentException($"phonetic sequence sketch must have {InputSize} values", nameof(sketch));
        }

        return new PhoneticRealizationEmbedding(network.Predict(sketch.Values));
    }

    public PhoneticSequenceTrainingResult Train(
        IReadOnlyList<PhoneticSequenceTrainingExample> examples,
        PackedNeuralTrainingOptions? options = null)
    {
        if (examples.Count == 0)
        {
            throw new ArgumentException("at least one training example is required", nameof(examples));
        }

        var packed = new PackedNeuralTrainingExample[examples.Count];
        for (var index = 0; index < examples.Count; index++)
        {
            var example = examples[index];
            if (example.Target.Values.Count != EmbeddingSize)
            {
                throw new ArgumentException($"phonetic target embedding must have {EmbeddingSize} values", nameof(examples));
            }

            packed[index] = new PackedNeuralTrainingExample(example.Sequence.ToSketch().Values, example.Target.Values);
        }

        var result = network.Train(packed, options);
        return new PhoneticSequenceTrainingResult(this, result.Steps);
    }
}
