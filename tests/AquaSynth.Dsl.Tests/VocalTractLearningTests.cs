using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

public sealed class VocalTractLearningTests
{
    [Fact]
    public void NeuralMapperEncodesPhoneticInputAndSemanticEmbedding()
    {
        var mapper = VocalTractNeuralMapper.Create(semanticEmbeddingSize: 12, hiddenLayerSizes: [32, 24, 16], seed: 7);
        var input = mapper.EncodeInput(
            new PhoneticEvent(
                "phone-a",
                "a",
                new PhoneticFeatures(
                    PhoneticManner.Vowel,
                    Height: VowelHeight.Open,
                    Backness: VowelBackness.Front),
                DurationSeconds: 0.2,
                Prosody: new PhoneticProsody(Stress: 0.5f, PitchTarget: 0.25f, Intensity: 0.75f)),
            new VocalTractSemanticEmbedding(Enumerable.Range(0, 12).Select(index => index / 12f).ToArray()));

        Assert.Equal(64, input.Length);
        Assert.Equal(32, mapper.MelBandCount);
        Assert.Equal(46, mapper.OutputSize);
        Assert.Equal([32, 24, 16], mapper.HiddenLayerSizes);
        Assert.All(input, value => Assert.InRange(value, -1f, 1f));
    }

    [Fact]
    public void NeuralMapperTrainingReducesLossForTinyVowelSet()
    {
        var mapper = VocalTractNeuralMapper.Create(semanticEmbeddingSize: 16, hiddenLayerSizes: [48, 32, 24], seed: 11);
        var examples = TinyTrainingSet();

        var before = AverageLoss(mapper, examples);
        var result = mapper.Train(examples, new VocalTractTrainingOptions(Epochs: 260, LearningRate: 0.05f, Seed: 11));
        var after = AverageLoss(mapper, examples);

        Assert.Equal(260, result.Steps.Count);
        Assert.True(after < before * 0.35f, $"expected loss to drop hard; before={before}, after={after}");
        Assert.True(result.Steps[^1].Loss < result.Steps[0].Loss, "training loss should decrease across epochs");
    }

    [Fact]
    public void SemanticEmbeddingCanSteerSamePhoneToDifferentTractTargets()
    {
        var mapper = VocalTractNeuralMapper.Create(semanticEmbeddingSize: 16, hiddenLayerSizes: [64, 48, 32], seed: 19);
        var softContext = Embedding(16, 0.15f, 0.2f);
        var hardContext = Embedding(16, 0.85f, -0.15f);
        var eventA = new PhoneticEvent(
            "phone-a",
            "a",
            new PhoneticFeatures(
                PhoneticManner.Vowel,
                Height: VowelHeight.Open,
                Backness: VowelBackness.Front),
            DurationSeconds: 0.2);

        var examples = new[]
        {
            new VocalTractTrainingExample(eventA, softContext, new VocalTractControlTarget(0.78f, 0.35f, 0.90f, 0.05f, 0.0f, 0.42f, 0.0f, 0.35f, AmDepth: 0.12f, FmDepth: 0.08f, LfoRate: 0.18f, LfoDepth: 0.10f, FilterCutoff: 0.62f, FilterResonance: 0.08f)),
            new VocalTractTrainingExample(eventA, hardContext, new VocalTractControlTarget(0.54f, 0.70f, 0.42f, 0.10f, 0.0f, 0.82f, 0.0f, 0.72f, AmDepth: 0.44f, FmDepth: 0.36f, LfoRate: 0.52f, LfoDepth: 0.41f, FilterCutoff: 0.38f, FilterResonance: 0.55f))
        };

        mapper.Train(examples, new VocalTractTrainingOptions(Epochs: 400, LearningRate: 0.04f, Seed: 19));
        var soft = mapper.Predict(eventA, softContext);
        var hard = mapper.Predict(eventA, hardContext);

        Assert.True(soft.LipAperture > hard.LipAperture + 0.20f, $"soft aperture={soft.LipAperture}, hard aperture={hard.LipAperture}");
        Assert.True(hard.GlottalTenseness > soft.GlottalTenseness + 0.20f, $"hard tenseness={hard.GlottalTenseness}, soft tenseness={soft.GlottalTenseness}");
        Assert.True(hard.Pressure > soft.Pressure + 0.15f, $"hard pressure={hard.Pressure}, soft pressure={soft.Pressure}");
        Assert.True(hard.FmDepth > soft.FmDepth + 0.15f, $"hard FM={hard.FmDepth}, soft FM={soft.FmDepth}");
        Assert.True(hard.FilterResonance > soft.FilterResonance + 0.20f, $"hard resonance={hard.FilterResonance}, soft resonance={soft.FilterResonance}");
    }

