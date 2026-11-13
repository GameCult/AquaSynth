using System.Security.Cryptography;
using System.Text;
using AquaSynth.Dsl;
using AquaSynth.Faust;
using Xunit.Abstractions;

namespace AquaSynth.Dsl.Tests;

// The place table in PatchScript owns where a consonant closes (cm behind the lip plane). Text-level tests: no Faust compile.
public sealed class PlaceTableTests(ITestOutputHelper output)
{
    private static string Word(string ipa, string descriptor = "") => IpaGestureExperiment.BuildWordScript(
        [(new IpaGestureExperimentTarget("w_0_a", "a", "", 0.18f), 1f), (new IpaGestureExperimentTarget("w_1_c", ipa, descriptor, 0.095f), 1f), (new IpaGestureExperimentTarget("w_2_a", "a", "", 0.18f), 1f)], 140);

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    private static float ConstrictionIndex(string script, string name) =>
        PatchScript.Parse(script).ControlSplines
            .Single(s => s.Name.StartsWith($"{name}_constriction_index", StringComparison.Ordinal) && s.SurfacePath == "/vocal/areas/0/area/constriction_index").Points[^1].Value;   // the held place; a boundary ramp may precede it

    // Re-pinned on the phone-transition cut (boundary ramps, 12 ms stop release, smoo_init, vocal attack): these ids moved on purpose; the invariant
    // that survives is that a word of labials writes no position surface (below), not these bytes. Pre-cut values on af0e44f: the vowel_set constriction_index spline value of /b/ and /n/ (and the hash of the whole script and of the
    // Faust source for /apa/ and /aba/, which this cut must not change by a byte; the ids are a SHA-256 prefix).
    private const float BIndex = 0.8333333f, NIndex = 0.7118056f;

    [Fact]
    public void PlaceTableKeepsLabialAndAlveolarPositions()
    {
        var b = ConstrictionIndex(Word("b"), "w_1_c");
        var n = ConstrictionIndex(Word("n"), "w_1_c");
        output.WriteLine($"b {b:R} n {n:R}");
        foreach (var (ipa, scriptSha, faustSha) in new[] { ("p", "0E86DB0F40C0F12A", "7FDFCB446676787E"), ("b", "A63403EBAAD016FB", "574AC2CE7AD191FC") })
        {
            var script = Word(ipa);
            var faust = FaustEmitter.Emit(PatchScript.Parse(script), new FaustExportOptions("pin", SampleRate: 48000)).Source;
            Assert.Equal(scriptSha, Sha(script));
            Assert.Equal(faustSha, Sha(faust));
        }

        Assert.Equal(BIndex, b);
        Assert.Equal(NIndex, n);
    }

    // The side stub is declared only for a word with a lateral: the script and the Faust source of /apa/ /aba/ /ana/ /ata/ are the bytes the scaffold
    // gave before the lateral cut (SHA-256 prefixes taken on the head of /k/, 8d50819 plus /d/).
    [Fact]
    public void ExistingWordsSourcesUnchanged()
    {
        foreach (var (ipa, scriptSha, faustSha) in new[]
        {
            ("p", "0E86DB0F40C0F12A", "7FDFCB446676787E"), ("b", "A63403EBAAD016FB", "574AC2CE7AD191FC"),
            ("n", "5C3FA24E40D9DE26", "AD4C6E3371E089CF"), ("t", "45941CCFD93561B3", "3FF82CED153C1543"),
        })
        {
            var script = Word(ipa);
            var faust = FaustEmitter.Emit(PatchScript.Parse(script), new FaustExportOptions("pin", SampleRate: 48000)).Source;
            Assert.Equal(scriptSha, Sha(script));
            Assert.Equal(faustSha, Sha(faust));
        }

        Assert.Contains(PatchScript.SideStubBranch, Word("l"));
    }

    [Fact]
    public void UnknownPlaceHasNoDistance()
    {
        Assert.True(PatchScript.TryPlaceDistanceCentimeters("voiceless_bilabial_plosive", out var lips));
        Assert.Equal(0f, lips);
        Assert.True(PatchScript.TryPlaceDistanceCentimeters("voiced_alveolar_nasal", out var ridge));
        Assert.Equal(2.555f, ridge);
        // velar: between the narrowest dorsal section of /u/ (section 26, 6.97 cm from the lips) and the narrowest pharyngeal section of /a/
        // (section 13, 12.14 cm); 9.81 cm is the 0.44 fraction of the 17.52 cm tract, which sits within 0.05 cm of the centre of a ladder cell
        // (their mean, 9.555 cm, is 0.3 cm from one)
        Assert.True(PatchScript.TryPlaceDistanceCentimeters("voiceless_velar_plosive", out var velar));
        Assert.InRange(velar, 6.97f, 12.14f);
        Assert.Equal(9.81f, velar, 3);
        foreach (var descriptor in new[] { "voiced_uvular_fricative", "voiced_palatal_approximant", "voiced_plosive", "" })
        {
            Assert.False(PatchScript.TryPlaceDistanceCentimeters(descriptor, out var cm), descriptor);
            Assert.True(float.IsNaN(cm), $"{descriptor} claims {cm}");
        }
    }

