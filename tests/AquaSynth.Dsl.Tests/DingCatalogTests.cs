using AquaSynthDingsMcp;

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
}
