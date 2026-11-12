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
        var word = WeksaPhonesReader.Parse(Line($"[{Ph("n", "none")},{Ph("ŋ", "primary")},{Ph("b", "none")},{Ph("θ", "none")}]"));
        var plan = WeksaWordPlan.From(word, "x");
        Assert.False(plan.Renderable);
        Assert.Equal(["ŋ", "θ"], plan.Unsupported.Select(p => p.Ipa));
        Assert.Empty(plan.Phones);
    }

    [Fact]
    public void WeksaWordPlanAcceptsT()
    {
        var word = WeksaPhonesReader.Parse(Line($"[{Ph("ɑ", "none")},{Ph("t", "none")},{Ph("ɑ", "none")}]"));
        var plan = WeksaWordPlan.From(word, "ata");
        Assert.True(plan.Renderable);
        Assert.Empty(plan.Unsupported);
        // t names a plosive, not the approximant GestureDescriptor falls back to for a phone the table lacks
        Assert.True(PatchScript.TryIpaDescriptor("t", out var descriptor));
        Assert.Equal("voiceless_alveolar_plosive", descriptor);
        var patch = PatchScript.Parse(IpaGestureExperiment.BuildWordScript(plan.Phones.ToArray(), 140));
        Assert.Contains(patch.ControlSplines, s => s.SurfacePath == "/vocal/contacts/0/release_flow" && s.Name.StartsWith("ata_1_t", StringComparison.Ordinal));
    }

    [Fact]
    public void WeksaWordPlanAcceptsK()
    {
        var word = WeksaPhonesReader.Parse(Line($"[{Ph("ɑ", "none")},{Ph("k", "none")},{Ph("ɑ", "none")}]"));
        var plan = WeksaWordPlan.From(word, "aka");
        Assert.True(plan.Renderable, string.Join(",", plan.Unsupported.Select(p => p.Ipa)));
        Assert.Empty(plan.Unsupported);
        Assert.True(PatchScript.TryIpaDescriptor("k", out var descriptor));
        Assert.Equal("voiceless_velar_plosive", descriptor);
        var patch = PatchScript.Parse(IpaGestureExperiment.BuildWordScript(plan.Phones.ToArray(), 140));
        Assert.Contains(patch.ControlSplines, s => s.SurfacePath == "/vocal/contacts/0/release_flow" && s.Name.StartsWith("aka_1_k", StringComparison.Ordinal));
    }

    [Fact]
    public void NearCloseVowelsRenderAsVowelsWithTheirOwnStoryShape()
    {
        var word = WeksaPhonesReader.Parse(Line($"[{Ph("b", "none")},{Ph("ɪ", "primary")},{Ph("n", "none")},{Ph("b", "none")},{Ph("ʊ", "none")}]"));
        var plan = WeksaWordPlan.From(word, "x");
        Assert.True(plan.Renderable);
        Assert.Equal([WeksaWordPlan.ConsonantSeconds, WeksaWordPlan.VowelSeconds * WeksaWordPlan.CueFor(WeksaStress.Primary).Duration, WeksaWordPlan.ConsonantSeconds, WeksaWordPlan.ConsonantSeconds, WeksaWordPlan.VowelSeconds],
            plan.Phones.Select(p => p.Target.DurationSeconds));
        var patch = PatchScript.Parse(IpaGestureExperiment.BuildWordScript(plan.Phones.ToArray(), 140));
        var ih = patch.ControlSplines.Where(s => s.Name.StartsWith("x_1_ɪ_vowel_", StringComparison.Ordinal)).ToDictionary(s => s.SurfacePath, s => s.Points[0].Value);
        Assert.Equal(1f, ih["/vocal/areas/0/area/vowel_ih"]);
        Assert.Equal(0f, ih["/vocal/areas/0/area/vowel_i"]);
        var oo = patch.ControlSplines.Where(s => s.Name.StartsWith("x_4_ʊ_vowel_", StringComparison.Ordinal)).ToDictionary(s => s.SurfacePath, s => s.Points[0].Value);
        Assert.Equal(1f, oo["/vocal/areas/0/area/vowel_oo_short"]);
        Assert.Equal(0f, oo["/vocal/areas/0/area/vowel_u"]);
    }

    private static WeksaWordPlan Plan(WeksaStress onI) => WeksaWordPlan.From(new WeksaPhones("en-US", "ˈnibu",
        [new("n", WeksaStress.None), new("i", onI), new("b", WeksaStress.None), new("u", WeksaStress.None)]), "w");

    [Fact]
    public void StressIsLongerLouderAndHigherOnTheMarkedPhoneOnly()
    {
        var none = Plan(WeksaStress.None).Phones;
        var primary = Plan(WeksaStress.Primary).Phones;
        var secondary = Plan(WeksaStress.Secondary).Phones;
        foreach (var k in new[] { 0, 2, 3 })
        {
            Assert.Equal(none[k].Target, primary[k].Target);
            Assert.Equal(none[k].Intensity, primary[k].Intensity);
        }

        Assert.Equal(WeksaWordPlan.VowelSeconds, none[1].Target.DurationSeconds);
        Assert.Equal(1f, none[1].Target.PitchRatio);
        Assert.Equal(1f, none[1].Intensity);
        Assert.Equal(WeksaWordPlan.VowelSeconds * 220f / 160f, primary[1].Target.DurationSeconds, 5);
        Assert.Equal(1.06f, primary[1].Target.PitchRatio);
        Assert.Equal(1.30f, primary[1].Intensity);
        // Secondary sits strictly between unstressed and primary on every cue.
        Assert.InRange(secondary[1].Target.DurationSeconds, none[1].Target.DurationSeconds + 1e-4f, primary[1].Target.DurationSeconds - 1e-4f);
        Assert.InRange(secondary[1].Target.PitchRatio, 1.001f, 1.059f);
        Assert.InRange(secondary[1].Intensity, 1.001f, 1.299f);
    }

    [Fact]
    public void StressReachesTheScriptAsDurationF0RatioAndSourceLevelAndShiftsLaterPhones()
    {
        var patch = (WeksaStress s) => PatchScript.Parse(IpaGestureExperiment.BuildWordScript(Plan(s).Phones.ToArray(), 140));
        var none = patch(WeksaStress.None);
        var primary = patch(WeksaStress.Primary);
        float First(SynthPatch p, string prefix, string surface) =>
            p.ControlSplines.Single(sp => sp.Name.StartsWith(prefix, StringComparison.Ordinal) && sp.SurfacePath == surface).Points[0].Value;

        // pitch_ratio surface range 0.5..2: normalized (1.06 - .5) / 1.5; unstressed holds 1 -> (1 - .5) / 1.5.
        Assert.Equal((1.06f - .5f) / 1.5f, First(primary, "w_1_i_source_pitch", "/vocal/sources/0/pitch_ratio"), 4);
        Assert.Equal((1f - .5f) / 1.5f, First(none, "w_1_i_source_pitch", "/vocal/sources/0/pitch_ratio"), 4);
        Assert.Equal(First(none, "w_1_i_source_level", "/vocal/sources/0/flow_scale") * 1.30f, First(primary, "w_1_i_source_level", "/vocal/sources/0/flow_scale"), 4);
        // The b after the stressed vowel starts later by the vowel's extra length.
        float Start(SynthPatch p) => p.PhonemeGestures.Single(g => g.Ipa == "b").StartSeconds;
        Assert.Equal(WeksaWordPlan.VowelSeconds * (220f / 160f - 1f), Start(primary) - Start(none), 4);
    }

    [Fact]
    public void PhonesTheWordPlanAcceptsAreExactlyTheDslTable()
    {
        foreach (var ipa in new[] { "n", "i", "b", "u", "a", "ə", "ɪ", "ʊ", "e", "ɛ", "æ", "ʌ", "ɑ", "ɔ", "o" })
            Assert.True(PatchScript.TryIpaDescriptor(ipa, out var descriptor) && descriptor.EndsWith("_vowel", StringComparison.Ordinal) == "iuaəɪʊeɛæʌɑɔo".Contains(ipa));
        foreach (var ipa in new[] { "ŋ", "θ", "ɹ", "l" })
            Assert.False(PatchScript.TryIpaDescriptor(ipa, out _));
    }
}
