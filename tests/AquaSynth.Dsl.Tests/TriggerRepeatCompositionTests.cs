using AquaSynth.Dsl;
using AquaSynth.Faust;

namespace AquaSynth.Dsl.Tests;

// A consumer writes `trigger` on a voice (envelope restarts on a grid) and `patch repeat` (sfxr pitch/arpeggio restart). These render
// the composition and assert what a listener-side measurement sees: the trigger restarts the envelope, the repeat restarts the pitch
// slide inside each trigger window and never cuts the envelope. Sound is not judged here; only the structure of the render.
[Collection("NativeFaust")]
public sealed class TriggerRepeatCompositionTests
{
    private const int Rate = 48000;

    private const string Voice = """
        voice
            wave=sine
            freq=200
            trigger=0.5
            attack=0.001
            sustain=0.12
            decay=0.1
            pitch_ramp=2.0
        """;

    private static float[]? Render(string script, float seconds)
    {
        var source = FaustEmitter.Emit(PatchScript.Parse(script), new FaustExportOptions("trigger_repeat", SampleRate: Rate)).Source;
        using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: Rate));
        if (!compiler.TryCompileSource(new AquaSynthCompileIdentity("trigger_repeat", "trigger_repeat", ""), source, seconds, out var patch, out var error))
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

    // Zero-crossing rate over a window, in Hz: a pitch measure that needs no search range.
    private static double CrossingHz(float[] x, double from, double to)
    {
        var a = (int)(from * Rate);
        var b = Math.Min(x.Length, (int)(to * Rate));
        var crossings = 0;
        for (var i = a + 1; i < b; i++) if ((x[i - 1] < 0) != (x[i] < 0)) crossings++;
        return crossings / 2.0 / ((b - a) / (double)Rate);
    }

    [Fact]
    public void TriggerRestartsTheEnvelopeWhileRepeatInsideTheWindowDoesNot()
    {
        // repeat 0.05 is shorter than sustain + decay (0.22): the envelope must still run to its release inside each trigger window.
        var x = Render("patch repeat=0.05\n" + Voice, 1.1f);
        if (x is null) return;

        Assert.True(Rms(x, 0.00, 0.10) > 0.1, "first hit sounds");
        Assert.True(Rms(x, 0.35, 0.48) < 0.01, "envelope released before the next trigger; repeat did not hold it open");
        Assert.True(Rms(x, 0.50, 0.60) > 0.1, "trigger restarts the envelope");
        Assert.True(Rms(x, 0.85, 0.98) < 0.01, "second window releases too");
        Assert.True(Rms(x, 1.00, 1.10) > 0.1, "third trigger");
    }

    [Fact]
    public void RepeatRestartsThePitchSlideInsideEachTriggerWindowAndTriggerRestartsItToo()
    {
        var withRepeat = Render("patch repeat=0.05\n" + Voice, 0.6f);
        var without = Render(Voice, 0.6f);
        if (withRepeat is null || without is null) return;

        // Without repeat the slide keeps climbing across the window; with repeat the pitch falls back at each 0.05 s boundary.
        var early = CrossingHz(without, 0.00, 0.04);
        var late = CrossingHz(without, 0.12, 0.16);
        Assert.True(late > early * 1.15, $"control: slide climbs without repeat ({early:F0} -> {late:F0} Hz)");

        var repeatedEarly = CrossingHz(withRepeat, 0.00, 0.04);
        var repeatedLate = CrossingHz(withRepeat, 0.10, 0.14);
        Assert.InRange(repeatedLate / repeatedEarly, 0.9, 1.1);

        // The trigger at 0.5 restarts age; the repeat grid inside the new window starts from the trigger, not from absolute time.
        var afterTrigger = CrossingHz(withRepeat, 0.50, 0.54);
        Assert.InRange(afterTrigger / repeatedEarly, 0.9, 1.1);
    }

    [Fact]
    public void TriggerAndRepeatLowerToDistinctAges()
    {
        var source = FaustEmitter.EmitScript("patch repeat=0.05\n" + Voice).Source;

        Assert.Contains("voice_0_age = wrap01(age / 0.5) * 0.5;", source);
        Assert.Contains("voice_0_pitch_age = voice_0_age - floor(voice_0_age / repeat_interval) * repeat_interval;", source);
        Assert.Contains("oneshot_adsr_at(voice_0_age,", source);
    }
}
