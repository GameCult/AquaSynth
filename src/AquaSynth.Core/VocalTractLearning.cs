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
    float LearningRate = 0.03f,
    int Seed = 1337);

public sealed record VocalTractTrainingStep(int Epoch, float Loss);

public sealed record VocalTractTrainingResult(
    VocalTractNeuralMapper Mapper,
    IReadOnlyList<VocalTractTrainingStep> Steps);

public sealed class VocalTractNeuralMapper
{
    private const int PhoneticFeatureCount = 48;
    private const int BaseOutputCount = 14;
    private const float NeutralMelBand = 0.5f;
    private readonly Layer[] layers;

    private VocalTractNeuralMapper(int semanticEmbeddingSize, int melBandCount, Layer[] layers)
    {
        SemanticEmbeddingSize = semanticEmbeddingSize;
        MelBandCount = melBandCount;
        this.layers = layers;
    }

    public int SemanticEmbeddingSize { get; }

    public int MelBandCount { get; }

    public int InputSize => PhoneticFeatureCount + 4 + SemanticEmbeddingSize;

    public int OutputSize => BaseOutputCount + MelBandCount;

    public IReadOnlyList<int> HiddenLayerSizes => layers.Take(layers.Length - 1).Select(layer => layer.OutputSize).ToArray();

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
        var random = new Random(seed);
        var sizes = new List<int> { PhoneticFeatureCount + 4 + semanticEmbeddingSize };
        sizes.AddRange(hiddenLayerSizes);
        sizes.Add(BaseOutputCount + melBandCount);

        var layers = new Layer[sizes.Count - 1];
        for (var index = 0; index < layers.Length; index++)
        {
            var activation = index == layers.Length - 1
                ? Activation.Sigmoid
                : Activation.Tanh;
            layers[index] = Layer.Create(sizes[index], sizes[index + 1], activation, random);
        }

