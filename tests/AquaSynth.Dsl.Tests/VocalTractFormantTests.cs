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

    // The authored vowel gestures (PatchScript's VowelShapes, reached through the IPA word script) rendered end to end through the
    // exporter, merged splines and the ladder; the source is a bright 10 Hz saw so the tract's response is sampled densely.
    private double[]? GesturePeaks(string ipa)
    {
        var script = IpaGestureExperiment.BuildWordScript([(new IpaGestureExperimentTarget($"v_{ipa}", ipa, "", 1.5f), 1f)], Pitch, 0.1f)
            .Replace("noise=.04", "noise=0 tilt=1", StringComparison.Ordinal)
            .Replace($"freq={Pitch}", $"freq={Pitch} min_freq=5", StringComparison.Ordinal);
        var window = Render(script);
        if (window is null) return null;
        var peaks = LpcPeaks(window);
        output.WriteLine($"gesture {ipa}: peaks " + string.Join(" ", peaks.Select(p => p.ToString("F0"))));
        Assert.True(peaks.Length >= 2, $"{ipa}: fewer than two spectral peaks");
        return peaks;
    }

    // Formant candidates as the peaks of an LPC envelope (order 12 on the 12 kHz decimation of a 0.25 s steady stretch, pre-emphasis
    // .97), the same recipe the render measurements use: the envelope sees through the harmonic lines of the voiced source.
    private static double[] LpcPeaks(float[] window)
    {
        const int Decimate = 4, Order = 12;
        var taps = Enumerable.Range(0, 4 * Decimate + 1).Select(n => 0.5 - 0.5 * Math.Cos(2 * Math.PI * (n + 1) / (4 * Decimate + 2))).ToArray();
        var norm = taps.Sum();
        var x = window.Skip(Rate / 4).Take(Rate / 4).Select(v => (double)v).ToArray();
        var mean = x.Average();
        var z = new List<double>();
        for (var n = 2 * Decimate; n < x.Length - 2 * Decimate; n += Decimate)
        {
            double acc = 0;
            for (var k = 0; k < taps.Length; k++) acc += taps[k] * (x[n + k - 2 * Decimate] - mean);
            z.Add(acc / norm);
        }

        var y = new double[z.Count];
        for (var n = 0; n < y.Length; n++) y[n] = (z[n] - (n > 0 ? .97 * z[n - 1] : 0)) * (0.54 - 0.46 * Math.Cos(2 * Math.PI * n / (y.Length - 1)));
        var r = Enumerable.Range(0, Order + 1).Select(lag => Enumerable.Range(0, y.Length - lag).Sum(n => y[n] * y[n + lag])).ToArray();
        r[0] *= 1 + 1e-9;
        var a = new double[Order + 1];
        a[0] = 1;
        var err = r[0];
        for (var m = 1; m <= Order; m++)
        {
            var acc = r[m];
            for (var k = 1; k < m; k++) acc += a[k] * r[m - k];
            var kappa = -acc / err;
            var next = (double[])a.Clone();
            for (var k = 1; k < m; k++) next[k] = a[k] + kappa * a[m - k];
            next[m] = kappa;
            a = next;
            err *= 1 - kappa * kappa;
        }

        var fs = Rate / (double)Decimate;
        double Envelope(double hz)
        {
            double re = 0, im = 0;
            for (var k = 0; k <= Order; k++) { re += a[k] * Math.Cos(2 * Math.PI * hz / fs * k); im -= a[k] * Math.Sin(2 * Math.PI * hz / fs * k); }
            return 1 / Math.Sqrt(re * re + im * im);
        }

        var peaks = new List<double>();
        var previous = Envelope(150);
        var current = Envelope(160);
        for (var hz = 170.0; hz < 4500; hz += 10)
        {
            var next = Envelope(hz);
            if (current > previous && current >= next) peaks.Add(hz - 10);
            (previous, current) = (current, next);
        }

        return peaks.ToArray();
    }

    // Measured through the exported ladder on 2026-11-08 (impulse peaks i 271/2313, u 292/876, a 703/1084; the textbook values are
    // i 270/2290, u 300/870, a 730/1090). The envelope of the 10 Hz render reads i 270/2380 and a 690/1060.
    public static TheoryData<string, double, double> GestureVowels => new()
    {
        { "i", 271, 2313 },
        { "a", 703, 1084 },
    };

    [Theory]
    [MemberData(nameof(GestureVowels))]
    public void AuthoredVowelGesturesPlaceF1AndF2NearTheTextbook(string ipa, double f1, double f2)
    {
        var peaks = GesturePeaks(ipa);
        if (peaks is null) return;

        Assert.InRange(peaks[0], f1 * .9, f1 * 1.1);
        Assert.InRange(peaks[1], f2 * .9, f2 * 1.1);
    }

    [Fact]
    public void AuthoredRoundedBackVowelPlacesF2NearTheTextbook()
    {
        // u's F1 (about 290 Hz) is under the order-12 envelope's resolution; its F2 is the lowest peak it resolves.
        var peaks = GesturePeaks("u");
        if (peaks is null) return;

        Assert.InRange(peaks[0], 876 * .9, 876 * 1.1);
    }

    [Fact]
    public void AuthoredVowelGesturesDifferInTheirFormants()
    {
        var i = GesturePeaks("i");
        var u = GesturePeaks("u");
        var a = GesturePeaks("a");
        if (i is null || u is null || a is null) return;

        Assert.True(a[0] > i[0] + 250, $"F1 i {i[0]} a {a[0]}");
        // F2: i 2380 > a 1060 > u 840.
        Assert.True(i[1] > a[1] + 800 && a[1] > u[0] + 100, $"F2 i {i[1]} a {a[1]} u {u[0]}");
    }
}
