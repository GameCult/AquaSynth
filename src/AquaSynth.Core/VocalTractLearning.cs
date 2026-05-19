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
    private readonly Layer[] layers;
    private readonly int[] hiddenLayerSizes;

    private VocalTractNeuralMapper(int semanticEmbeddingSize, int melBandCount, Layer[] layers)
    {
        SemanticEmbeddingSize = semanticEmbeddingSize;
        MelBandCount = melBandCount;
        this.layers = layers;
        hiddenLayerSizes = layers.Take(layers.Length - 1).Select(layer => layer.OutputSize).ToArray();
    }

    public int SemanticEmbeddingSize { get; }

    public int MelBandCount { get; }

    public int InputSize => PhoneticFeatureCount + 4 + SemanticEmbeddingSize;

    public int OutputSize => BaseOutputCount + MelBandCount;

    public IReadOnlyList<int> HiddenLayerSizes => hiddenLayerSizes;

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
        var scratch = TrainingScratch.Create(layers, InputSize, OutputSize);
        EncodeInput(phoneticEvent, semanticEmbedding, scratch.Activations[0]);
        Forward(scratch);
        return TargetFromVector(scratch.Output);
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
        Validate(options);

        var steps = new List<VocalTractTrainingStep>();
        var order = Enumerable.Range(0, examples.Count).ToArray();
        var random = new Random(options.Seed);
        var scratch = TrainingScratch.Create(layers, InputSize, OutputSize);
        var gradients = LayerBuffers.Create(layers);
        var adamState = options.Optimizer == VocalTractOptimizer.Adam
            ? OptimizerState.Create(layers)
            : null;
        var updateStep = 0;

        for (var epoch = 0; epoch < options.Epochs; epoch++)
        {
            if (options.Shuffle)
            {
                Shuffle(order, random);
            }

            var loss = 0f;
            for (var batchStart = 0; batchStart < order.Length; batchStart += options.BatchSize)
            {
                var batchEnd = Math.Min(batchStart + options.BatchSize, order.Length);
                gradients.Clear();

                for (var orderIndex = batchStart; orderIndex < batchEnd; orderIndex++)
                {
                    var example = examples[order[orderIndex]];
                    EncodeInput(example.Event, example.SemanticEmbedding, scratch.Activations[0]);
                    FillTarget(example.Target, scratch.Target);
                    Forward(scratch);
                    loss += MeanSquaredError(scratch.Output, scratch.Target);
                    AccumulateGradients(scratch, gradients);
                }

                updateStep++;
                var scale = 1f / (batchEnd - batchStart);
                if (options.Optimizer == VocalTractOptimizer.Adam)
                {
                    adamState!.ApplyAdam(layers, gradients, scale, options.LearningRate, options.Beta1, options.Beta2, options.Epsilon, updateStep);
                }
                else
                {
                    ApplySgd(gradients, scale, options.LearningRate);
                }
            }

            steps.Add(new VocalTractTrainingStep(epoch + 1, loss / examples.Count));
        }

        return new VocalTractTrainingResult(this, steps);
    }

    public float[] EncodeInput(PhoneticEvent phoneticEvent, VocalTractSemanticEmbedding semanticEmbedding)
    {
        var input = new float[InputSize];
        EncodeInput(phoneticEvent, semanticEmbedding, input);
        return input;
    }

    private static void Validate(VocalTractTrainingOptions options)
    {
        if (options.Epochs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "epoch count must be positive");
        }

        if (options.LearningRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "learning rate must be positive");
        }

        if (options.BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "batch size must be positive");
        }
    }

    private void Forward(TrainingScratch scratch)
    {
        for (var index = 0; index < layers.Length; index++)
        {
            layers[index].Forward(scratch.Activations[index], scratch.Activations[index + 1]);
        }
    }

    private void AccumulateGradients(TrainingScratch scratch, LayerBuffers gradients)
    {
        var lastLayerIndex = layers.Length - 1;
        var outputDelta = scratch.Deltas[lastLayerIndex];
        var output = scratch.Activations[^1];
        for (var index = 0; index < outputDelta.Length; index++)
        {
            var actual = output[index];
            outputDelta[index] = 2f * (actual - scratch.Target[index]) * layers[lastLayerIndex].Derivative(actual);
        }

        for (var layerIndex = lastLayerIndex - 1; layerIndex >= 0; layerIndex--)
        {
            layers[layerIndex].BackpropagateDelta(layers[layerIndex + 1], scratch.Deltas[layerIndex + 1], scratch.Activations[layerIndex + 1], scratch.Deltas[layerIndex]);
        }

        for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
        {
            gradients.Accumulate(layerIndex, scratch.Activations[layerIndex], scratch.Deltas[layerIndex]);
        }
    }

    private void ApplySgd(LayerBuffers gradients, float scale, float learningRate)
    {
        for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
        {
            layers[layerIndex].ApplySgd(gradients.WeightBuffers[layerIndex], gradients.BiasBuffers[layerIndex], scale, learningRate);
        }
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

    private void FillTarget(VocalTractControlTarget target, float[] output) =>
        target.ToVector(output, MelBandCount, NeutralMelBand);

    private static int OneHot(float[] input, int offset, int value, int count)
    {
        input[offset + Math.Clamp(value, 0, count - 1)] = 1;
        return offset + count;
    }

    private static void Shuffle(int[] values, Random random)
    {
        for (var index = values.Length - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
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

    private sealed class TrainingScratch
    {
        private TrainingScratch(float[][] activations, float[][] deltas, float[] target)
        {
            Activations = activations;
            Deltas = deltas;
            Target = target;
        }

        public float[][] Activations { get; }

        public float[][] Deltas { get; }

        public float[] Target { get; }

        public float[] Output => Activations[^1];

        public static TrainingScratch Create(IReadOnlyList<Layer> layers, int inputSize, int outputSize)
        {
            var activations = new float[layers.Count + 1][];
            var deltas = new float[layers.Count][];
            activations[0] = new float[inputSize];
            for (var index = 0; index < layers.Count; index++)
            {
                activations[index + 1] = new float[layers[index].OutputSize];
                deltas[index] = new float[layers[index].OutputSize];
            }

            return new TrainingScratch(activations, deltas, new float[outputSize]);
        }
    }

    private sealed class LayerBuffers
    {
        private LayerBuffers(float[][] weightBuffers, float[][] biasBuffers)
        {
            WeightBuffers = weightBuffers;
            BiasBuffers = biasBuffers;
        }

        public float[][] WeightBuffers { get; }

        public float[][] BiasBuffers { get; }

        public static LayerBuffers Create(IReadOnlyList<Layer> layers)
        {
            var weights = new float[layers.Count][];
            var biases = new float[layers.Count][];
            for (var index = 0; index < layers.Count; index++)
            {
                weights[index] = new float[layers[index].Weights.Length];
                biases[index] = new float[layers[index].Biases.Length];
            }

            return new LayerBuffers(weights, biases);
        }

        public void Clear()
        {
            for (var index = 0; index < WeightBuffers.Length; index++)
            {
                Array.Clear(WeightBuffers[index]);
                Array.Clear(BiasBuffers[index]);
            }
        }

        public void Accumulate(int layerIndex, float[] input, float[] delta)
        {
            var weightGradients = WeightBuffers[layerIndex];
            var biasGradients = BiasBuffers[layerIndex];
            var inputSize = input.Length;

            unsafe
            {
                fixed (float* inputBase = input)
                fixed (float* deltaBase = delta)
                fixed (float* weightBase = weightGradients)
                fixed (float* biasBase = biasGradients)
                {
                    for (var output = 0; output < delta.Length; output++)
                    {
                        var deltaValue = deltaBase[output];
                        var weightRow = weightBase + output * inputSize;
                        for (var column = 0; column < inputSize; column++)
                        {
                            weightRow[column] += deltaValue * inputBase[column];
                        }

                        biasBase[output] += deltaValue;
                    }
                }
            }
        }
    }

    private sealed class Layer
    {
        private readonly Activation activation;

        private Layer(int inputSize, int outputSize, float[] weights, float[] biases, Activation activation)
        {
            InputSize = inputSize;
            OutputSize = outputSize;
            Weights = weights;
            Biases = biases;
            this.activation = activation;
        }

        public int InputSize { get; }

        public int OutputSize { get; }

        public float[] Weights { get; }

        public float[] Biases { get; }

        public static Layer Create(int inputSize, int outputSize, Activation activation, Random random)
        {
            var scale = MathF.Sqrt(2f / Math.Max(1, inputSize));
            var weights = new float[inputSize * outputSize];
            for (var index = 0; index < weights.Length; index++)
            {
                weights[index] = ((float)random.NextDouble() * 2f - 1f) * scale;
            }

            return new Layer(inputSize, outputSize, weights, new float[outputSize], activation);
        }

        public void Forward(float[] input, float[] output)
        {
            unsafe
            {
                fixed (float* inputBase = input)
                fixed (float* outputBase = output)
                fixed (float* weightBase = Weights)
                fixed (float* biasBase = Biases)
                {
                    for (var row = 0; row < OutputSize; row++)
                    {
                        var sum = biasBase[row];
                        var weightRow = weightBase + row * InputSize;
                        for (var column = 0; column < InputSize; column++)
                        {
                            sum += weightRow[column] * inputBase[column];
                        }

                        outputBase[row] = Activate(sum);
                    }
                }
            }
        }

        public void BackpropagateDelta(Layer nextLayer, float[] nextDelta, float[] activationValues, float[] delta)
        {
            unsafe
            {
                fixed (float* nextDeltaBase = nextDelta)
                fixed (float* activationBase = activationValues)
                fixed (float* deltaBase = delta)
                fixed (float* nextWeightBase = nextLayer.Weights)
                {
                    for (var output = 0; output < OutputSize; output++)
                    {
                        var sum = 0f;
                        for (var nextOutput = 0; nextOutput < nextLayer.OutputSize; nextOutput++)
                        {
                            sum += nextWeightBase[nextOutput * nextLayer.InputSize + output] * nextDeltaBase[nextOutput];
                        }

                        deltaBase[output] = sum * Derivative(activationBase[output]);
                    }
                }
            }
        }

        public float Derivative(float activated) =>
            activation switch
            {
                Activation.Sigmoid => activated * (1f - activated),
                Activation.Tanh => 1f - activated * activated,
                _ => 1
            };

        public void ApplySgd(float[] weightGradients, float[] biasGradients, float scale, float learningRate)
        {
            unsafe
            {
                fixed (float* weightBase = Weights)
                fixed (float* biasBase = Biases)
                fixed (float* weightGradientBase = weightGradients)
                fixed (float* biasGradientBase = biasGradients)
                {
                    for (var index = 0; index < Weights.Length; index++)
                    {
                        weightBase[index] -= learningRate * weightGradientBase[index] * scale;
                    }

                    for (var index = 0; index < Biases.Length; index++)
                    {
                        biasBase[index] -= learningRate * biasGradientBase[index] * scale;
                    }
                }
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

    private sealed class OptimizerState
    {
        private readonly LayerBuffers firstMoment;
        private readonly LayerBuffers secondMoment;

        private OptimizerState(LayerBuffers firstMoment, LayerBuffers secondMoment)
        {
            this.firstMoment = firstMoment;
            this.secondMoment = secondMoment;
        }

        public static OptimizerState Create(IReadOnlyList<Layer> layers) =>
            new(LayerBuffers.Create(layers), LayerBuffers.Create(layers));

        public void ApplyAdam(
            IReadOnlyList<Layer> layers,
            LayerBuffers gradients,
            float scale,
            float learningRate,
            float beta1,
            float beta2,
            float epsilon,
            int step)
        {
            var beta1Correction = 1f - MathF.Pow(beta1, step);
            var beta2Correction = 1f - MathF.Pow(beta2, step);

            for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                ApplyAdam(
                    layers[layerIndex].Weights,
                    gradients.WeightBuffers[layerIndex],
                    firstMoment.WeightBuffers[layerIndex],
                    secondMoment.WeightBuffers[layerIndex],
                    scale,
                    learningRate,
                    beta1,
                    beta2,
                    epsilon,
                    beta1Correction,
                    beta2Correction);
                ApplyAdam(
                    layers[layerIndex].Biases,
                    gradients.BiasBuffers[layerIndex],
                    firstMoment.BiasBuffers[layerIndex],
                    secondMoment.BiasBuffers[layerIndex],
                    scale,
                    learningRate,
                    beta1,
                    beta2,
                    epsilon,
                    beta1Correction,
                    beta2Correction);
            }
        }

        private static void ApplyAdam(
            float[] values,
            float[] gradients,
            float[] firstMoment,
            float[] secondMoment,
            float scale,
            float learningRate,
            float beta1,
            float beta2,
            float epsilon,
            float beta1Correction,
            float beta2Correction)
        {
            unsafe
            {
                fixed (float* valueBase = values)
                fixed (float* gradientBase = gradients)
                fixed (float* firstBase = firstMoment)
                fixed (float* secondBase = secondMoment)
                {
                    for (var index = 0; index < values.Length; index++)
                    {
                        var gradient = gradientBase[index] * scale;
                        firstBase[index] = beta1 * firstBase[index] + (1f - beta1) * gradient;
                        secondBase[index] = beta2 * secondBase[index] + (1f - beta2) * gradient * gradient;
                        var mHat = firstBase[index] / beta1Correction;
                        var vHat = secondBase[index] / beta2Correction;
                        valueBase[index] -= learningRate * mHat / (MathF.Sqrt(vHat) + epsilon);
                    }
                }
            }
        }
    }
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
