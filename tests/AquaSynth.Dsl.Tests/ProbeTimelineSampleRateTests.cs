using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

public sealed class ProbeTimelineSampleRateTests
{
    private const string TimelineScript = """
        morphology name=oral length_cm=17.52 diameters=.55,.75,1.05,1.45,1.25,.8 tongue_index=3 tongue_diameter=1.4 constriction_index=4 constriction_diameter=4 lip_opening=4
        waveguide_path name=oral_path morphology=oral speed=350 loss=.998
        source_port name=folds path=oral_path pressure=.7 tension=.55 opening=.5 noise=0 impedance=.3 flow_scale=.12
        radiation_load name=mouth path=oral_path aperture=.8 reflection=-.82 impedance=.28
        probe_timeline name=flow blocks=4 block_size=64
        vocal_network name=voice paths=oral_path sources=folds radiation=mouth probe=flow
        vocal network=voice freq=140 gain=.2 sustain=.3
        """;

    [Fact]
    public void ProbeTimelineReadsTheSampleRateItIsGiven()
    {
        var patch = PatchScript.Parse(TimelineScript + "\ncontrol_spline name=speed_step surface=/vocal/paths/0/speed interp=hold points=0:.25,0.0028:.5");
        float Delay(int block, float? rate) => (rate is { } r ? ProbeTimelineReport.Build(patch, "voice", blocks: 3, sampleRate: r) : ProbeTimelineReport.Build(patch, "voice", blocks: 3))
            .First(s => s.Signal == "delay_samples" && s.Block == block).Value;
        // Block 0 (t = 0) reads speed 325 m/s: the delay is length / speed * rate, so 44.1 kHz and 48 kHz stand in the ratio of the rates.
        Assert.Equal(0.1752f / 325 * 48000, Delay(0, 48000), 3);
        Assert.Equal(0.1752f / 325 * 44100, Delay(0, 44100), 3);
        Assert.Equal(Delay(0, 48000), Delay(0, null), 3);
        // Block 2 sits at 64 * 2 / rate = 2.67 ms at 48 kHz (before the 2.8 ms speed step: 325 m/s) and 2.90 ms at 44.1 kHz (after it: 550 m/s).
        Assert.Equal(0.1752f / 325 * 48000, Delay(2, 48000), 3);
        Assert.Equal(0.1752f / 550 * 44100, Delay(2, 44100), 3);
    }

    [Fact]
    public void EmittedSourceFollowsTheRequestedSampleRate()
    {
        // The vocal ladder's delay-line lengths are length / speed * rate samples, so the emitted DSP carries the rate it was asked for.
        var at48 = FaustEmitter.EmitScript(TimelineScript, new FaustExportOptions("rate_probe", SampleRate: 48000)).Source;
        var at44 = FaustEmitter.EmitScript(TimelineScript, new FaustExportOptions("rate_probe", SampleRate: 44100)).Source;
        Assert.NotEqual(at48, at44);
        Assert.Equal(at48, FaustEmitter.EmitScript(TimelineScript, new FaustExportOptions("rate_probe")).Source);
    }
}
