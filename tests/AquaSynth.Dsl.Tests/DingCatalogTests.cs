using System.Text.Json;
using AquaSynth.Dings;

namespace AquaSynth.Dsl.Tests;

public sealed class DingCatalogTests
{
    [Fact]
    public void CuratedInstrumentsStayDistinctAndInsidePleasantnessEnvelope() =>
        Assert.Empty(DingCatalog.ValidateCuration());

    [Fact]
    public void MinimalDingsVocabularyIsComplete()
    {
        string[] expected = ["session.start", "task.acknowledge", "task.complete", "task.error", "input.required", "resource.limit", "user.spam", "session.end", "task.progress"];
        Assert.Equal(expected.Order(), DingCatalog.Events.Keys.Order());
    }

    [Fact]
    public void InstrumentsUseOneSemanticPitchControlAndNoNoise()
    {
        foreach (var instrument in DingCatalog.Instruments.Values)
        {
            Assert.Contains("/ding/frequency", instrument.Script, StringComparison.Ordinal);
            Assert.DoesNotContain("wave=noise", instrument.Script, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void MultiNoteMotifsUseNonUrgentOnsetSpacing()
    {
        foreach (var dingEvent in DingCatalog.Events.Values)
        foreach (var pair in dingEvent.Notes.Zip(dingEvent.Notes.Skip(1)))
            Assert.InRange(pair.Second.DelayMilliseconds - pair.First.DelayMilliseconds, 180, 320);
    }

    // Independent oracle: literals captured from the pre-DingCatalog.Identity derivation in AquaDaemonClient/AquaLower.
    // Session ids, compile keys and store file names hang off these; they must not move without the deferred revision decision.
    private static readonly Dictionary<string, (string PatchId, string FaustName)> Pinned = new()
    {
        ["warm-bell"] = ("dings.warm-bell", "dings_warm_bell"),
        ["glass-chime"] = ("dings.glass-chime", "dings_glass_chime"),
        ["soft-triangle"] = ("dings.soft-triangle", "dings_soft_triangle"),
        ["muted-pluck"] = ("dings.muted-pluck", "dings_muted_pluck"),
    };
    private static Dictionary<string, string> PinnedFaustNames => Pinned.ToDictionary(x => x.Key, x => x.Value.FaustName);

    [Fact]
    public void IdentityPinsTheOldDerivedValuesForAllFourInstruments()
    {
        Assert.Equal(Pinned.Keys.Order(), DingCatalog.Instruments.Keys.Order());
        foreach (var instrument in DingCatalog.Instruments.Values)
        {
            var identity = DingCatalog.Identity(instrument);
            Assert.Equal(Pinned[instrument.Id].PatchId, identity.PatchId);
            Assert.Equal(Pinned[instrument.Id].FaustName, identity.FaustName);
            Assert.Equal(1, identity.Revision);
        }
    }

    // tools/aqua-feel measures the lowered files in tools/aqua-feel/lowered-dings; regenerate them with
    // `dotnet run --project tools/AquaLower -- --dings tools/aqua-feel/lowered-dings` when the catalog changes.
    [Fact]
    public void LoweredDingsFedToTheFeelMeterMatchTheCatalog()
    {
        var dir = Path.Combine(RepositoryRoot(), "tools", "aqua-feel", "lowered-dings");
        foreach (var instrument in DingCatalog.Instruments.Values)
        {
            var expected = FaustEmitter.EmitScript(instrument.Script, new FaustExportOptions(PinnedFaustNames[instrument.Id])).Source;
            Assert.Equal(expected, File.ReadAllText(Path.Combine(dir, instrument.Id + ".dsp")));
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "dings.json")));
        var instruments = doc.RootElement.GetProperty("instruments").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!);
        Assert.Equal(DingCatalog.Instruments.Keys.Order(), instruments.Keys.Order());
        foreach (var instrument in DingCatalog.Instruments.Values)
            Assert.Equal(instrument.RootFrequency, instruments[instrument.Id].GetProperty("rootFrequency").GetSingle());
        var events = doc.RootElement.GetProperty("events").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!);
        Assert.Equal(DingCatalog.Events.Keys.Order(), events.Keys.Order());
        foreach (var ev in DingCatalog.Events.Values)
        {
            var notes = events[ev.Id].GetProperty("notes").EnumerateArray().Select(n => (n.GetProperty("semitones").GetDouble(), n.GetProperty("delayMilliseconds").GetInt32(), n.GetProperty("gain").GetSingle())).ToArray();
            Assert.Equal(ev.Notes.Select(n => (n.Semitones, n.DelayMilliseconds, n.Gain)).ToArray(), notes);
        }
    }

    // Negative check on callers: reverting either call site to an inline rule leaves the catalog tests green, so read the sources.
    [Theory]
    [InlineData("tools/AquaSynthDingsHost/AquaDaemonClient.cs")]
    [InlineData("tools/AquaLower/Program.cs")]
    public void CallersDeriveIdentityOnlyThroughTheCatalog(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));
        Assert.Contains("DingCatalog.Identity(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"dings_\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$\"dings", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AquaSynth.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("AquaSynth.sln not found above the test binaries.");
    }
}
