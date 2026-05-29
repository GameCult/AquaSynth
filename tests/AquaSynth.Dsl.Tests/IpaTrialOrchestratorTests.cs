using AquaSynth.Dsl;
using AquaSynth.Faust;

namespace AquaSynth.Dsl.Tests;

public sealed class IpaTrialOrchestratorTests
{
    [Fact]
    public async Task IpaTrialResultsRoundTripThroughCultCache()
    {
        var directory = TempDirectory();
        try
        {
            var store = Path.Combine(directory, "ipa-trial-results.cc");
            var result = new IpaTrialResult(
                "trial-a",
                "batch-a",
                "2026-05-29T12:00:00.0000000Z",
                "vowels",
                ["a"],
                "open-vowel",
                "candidate-a",
                "hypothesis-worker",
                "Try an open vowel descriptor against an open vowel fixture.",
                "candidate.aqua",
                "reference.wav",
                "candidate.wav",
                "timeline.csv",
                [new SpeechScoreMetric("log_mel_cosine", .42f, 1)],
                [new SpeechRenderArtifact("comparison-report", "report.txt", "sha256:test")],
                "science-evaluator",
                "Weak but measurable spectrogram pressure.",
                "pressure",
                ["fixture reference is not an IPA sample"],
                [new SpeechTimingReceipt("ipa-trial-render-score", "2026-05-29T12:00:00.0000000Z", 3, 20, .5f, "test")]);

            await IpaTrialResultCultCacheStore.UpsertResultsAsync(store, [result]);
            var loaded = await IpaTrialResultCultCacheStore.ReadResultsAsync(store);

            var single = Assert.Single(loaded);
            Assert.Equal("trial-a", single.TrialId);
            Assert.Equal("candidate-a", single.CandidateId);
            Assert.Contains(single.Metrics, metric => metric.Name == "log_mel_cosine");
            Assert.Contains(single.KnownLies, lie => lie.Contains("not an IPA sample", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task IpaTrialOrchestratorWritesFiveSeedTrialsWhenEnabled()
    {
        if (Environment.GetEnvironmentVariable("AQUASYNTH_RUN_IPA_TRIALS") != "1")
        {
            return;
        }

        if (FaustCompiler.FindFaust() is null)
        {
            return;
        }

        var artifactRoot = Path.Combine(
            RepositoryRoot(),
            "artifacts",
            "parity",
            "ipa-trials",
            DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff"));
        var result = await IpaTrialOrchestrator.RunAsync(
            artifactRoot,
            options: new IpaTrialOrchestrationOptions(BatchId: "five-seed-trials"));

        Assert.True(File.Exists(result.TrialResultStorePath));
        Assert.True(File.Exists(result.SummaryPath));
        Assert.True(File.Exists(result.EvaluatorReportPath));
        Assert.Equal(8, result.TrialResults.Count);
        Assert.Equal(5, result.TrialResults.Select(item => item.TargetSetId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(result.TrialResults, trial => Assert.NotEmpty(trial.Metrics));
        Assert.Contains(result.TrialResults, trial => trial.Metrics.Any(metric => metric.Name == "log_mel_cosine"));

        var loaded = await IpaTrialResultCultCacheStore.ReadResultsAsync(result.TrialResultStorePath);
        Assert.Equal(result.TrialResults.Count, loaded.Count);
    }

    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aquasynth-ipa-trial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AquaSynth.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Environment.CurrentDirectory;
    }
}
