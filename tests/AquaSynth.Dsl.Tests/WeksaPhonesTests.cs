using System.Text.RegularExpressions;
using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

// Fixtures/Weksa/nibu-en-US.phones.json is Weksa PR 16's fixture (see PROVENANCE.txt); the malformed cases below are
// edits of it, not invented phones.
public sealed class WeksaPhonesTests
{
    private static string Fixture() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Weksa", "nibu-en-US.phones.json"));

    private static string Line(string phones, string extra = "") =>
        $$"""{"language":"en-US","source":"ˈnibu",{{extra}}"phones":{{phones}}}""";

    private static string Ph(string ipa, string stress) => $$"""{"ipa":"{{ipa}}","stress":"{{stress}}"}""";

    [Fact]
    public void AcceptsNibuFixtureAsNIBUWithPrimaryStressOnI()
    {
        var word = WeksaPhonesReader.Parse(Fixture());
        Assert.Equal("en-US", word.Language);
        Assert.Equal("ˈnibu", word.Source);
        Assert.Equal(["n", "i", "b", "u"], word.Phones.Select(p => p.Ipa));
        Assert.Equal([WeksaStress.None, WeksaStress.Primary, WeksaStress.None, WeksaStress.None], word.Phones.Select(p => p.Stress));
        var plan = WeksaWordPlan.From(word, "nibu");
        Assert.True(plan.Renderable);
        Assert.Equal(["n", "i", "b", "u"], plan.Phones.Select(p => p.Target.Ipa));
    }

    public static IEnumerable<object[]> Refused()
    {
        yield return ["not json at all"];
        yield return [Fixture0().Replace("}]}", "}]")];                                  // truncated
        yield return [Fixture0() + " trailing"];                                           // trailing garbage
        yield return ["null"];
        yield return [Line($"[{Ph("n", "tertiary")}]")];                                  // unknown stress value
        yield return [Line($"[{Ph("n", "Primary")}]")];                                   // stress is exact, not case-folded
        yield return [Line("""[{"ipa":"n"}]""")];                                         // missing stress
        yield return [Line("""[{"stress":"none"}]""")];                                   // missing ipa
        yield return ["""{"language":"en-US","phones":[{"ipa":"n","stress":"none"}]}"""]; // missing source
        yield return ["""{"source":"n","phones":[{"ipa":"n","stress":"none"}]}"""];       // missing language
        yield return ["""{"language":"en-US","source":"n"}"""];                           // missing phones
        yield return [Line($"[{Ph("n", "none")}]", "\"extra\":1,")];                      // extra top-level key
        yield return [Line("""[{"ipa":"n","stress":"none","dur":90}]""")];                // extra phone key
        yield return [Line("""[{"ipa":5,"stress":"none"}]""")];                           // wrong type
        yield return [Line("""[{"ipa":"n","stress":1}]""")];                              // stress as number
        yield return [Line("""[{"ipa":null,"stress":"none"}]""")];
        yield return [Line("""[{"ipa":"n","stress":null}]""")];
        yield return [Line("""[null]""")];
        yield return [Line("""[{"ipa":"","stress":"none"}]""")];
        yield return [Line("{}")];                                                         // phones not a list
        yield return [Line("[]")];                                                         // empty phones
    }

    private static string Fixture0() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Weksa", "nibu-en-US.phones.json")).Trim();

    [Theory]
    [MemberData(nameof(Refused))]
    public void RefusesMalformedInput(string json) => Assert.Throws<WeksaPhonesException>(() => WeksaPhonesReader.Parse(json));

    [Fact]
    public void ReportsPhonesTheTractCannotMakeAndRendersNothing()
    {
        var word = WeksaPhonesReader.Parse(Line($"[{Ph("n", "none")},{Ph("ɪ", "primary")},{Ph("b", "none")},{Ph("ʊ", "none")}]"));
        var plan = WeksaWordPlan.From(word, "x");
        Assert.False(plan.Renderable);
        Assert.Equal(["ɪ", "ʊ"], plan.Unsupported.Select(p => p.Ipa));
        Assert.Empty(plan.Phones);
    }

    [Fact]
    public void StressMovesOnlyTheIntensityOfTheMarkedPhone()
    {
        string Script(WeksaStress onI) => IpaGestureExperiment.BuildWordScript(
            WeksaWordPlan.From(new WeksaPhones("en-US", "ˈnibu",
                [new("n", WeksaStress.None), new("i", onI), new("b", WeksaStress.None), new("u", WeksaStress.None)]), "w").Phones.ToArray());

        var none = Script(WeksaStress.None).Split('\n');
        var primary = Script(WeksaStress.Primary).Split('\n');
        Assert.Equal(none.Length, primary.Length);
        var differing = Enumerable.Range(0, none.Length).Where(i => none[i] != primary[i]).ToArray();
        Assert.Single(differing);
        Assert.Contains("ipa=i ", primary[differing[0]]);
        var strip = (string l) => Regex.Replace(l, @"intensity=\S+", "");
        Assert.Equal(strip(none[differing[0]]), strip(primary[differing[0]]));
        Assert.Contains("intensity=1.2", primary[differing[0]]);
    }

    [Fact]
    public void PhonesTheWordPlanAcceptsAreExactlyTheDslTable()
    {
        foreach (var ipa in new[] { "n", "i", "b", "u", "a", "ə" })
            Assert.True(PatchScript.TryIpaDescriptor(ipa, out _));
        Assert.False(PatchScript.TryIpaDescriptor("ɪ", out _));
    }
}
