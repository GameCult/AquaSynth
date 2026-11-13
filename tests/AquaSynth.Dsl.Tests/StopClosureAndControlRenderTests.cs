using AquaSynth.Dsl;
using AquaSynth.Faust;
using Xunit.Abstractions;

namespace AquaSynth.Dsl.Tests;

// Renders behind the tract-coarticulation findings of the PR 52 Soul pass: what the closing ramp into a stop sounds like, what the DSP
// does with a stop's 12 ms opening, and what the control smoothers and the word attack do at the start of a render. Each reads the
// rendered signal (or the DSP's own probe), not the emitted text.
[Collection("NativeFaust")]
public sealed class StopClosureAndControlRenderTests(ITestOutputHelper output)
{
    private const int Rate = 48000;

    private static AquaSynthCompiledPatch? Compile(string source, float seconds)
    {
        using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: Rate));
        if (!compiler.TryCompileSource(new AquaSynthCompileIdentity("stop_closure", "stop_closure", ""), source, seconds, out var patch, out var error))
        {
            Assert.Fail($"compile failed: {error}");
        }

        return patch;
    }

    private static string Word(string ipa) => IpaGestureExperiment.BuildWordScript(
        [(new IpaGestureExperimentTarget("w_0_a", "a", "", 0.18f), 1f), (new IpaGestureExperimentTarget("w_1_c", ipa, "", 0.095f), 1f), (new IpaGestureExperimentTarget("w_2_a", "a", "", 0.18f), 1f)], 140);

    // Power above 4 kHz (100 Hz grid to 12 kHz) of a Hann-windowed stretch.
    private static double HighPower(float[] x, double from, double to)
    {
        var a = (int)(from * Rate);
        var n = (int)((to - from) * Rate);
        var seg = Enumerable.Range(0, n).Select(i => x[a + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)))).ToArray();
        double sum = 0;
        for (var hz = 4000; hz < 12000; hz += 100)
        {
            double re = 0, im = 0;
            for (var i = 0; i < n; i++) { var w = 2 * Math.PI * hz * i / Rate; re += seg[i] * Math.Cos(w); im -= seg[i] * Math.Sin(w); }
            sum += re * re + im * im;
        }

        return sum;
    }

    // The 30 ms boundary ramp into a stop moves the contact, constriction and lip surfaces through every aperture between open and shut. The
    // burst noise is 4 * opening * (1 - opening) times a gain, and the gain was held through the closure, so each of those passes was a 15-20 ms
    // hiss before the stop. The interval is the stop's start to its release, against the same length of the vowel before it, in the >4 kHz
    // band, with the glottal source silent and the burst gain kept: the voiced stops' own voicing runs through the ramp and its harmonics
    // fill that band (/d/ read -11.6 dB with the source on and -52.8 with it off; with the source on it swings between -13.5 and -9.3 dB
    // with f0 alone, 139.5 to 145 Hz, as the harmonics fall on and off the 100 Hz grid), so with the source on the measure cannot tell the
    // voicing from the hiss. Source off, the burst gain held through the closure read +5 dB (/p/) and +14 dB (/t/, /d/) on 3b62b9c; gated,
    // every stop reads -52 dB. -30 is the bound between the two. /g/ has no burst gain to hold (the gate mutant leaves it at -52 dB); it is here as a stop that must stay quiet.
    [FaustTheory]
    [InlineData("p")]
    [InlineData("b")]
    [InlineData("t")]
    [InlineData("d")]
    [InlineData("k")]
    [InlineData("g")]
    public void ClosingIntoAStopMakesNoHighFrequencyNoise(string ipa)
    {
        var parsed = PatchScript.Parse(Word(ipa));
        var voiceless = parsed with
        {
            ControlSplines = parsed.ControlSplines.Select(s => s.Name.StartsWith("w_1_c_source_level", StringComparison.Ordinal)
                ? s with { Points = s.Points.Select(q => q with { Value = 0, OutValue = 0, InValue = 0 }).ToArray() }
                : s).ToArray()
        };
        using var patch = Compile(FaustEmitter.Emit(voiceless, new FaustExportOptions("stop_closure", SampleRate: Rate)).Source, 0.5f);
        var x = patch!.Render(0.5f);
        const double from = 0.18;
        var release = PatchScript.StopReleaseStartSeconds(0.095f);
        var closing = HighPower(x, from, from + release);
        var vowel = HighPower(x, from - release, from);
        var db = 10 * Math.Log10(closing / vowel);
        output.WriteLine($"/{ipa}/ >4 kHz energy of the closing interval {db:F1} dB re the vowel's before it, source silent");
        Assert.True(db < -30, $"/{ipa}/ closes with {db:F1} dB re the vowel's own >4 kHz energy");
    }

    // The Faust lowering of a Bezier segment ignores the handle times and runs the curve on the linear fraction of the segment; the C# timeline
    // solves the curve's time, so a handle longer than the segment folds it. The stop's 12 ms opening had 23.75 ms handles: the contact opening
    // read 0.05 to 0.87 between 6 and 7 ms in C# and 0.46 to 0.57 in the DSP. This reads the opening through the DSP's own contact reservoir probe
    // (stored pressure + (1 - opening) * resistance, both at their defaults once the other splines are dropped) and compares it with the timeline.
    [FaustFact]
    public void TheDspRendersTheStopOpeningAsTheTimelineEvaluatesIt()
    {
        var script = Word("p");
        var parsed = PatchScript.Parse(script);
        var contact = parsed.ControlSplines.Where(s => s.SurfacePath == "/vocal/contacts/0/opening").ToArray();
        var alone = parsed with { ControlSplines = contact };
        var timeline = new ControlSplineTimeline(contact);
        var source = FaustEmitter.Emit(alone, new FaustExportOptions("stop_opening", SampleRate: Rate, DebugProbeUi: true)).Source;
        using var patch = Compile(source, 0.5f);
        using var stream = patch!.CreateStreamingPatch();
        var reservoir = stream.ProbePaths.Single(p => p.EndsWith("/contact/contact/reservoir", StringComparison.Ordinal));
        var release = 0.18f + PatchScript.StopReleaseStartSeconds(0.095f);
        var outputs = new[] { new float[48] };
        var dsp = new List<(float Time, float Reservoir)>();
        for (var block = 0; block < 400; block++)
        {
            stream.ProcessBlock([], outputs, 48);
            var t = (block + 1) * 48f / Rate;
            if (t >= release - 0.004f && t <= release + 0.016f) dsp.Add((t, stream.ReadProbe(reservoir)));
        }

        // reservoir is linear in the opening; scale it by its own end points
        var shut = dsp.First().Reservoir;
        var open = dsp.Last().Reservoir;
        var worst = 0f;
        foreach (var (time, value) in dsp)
        {
            var dspOpening = (value - shut) / (open - shut);
            var curve = timeline.ValueAt("/vocal/contacts/0/opening", time);
            var shutValue = timeline.ValueAt("/vocal/contacts/0/opening", dsp.First().Time);
            var openValue = timeline.ValueAt("/vocal/contacts/0/opening", dsp.Last().Time);
            var timelineOpening = (curve - shutValue) / (openValue - shutValue);
            worst = MathF.Max(worst, MathF.Abs(dspOpening - timelineOpening));
        }

        output.WriteLine($"worst difference between the DSP's opening and the timeline's over the release: {worst:F3}");
        Assert.True(worst < 0.08f, $"the DSP's stop opening departs from the timeline by {worst:F3}");
    }

    // The same fact without the DSP: every handle of an authored spline is a third of its segment, so the C# curve is the curve the lowering runs.
    [Fact]
    public void EveryAuthoredSplineHandleIsAThirdOfItsSegment()
    {
        var splines = PatchScript.Parse(Word("p")).ControlSplines.Where(s => s.Points.Count > 1).ToArray();
        Assert.NotEmpty(splines);
        foreach (var spline in splines)
        {
            for (var i = 0; i + 1 < spline.Points.Count; i++)
            {
                var third = (spline.Points[i + 1].TimeSeconds - spline.Points[i].TimeSeconds) / 3;
                Assert.Equal(third, spline.Points[i].OutTimeOffsetSeconds, 5);
                Assert.Equal(third, spline.Points[i + 1].InTimeOffsetSeconds, 5);
            }
        }
    }

    // Replaces the emitted patch's process with one control through the emitted smoo_init, so the smoother under test is the one the exporter
    // writes. Starts at its input at sample 0, and moves at si.smoo's 22.7 ms time constant when the control moves (5 ms gave 0.89 here).
    [FaustFact]
    public void ASmootherStartsAtItsInputAndMovesAtSiSmoosPace()
    {
        var emitted = FaustEmitter.Emit(PatchScript.Parse(Word("p")), new FaustExportOptions("smoo", SampleRate: Rate)).Source;
        var lines = emitted.Split('\n').Select(l => l.StartsWith("process =", StringComparison.Ordinal) ? "process = hslider(\"/vocal/sources/0/pressure\", 0.7, 0, 1, 0.001) : smoo_init;" : l);
        using var patch = Compile(string.Join('\n', lines), 0.2f);
        using var stream = patch!.CreateStreamingPatch();
        var first = new[] { new float[480] };
        stream.ProcessBlock([], first, 480);
        output.WriteLine($"sample 0 {first[0][0]:F4}, after 10 ms {first[0][479]:F4}");
        Assert.Equal(0.7f, first[0][0], 4);
        Assert.Equal(0.7f, first[0][479], 4);
        var after = new[] { new float[240] };
        stream.ProcessBlock([], after, 240, new Dictionary<string, float> { ["/vocal/sources/0/pressure"] = 1f });
        // 5 ms into a step from 0.7 to 1 with a 22.7 ms time constant
        var expected = 0.7 + 0.3 * (1 - Math.Pow(1 - 44.1 / Rate, 0.005 * Rate));
        output.WriteLine($"5 ms after a step to 1: {after[0][239]:F4}, si.smoo would be {expected:F4}");
        Assert.InRange(after[0][239], expected - 0.01, expected + 0.01);
    }

    // A word starts with a 12 ms attack, not a step: the control smoothers no longer fade it in, and a step onset would be a click.
    [FaustFact]
    public void AWordBeginsWithATwelveMillisecondAttack()
    {
        double FirstMillisecondsPeak(string script)
        {
            using var patch = Compile(FaustEmitter.Emit(PatchScript.Parse(script), new FaustExportOptions("attack", SampleRate: Rate)).Source, 0.4f);
            var x = patch!.Render(0.4f);
            var steady = x.Skip((int)(0.05 * Rate)).Take((int)(0.1 * Rate)).Max(Math.Abs);
            return x.Take((int)(0.002 * Rate)).Max(Math.Abs) / steady;
        }

        var script = IpaGestureExperiment.BuildWordScript([(new IpaGestureExperimentTarget("w_0_a", "a", "", 0.3f), 1f)], 140);
        Assert.Contains("attack=.012", script);
        var attacked = FirstMillisecondsPeak(script);
        var stepped = FirstMillisecondsPeak(script.Replace("attack=.012 ", "", StringComparison.Ordinal));
        output.WriteLine($"peak of the first 2 ms re the steady peak: with the attack {attacked:F3}, without {stepped:F3}");
        Assert.True(attacked < 0.25, $"the first 2 ms reach {attacked:F2} of the steady peak");
        Assert.True(stepped > 2 * attacked, "without the attack the onset is a step");
    }
}