    [Fact]
    public void NeuralMapperCanLearnExpressiveModulatorAndFilterControls()
    {
        var mapper = VocalTractNeuralMapper.Create(semanticEmbeddingSize: 16, hiddenLayerSizes: [64, 48, 32], seed: 31);
        var fricative = new PhoneticEvent(
            "phone-s",
            "s",
            new PhoneticFeatures(PhoneticManner.Fricative, PhoneticPlace.Alveolar, Phonation.Voiceless),
            DurationSeconds: 0.12,
            Prosody: new PhoneticProsody(Intensity: 0.9f));
        var still = Embedding(16, -0.45f, 0.05f);
        var animated = Embedding(16, 0.55f, 0.30f);
        var examples = new[]
        {
            new VocalTractTrainingExample(fricative, still, new VocalTractControlTarget(0.42f, 0.82f, 0.18f, 0.0f, 0.0f, 0.04f, 0.92f, 0.64f, AmDepth: 0.04f, FmDepth: 0.02f, LfoRate: 0.08f, LfoDepth: 0.03f, FilterCutoff: 0.72f, FilterResonance: 0.25f)),
            new VocalTractTrainingExample(fricative, animated, new VocalTractControlTarget(0.35f, 0.90f, 0.12f, 0.0f, 0.0f, 0.08f, 0.98f, 0.84f, AmDepth: 0.62f, FmDepth: 0.48f, LfoRate: 0.70f, LfoDepth: 0.58f, FilterCutoff: 0.42f, FilterResonance: 0.76f))
        };

        mapper.Train(examples, new VocalTractTrainingOptions(Epochs: 420, LearningRate: 0.04f, Seed: 31));
        var stillPrediction = mapper.Predict(fricative, still);
        var animatedPrediction = mapper.Predict(fricative, animated);

        Assert.True(animatedPrediction.AmDepth > stillPrediction.AmDepth + 0.25f);
        Assert.True(animatedPrediction.FmDepth > stillPrediction.FmDepth + 0.20f);
        Assert.True(animatedPrediction.LfoDepth > stillPrediction.LfoDepth + 0.20f);
        Assert.True(animatedPrediction.FilterResonance > stillPrediction.FilterResonance + 0.25f);
        Assert.True(stillPrediction.FilterCutoff > animatedPrediction.FilterCutoff + 0.15f);
    }

    [Fact]
    public void NeuralMapperCanLearnMelSpectralEnvelopeControls()
    {
        var mapper = VocalTractNeuralMapper.Create(semanticEmbeddingSize: 16, melBandCount: 12, hiddenLayerSizes: [72, 48, 32], seed: 43);
        var vowel = new PhoneticEvent(
            "phone-a",
            "a",
            new PhoneticFeatures(PhoneticManner.Vowel, Height: VowelHeight.Open, Backness: VowelBackness.Front),
            DurationSeconds: 0.2,
            Prosody: new PhoneticProsody(Intensity: 0.8f));
        var darkContext = Embedding(16, -0.60f, 0.10f);
        var brightContext = Embedding(16, 0.60f, 0.12f);
        var darkEnvelope = MelEnvelope(12, low: 0.86f, mid: 0.52f, high: 0.18f);
        var brightEnvelope = MelEnvelope(12, low: 0.22f, mid: 0.58f, high: 0.90f);
        var examples = new[]
        {
            new VocalTractTrainingExample(vowel, darkContext, new VocalTractControlTarget(0.68f, 0.34f, 0.72f, 0.10f, 0.0f, 0.48f, 0.0f, 0.42f, MelSpectralEnvelope: darkEnvelope)),
            new VocalTractTrainingExample(vowel, brightContext, new VocalTractControlTarget(0.58f, 0.52f, 0.56f, 0.02f, 0.0f, 0.54f, 0.0f, 0.46f, MelSpectralEnvelope: brightEnvelope))
        };

        mapper.Train(examples, new VocalTractTrainingOptions(Epochs: 520, LearningRate: 0.04f, Seed: 43));
        var dark = mapper.Predict(vowel, darkContext);
        var bright = mapper.Predict(vowel, brightContext);

        Assert.NotNull(dark.MelSpectralEnvelope);
        Assert.NotNull(bright.MelSpectralEnvelope);
        Assert.Equal(12, dark.MelSpectralEnvelope.Count);
        Assert.True(dark.MelSpectralEnvelope[1] > bright.MelSpectralEnvelope[1] + 0.25f);
        Assert.True(bright.MelSpectralEnvelope[^2] > dark.MelSpectralEnvelope[^2] + 0.25f);
    }

