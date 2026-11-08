using AquaSynth.Dsl;
using AquaSynth.Faust;
using Xunit.Abstractions;

namespace AquaSynth.Dsl.Tests;

// The vocal_network resonator is a Kelly-Lochbaum ladder, so the same glottal source gives different vowels as the area
// function moves. These tests render through the native Faust path with a 10 Hz glottal source (a 10 Hz line spectrum
// samples the tract's response densely) and read formants as local maxima of the harmonic lines, flattened by the saw's 1/f.
// Expected values are the ladder prototype's impulse-response peaks (proto/kl.mjs, the same area model and constants).
[Collection("NativeFaust")]
public sealed class VocalTractFormantTests(ITestOutputHelper output)
{
    private const int Rate = 48000;
    private const float Pitch = 10f;

    private static string Script(float tongueIndex, float tongueDiameter, float lipOpening, float velum = .01f) => $$"""
        morphology name=oral length_cm=17 diameters=.55,.75,1.05,1.45,1.25,.8 tongue_index={{tongueIndex}} tongue_diameter={{tongueDiameter}} constriction_index=4 constriction_diameter=4 lip_opening={{lipOpening}}
        morphology name=nasal length_cm=12 diameters=.04,.28,.52,.72
        waveguide_path name=oral_path morphology=oral loss=.998
        waveguide_path name=nasal_path morphology=nasal loss=.997
        source_port name=folds path=oral_path pressure=.7 tension=.55 opening=.5 noise=0 impedance=.3 flow_scale=.12 tilt=0
        branch_port name=velopharynx from=oral_path from_position=.45 to=nasal_path opening={{velum}} coupling=1
        radiation_load name=mouth path=oral_path aperture=.8 reflection=-.82 impedance=.28
        radiation_load name=nostril path=nasal_path aperture=.8 reflection=-.82 impedance=.28
        vocal_network name=voice paths=oral_path,nasal_path sources=folds branches=velopharynx radiation=mouth,nostril
        vocal network=voice freq={{Pitch}} min_freq=5 gain=.5 sustain=1.5
        """;

