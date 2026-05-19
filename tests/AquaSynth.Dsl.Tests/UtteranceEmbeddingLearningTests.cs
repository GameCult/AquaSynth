using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

public sealed class UtteranceEmbeddingLearningTests
{
    [Fact]
    public void UtteranceEmbeddingInputPacksTextProsodyAndCharacterState()
    {
        var input = new UtteranceEmbeddingInput(
            SpeechTextEmbedding: [0.1f, 0.2f, 0.3f],
            ProsodyAndEmphasisHints: [0.7f, 0.8f],
            CharacterStateVector: [0.4f, 0.5f, 0.6f]);

        var features = input.ToFeatureVector();

        Assert.Equal([0.1f, 0.2f, 0.3f, 0.7f, 0.8f, 0.4f, 0.5f, 0.6f], features.Values);
    }

    [Fact]
    public void UtteranceEmbeddingEncoderUsesPackedAquaSynthTraining()
    {
        var encoder = UtteranceEmbeddingNeuralEncoder.Create(inputSize: 10, embeddingSize: 16, hiddenLayerSizes: [48, 32, 24], seed: 61);
        var examples = TrainingSet();

        var before = AverageLoss(encoder, examples);
        var result = encoder.Train(examples, new PackedNeuralTrainingOptions(Epochs: 320, LearningRate: 0.035f, Seed: 61, BatchSize: 2));
        var after = AverageLoss(encoder, examples);

        Assert.Equal([48, 32, 24], encoder.HiddenLayerSizes);
        Assert.Equal(320, result.Steps.Count);
        Assert.True(after < before * 0.35f, $"expected utterance-embedding loss to drop; before={before}, after={after}");
    }

    [Fact]
    public void UtteranceEmbeddingCanFeedVocalTractSemanticEmbedding()
    {
        var vector = new UtteranceEmbedding(Enumerable.Range(0, 16).Select(index => index / 16f).ToArray());

        var embedding = vector.ToSemanticEmbedding();

        Assert.Equal(vector.Values, embedding.Values);
    }

    [Fact]
    public void UtteranceEmbeddingEncoderSeparatesContrastingDeliveryTargets()
    {
        var encoder = UtteranceEmbeddingNeuralEncoder.Create(inputSize: 10, embeddingSize: 16, hiddenLayerSizes: [64, 48, 32], seed: 67);
        var clippedThreat = new UtteranceEmbeddingFeatureVector([0.95f, 0.85f, 0.15f, 0.80f, 0.70f, 0.10f, 0.20f, 0.90f, 0.75f, 0.30f]);
        var warmReassurance = new UtteranceEmbeddingFeatureVector([0.20f, 0.25f, 0.88f, 0.15f, 0.30f, 0.80f, 0.75f, 0.18f, 0.25f, 0.70f]);
        var threatTarget = Vector(16, 0.86f, 0.72f, 0.18f);
        var warmTarget = Vector(16, 0.22f, 0.48f, 0.88f);
        var examples = new[]
        {
            new UtteranceEmbeddingTrainingExample(clippedThreat, threatTarget),
            new UtteranceEmbeddingTrainingExample(warmReassurance, warmTarget)
        };

        encoder.Train(examples, new PackedNeuralTrainingOptions(Epochs: 420, LearningRate: 0.035f, Seed: 67, BatchSize: 2));
        var threat = encoder.Encode(clippedThreat);
        var warm = encoder.Encode(warmReassurance);

        Assert.True(threat.Values[0] > warm.Values[0] + 0.25f);
        Assert.True(warm.Values[^1] > threat.Values[^1] + 0.25f);
    }

    private static IReadOnlyList<UtteranceEmbeddingTrainingExample> TrainingSet() =>
    [
        new(new UtteranceEmbeddingFeatureVector([0.95f, 0.82f, 0.10f, 0.75f, 0.70f, 0.12f, 0.18f, 0.88f, 0.74f, 0.28f]), Vector(16, 0.86f, 0.70f, 0.20f)),
        new(new UtteranceEmbeddingFeatureVector([0.20f, 0.22f, 0.88f, 0.18f, 0.28f, 0.82f, 0.72f, 0.18f, 0.26f, 0.74f]), Vector(16, 0.20f, 0.50f, 0.88f)),
        new(new UtteranceEmbeddingFeatureVector([0.55f, 0.42f, 0.48f, 0.35f, 0.92f, 0.34f, 0.42f, 0.52f, 0.62f, 0.44f]), Vector(16, 0.48f, 0.82f, 0.50f))
    ];

    private static UtteranceEmbedding Vector(int size, float left, float center, float right)
    {
        var values = new float[size];
        for (var index = 0; index < size; index++)
        {
            var position = index / Math.Max(1f, size - 1f);
            values[index] = position < 0.5f
                ? Lerp(left, center, position * 2f)
                : Lerp(center, right, (position - 0.5f) * 2f);
        }

        return new UtteranceEmbedding(values);
    }

    private static float AverageLoss(UtteranceEmbeddingNeuralEncoder encoder, IReadOnlyList<UtteranceEmbeddingTrainingExample> examples)
    {
        var loss = 0f;
        foreach (var example in examples)
        {
            var prediction = encoder.Encode(example.Features);
            for (var index = 0; index < prediction.Values.Count; index++)
            {
                var difference = prediction.Values[index] - example.Target.Values[index];
                loss += difference * difference;
            }
        }

        return loss / examples.Count / encoder.EmbeddingSize;
    }

    private static float Lerp(float left, float right, float amount) =>
        left + (right - left) * amount;
}
