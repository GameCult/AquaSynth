namespace AquaSynth.Dsl;

public sealed record UtteranceEmbeddingInput(
    IReadOnlyList<float> SpeechTextEmbedding,
    IReadOnlyList<float> PhoneticRealizationEmbedding,
    IReadOnlyList<float> ProsodyAndEmphasisHints,
    IReadOnlyList<float> CharacterStateVector)
{
    public UtteranceEmbeddingFeatureVector ToFeatureVector()
    {
        var values = new float[SpeechTextEmbedding.Count + PhoneticRealizationEmbedding.Count + ProsodyAndEmphasisHints.Count + CharacterStateVector.Count];
        var offset = Copy(SpeechTextEmbedding, values, 0);
        offset = Copy(PhoneticRealizationEmbedding, values, offset);
        offset = Copy(ProsodyAndEmphasisHints, values, offset);
        Copy(CharacterStateVector, values, offset);
        return new UtteranceEmbeddingFeatureVector(values);
    }

    private static int Copy(IReadOnlyList<float> source, float[] target, int offset)
    {
        for (var index = 0; index < source.Count; index++)
        {
            target[offset + index] = source[index];
        }

        return offset + source.Count;
    }
}

public static class WeksaUtteranceEmbeddingHandoff
{
    public const string SchemaVersion = "weksa.utterance_embedding_handoff.v0.1";
    public const string SpeechTextEmbeddingModelId = "bge-m3:latest";
    public const string PhoneticRealizationModelId = "aquasynth.panphon_sequence_encoder.v0.1";
    public const int SpeechTextEmbeddingSize = 1024;
    public const int PhoneticRealizationEmbeddingSize = 256;
    public const int ProsodyAndEmphasisHintSize = 32;
    public const int CharacterStateVectorSize = 64;
    public const int UtteranceEmbeddingSize = 64;
    public const int InputSize = SpeechTextEmbeddingSize + PhoneticRealizationEmbeddingSize + ProsodyAndEmphasisHintSize + CharacterStateVectorSize;

    public static void Validate(UtteranceEmbeddingInput input)
    {
        ValidateSize(input.SpeechTextEmbedding, SpeechTextEmbeddingSize, nameof(input.SpeechTextEmbedding));
        ValidateSize(input.PhoneticRealizationEmbedding, PhoneticRealizationEmbeddingSize, nameof(input.PhoneticRealizationEmbedding));
        ValidateSize(input.ProsodyAndEmphasisHints, ProsodyAndEmphasisHintSize, nameof(input.ProsodyAndEmphasisHints));
        ValidateSize(input.CharacterStateVector, CharacterStateVectorSize, nameof(input.CharacterStateVector));
    }

    private static void ValidateSize(IReadOnlyList<float> values, int expected, string name)
    {
        if (values.Count != expected)
        {
            throw new ArgumentException($"{SchemaVersion} requires {name} to have {expected} values", name);
        }
    }
}

public sealed record UtteranceEmbeddingFeatureVector(IReadOnlyList<float> Values);

public sealed record UtteranceEmbedding(IReadOnlyList<float> Values)
{
    public VocalTractSemanticEmbedding ToSemanticEmbedding() =>
        new(Values);
}

public sealed record UtteranceEmbeddingTrainingExample(
    UtteranceEmbeddingFeatureVector Features,
    UtteranceEmbedding Target);

public sealed record UtteranceEmbeddingTrainingResult(
    UtteranceEmbeddingNeuralEncoder Encoder,
    IReadOnlyList<PackedNeuralTrainingStep> Steps);

public sealed class UtteranceEmbeddingNeuralEncoder
{
    private readonly PackedNeuralNetwork network;

    private UtteranceEmbeddingNeuralEncoder(int inputSize, int embeddingSize, PackedNeuralNetwork network)
    {
        InputSize = inputSize;
        EmbeddingSize = embeddingSize;
        this.network = network;
    }

    public int InputSize { get; }

    public int EmbeddingSize { get; }

    public IReadOnlyList<int> HiddenLayerSizes => network.HiddenLayerSizes;

    public static UtteranceEmbeddingNeuralEncoder Create(
        int inputSize,
        int embeddingSize,
        IReadOnlyList<int>? hiddenLayerSizes = null,
        int seed = 1337)
    {
        if (inputSize <= 0) throw new ArgumentOutOfRangeException(nameof(inputSize), "input size must be positive");
        if (embeddingSize <= 0) throw new ArgumentOutOfRangeException(nameof(embeddingSize), "embedding size must be positive");

        var network = PackedNeuralNetwork.Create(inputSize, embeddingSize, hiddenLayerSizes ?? [64, 64, 32], seed);
        return new UtteranceEmbeddingNeuralEncoder(inputSize, embeddingSize, network);
    }

    public UtteranceEmbedding Encode(UtteranceEmbeddingInput input) =>
        Encode(input.ToFeatureVector());

    public UtteranceEmbedding Encode(UtteranceEmbeddingFeatureVector features)
    {
        ValidateSize(features.Values, InputSize, nameof(features));
        return new UtteranceEmbedding(network.Predict(features.Values));
    }

    public PackedNeuralBackpropagation TrainSingleFromOutputGradient(
        UtteranceEmbeddingFeatureVector features,
        IReadOnlyList<float> outputGradient,
        float learningRate)
    {
        ValidateSize(features.Values, InputSize, nameof(features));
        return network.TrainSingleFromOutputGradient(features.Values, outputGradient, learningRate);
    }

    public UtteranceEmbeddingTrainingResult Train(
        IReadOnlyList<UtteranceEmbeddingTrainingExample> examples,
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
            ValidateSize(example.Features.Values, InputSize, nameof(example.Features));
            ValidateSize(example.Target.Values, EmbeddingSize, nameof(example.Target));
            packed[index] = new PackedNeuralTrainingExample(example.Features.Values, example.Target.Values);
        }

        var result = network.Train(packed, options);
        return new UtteranceEmbeddingTrainingResult(this, result.Steps);
    }

    private static void ValidateSize(IReadOnlyList<float> values, int expected, string name)
    {
        if (values.Count != expected)
        {
            throw new ArgumentException($"{name} must have {expected} values", name);
        }
    }
}
