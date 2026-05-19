namespace AquaSynth.Dsl;

public sealed record PackedNeuralTrainingExample(
    IReadOnlyList<float> Input,
    IReadOnlyList<float> Target);

public sealed record PackedNeuralTrainingOptions(
    int Epochs = 200,
    float LearningRate = 0.01f,
    int Seed = 1337,
    PackedNeuralOptimizer Optimizer = PackedNeuralOptimizer.Adam,
    int BatchSize = 32,
    float Beta1 = 0.9f,
    float Beta2 = 0.999f,
    float Epsilon = 0.00000001f,
    bool Shuffle = true);

public enum PackedNeuralOptimizer
{
    Sgd,
    Adam
}

public sealed record PackedNeuralTrainingStep(int Epoch, float Loss);

public sealed record PackedNeuralTrainingResult(
    PackedNeuralNetwork Network,
    IReadOnlyList<PackedNeuralTrainingStep> Steps);

public sealed record PackedNeuralBackpropagation(
    float Loss,
    float[] Output,
    float[] InputGradient);

public sealed class PackedNeuralNetwork
{
    private readonly Layer[] layers;

    private PackedNeuralNetwork(int inputSize, int outputSize, Layer[] layers)
    {
        InputSize = inputSize;
        OutputSize = outputSize;
        this.layers = layers;
        HiddenLayerSizes = layers.Take(layers.Length - 1).Select(layer => layer.OutputSize).ToArray();
    }

    public int InputSize { get; }

    public int OutputSize { get; }

    public IReadOnlyList<int> HiddenLayerSizes { get; }

    public static PackedNeuralNetwork Create(
        int inputSize,
        int outputSize,
        IReadOnlyList<int>? hiddenLayerSizes = null,
        int seed = 1337)
    {
        if (inputSize <= 0) throw new ArgumentOutOfRangeException(nameof(inputSize), "input size must be positive");
        if (outputSize <= 0) throw new ArgumentOutOfRangeException(nameof(outputSize), "output size must be positive");

        hiddenLayerSizes ??= [64, 64, 32];
        var random = new Random(seed);
        var sizes = new List<int> { inputSize };
        sizes.AddRange(hiddenLayerSizes);
        sizes.Add(outputSize);

        var layers = new Layer[sizes.Count - 1];
        for (var index = 0; index < layers.Length; index++)
        {
            var activation = index == layers.Length - 1
                ? Activation.Sigmoid
                : Activation.Tanh;
            layers[index] = Layer.Create(sizes[index], sizes[index + 1], activation, random);
        }

        return new PackedNeuralNetwork(inputSize, outputSize, layers);
    }

    public float[] Predict(IReadOnlyList<float> input)
    {
        if (input.Count != InputSize)
        {
            throw new ArgumentException($"input must have {InputSize} values", nameof(input));
        }

        var scratch = TrainingScratch.Create(layers, InputSize, OutputSize);
        Copy(input, scratch.Activations[0]);
        Forward(scratch);
        return scratch.Output.ToArray();
    }

    public PackedNeuralTrainingResult Train(
        IReadOnlyList<PackedNeuralTrainingExample> examples,
        PackedNeuralTrainingOptions? options = null)
    {
        if (examples.Count == 0)
        {
            throw new ArgumentException("at least one training example is required", nameof(examples));
        }

        options ??= new PackedNeuralTrainingOptions();
        Validate(options);

        var steps = new List<PackedNeuralTrainingStep>();
        var order = Enumerable.Range(0, examples.Count).ToArray();
        var random = new Random(options.Seed);
        var scratch = TrainingScratch.Create(layers, InputSize, OutputSize);
        var gradients = LayerBuffers.Create(layers);
        var adamState = options.Optimizer == PackedNeuralOptimizer.Adam
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
                    CopyChecked(example.Input, scratch.Activations[0], "input");
                    CopyChecked(example.Target, scratch.Target, "target");
                    Forward(scratch);
                    loss += MeanSquaredError(scratch.Output, scratch.Target);
                    AccumulateGradients(scratch, gradients);
                }

