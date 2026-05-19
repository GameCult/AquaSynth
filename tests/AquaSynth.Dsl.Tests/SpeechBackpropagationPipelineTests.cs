using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

public sealed class SpeechBackpropagationPipelineTests
{
    [Fact]
    public void SpeechPipelineBackpropagatesSynthDriverLossIntoUtteranceEncoder()
    {
        var utteranceEncoder = UtteranceEmbeddingNeuralEncoder.Create(
            inputSize: 10,
            embeddingSize: 16,
            hiddenLayerSizes: [48, 32, 24],
            seed: 211);
        var synthDriver = VocalTractNeuralMapper.Create(
            semanticEmbeddingSize: 16,
            melBandCount: 12,
            hiddenLayerSizes: [72, 48, 32],
            seed: 223);
        var pipeline = new SpeechBackpropagationPipeline(utteranceEncoder, synthDriver);
        var examples = TrainingSet();

        var before = AverageLoss(pipeline, examples);
        var result = pipeline.Train(examples, new SpeechBackpropagationTrainingOptions(
            Epochs: 520,
            UtteranceLearningRate: 0.055f,
            SynthDriverLearningRate: 0.055f,
            Seed: 229));
        var after = AverageLoss(pipeline, examples);

        Assert.Equal(520, result.Steps.Count);
        Assert.True(after < before * 0.55f, $"expected end-to-end loss to drop; before={before}, after={after}");
        Assert.True(result.Steps[^1].Loss < result.Steps[0].Loss, "pipeline loss should decrease across epochs");
    }

    [Fact]
    public void SpeechPipelineAcceptsExternalRenderedLossGradient()
    {
        var utteranceEncoder = UtteranceEmbeddingNeuralEncoder.Create(
            inputSize: 10,
            embeddingSize: 8,
            hiddenLayerSizes: [24, 16],
            seed: 301);
        var synthDriver = VocalTractNeuralMapper.Create(
            semanticEmbeddingSize: 8,
            melBandCount: 4,
            hiddenLayerSizes: [28, 20],
            seed: 307);
        var pipeline = new SpeechBackpropagationPipeline(utteranceEncoder, synthDriver);
        var example = Example(
            "a",
            new PhoneticFeatures(PhoneticManner.Vowel, Height: VowelHeight.Open, Backness: VowelBackness.Front),
            new UtteranceEmbeddingInput([0.9f, 0.1f, 0.2f, 0.7f], [0.8f, 0.3f, 0.2f], [0.6f, 0.2f, 0.4f]),
            SmallTarget([0.9f, 0.7f, 0.3f, 0.1f]));
        var before = pipeline.Predict(example).ToVector(pipeline.SynthDriver.MelBandCount);
        var gradient = new float[pipeline.SynthDriver.OutputSize];
        gradient[14] = before[14] - 0.95f;
        gradient[15] = before[15] - 0.75f;
        gradient[16] = before[16] - 0.25f;
        gradient[17] = before[17] - 0.05f;

        var result = pipeline.TrainSingleFromSynthOutputGradient(
            example.UtteranceInput,
            example.Event,
            gradient,
            utteranceLearningRate: 0.08f,
            synthDriverLearningRate: 0.08f,
            loss: 1.25f);
        var after = pipeline.Predict(example).ToVector(pipeline.SynthDriver.MelBandCount);

        Assert.Equal(1.25f, result.Loss);
        Assert.True(Math.Abs(after[14] - 0.95f) < Math.Abs(before[14] - 0.95f), "external output gradient should move the synth-driver output toward the rendered-loss target");
    }

    private static IReadOnlyList<SpeechBackpropagationTrainingExample> TrainingSet() =>
    [
        Example(
            "a",
            new PhoneticFeatures(PhoneticManner.Vowel, Height: VowelHeight.Open, Backness: VowelBackness.Front),
            new UtteranceEmbeddingInput([0.92f, 0.20f, 0.35f, 0.70f], [0.88f, 0.32f, 0.20f], [0.65f, 0.20f, 0.44f]),
            Target([0.82f, 0.74f, 0.66f, 0.58f, 0.50f, 0.42f, 0.34f, 0.28f, 0.22f, 0.18f, 0.16f, 0.14f])),
        Example(
            "sa",
            new PhoneticFeatures(PhoneticManner.Fricative, PhoneticPlace.Alveolar, Phonation.Voiceless),
            new UtteranceEmbeddingInput([0.35f, 0.42f, 0.90f, 0.44f], [0.70f, 0.92f, 0.75f], [0.28f, 0.60f, 0.82f]),
            Target([0.12f, 0.16f, 0.22f, 0.34f, 0.48f, 0.62f, 0.76f, 0.88f, 0.94f, 0.90f, 0.82f, 0.72f])),
        Example(
            "ma",
            new PhoneticFeatures(PhoneticManner.Nasal, PhoneticPlace.Bilabial, Phonation.Voiced, Nasalized: true),
            new UtteranceEmbeddingInput([0.72f, 0.52f, 0.24f, 0.64f], [0.62f, 0.40f, 0.32f], [0.74f, 0.80f, 0.36f]),
            Target([0.68f, 0.72f, 0.70f, 0.62f, 0.54f, 0.46f, 0.40f, 0.36f, 0.32f, 0.30f, 0.28f, 0.26f]))
    ];

    private static SpeechBackpropagationTrainingExample Example(
        string ipa,
        PhoneticFeatures features,
        UtteranceEmbeddingInput input,
        VocalTractControlTarget target) =>
        new(input, new PhoneticEvent($"phone-{ipa}", ipa, features, DurationSeconds: 0.2), target);

    private static VocalTractControlTarget Target(IReadOnlyList<float> mel) =>
        new(
            TongueBody: mel[3],
            TongueTip: mel[8],
            LipAperture: mel[0],
            LipRounding: 0.08f,
            Velum: mel[1],
            GlottalTenseness: mel[4],
            Turbulence: mel[9],
            Pressure: mel[6],
            AmDepth: mel[2],
            FmDepth: mel[7],
            LfoRate: 0.22f,
            LfoDepth: mel[5],
            FilterCutoff: mel[10],
            FilterResonance: mel[11],
            MelSpectralEnvelope: mel);

    private static VocalTractControlTarget SmallTarget(IReadOnlyList<float> mel) =>
        new(
            TongueBody: 0.55f,
            TongueTip: 0.45f,
            LipAperture: 0.70f,
            LipRounding: 0.08f,
            Velum: 0.05f,
            GlottalTenseness: 0.48f,
            Turbulence: 0.20f,
            Pressure: 0.62f,
            AmDepth: 0.10f,
            FmDepth: 0.12f,
            LfoRate: 0.22f,
            LfoDepth: 0.08f,
            FilterCutoff: 0.54f,
            FilterResonance: 0.20f,
            MelSpectralEnvelope: mel);

    private static float AverageLoss(SpeechBackpropagationPipeline pipeline, IReadOnlyList<SpeechBackpropagationTrainingExample> examples)
    {
        var loss = 0f;
        foreach (var example in examples)
        {
            var prediction = pipeline.Predict(example);
            loss += MeanSquaredError(
                prediction.ToVector(pipeline.SynthDriver.MelBandCount),
                example.Target.ToVector(pipeline.SynthDriver.MelBandCount));
        }

        return loss / examples.Count;
    }

    private static float MeanSquaredError(IReadOnlyList<float> actual, IReadOnlyList<float> target)
    {
        var loss = 0f;
        for (var index = 0; index < actual.Count; index++)
        {
            var difference = actual[index] - target[index];
            loss += difference * difference;
        }

        return loss / actual.Count;
    }
}
