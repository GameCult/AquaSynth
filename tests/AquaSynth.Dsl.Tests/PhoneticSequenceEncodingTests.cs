using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

public sealed class PhoneticSequenceEncodingTests
{
    [Fact]
    public void PanPhonSequenceSketchPreservesOrderAndFeatureStatistics()
    {
        var sequence = new PanPhonSequence(
        [
            Frame("p", Syl: -1, Son: -1, Cons: 1, Cont: -1, Lab: 1, Stress: 0.8f, Boundary: 0),
            Frame("a", Syl: 1, Son: 1, Cons: -1, Cont: 1, Lo: 1, Stress: 0.8f, Boundary: 1)
        ]);

        var sketch = sequence.ToSketch();

        Assert.Equal(PhoneticSequenceSketch.Size, sketch.Values.Count);
        Assert.Equal(0.125f, sketch.Values[0], precision: 3);
        Assert.Contains(sketch.Values, value => value > 0.9f);
        Assert.Contains(sketch.Values, value => value < -0.9f);
    }

    [Fact]
    public void PhoneticSequenceEncoderLearnsCompactPanphonRealizationEmbedding()
    {
        var encoder = PhoneticSequenceNeuralEncoder.Create(hiddenLayerSizes: [96, 64], seed: 901);
        var examples = new[]
        {
            new PhoneticSequenceTrainingExample(
                new PanPhonSequence([Frame("p", Cons: 1, Cont: -1, Lab: 1), Frame("a", Syl: 1, Son: 1, Lo: 1)]),
                Target(0.86f, 0.20f, 0.30f)),
            new PhoneticSequenceTrainingExample(
                new PanPhonSequence([Frame("s", Cons: 1, Cont: 1, Strid: 1, Cor: 1), Frame("a", Syl: 1, Son: 1, Lo: 1)]),
                Target(0.22f, 0.88f, 0.42f)),
            new PhoneticSequenceTrainingExample(
                new PanPhonSequence([Frame("m", Cons: 1, Son: 1, Nas: 1, Lab: 1), Frame("a", Syl: 1, Son: 1, Lo: 1)]),
                Target(0.68f, 0.44f, 0.86f))
        };

        var before = AverageLoss(encoder, examples);
        var result = encoder.Train(examples, new PackedNeuralTrainingOptions(Epochs: 360, LearningRate: 0.035f, Seed: 907, BatchSize: 3));
        var after = AverageLoss(encoder, examples);

        Assert.Equal(WeksaUtteranceEmbeddingHandoff.PhoneticRealizationEmbeddingSize, encoder.EmbeddingSize);
        Assert.Equal(360, result.Steps.Count);
        Assert.True(after < before * 0.40f, $"expected PanPhon sequence encoder loss to drop; before={before}, after={after}");
    }

    private static PanPhonSequenceFrame Frame(
        string segment,
        float Syl = 0,
        float Son = 0,
        float Cons = 0,
        float Cont = 0,
        float Strid = 0,
        float Lab = 0,
        float Cor = 0,
        float Nas = 0,
        float Lo = 0,
        float Stress = 0,
        float Boundary = 0)
    {
        var features = new float[PanPhonFeatureSet.FeatureCount];
        features[0] = Syl;
        features[1] = Son;
        features[2] = Cons;
        features[3] = Cont;
        features[6] = Nas;
        features[7] = Strid;
        features[12] = Cor;
        features[14] = Lab;
        features[16] = Lo;
        return new PanPhonSequenceFrame(segment, features, Stress: Stress, Boundary: Boundary, DurationSeconds: 0.08f);
    }

    private static PhoneticRealizationEmbedding Target(float left, float center, float right)
    {
        var values = new float[WeksaUtteranceEmbeddingHandoff.PhoneticRealizationEmbeddingSize];
        for (var index = 0; index < values.Length; index++)
        {
            var position = index / Math.Max(1f, values.Length - 1f);
            values[index] = position < 0.5f
                ? Lerp(left, center, position * 2f)
                : Lerp(center, right, (position - 0.5f) * 2f);
        }

        return new PhoneticRealizationEmbedding(values);
    }

    private static float AverageLoss(PhoneticSequenceNeuralEncoder encoder, IReadOnlyList<PhoneticSequenceTrainingExample> examples)
    {
        var loss = 0f;
        foreach (var example in examples)
        {
            var prediction = encoder.Encode(example.Sequence);
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