    private static IReadOnlyList<VocalTractTrainingExample> TinyTrainingSet() =>
    [
        new(
            new PhoneticEvent(
                "a",
                "a",
                new PhoneticFeatures(PhoneticManner.Vowel, Height: VowelHeight.Open, Backness: VowelBackness.Front),
                DurationSeconds: 0.20,
                Prosody: new PhoneticProsody(Intensity: 0.8f)),
            Embedding(16, 0.20f, 0.10f),
            new VocalTractControlTarget(0.78f, 0.30f, 0.88f, 0.04f, 0.0f, 0.52f, 0.0f, 0.42f, AmDepth: 0.10f, FmDepth: 0.04f, LfoRate: 0.12f, LfoDepth: 0.06f, FilterCutoff: 0.62f, FilterResonance: 0.10f, MelSpectralEnvelope: MelEnvelope(32, 0.52f, 0.60f, 0.42f))),
        new(
            new PhoneticEvent(
                "i",
                "i",
                new PhoneticFeatures(PhoneticManner.Vowel, Height: VowelHeight.Close, Backness: VowelBackness.Front),
                DurationSeconds: 0.18,
                Prosody: new PhoneticProsody(Intensity: 0.7f)),
            Embedding(16, -0.15f, 0.35f),
            new VocalTractControlTarget(0.25f, 0.82f, 0.24f, 0.02f, 0.0f, 0.50f, 0.0f, 0.38f, AmDepth: 0.08f, FmDepth: 0.05f, LfoRate: 0.16f, LfoDepth: 0.05f, FilterCutoff: 0.80f, FilterResonance: 0.06f, MelSpectralEnvelope: MelEnvelope(32, 0.32f, 0.78f, 0.66f))),
        new(
            new PhoneticEvent(
                "u",
                "u",
                new PhoneticFeatures(PhoneticManner.Vowel, Height: VowelHeight.Close, Backness: VowelBackness.Back, Rounded: true),
                DurationSeconds: 0.22,
                Prosody: new PhoneticProsody(Intensity: 0.72f)),
            Embedding(16, 0.40f, -0.25f),
            new VocalTractControlTarget(0.18f, 0.26f, 0.22f, 0.88f, 0.0f, 0.48f, 0.0f, 0.36f, AmDepth: 0.18f, FmDepth: 0.12f, LfoRate: 0.22f, LfoDepth: 0.14f, FilterCutoff: 0.48f, FilterResonance: 0.18f, MelSpectralEnvelope: MelEnvelope(32, 0.72f, 0.46f, 0.22f))),
        new(
            new PhoneticEvent(
                "s",
                "s",
                new PhoneticFeatures(PhoneticManner.Fricative, PhoneticPlace.Alveolar, Phonation.Voiceless),
                DurationSeconds: 0.12,
                Prosody: new PhoneticProsody(Intensity: 0.85f)),
            Embedding(16, -0.30f, -0.20f),
            new VocalTractControlTarget(0.42f, 0.86f, 0.18f, 0.0f, 0.0f, 0.05f, 0.92f, 0.70f, AmDepth: 0.45f, FmDepth: 0.35f, LfoRate: 0.55f, LfoDepth: 0.40f, FilterCutoff: 0.58f, FilterResonance: 0.62f, MelSpectralEnvelope: MelEnvelope(32, 0.18f, 0.56f, 0.92f)))
    ];

    private static float AverageLoss(VocalTractNeuralMapper mapper, IReadOnlyList<VocalTractTrainingExample> examples)
    {
        var loss = 0f;
        foreach (var example in examples)
        {
            var prediction = mapper.Predict(example.Event, example.SemanticEmbedding);
            loss += Loss(mapper, prediction, example.Target);
        }

        return loss / examples.Count;
    }

    private static float Loss(VocalTractNeuralMapper mapper, VocalTractControlTarget prediction, VocalTractControlTarget target)
    {
        var predicted = prediction.ToVector(mapper.MelBandCount);
        var expected = target.ToVector(mapper.MelBandCount);
        var loss = 0f;
        for (var index = 0; index < predicted.Length; index++)
        {
            var difference = predicted[index] - expected[index];
            loss += difference * difference;
        }

        return loss / predicted.Length;
    }

    private static VocalTractSemanticEmbedding Embedding(int size, float offset, float slope)
    {
        var values = new float[size];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = MathF.Tanh(offset + slope * index / Math.Max(1, size - 1));
        }

        return new VocalTractSemanticEmbedding(values);
    }

    private static float[] MelEnvelope(int size, float low, float mid, float high)
    {
        var values = new float[size];
        for (var index = 0; index < values.Length; index++)
        {
            var position = index / Math.Max(1f, size - 1f);
            values[index] = position < 0.5f
                ? Lerp(low, mid, position * 2f)
                : Lerp(mid, high, (position - 0.5f) * 2f);
        }

        return values;
    }

    private static float Lerp(float left, float right, float amount) =>
        left + (right - left) * amount;
}
