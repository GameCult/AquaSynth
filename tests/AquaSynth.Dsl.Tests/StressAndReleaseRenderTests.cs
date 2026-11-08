using AquaSynth.Dsl;
using AquaSynth.Faust;
using Xunit.Abstractions;

namespace AquaSynth.Dsl.Tests;

// Renders the two cues the tract gained in this pass through the native Faust path: the source's f0 ratio (the stress pitch cue) and
// the plosive release burst. Each asserts a measured signal, not the emitted text.
[Collection("NativeFaust")]
public sealed class StressAndReleaseRenderTests(ITestOutputHelper output)
{
    private const int Rate = 48000;

    private static float[]? Render(string script, float seconds, Func<SynthPatch, SynthPatch>? edit = null)
    {
        var parsed = PatchScript.Parse(script);
        var source = FaustEmitter.Emit(edit is null ? parsed : edit(parsed), new FaustExportOptions("stress_release", SampleRate: Rate)).Source;
        using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: Rate));
        if (!compiler.TryCompileSource(new AquaSynthCompileIdentity("stress_release", "stress_release", ""), source, seconds, out var patch, out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return null;
            }

            Assert.Fail($"compile failed: {error}");
        }

        using (patch) return patch!.Render(seconds);
    }

    private static double Rms(float[] x, double from, double to)
    {
        var a = (int)(from * Rate);
        var b = Math.Min(x.Length, (int)(to * Rate));
        double sum = 0;
        for (var i = a; i < b; i++) sum += (double)x[i] * x[i];
        return Math.Sqrt(sum / Math.Max(1, b - a));
    }

    private static double Peak(float[] x, double from, double to) =>
        x.Skip((int)(from * Rate)).Take((int)((to - from) * Rate)).Max(Math.Abs);

    // f0 by the highest autocorrelation peak between 60 and 400 Hz.
    private static double F0(float[] x, double from, double to)
    {
        var seg = x.Skip((int)(from * Rate)).Take((int)((to - from) * Rate)).Select(v => (double)v).ToArray();
        double best = 0; var bestLag = 0;
        for (var lag = Rate / 400; lag <= Rate / 60; lag++)
        {
            double c = 0;
            for (var i = 0; i + lag < seg.Length; i++) c += seg[i] * seg[i + lag];
            if (c > best) { best = c; bestLag = lag; }
        }

        return (double)Rate / bestLag;
    }

    [Fact]
    public void SourcePitchRatioScalesTheRenderedF0()
    {
        var f0 = (float ratio) =>
        {
            var script = IpaGestureExperiment.BuildWordScript([(new IpaGestureExperimentTarget("w_0_a", "a", "", 0.5f, PitchRatio: ratio), 1f)], 110);
            var x = Render(script, 0.6f);
            return x is null ? double.NaN : F0(x, 0.2, 0.5);
        };
        var unity = f0(1f);
        var up = f0(1.5f);
        output.WriteLine($"f0 at ratio 1: {unity:F1}, ratio 1.5: {up:F1}");
        if (double.IsNaN(unity)) return;
        Assert.InRange(unity, 108, 112);
        Assert.InRange(up, 163, 167);
    }

    [Fact]
    public void IntensityIsAnAmplitudeFactorOnTheRenderedLevel()
    {
        double Level(float intensity)
        {
            var script = IpaGestureExperiment.BuildWordScript([(new IpaGestureExperimentTarget("w_0_a", "a", "", 0.5f), intensity)], 110);
            var x = Render(script, 0.6f);
            return x is null ? double.NaN : Rms(x, 0.3, 0.5);
        }

        var unity = Level(1f);
        var stressed = Level(1.26f);
        if (double.IsNaN(unity)) return;
        var db = 20 * Math.Log10(stressed / unity);
        output.WriteLine($"level at intensity 1.26 against 1: {db:F2} dB (factor wanted {20 * Math.Log10(1.26):F2})");
        Assert.InRange(db, 1.7, 2.3);
    }

    [Fact]
    public void PlosiveReleaseBurstIsAboveTheClosureAndBelowTheFollowingVowel()
    {
        var script = IpaGestureExperiment.BuildWordScript(
            [(new IpaGestureExperimentTarget("w_0_b", "b", "", 0.095f), 1f), (new IpaGestureExperimentTarget("w_1_a", "a", "", 0.18f), 1f)], 140);
        var withBurst = Render(script, 0.4f);
        var withoutBurst = Render(script, 0.4f, p => p with { ControlSplines = p.ControlSplines.Where(s => !s.SurfacePath.EndsWith("/release_flow", StringComparison.Ordinal)).ToArray() });
        if (withBurst is null || withoutBurst is null) return;
        // Everything but the burst's noise is deterministic and shared, so the difference of the two renders is the burst alone.
        var burst = withBurst.Zip(withoutBurst, (x, y) => x - y).ToArray();
        const double releaseFrom = 0.095 * 0.65, vowelFrom = 0.095;
        var vowelPeak = Peak(withBurst, vowelFrom + 0.06, vowelFrom + 0.18);
        var burstPeak = Peak(burst, releaseFrom, vowelFrom + 0.01);
        var burstBeforeRelease = Peak(burst, 0, releaseFrom - 0.005);
        var burstAfter = Peak(burst, vowelFrom + 0.04, 0.4);
        output.WriteLine($"burst peak {burstPeak:F4} in the release, {burstBeforeRelease:F5} in the closure, {burstAfter:F5} in the vowel; vowel peak {vowelPeak:F4}");
        Assert.True(burstPeak > 0.004, "the burst is a transient of audible size in the release window");
        Assert.True(burstBeforeRelease < burstPeak / 20, "the closure is silent: no burst before the release");
        Assert.True(burstAfter < burstPeak / 5, "the burst is over once the vowel is open");
        Assert.True(burstPeak < vowelPeak / 2, "the burst stays well under the following vowel's peak (the b level fix)");
    }
}
