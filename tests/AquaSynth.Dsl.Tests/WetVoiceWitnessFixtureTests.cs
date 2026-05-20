using System.Text.Json;

namespace AquaSynth.Dsl.Tests;

public sealed class WetVoiceWitnessFixtureTests
{
    [Fact]
    public void WetVoiceWitnessFixtureCarriesInspectableStageArtifacts()
    {
        var root = Path.Combine(RepositoryRoot(), "tests", "AquaSynth.Dsl.Tests", "Fixtures", "Speech", "WetVoice01");
        Assert.True(Directory.Exists(root), "wet-voice-01 fixture root");

        var requiredFiles = new[]
        {
            "README.md",
            "structured-utterance.schema.json",
            "structured-utterance.json",
            "automation-trace.json",
            "notes.json",
            Path.Combine("render", "RENDER.md")
        };

        foreach (var relativePath in requiredFiles)
        {
            Assert.True(File.Exists(Path.Combine(root, relativePath)), $"missing witness artifact `{relativePath}`");
        }
    }

    [Fact]
    public void WetVoiceWitnessDefinesBoundaryAndPinnedMetadata()
    {
        var root = Path.Combine(RepositoryRoot(), "tests", "AquaSynth.Dsl.Tests", "Fixtures", "Speech", "WetVoice01");

        using var utterance = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "structured-utterance.json")));
        using var automation = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "automation-trace.json")));
        using var notes = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "notes.json")));

        Assert.Equal("wet-voice-01", utterance.RootElement.GetProperty("witnessId").GetString());
        Assert.Equal("Weksa", utterance.RootElement.GetProperty("provenance").GetProperty("semanticOwner").GetString());
        Assert.True(utterance.RootElement.GetProperty("segments").GetArrayLength() > 0, "structured utterance segments");

        Assert.Equal("AquaSynth", automation.RootElement.GetProperty("ownedBy").GetString());
        Assert.True(automation.RootElement.GetProperty("automationStages").GetArrayLength() > 0, "automation stages");
        Assert.True(automation.RootElement.GetProperty("events").GetArrayLength() > 0, "automation events");

        Assert.Equal("topic_040d9807-2efa-4f89-acce-ca05300bfc2a", notes.RootElement.GetProperty("topicId").GetString());
        Assert.True(notes.RootElement.TryGetProperty("timing", out _), "timing notes");
        Assert.True(notes.RootElement.TryGetProperty("confidence", out _), "confidence notes");
        Assert.True(notes.RootElement.TryGetProperty("provenance", out _), "provenance notes");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AquaSynth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("could not find repository root");
    }
}