    private static bool HasPositionSurface(string script) =>
        PatchScript.Parse(script).ControlSurfaces.Any(surface => surface.Path == "/vocal/contacts/0/position");

    private static float[] SitePoints(string script, string name) =>
        PatchScript.Parse(script).ControlSplines.Single(s => s.Name.StartsWith($"{name}_burst_site", StringComparison.Ordinal)).Points.Select(p => p.Value).ToArray();

    private static string Faust(string script) => FaustEmitter.Emit(PatchScript.Parse(script), new FaustExportOptions("site", SampleRate: 48000)).Source;

    [Fact]
    public void AlveolarBurstGoesInAtTheRidgeAndLabialBurstStaysAtTheGlottalEnd()
    {
        var t = Word("t", "voiceless_alveolar_plosive");
        Assert.True(HasPositionSurface(t));
        Assert.All(SitePoints(t, "w_1_c"), v => Assert.Equal(1 - 2.555f / StoryVowelAreas.MeanLengthCentimeters, v, 4));
        Assert.Contains("_injection0_site", Faust(t));

        // a word of labials never makes the surface: no spline, and the burst joins the glottal path input as before
        var p = Word("p");
        Assert.False(HasPositionSurface(p));
        Assert.DoesNotContain("_injection", Faust(p));
    }

    [Fact]
    public void ALabialAfterAnotherPlosiveInTheSameWordIsPlacedAtTheGlottalEnd()
    {
        IpaGestureExperimentTarget Phone(string label, string ipa, float s) => new($"w_{label}", ipa, "", s);
        var script = IpaGestureExperiment.BuildWordScript(
            [(Phone("0_b", "b", 0.095f), 1f), (Phone("1_a", "a", 0.18f), 1f), (new IpaGestureExperimentTarget("w_2_t", "t", "voiceless_alveolar_plosive", 0.095f), 1f), (Phone("3_a", "a", 0.18f), 1f), (Phone("4_p", "p", 0.095f), 1f)], 140);
        Assert.All(SitePoints(script, "w_0_b"), v => Assert.Equal(0f, v));
        Assert.All(SitePoints(script, "w_4_p").Skip(1), v => Assert.Equal(0f, v));
        // after the 30 ms boundary ramp from the previous plosive's place (the first point is that place)
        Assert.All(SitePoints(script, "w_2_t").Skip(1), v => Assert.True(v > 0.8f));
    }

    [Fact]
    public void ATongueSealedStopIsClosedCompletelyUntilItsContactOpensAndALabialKeepsItsOldSpline()
    {
        float[] Diameter(string script) => PatchScript.Parse(script).ControlSplines
            .Single(s => s.Name.StartsWith("w_1_c_constriction_diameter", StringComparison.Ordinal)).Points.Select(p => p.Value).ToArray();
        var t = PatchScript.Parse(Word("t", "voiceless_alveolar_plosive")).ControlSplines
            .Single(s => s.Name.StartsWith("w_1_c_constriction_diameter", StringComparison.Ordinal)).Points;
        // lead-in (the vowel's open 1), arrival, closure hold, end: shut from the arrival to the hold, and the hold ends StopReleaseSeconds before the stop does
        Assert.Equal(1f, t[0].Value);
        Assert.Equal(0f, t[1].Value);
        Assert.Equal(0f, t[2].Value);
        Assert.Equal(1 - 0.012f / 0.095f, (t[2].TimeSeconds - 0.18f) / 0.095f, 3);
        var b = Diameter(Word("b"));
        Assert.True(b[1] > 0 && Math.Abs(b[1] - b[2]) < 1e-6f, "the lip-sealed stop keeps the 0.03 aperture spline (its source text is pinned)");
    }

    [Fact]
    public void VelarBurstGoesInAtTheVelarPlaceAndFurtherBackThanTheRidge()
    {
        var k = Word("k", "voiceless_velar_plosive");
        Assert.True(HasPositionSurface(k));
        Assert.All(SitePoints(k, "w_1_c"), v => Assert.Equal(1 - 9.81f / StoryVowelAreas.MeanLengthCentimeters, v, 4));
        Assert.Contains("_injection0_site", Faust(k));
        Assert.Equal(1 - 9.81f / StoryVowelAreas.MeanLengthCentimeters, ConstrictionIndex(k, "w_1_c") / BIndex, 4);
    }
}
