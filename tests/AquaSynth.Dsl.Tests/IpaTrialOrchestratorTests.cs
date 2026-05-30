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
                [new SpeechTimingReceipt("ipa-trial-render-score", "2026-05-29T12:00:00.0000000Z", 3, 20, .5f, "test")],
                [new PrimitiveTimelineFact("contact_release_peak", "contact:contact", "released_flow", .12f, "flow", 2, 2, "release witness")]);

            await IpaTrialResultCultCacheStore.UpsertResultsAsync(store, [result]);
            var loaded = await IpaTrialResultCultCacheStore.ReadResultsAsync(store);

            var single = Assert.Single(loaded);
            Assert.Equal("trial-a", single.TrialId);
            Assert.Equal("candidate-a", single.CandidateId);
            Assert.Contains(single.Metrics, metric => metric.Name == "log_mel_cosine");
            Assert.Contains(single.KnownLies, lie => lie.Contains("not an IPA sample", StringComparison.Ordinal));
            Assert.Contains(single.TimelineFacts, fact => fact.Name == "contact_release_peak" && fact.Value > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PrimitiveTimelineFactsExtractPhaseWitnesses()
    {
        var timeline = new[]
        {
            new ProbeTimelineSample(0, "contact:contact", "opening", .1f),
            new ProbeTimelineSample(1, "contact:contact", "opening", .8f),
            new ProbeTimelineSample(0, "contact:contact", "reservoir", .4f),
            new ProbeTimelineSample(1, "contact:contact", "released_flow", .3f),
            new ProbeTimelineSample(0, "source:folds", "flow", .2f),
            new ProbeTimelineSample(1, "branch:velopharynx", "admittance", .15f),
            new ProbeTimelineSample(1, "radiation:mouth", "output", .7f),
            new ProbeTimelineSample(0, "path:oral_path", "passivity_ratio", .99f),
            new ProbeTimelineSample(1, "path:oral_path", "energy_in", .5f),
            new ProbeTimelineSample(1, "path:oral_path", "energy_out", .45f),
        };

        var facts = PrimitiveTimelineFactExtractor.Extract(timeline);

        Assert.Contains(facts, fact => fact.Name == "contact_closed_blocks" && fact.Value == 1);
        Assert.Contains(facts, fact => fact.Name == "contact_release_peak" && Math.Abs(fact.Value - .3f) < .0001f);
        Assert.Contains(facts, fact => fact.Name == "contact_release_peak_block" && fact.Value == 1);
        Assert.Contains(facts, fact => fact.Name == "branch_admittance_peak" && Math.Abs(fact.Value - .15f) < .0001f);
        Assert.Contains(facts, fact => fact.Name == "radiation_output_peak" && Math.Abs(fact.Value - .7f) < .0001f);
        Assert.Contains(facts, fact => fact.Name == "path_passivity_max" && Math.Abs(fact.Value - .99f) < .0001f);
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
        Assert.Equal(25, result.TrialResults.Count);
        Assert.Equal(5, result.TrialResults.Select(item => item.TargetSetId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(result.TrialResults, trial => Assert.NotEmpty(trial.Metrics));
        Assert.Contains(result.TrialResults, trial => trial.Metrics.Any(metric => metric.Name == "log_mel_cosine"));

        var loaded = await IpaTrialResultCultCacheStore.ReadResultsAsync(result.TrialResultStorePath);
        Assert.Equal(result.TrialResults.Count, loaded.Count);
    }

    [Fact]
    public async Task IpaTrialOrchestratorScoresAgentAuthoredCandidateScriptsWhenEnabled()
    {
        if (Environment.GetEnvironmentVariable("AQUASYNTH_RUN_IPA_TRIALS") != "1")
        {
            return;
        }

        if (FaustCompiler.FindFaust() is null)
        {
            return;
        }

        var directory = TempDirectory();
        try
        {
            var script = IpaGestureExperiment.BuildCandidateScript(
                new IpaGestureExperimentTarget("a", "a", "voiced_open_back_unrounded_vowel", .12f),
                new IpaGestureExperimentVariant("agent-smoke"));
            var scriptPath = Path.Combine(directory, "a__agent-smoke.aqua");
            await File.WriteAllTextAsync(scriptPath, script);

            var result = await IpaTrialOrchestrator.RunCandidateScriptsAsync(
                Path.Combine(directory, "artifacts"),
                [new IpaTrialScriptCandidate("a", "a__agent-smoke", scriptPath, "test-agent", "score externally authored candidate")],
                options: new IpaTrialOrchestrationOptions(BatchId: "agent-smoke"));

            var single = Assert.Single(result.TrialResults);
            Assert.Equal("a__agent_smoke", single.CandidateId);
            Assert.Contains(single.Metrics, metric => metric.Name == "log_mel_cosine");
            Assert.True(File.Exists(single.CandidatePatchUri));
            Assert.True(File.Exists(result.TrialResultStorePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
