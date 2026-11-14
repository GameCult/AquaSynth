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

    private static string Script(float tilt, float pitch, string extra = "") => $$"""
        morphology name=oral length_cm=17 diameters=.6,.8,1.2,1.5,.9
        waveguide_path name=oral_path morphology=oral loss=.998
        source_port name=folds path=oral_path pressure=.7 tension=.55 opening=.45 noise=0 impedance=.3 tilt={{tilt}} {{extra}}
        radiation_load name=mouth path=oral_path aperture=.8 reflection=-.82 impedance=.28
        vocal_network name=voice paths=oral_path sources=folds radiation=mouth
        vocal network=voice freq={{pitch}} gain=.2 sustain=1.5
        """;

    // One second of steady output, so every harmonic of Pitch and every other integer-Hz frequency sits on its own DFT bin.
    private float[]? Render(float tilt, float pitch = Pitch, int skip = 4800, string extra = "")
    {
        var source = FaustEmitter.Emit(PatchScript.Parse(Script(tilt, pitch, extra)), new FaustExportOptions("glottal_spectrum")).Source;
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

    [Fact]
    public void PulseAndNoiseHighPassAreAuthoredSourceFieldsDefaultingToSawAndWhite()
    {
        var port = Assert.Single(PatchScript.Parse(Script(.5f, Pitch, "pulse=rosenberg noise_hp=2000")).SourcePorts);
        Assert.Equal(GlottalPulse.Rosenberg, port.Pulse);
        Assert.Equal(2000f, port.NoiseHighPassHz);
        var defaulted = Assert.Single(PatchScript.Parse(Script(.5f, Pitch)).SourcePorts);
        Assert.Equal(GlottalPulse.Saw, defaulted.Pulse);
        Assert.Equal(0f, defaulted.NoiseHighPassHz);
        Assert.Throws<PatchScriptException>(() => PatchScript.Parse(Script(.5f, Pitch, "pulse=triangle")));
    }

    [Fact]
    public void OnlyTheRosenbergPulseEmitsThePhasorAndOnlyAHighPassedSourceEmitsTheNoiseFilter()
    {
        string Emit(string extra) => FaustEmitter.Emit(PatchScript.Parse(Script(.5f, Pitch, extra)), new FaustExportOptions("pulse_emit")).Source;
        Assert.DoesNotContain("lf_sawpos", Emit(""), StringComparison.Ordinal);
        Assert.Contains("lf_sawpos", Emit("pulse=rosenberg"), StringComparison.Ordinal);
        Assert.DoesNotContain("fi.highpass(2, 2000", Emit(""), StringComparison.Ordinal);
        Assert.Contains("fi.highpass(2, 2000", Emit("noise_hp=2000"), StringComparison.Ordinal);
    }

    [Fact]
    public void RosenbergPulseIsPeriodicAtThePitchAndRendersWithinSixDbOfTheSaw()
    {
        var saw = Render(.5f);
        var pulse = Render(.5f, extra: "pulse=rosenberg");
        if (saw is null || pulse is null) return;

        double Rms(float[] w) => Math.Sqrt(w.Sum(v => (double)v * v) / w.Length);
        var difference = Db(Rms(pulse) / Rms(saw));
        output.WriteLine($"rosenberg rms {Rms(pulse):E3} saw rms {Rms(saw):E3} ({difference:F2} dB)");
        // The source rms is matched, the rendered level is not: the tract passes the saw's strong upper harmonics and the pulse has none (-3.2 dB here, -2.4 dB on /ɑmi/).
        Assert.InRange(difference, -6, 0);
        var fundamental = Magnitude(pulse, Pitch);
        var between = Magnitude(pulse, Pitch * 1.5);
        Assert.True(Db(between / fundamental) < -30, $"energy between harmonics {Db(between / fundamental):F1} dB re the fundamental");
    }

    // One expression compiled alone as a one-output DSP. The pulse is run at 100 Hz, which divides the rate (480 samples a period), so every sample
    // sits at a known phase; the patch path scales a bare signal by a constant, so levels are read against os.sawtooth through the same path.
    // Expected values are Rosenberg C written out here, independent of the emitted formula's spelling.
    private float[]? RenderBare(string expression, double seconds)
    {
        var dsp = $"import(\"stdfaust.lib\");\nprocess = {expression};\n";
        using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: Rate));
        if (!compiler.TryCompileSource(new AquaSynthCompileIdentity("pulse_bare", "pulse_bare", ""), dsp, (float)seconds + .1f, out var patch, out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return null;
            }

            Assert.Fail($"bare pulse compile failed: {error}");
        }

        using (patch) return patch!.Render((float)seconds);
    }

    private static double RosenbergC(double p) =>
        p < 0.4 ? 0.5 * (1 - Math.Cos(Math.PI * p / 0.4)) : p < 0.56 ? Math.Cos(Math.PI * 0.5 * (p - 0.4) / 0.16) : 0;

    [Fact]
    public void RosenbergPulseIsTheDocumentedRosenbergCZeroMeanAtTheSawsRms()
    {
        const int period = 480;
        var pulse = RenderBare(FaustEmitter.RosenbergPulse("100.0"), 0.5);
        var saw = RenderBare("os.sawtooth(100.0)", 0.5);
        if (pulse is null || saw is null) return;
        var cycle = pulse.Skip(period).Take(period).ToArray();
        double Rms(IEnumerable<float> w) => Math.Sqrt(w.Sum(v => (double)v * v) / w.Count());
        // Calibrate the path's scale on the saw (rms 1/sqrt3 at the source): read every pulse sample back at source level.
        var scale = Rms(saw.Skip(period).Take(period)) / (1 / Math.Sqrt(3));
        cycle = cycle.Select(v => (float)(v / scale)).ToArray();

        // Rosenberg C has mean 0.301859 and rms 0.372667 about it; the emitted pulse is that, zero-mean, scaled to the saw's rms.
        Assert.InRange(cycle.Average(), -0.003, 0.003);
        var rms = Rms(cycle);
        output.WriteLine($"pulse rms {rms:F5} (saw 1/sqrt3 = {1 / Math.Sqrt(3):F5}), path scale {scale:F4}, peak {cycle.Max():F4}, floor {cycle.Min():F4}");
        Assert.InRange(Db(rms / (1 / Math.Sqrt(3))), -0.05, 0.05);

        // Shape at every phase point. The phasor's alignment to the sample grid is not part of the contract, so find the shift that fits,
        // then hold every point to the closed form: opening cosine, return cosine, closed plateau. A linear opening, another scale or no return phase all miss.
        double Expected(int n, int shift) => (RosenbergC(((n + shift) % period) / (double)period) - 0.301859) * 1.549;
        var shift = Enumerable.Range(0, period).MinBy(k => Enumerable.Range(0, period).Sum(n => Math.Pow(cycle[n] - Expected(n, k), 2)));
        var worst = Enumerable.Range(0, period).Max(n => Math.Abs(cycle[n] - Expected(n, shift)));
        output.WriteLine($"best shift {shift} samples, worst deviation from Rosenberg C {worst:E2}");
        Assert.True(worst < 0.01, $"worst deviation from Rosenberg C {worst:E2} at shift {shift}");
        // The regions by value: the opening peaks at p = 0.4 at (1 - mean) * 1.549, the closed floor is -mean * 1.549 and holds for 0.44 of the period (plus the tail of the return cosine inside the tolerance).
        Assert.InRange(cycle.Max(), (1 - 0.301859) * 1.549 - 0.01, (1 - 0.301859) * 1.549 + 0.01);
        Assert.InRange(cycle.Min(), -0.301859 * 1.549 - 0.01, -0.301859 * 1.549 + 0.01);
        Assert.InRange(cycle.Count(v => Math.Abs(v - (-0.301859 * 1.549)) < 0.01), (int)(0.44 * period) - 15, (int)(0.44 * period) + 15);
    }

    [Fact]
    public void NoiseHighPassCornerIsTheAuthoredHertz()
    {
        // The source's noise alone (pressure 0 silences the voiced part), white and through fi.highpass(2, fc): band energy against the white render tells the corner.
        // A 2 kHz corner leaves almost nothing under 700 Hz, passes 3-3.8 kHz whole and takes a few dB off 1.6-2.4 kHz; a 1 kHz corner passes the middle band; a 4 kHz corner cuts it.
        double Band(float[] w, double lo, double hi)
        {
            double sum = 0;
            for (var hz = lo; hz < hi; hz += 25) sum += Math.Pow(Magnitude(w, hz), 2);
            return sum;
        }

        double[] Shape(string extra)
        {
            var window = Render(.5f, extra: "pressure=0 noise=2 " + extra);
            return window is null ? [] : [Band(window, 100, 700), Band(window, 1600, 2400), Band(window, 3000, 3800)];
        }

        var white = Shape("");
        var at2k = Shape("noise_hp=2000 pulse=saw");
        if (white.Length == 0 || at2k.Length == 0) return;
        var at1k = Shape("noise_hp=1000");
        var at4k = Shape("noise_hp=4000");
        output.WriteLine($"band energies (100-700, 1600-2400, 3000-3800 Hz) white {string.Join(" ", white.Select(b => b.ToString("E2")))}");
        foreach (var (name, shape) in new[] { ("1k", at1k), ("2k", at2k), ("4k", at4k) }) output.WriteLine($"hp {name}: " + string.Join(" ", shape.Select(b => b.ToString("E2"))));
        double Rel(double[] shape, int band) => Db(Math.Sqrt(shape[band] / white[band]));
        Assert.True(Rel(at2k, 0) < -18, $"2 kHz corner leaves {Rel(at2k, 0):F1} dB at 100-700 Hz");
        Assert.InRange(Rel(at2k, 1), -6, -1.5);
        Assert.True(Rel(at2k, 2) > -1.5, $"2 kHz corner leaves {Rel(at2k, 2):F1} dB at 3-3.8 kHz");
        // A 1 kHz corner passes the 1.6-2.4 kHz band nearly whole; a 4 kHz corner cuts it hard.
        Assert.True(Rel(at1k, 1) > -2, $"1 kHz corner: {Rel(at1k, 1):F1} dB at 1.6-2.4 kHz");
        Assert.True(Rel(at4k, 1) < -9, $"4 kHz corner: {Rel(at4k, 1):F1} dB at 1.6-2.4 kHz");
    }
}