                updateStep++;
                var scale = 1f / (batchEnd - batchStart);
                if (options.Optimizer == PackedNeuralOptimizer.Adam)
                {
                    adamState!.ApplyAdam(layers, gradients, scale, options.LearningRate, options.Beta1, options.Beta2, options.Epsilon, updateStep);
                }
                else
                {
                    ApplySgd(gradients, scale, options.LearningRate);
                }
            }

            steps.Add(new PackedNeuralTrainingStep(epoch + 1, loss / examples.Count));
        }

        return new PackedNeuralTrainingResult(this, steps);
    }

    public PackedNeuralBackpropagation TrainSingle(
        IReadOnlyList<float> input,
        IReadOnlyList<float> target,
        float learningRate)
    {
        if (learningRate <= 0) throw new ArgumentOutOfRangeException(nameof(learningRate), "learning rate must be positive");
        if (target.Count != OutputSize) throw new ArgumentException($"target must have {OutputSize} values", nameof(target));

        var outputGradient = new float[OutputSize];
        var scratch = TrainingScratch.Create(layers, InputSize, OutputSize);
        CopyChecked(input, scratch.Activations[0], "input");
        Copy(target, scratch.Target);
        Forward(scratch);
        var loss = MeanSquaredError(scratch.Output, scratch.Target);
        for (var index = 0; index < outputGradient.Length; index++)
        {
            outputGradient[index] = 2f * (scratch.Output[index] - scratch.Target[index]) / Math.Max(1, OutputSize);
        }

        return TrainSingleFromOutputGradient(scratch, outputGradient, learningRate, loss);
    }

    public PackedNeuralBackpropagation TrainSingleFromOutputGradient(
        IReadOnlyList<float> input,
        IReadOnlyList<float> outputGradient,
        float learningRate)
    {
        if (learningRate <= 0) throw new ArgumentOutOfRangeException(nameof(learningRate), "learning rate must be positive");
        if (outputGradient.Count != OutputSize) throw new ArgumentException($"output gradient must have {OutputSize} values", nameof(outputGradient));

        var scratch = TrainingScratch.Create(layers, InputSize, OutputSize);
        CopyChecked(input, scratch.Activations[0], "input");
        Forward(scratch);
        return TrainSingleFromOutputGradient(scratch, outputGradient, learningRate, 0);
    }

    private static void Validate(PackedNeuralTrainingOptions options)
    {
        if (options.Epochs <= 0) throw new ArgumentOutOfRangeException(nameof(options), "epoch count must be positive");
        if (options.LearningRate <= 0) throw new ArgumentOutOfRangeException(nameof(options), "learning rate must be positive");
        if (options.BatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(options), "batch size must be positive");
    }

    private void Forward(TrainingScratch scratch)
    {
        for (var index = 0; index < layers.Length; index++)
        {
            layers[index].Forward(scratch.Activations[index], scratch.Activations[index + 1]);
        }
    }

    private PackedNeuralBackpropagation TrainSingleFromOutputGradient(
        TrainingScratch scratch,
        IReadOnlyList<float> outputGradient,
        float learningRate,
        float loss)
    {
        var gradients = LayerBuffers.Create(layers);
        AccumulateGradientsFromOutputGradient(scratch, outputGradient, gradients);
        var inputGradient = InputGradient(scratch);
        ApplySgd(gradients, 1f, learningRate);
        return new PackedNeuralBackpropagation(loss, scratch.Output.ToArray(), inputGradient);
    }

    private void AccumulateGradients(TrainingScratch scratch, LayerBuffers gradients)
    {
        var outputGradient = new float[OutputSize];
        for (var index = 0; index < outputGradient.Length; index++)
        {
            outputGradient[index] = 2f * (scratch.Output[index] - scratch.Target[index]);
        }

        AccumulateGradientsFromOutputGradient(scratch, outputGradient, gradients);
    }

    private void AccumulateGradientsFromOutputGradient(TrainingScratch scratch, IReadOnlyList<float> outputGradient, LayerBuffers gradients)
    {
        var lastLayerIndex = layers.Length - 1;
        var outputDelta = scratch.Deltas[lastLayerIndex];
        var output = scratch.Activations[^1];
        for (var index = 0; index < outputDelta.Length; index++)
        {
            var actual = output[index];
            outputDelta[index] = outputGradient[index] * layers[lastLayerIndex].Derivative(actual);
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

    private float[] InputGradient(TrainingScratch scratch)
    {
        var inputGradient = new float[InputSize];
        layers[0].BackpropagateInputGradient(scratch.Deltas[0], inputGradient);
        return inputGradient;
    }

    private void ApplySgd(LayerBuffers gradients, float scale, float learningRate)
    {
        for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
        {
            layers[layerIndex].ApplySgd(gradients.WeightBuffers[layerIndex], gradients.BiasBuffers[layerIndex], scale, learningRate);
        }
    }

    private static void CopyChecked(IReadOnlyList<float> source, float[] target, string name)
    {
        if (source.Count != target.Length)
        {
            throw new ArgumentException($"{name} must have {target.Length} values");
        }

        Copy(source, target);
    }

    private static void Copy(IReadOnlyList<float> source, float[] target)
    {
        for (var index = 0; index < target.Length; index++)
        {
            target[index] = source[index];
        }
    }

    private static void Shuffle(int[] values, Random random)
    {
        for (var index = values.Length - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
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

        public void BackpropagateInputGradient(float[] nextDelta, float[] inputGradient)
        {
            unsafe
            {
                fixed (float* nextDeltaBase = nextDelta)
                fixed (float* inputGradientBase = inputGradient)
                fixed (float* weightBase = Weights)
                {
                    for (var input = 0; input < InputSize; input++)
                    {
                        var sum = 0f;
                        for (var output = 0; output < OutputSize; output++)
                        {
                            sum += weightBase[output * InputSize + input] * nextDeltaBase[output];
                        }

                        inputGradientBase[input] = sum;
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
