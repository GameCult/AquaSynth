using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

public sealed class SpeechDistributedTrainingTests
{
    [Fact]
    public async Task SpeechRenderRequestsAndResultsRoundTripThroughCultCache()
    {
        var directory = TempDirectory();
        try
        {
            var requestStore = Path.Combine(directory, "speech-requests.cc");
            var resultStore = Path.Combine(directory, "speech-results.cc");
            var pipeline = Pipeline();
            var examples = TrainingSet();

            var requests = SpeechDistributedTrainingCoordinator.CreateRenderRequests("batch-a", pipeline, examples);
            await SpeechDistributedTrainingCultCacheStore.UpsertRequestsAsync(requestStore, requests);
            var loadedRequests = await SpeechDistributedTrainingCultCacheStore.ReadRequestsAsync(requestStore);
            var results = SpeechDistributedTrainingCoordinator.ScoreControlVectorRequests(loadedRequests, "worker-manycore-01");
            await SpeechDistributedTrainingCultCacheStore.UpsertResultsAsync(resultStore, results);
            var loadedResults = await SpeechDistributedTrainingCultCacheStore.ReadResultsAsync(resultStore);

            Assert.Equal(examples.Count, loadedRequests.Count);
            Assert.Equal(examples.Count, loadedResults.Count);
            Assert.All(loadedResults, result => Assert.Equal(SpeechRenderStatus.Succeeded, result.Status));
            Assert.All(loadedResults, result => Assert.Equal(pipeline.SynthDriver.OutputSize, result.OutputGradient.Length));
            Assert.Contains(loadedRequests, request => request.RendererProfileId == "compiled-faust-worker");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SyncedSpeechScoresDriveBackpropagationAndCheckpoint()
    {
        var directory = TempDirectory();
        try
        {
            var requestStore = Path.Combine(directory, "speech-requests.cc");
            var resultStore = Path.Combine(directory, "speech-results.cc");
            var checkpointStore = Path.Combine(directory, "speech-checkpoints.cc");
            var pipeline = Pipeline();
            var examples = TrainingSet();
            var before = AverageLoss(pipeline, examples);

            var requests = SpeechDistributedTrainingCoordinator.CreateRenderRequests("batch-train", pipeline, examples);
            await SpeechDistributedTrainingCultCacheStore.UpsertRequestsAsync(requestStore, requests);

            var workerRequests = await SpeechDistributedTrainingCultCacheStore.ReadRequestsAsync(requestStore);
            var workerResults = SpeechDistributedTrainingCoordinator.ScoreControlVectorRequests(workerRequests, "worker-manycore-01");
            await SpeechDistributedTrainingCultCacheStore.UpsertResultsAsync(resultStore, workerResults);

            var trainerRequests = await SpeechDistributedTrainingCultCacheStore.ReadRequestsAsync(requestStore);
            var trainerResults = await SpeechDistributedTrainingCultCacheStore.ReadResultsAsync(resultStore);
            var applied = SpeechDistributedTrainingCoordinator.ApplyResults(
                pipeline,
                trainerRequests,
                trainerResults,
                "checkpoint-0001",
                new SpeechDistributedTrainingOptions(
                    UtteranceLearningRate: 0.18f,
                    SynthDriverLearningRate: 0.18f));
            await SpeechDistributedTrainingCultCacheStore.UpsertCheckpointsAsync(checkpointStore, [applied.Checkpoint]);

            var after = AverageLoss(pipeline, examples);
            var checkpoints = await SpeechDistributedTrainingCultCacheStore.ReadCheckpointsAsync(checkpointStore);

            Assert.True(after < before, $"expected synced score gradients to reduce loss; before={before}, after={after}");
            Assert.Equal(examples.Count, applied.Checkpoint.AppliedResultCount);
            Assert.Single(checkpoints);
            Assert.Equal("checkpoint-0001", checkpoints[0].CheckpointId);
            Assert.All(applied.Backpropagations, backprop => Assert.True(backprop.InputGradient.Length > 0));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SpeechBackpropagationPipeline Pipeline()
    {
        var utteranceEncoder = UtteranceEmbeddingNeuralEncoder.Create(
            inputSize: 10,
            embeddingSize: 8,
            hiddenLayerSizes: [24, 16],
            seed: 701);
        var synthDriver = VocalTractNeuralMapper.Create(
            semanticEmbeddingSize: 8,
            melBandCount: 6,
            hiddenLayerSizes: [32, 24],
            seed: 709);
        return new SpeechBackpropagationPipeline(utteranceEncoder, synthDriver);
    }

    private static IReadOnlyList<SpeechBackpropagationTrainingExample> TrainingSet() =>
    [
        Example(
            "a",
            new PhoneticFeatures(PhoneticManner.Vowel, Height: VowelHeight.Open, Backness: VowelBackness.Front),
            new UtteranceEmbeddingInput([0.92f, 0.20f, 0.35f, 0.70f], [0.88f, 0.32f, 0.20f], [0.65f, 0.20f, 0.44f]),
            Target([0.82f, 0.74f, 0.66f, 0.58f, 0.50f, 0.42f])),
        Example(
            "sa",
            new PhoneticFeatures(PhoneticManner.Fricative, PhoneticPlace.Alveolar, Phonation.Voiceless),
            new UtteranceEmbeddingInput([0.35f, 0.42f, 0.90f, 0.44f], [0.70f, 0.92f, 0.75f], [0.28f, 0.60f, 0.82f]),
            Target([0.12f, 0.16f, 0.22f, 0.34f, 0.48f, 0.62f])),
        Example(
            "ma",
            new PhoneticFeatures(PhoneticManner.Nasal, PhoneticPlace.Bilabial, Phonation.Voiced, Nasalized: true),
            new UtteranceEmbeddingInput([0.72f, 0.52f, 0.24f, 0.64f], [0.62f, 0.40f, 0.32f], [0.74f, 0.80f, 0.36f]),
            Target([0.68f, 0.72f, 0.70f, 0.62f, 0.54f, 0.46f]))
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
            TongueTip: mel[4],
            LipAperture: mel[0],
            LipRounding: 0.08f,
            Velum: mel[1],
            GlottalTenseness: mel[2],
            Turbulence: mel[5],
            Pressure: mel[3],
            AmDepth: mel[1],
            FmDepth: mel[2],
            LfoRate: 0.22f,
            LfoDepth: mel[4],
            FilterCutoff: mel[5],
            FilterResonance: 0.18f,
            MelSpectralEnvelope: mel);

    private static float AverageLoss(SpeechBackpropagationPipeline pipeline, IReadOnlyList<SpeechBackpropagationTrainingExample> examples)
    {
        var loss = 0f;
        foreach (var example in examples)
        {
            loss += MeanSquaredError(
                pipeline.Predict(example).ToVector(pipeline.SynthDriver.MelBandCount),
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

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aquasynth-speech-distributed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
