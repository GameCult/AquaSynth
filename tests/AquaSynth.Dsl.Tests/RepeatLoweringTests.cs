using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

// sfxr's repeat restarts the pitch slide and the arpeggio; it does not restart the envelope. Before this, `patch repeat` wrapped the
// whole age, so a repeat shorter than sustain+decay never reached the release (the 2026-11-11 explosion/golf/powerup finding).
public sealed class RepeatLoweringTests
{
    private const string Script = """
        patch repeat=0.11315
        voice
            wave=sine
            freq=57.5946
            sustain=0.1306122449
            decay=0.1777777778
            pitch_ramp=-0.208544
            arp_delay=0.05
            arp_mult=1.5
        """;

    [Fact]
    public void RepeatWrapsPitchAndArpeggioAgeButNotTheEnvelope()
    {
        var source = FaustEmitter.EmitScript(Script).Source;

        Assert.Contains("age = time;", source);
        Assert.DoesNotContain("floor(time /", source);
        Assert.Contains("voice_0_pitch_age = voice_0_age - floor(voice_0_age / repeat_interval) * repeat_interval;", source);
        Assert.Contains("0.208544", source);
        Assert.Contains("* voice_0_pitch_age", source);
        Assert.Contains("select2(voice_0_pitch_age <", source);
        Assert.Contains("oneshot_adsr_at(voice_0_age,", source);
        Assert.DoesNotContain("oneshot_adsr_at(voice_0_pitch_age", source);
    }

    [Fact]
    public void WithoutRepeatThereIsNoPitchAge()
    {
        var source = FaustEmitter.EmitScript(Script.Replace("patch repeat=0.11315\n", "")).Source;

        Assert.DoesNotContain("pitch_age", source);
        Assert.DoesNotContain("repeat_interval", source);
    }
}
