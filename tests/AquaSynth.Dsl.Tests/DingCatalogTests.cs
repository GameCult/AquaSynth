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

    // tools/aqua-feel measures the lowered files in tools/aqua-feel/lowered-dings; regenerate them with
    // `dotnet run --project tools/AquaLower -- --dings tools/aqua-feel/lowered-dings` when the catalog changes.
    [Fact]
    public void LoweredDingsFedToTheFeelMeterMatchTheCatalog()
    {
        var dir = Path.Combine(RepositoryRoot(), "tools", "aqua-feel", "lowered-dings");
        foreach (var instrument in DingCatalog.Instruments.Values)
        {
            var expected = FaustEmitter.EmitScript(instrument.Script, new FaustExportOptions("dings_" + instrument.Id.Replace('-', '_'))).Source;
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

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AquaSynth.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("AquaSynth.sln not found above the test binaries.");
    }
}