    // Three seconds of the 10 Hz source would be wasteful: one second after the attack is ten whole periods, so every
    // harmonic of Pitch sits exactly on a DFT bin.
    private float[]? Render(string script)
    {
        var source = FaustEmitter.Emit(PatchScript.Parse(script), new FaustExportOptions("vocal_formants", SampleRate: Rate)).Source;
        using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: Rate));
        if (!compiler.TryCompileSource(new AquaSynthCompileIdentity("vocal_formants", "vocal_formants", ""), source, 1.4f, out var patch, out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return null;
            }

            Assert.Fail($"vocal formant compile failed: {error}");
        }

        using (patch)
        {
            var samples = patch!.Render(1.0f);
            Assert.True(samples.Length >= Rate + 4800, $"render too short: {samples.Length}");
            return samples.Skip(4800).Take(Rate).ToArray();
        }
    }

    // Harmonic line magnitudes of the saw, multiplied by frequency to undo its 1/f slope.
    private static double[] Lines(float[] window)
    {
        var n = 1 << 16;
        var re = new double[n];
        var im = new double[n];
        for (var i = 0; i < window.Length; i++) re[i] = window[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / window.Length));
        Fft(re, im);
        var lines = new double[(int)(5000 / Pitch) + 1];
        for (var k = 1; k < lines.Length; k++)
        {
            var bin = (int)Math.Round(k * Pitch * n / Rate);
            lines[k] = Math.Sqrt(re[bin] * re[bin] + im[bin] * im[bin]) * k;
        }

        return lines;
    }

    // Local maxima of the flattened lines that are tallest within +-40 Hz and above 10% of the tallest, in ascending Hz.
    private static double[] Peaks(float[] window)
    {
        var lines = Lines(window);
        var span = (int)(40 / Pitch);
        var floor = 0.1 * lines.Max();
        var peaks = new List<double>();
        for (var k = (int)(150 / Pitch); k < lines.Length - span; k++)
        {
            if (lines[k] < floor) continue;
            var tallest = true;
            for (var j = k - span; j <= k + span; j++) tallest &= lines[j] <= lines[k];
            if (tallest) peaks.Add(k * Pitch);
        }

        return peaks.ToArray();
    }

    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            for (var i = 0; i < n; i += len)
            {
                for (var k = 0; k < len / 2; k++)
                {
                    double wr = Math.Cos(angle * k), wi = Math.Sin(angle * k);
                    var a = i + k;
                    var b = i + k + len / 2;
                    var vr = re[b] * wr - im[b] * wi;
                    var vi = re[b] * wi + im[b] * wr;
                    (re[b], im[b]) = (re[a] - vr, im[a] - vi);
                    (re[a], im[a]) = (re[a] + vr, im[a] + vi);
                }
            }
        }
    }

    public static TheoryData<string, float, float, float, double, double> Vowels => new()
    {
        { "i", 3f, .15f, 2.2f, 271, 2313 },
        { "schwa", 2f, 1.2f, .6f, 497, 1503 },
        { "a", 1.5f, .5f, 2.2f, 731, 1257 },
    };

    private double[]? FirstTwo(string name, float ti, float td, float lo)
    {
        var window = Render(Script(ti, td, lo));
        if (window is null) return null;
        var peaks = Peaks(window);
        output.WriteLine($"{name}: peaks " + string.Join(" ", peaks.Select(p => p.ToString("F0"))));
        Assert.True(peaks.Length >= 2, $"{name}: fewer than two spectral peaks");
        return peaks;
    }

    [Theory]
    [MemberData(nameof(Vowels))]
    public void VowelAreaFunctionsPlaceF1AndF2WhereThePrototypePlacedThem(string name, float ti, float td, float lo, double f1, double f2)
    {
        var peaks = FirstTwo(name, ti, td, lo);
        if (peaks is null) return;

        Assert.InRange(peaks[0], f1 * .92, f1 * 1.08);
        Assert.InRange(peaks[1], f2 * .92, f2 * 1.08);
    }

    [Fact]
    public void VowelsDifferInTheirFormants()
    {
        var i = FirstTwo("i", 3f, .15f, 2.2f);
        var schwa = FirstTwo("schwa", 2f, 1.2f, .6f);
        var a = FirstTwo("a", 1.5f, .5f, 2.2f);
        if (i is null || schwa is null || a is null) return;

        // F1 rises i < schwa < a as the jaw opens; F2 falls i > schwa > a as the constriction moves back.
        Assert.True(i[0] + 100 < schwa[0] && schwa[0] + 100 < a[0], $"F1 {i[0]} {schwa[0]} {a[0]}");
        Assert.True(i[1] > schwa[1] + 300 && schwa[1] > a[1] + 100, $"F2 {i[1]} {schwa[1]} {a[1]}");
    }

    [Fact]
    public void LadderStaysBoundedForASecondWithTheVelumOpen()
    {
        foreach (var (ti, td, lo, velum) in new[] { (3f, .15f, 2.2f, .01f), (1.5f, .5f, 2.2f, 1f), (2f, 1.2f, .6f, 1f) })
        {
            var window = Render(Script(ti, td, lo, velum));
            if (window is null) return;
            var quarter = window.Length / 4;
            var rms = Enumerable.Range(0, 4).Select(q => Math.Sqrt(window.Skip(q * quarter).Take(quarter).Sum(x => (double)x * x) / quarter)).ToArray();
            output.WriteLine($"velum {velum} ti {ti}: peak {window.Max(Math.Abs):F3} rms by quarter {string.Join(" ", rms.Select(r => r.ToString("E2")))}");
            Assert.All(window, sample => Assert.True(float.IsFinite(sample) && Math.Abs(sample) < 4, $"sample {sample}"));
            Assert.True(rms[0] > 1e-4, $"silent: rms {rms[0]}");
            Assert.True(rms[3] < 2 * rms[0] + 1e-6 && rms[3] > rms[0] / 2, $"rms drifted {rms[0]:E2} -> {rms[3]:E2}");
        }
    }

    private static double Rms(float[] window) => Math.Sqrt(window.Sum(x => (double)x * x) / window.Length);

    [Fact]
    public void WithTheMouthClosedOnlyAnOpenVelumLetsSoundOutThroughTheNose()
    {
        // constriction_diameter .001 at tract index 4 shuts the oral ladder; the only way out is the junction into the nasal ladder.
        string Closed(float velum) => Script(1.5f, .5f, 2.2f, velum)
            .Replace("constriction_diameter=4", "constriction_diameter=.001", StringComparison.Ordinal)
            .Replace("diameters=.04,.28,.52,.72", "diameters=.9,1.3,1.5,1.2", StringComparison.Ordinal);
        var shut = Render(Closed(.01f));
        var open = Render(Closed(1f));
        if (shut is null || open is null) return;

        output.WriteLine($"mouth shut: velum closed rms {Rms(shut):E2}, velum open rms {Rms(open):E2}; open peaks {string.Join(" ", Peaks(open).Select(p => p.ToString("F0")))}");
        Assert.True(Rms(open) > 5 * Rms(shut), $"nasal output rms {Rms(open):E2} vs velum-closed {Rms(shut):E2}");
    }
}
