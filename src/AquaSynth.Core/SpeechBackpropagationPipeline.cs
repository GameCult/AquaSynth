namespace AquaSynth.Dsl;

public sealed record SpeechBackpropagationTrainingExample(
    UtteranceEmbeddingInput UtteranceInput,
    PhoneticEvent Event,
    VocalTractControlTarget Target);

public sealed record SpeechBackpropagationTrainingOptions(
    int Epochs = 200,
    float UtteranceLearningRate = 0.01f,
    float SynthDriverLearningRate = 0.01f,
    int Seed = 1337,
    bool Shuffle = true);

public sealed record SpeechBackpropagationTrainingStep(int Epoch, float Loss);

public sealed record SpeechBackpropagationTrainingResult(
    SpeechBackpropagationPipeline Pipeline,
    IReadOnlyList<SpeechBackpropagationTrainingStep> Steps);

public sealed class SpeechBackpropagationPipeline(
    UtteranceEmbeddingNeuralEncoder utteranceEncoder,
    VocalTractNeuralMapper synthDriver)
{
    public UtteranceEmbeddingNeuralEncoder UtteranceEncoder { get; } = utteranceEncoder;

    public VocalTractNeuralMapper SynthDriver { get; } = synthDriver;

    public SpeechBackpropagationTrainingResult Train(
        IReadOnlyList<SpeechBackpropagationTrainingExample> examples,
        SpeechBackpropagationTrainingOptions? options = null)
    {
        if (examples.Count == 0)
        {
            throw new ArgumentException("at least one training example is required", nameof(examples));
        }

        options ??= new SpeechBackpropagationTrainingOptions();
        if (options.Epochs <= 0) throw new ArgumentOutOfRangeException(nameof(options), "epoch count must be positive");
        if (options.UtteranceLearningRate <= 0) throw new ArgumentOutOfRangeException(nameof(options), "utterance learning rate must be positive");
        if (options.SynthDriverLearningRate <= 0) throw new ArgumentOutOfRangeException(nameof(options), "synth-driver learning rate must be positive");

        var order = Enumerable.Range(0, examples.Count).ToArray();
        var random = new Random(options.Seed);
        var steps = new List<SpeechBackpropagationTrainingStep>();

        for (var epoch = 0; epoch < options.Epochs; epoch++)
        {
            if (options.Shuffle)
            {
                Shuffle(order, random);
            }

            var loss = 0f;
            foreach (var index in order)
            {
                loss += TrainSingle(examples[index], options);
            }

            steps.Add(new SpeechBackpropagationTrainingStep(epoch + 1, loss / examples.Count));
        }

        return new SpeechBackpropagationTrainingResult(this, steps);
    }

    public VocalTractControlTarget Predict(SpeechBackpropagationTrainingExample example)
    {
        var embedding = UtteranceEncoder.Encode(example.UtteranceInput).ToSemanticEmbedding();
        return SynthDriver.Predict(example.Event, embedding);
    }

    private float TrainSingle(SpeechBackpropagationTrainingExample example, SpeechBackpropagationTrainingOptions options)
    {
        var utteranceFeatures = example.UtteranceInput.ToFeatureVector();
        var embedding = UtteranceEncoder.Encode(utteranceFeatures);
        var synthResult = SynthDriver.TrainSingle(
            example.Event,
            embedding.ToSemanticEmbedding(),
            example.Target,
            options.SynthDriverLearningRate);
        var embeddingGradient = SemanticEmbeddingGradient(synthResult.InputGradient);
        UtteranceEncoder.TrainSingleFromOutputGradient(utteranceFeatures, embeddingGradient, options.UtteranceLearningRate);
        return synthResult.Loss;
    }

    private float[] SemanticEmbeddingGradient(IReadOnlyList<float> synthInputGradient)
    {
        if (synthInputGradient.Count != SynthDriver.InputSize)
        {
            throw new InvalidOperationException("synth input gradient length does not match synth driver input size");
        }

        var gradient = new float[SynthDriver.SemanticEmbeddingSize];
        var offset = SynthDriver.InputSize - SynthDriver.SemanticEmbeddingSize;
        for (var index = 0; index < gradient.Length; index++)
        {
            gradient[index] = synthInputGradient[offset + index];
        }

        return gradient;
    }

    private static void Shuffle(int[] values, Random random)
    {
        for (var index = values.Length - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }
}