        return new VocalTractNeuralMapper(semanticEmbeddingSize, melBandCount, layers);
    }

    public VocalTractControlTarget Predict(PhoneticEvent phoneticEvent, VocalTractSemanticEmbedding semanticEmbedding)
    {
        var output = Forward(BuildInput(phoneticEvent, semanticEmbedding));
        return TargetFromVector(output[^1]);
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
        var steps = new List<VocalTractTrainingStep>();
        for (var epoch = 0; epoch < options.Epochs; epoch++)
        {
            var loss = 0f;
            foreach (var example in examples)
            {
                var input = BuildInput(example.Event, example.SemanticEmbedding);
                var activations = Forward(input);
                var target = VectorFromTarget(example.Target);
                loss += MeanSquaredError(activations[^1], target);
                Backward(activations, target, options.LearningRate);
            }

            steps.Add(new VocalTractTrainingStep(epoch + 1, loss / examples.Count));
        }

        return new VocalTractTrainingResult(this, steps);
    }

    public float[] EncodeInput(PhoneticEvent phoneticEvent, VocalTractSemanticEmbedding semanticEmbedding) =>
        BuildInput(phoneticEvent, semanticEmbedding);

    private float[][] Forward(float[] input)
    {
        var activations = new float[layers.Length + 1][];
        activations[0] = input;
        for (var index = 0; index < layers.Length; index++)
        {
            activations[index + 1] = layers[index].Forward(activations[index]);
        }

        return activations;
    }

    private void Backward(float[][] activations, float[] target, float learningRate)
    {
        var deltas = new float[layers.Length][];
        var lastLayerIndex = layers.Length - 1;
        deltas[lastLayerIndex] = new float[layers[lastLayerIndex].OutputSize];
        for (var output = 0; output < deltas[lastLayerIndex].Length; output++)
        {
            var actual = activations[^1][output];
            deltas[lastLayerIndex][output] = 2f * (actual - target[output]) * layers[lastLayerIndex].Derivative(actual);
        }

        for (var layerIndex = lastLayerIndex - 1; layerIndex >= 0; layerIndex--)
        {
            var layer = layers[layerIndex];
            var next = layers[layerIndex + 1];
            deltas[layerIndex] = new float[layer.OutputSize];
            for (var output = 0; output < layer.OutputSize; output++)
            {
                var sum = 0f;
                for (var nextOutput = 0; nextOutput < next.OutputSize; nextOutput++)
                {
                    sum += next.Weights[nextOutput][output] * deltas[layerIndex + 1][nextOutput];
                }

                deltas[layerIndex][output] = sum * layer.Derivative(activations[layerIndex + 1][output]);
            }
        }

        for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
        {
            layers[layerIndex].ApplyGradient(activations[layerIndex], deltas[layerIndex], learningRate);
        }
    }

    private float[] BuildInput(PhoneticEvent phoneticEvent, VocalTractSemanticEmbedding semanticEmbedding)
    {
        if (semanticEmbedding.Values.Count != SemanticEmbeddingSize)
        {
            throw new ArgumentException($"semantic embedding must have {SemanticEmbeddingSize} values", nameof(semanticEmbedding));
        }

        var input = new float[InputSize];
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

        return input;
    }

    private static int OneHot(float[] input, int offset, int value, int count)
    {
        input[offset + Math.Clamp(value, 0, count - 1)] = 1;
        return offset + count;
    }

    private float[] VectorFromTarget(VocalTractControlTarget target) =>
        target.ToVector(MelBandCount, NeutralMelBand);

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

    private static float MeanSquaredError(IReadOnlyList<float> actual, IReadOnlyList<float> target)
    {
        var sum = 0f;
        for (var index = 0; index < actual.Count; index++)
        {
            var difference = actual[index] - target[index];
            sum += difference * difference;
        }

        return sum / actual.Count;
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0, 1);

    private sealed class Layer
    {
        private readonly Activation activation;

        private Layer(float[][] weights, float[] biases, Activation activation)
        {
            Weights = weights;
            Biases = biases;
            this.activation = activation;
        }

        public float[][] Weights { get; }

        public float[] Biases { get; }

        public int OutputSize => Biases.Length;

        public static Layer Create(int inputSize, int outputSize, Activation activation, Random random)
        {
            var scale = MathF.Sqrt(2f / Math.Max(1, inputSize));
            var weights = new float[outputSize][];
            for (var output = 0; output < outputSize; output++)
            {
                weights[output] = new float[inputSize];
                for (var input = 0; input < inputSize; input++)
                {
                    weights[output][input] = ((float)random.NextDouble() * 2f - 1f) * scale;
                }
            }

            return new Layer(weights, new float[outputSize], activation);
        }

        public float[] Forward(IReadOnlyList<float> input)
        {
            var output = new float[OutputSize];
            for (var row = 0; row < OutputSize; row++)
            {
                var sum = Biases[row];
                for (var column = 0; column < input.Count; column++)
                {
                    sum += Weights[row][column] * input[column];
                }

                output[row] = Activate(sum);
            }

            return output;
        }

        public float Derivative(float activated) =>
            activation switch
            {
                Activation.Sigmoid => activated * (1f - activated),
                Activation.Tanh => 1f - activated * activated,
                _ => 1
            };

        public void ApplyGradient(IReadOnlyList<float> input, IReadOnlyList<float> delta, float learningRate)
        {
            for (var output = 0; output < OutputSize; output++)
            {
                for (var column = 0; column < input.Count; column++)
                {
                    Weights[output][column] -= learningRate * delta[output] * input[column];
                }

                Biases[output] -= learningRate * delta[output];
            }
        }

        private float Activate(float value) =>
            activation switch
            {
                Activation.Sigmoid => 1f / (1f + MathF.Exp(-value)),
                Activation.Tanh => MathF.Tanh(value),
                _ => value
            };
    }

    private enum Activation
    {
        Tanh,
        Sigmoid
    }
}

public static class VocalTractControlTargetExtensions
{
    public static float[] ToVector(this VocalTractControlTarget target, int melBandCount = 0, float neutralMelBand = 0.5f)
    {
        var output = new float[14 + melBandCount];
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

        return output;
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0, 1);
}
