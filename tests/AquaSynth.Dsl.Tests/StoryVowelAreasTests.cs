using System.Globalization;
using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

// StoryVowelAreas.cs is the code owner of Story (2008) Table I; docs/research/data/story2008-area-functions.csv is the cited copy.
// Nothing here says how any vowel sounds: these tests guard the transcription, the descriptor lookup and the sample-rate threading.
public sealed class StoryVowelAreasTests
{
    private static string RepoFile(params string[] relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AquaSynth.sln")) || Directory.Exists(Path.Combine(dir.FullName, "docs", "research")))
            {
                return Path.Combine([dir.FullName, .. relative]);
            }
        }

        throw new InvalidOperationException("repository root not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void EveryCellOfTheCodeTableEqualsTheCitedCsv()
    {
        var lines = File.ReadAllLines(RepoFile("docs", "research", "data", "story2008-area-functions.csv"))
            .Where(line => !line.StartsWith('#') && line.Length > 0).ToArray();
        var header = lines[0].Split(',');
        Assert.Equal("section", header[0]);
        Assert.Equal(StoryVowelAreas.All.Select(v => v.Ipa), header.Skip(1));
        var sections = lines.Skip(1).Where(line => char.IsDigit(line[0])).Select(line => line.Split(',')).ToArray();
        Assert.Equal(StoryVowelAreas.Sections, sections.Length);
        for (var v = 0; v < StoryVowelAreas.All.Count; v++)
        {
            Assert.Equal(StoryVowelAreas.Sections, StoryVowelAreas.All[v].Areas.Count);
            for (var s = 0; s < sections.Length; s++)
            {
                Assert.Equal(float.Parse(sections[s][v + 1], CultureInfo.InvariantCulture), StoryVowelAreas.All[v].Areas[s]);
            }
        }
    }

    [Fact]
    public void AsciiIdsAreUniqueAndDescriptorsSelectTheirVowelExactly()
    {
        Assert.Equal(11, StoryVowelAreas.All.Select(v => v.Id).Distinct().Count());
        Assert.Equal(11, StoryVowelAreas.All.Select(v => v.Descriptor).Distinct().Count());
        foreach (var vowel in StoryVowelAreas.All)
        {
            Assert.True(vowel.Descriptor.EndsWith("_vowel", StringComparison.Ordinal));
            Assert.Equal(vowel.Id, StoryVowelAreas.IdForDescriptor(vowel.Descriptor));
            Assert.True(PatchScript.TryIpaDescriptor(vowel.Ipa, out var descriptor));
            Assert.Equal(vowel.Descriptor, descriptor);
        }

        Assert.Equal("ah", StoryVowelAreas.IdForDescriptor("voiced_open_back_unrounded_vowel"));
        PatchScript.TryIpaDescriptor("a", out var a);
        Assert.Equal("ah", StoryVowelAreas.IdForDescriptor(a));
        // The schwa and a vowel descriptor with no Story shape both select no vowel: the geometric-mean shape.
        PatchScript.TryIpaDescriptor("ə", out var schwa);
        Assert.Null(StoryVowelAreas.IdForDescriptor(schwa));
        Assert.Null(StoryVowelAreas.IdForDescriptor("voiced_close_central_unrounded_vowel"));
    }

    [Fact]
    public void ResamplingInterpolatesBetweenSectionCentresAndHoldsTheEnds()
    {
        float[] areas = [1, 3, 5, 7];
        Assert.Equal(1, StoryVowelAreas.AreaAt(areas, 0));
        Assert.Equal(7, StoryVowelAreas.AreaAt(areas, 1));
        Assert.Equal(3, StoryVowelAreas.AreaAt(areas, 0.375f), 4);
        Assert.Equal(4, StoryVowelAreas.AreaAt(areas, 0.5f), 4);
    }
}
