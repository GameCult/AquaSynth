using AquaSynth.Dsl;
using AquaSynth.Faust;
using Xunit.Abstractions;

namespace AquaSynth.Dsl.Tests;

// The primitive vocal network has no resonators, so the rendered spectrum is the glottal source's own: harmonics of the
// pitch with a tilt-controlled rolloff. These tests render through the native Faust path and measure by DFT.
[Collection("NativeFaust")]
public sealed class GlottalSourceSpectrumTests(ITestOutputHelper output)
{
    private const int Rate = 48000;
    private const float Pitch = 137f;

    private static string Script(float tilt, float pitch) => $$"""
        morphology name=oral length_cm=17 diameters=.6,.8,1.2,1.5,.9
        waveguide_path name=oral_path morphology=oral loss=.998
        source_port name=folds path=oral_path pressure=.7 tension=.55 opening=.45 noise=0 impedance=.3 tilt={{tilt}}
        radiation_load name=mouth path=oral_path aperture=.8 reflection=-.82 impedance=.28
        vocal_network name=voice paths=oral_path sources=folds radiation=mouth
        vocal network=voice freq={{pitch}} gain=.2 sustain=1.5
        """;

    // One second of steady output, so every harmonic of Pitch and every other integer-Hz frequency sits on its own DFT bin.
    private float[]? Render(float tilt, float pitch = Pitch, int skip = 4800)
    {
        var source = FaustEmitter.Emit(PatchScript.Parse(Script(tilt, pitch)), new FaustExportOptions("glottal_spectrum")).Source;
        using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: Rate));
        if (!compiler.TryCompileSource(new AquaSynthCompileIdentity("glottal_spectrum", "glottal_spectrum", ""), source, 1.4f, out var patch, out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return null;
            }

            Assert.Fail($"glottal spectrum compile failed: {error}");
        }

        using (patch)
        {
            var samples = patch!.Render(1.0f);
            Assert.True(samples.Length >= Rate + skip, $"render too short: {samples.Length}");
            return samples.Skip(skip).Take(Rate).ToArray();
        }
    }

    private static double Magnitude(float[] window, double hz)
    {
        double re = 0, im = 0;
        var step = 2 * Math.PI * hz / Rate;
        for (var n = 0; n < window.Length; n++)
        {
            re += window[n] * Math.Cos(step * n);
            im -= window[n] * Math.Sin(step * n);
        }

        return Math.Sqrt(re * re + im * im) * 2 / window.Length;
    }

    private static double Db(double ratio) => 20 * Math.Log10(Math.Max(ratio, 1e-12));

    private double[]? HarmonicsDb(float tilt, int count)
    {
        var window = Render(tilt);
        if (window is null) return null;
        var fundamental = Magnitude(window, Pitch);
        Assert.True(fundamental > 1e-5, $"tilt {tilt}: silent fundamental {fundamental}");
        var levels = Enumerable.Range(1, count).Select(k => Db(Magnitude(window, Pitch * k) / fundamental)).ToArray();
        output.WriteLine($"tilt {tilt}: " + string.Join(" ", levels.Select(level => level.ToString("F1"))) + $"  (fundamental {fundamental:E2})");
        return levels;
    }

    [Fact]
    public void GlottalSourceHasHarmonicsAboveTheFundamental()
    {
        var levels = HarmonicsDb(.5f, 8);
        if (levels is null) return;

        // a sine source would leave harmonics 2..8 at the numerical floor, far below -60 dB
        for (var k = 1; k < 8; k++)
        {
            Assert.True(levels[k] > -45, $"harmonic {k + 1} is {levels[k]:F1} dB re fundamental");
        }
    }

    [Fact]
    public void HigherTiltRollsHarmonicsOffMoreSteeply()
    {
        var bright = HarmonicsDb(.1f, 10);
        var dark = HarmonicsDb(.9f, 10);
        if (bright is null || dark is null) return;

        Assert.True(bright[9] - dark[9] > 12, $"harmonic 10 re fundamental: tilt .1 {bright[9]:F1} dB, tilt .9 {dark[9]:F1} dB");
        Assert.True(bright[4] - dark[4] > 6, $"harmonic 5 re fundamental: tilt .1 {bright[4]:F1} dB, tilt .9 {dark[4]:F1} dB");
    }

    [Fact]
    public void BrightestHighPitchedSourceDoesNotAlias()
    {
        // 2744 Hz is 17.49 samples per period, and 48000 and 2744 share a factor of 8, so a naive saw folds its partials above
        // Nyquist onto multiples of 8 Hz that are not harmonics of the pitch; the band-limited saw leaves them 44.8 dB down (worst bin, measured) and a naive saw 18.9 dB down. The threshold sits between.
        const float pitch = 2744f;
        // The tract rings for a few hundred ms after the note starts; that decaying transient is not periodic and would fill the gaps between harmonics, so the window starts after it.
        var window = Render(0f, pitch, skip: 16800);
        if (window is null) return;

        // The vocal tract shapes the harmonics, so the reference is the strongest of them rather than the fundamental.
        var strongest = Enumerable.Range(1, 6).Max(k => Magnitude(window, pitch * k));
        var worst = Db(Enumerable.Range(100, 900).Where(n => n % 343 != 0).Max(n => Magnitude(window, 8.0 * n)) / strongest);
        output.WriteLine($"tilt 0 pitch {pitch}: worst non-harmonic level 800-8000 Hz {worst:F1} dB re strongest harmonic");
        Assert.True(worst < -32, $"non-harmonic energy {worst:F1} dB re strongest harmonic");
    }

    [Fact]
    public void TiltIsAnAuthoredSourceControlSurfaceDefaultingToHalf()
    {
        var authored = PatchScript.Parse(Script(.8f, Pitch));
        Assert.Equal(.8f, Assert.Single(authored.SourcePorts).Tilt, 5);
        Assert.Contains(authored.ControlSurfaces, surface => surface.Path == "/vocal/sources/0/tilt");

        var defaulted = PatchScript.Parse(Script(.8f, Pitch).Replace("tilt=0.8", "", StringComparison.Ordinal));
        Assert.Equal(.5f, Assert.Single(defaulted.SourcePorts).Tilt, 5);
    }
}
