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

    private static float[] Render(string script, float seconds, Func<SynthPatch, SynthPatch>? edit = null)
    {
        var parsed = PatchScript.Parse(script);
        var source = FaustEmitter.Emit(edit is null ? parsed : edit(parsed), new FaustExportOptions("stress_release", SampleRate: Rate)).Source;
        using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: Rate));
        if (!compiler.TryCompileSource(new AquaSynthCompileIdentity("stress_release", "stress_release", ""), source, seconds, out var patch, out var error))
        {
            Assert.Fail($"compile failed: {error}");
        }

        using (patch) return patch!.Render(seconds);
    }

    // The stop's release time and the start of its closure (every articulator at its shut value, after the boundary ramp into it), both from the
    // one owner in PatchScript rather than as fractions of the gesture; the consonant is [from, to).
    private static double ReleaseAt(double from, double to) => from + PatchScript.StopReleaseStartSeconds((float)(to - from));
    private static double ClosureFrom(double from) => from + PatchScript.PhoneTransitionSeconds;

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

    [FaustFact]
    public void SourcePitchRatioScalesTheRenderedF0()
    {
        var f0 = (float ratio) =>
        {
            var script = IpaGestureExperiment.BuildWordScript([(new IpaGestureExperimentTarget("w_0_a", "a", "", 0.5f, PitchRatio: ratio), 1f)], 110);
            var x = Render(script, 0.6f);
            return F0(x, 0.2, 0.5);
        };
        var unity = f0(1f);
        var up = f0(1.5f);
        output.WriteLine($"f0 at ratio 1: {unity:F1}, ratio 1.5: {up:F1}");
        Assert.InRange(unity, 108, 112);
        Assert.InRange(up, 163, 167);
    }

    [FaustFact]
    public void IntensityIsAnAmplitudeFactorOnTheRenderedLevel()
    {
        double Level(float intensity)
        {
            var script = IpaGestureExperiment.BuildWordScript([(new IpaGestureExperimentTarget("w_0_a", "a", "", 0.5f), intensity)], 110);
            var x = Render(script, 0.6f);
            return Rms(x, 0.3, 0.5);
        }

        var unity = Level(1f);
        var amplitude = WeksaWordPlan.CueFor(WeksaStress.Primary).Amplitude;
        var stressed = Level(amplitude);
        var db = 20 * Math.Log10(stressed / unity);
        output.WriteLine($"level at the primary-stress amplitude against 1: {db:F2} dB (factor wanted {20 * Math.Log10(amplitude):F2})");
        var wanted = 20 * Math.Log10(amplitude);
        Assert.InRange(db, wanted - 0.3, wanted + 0.3);
    }

    [FaustFact]
    public void PlosiveReleaseBurstIsAboveTheClosureAndBelowTheFollowingVowel()
    {
        var script = IpaGestureExperiment.BuildWordScript(
            [(new IpaGestureExperimentTarget("w_0_b", "b", "", 0.095f), 1f), (new IpaGestureExperimentTarget("w_1_a", "a", "", 0.18f), 1f)], 140);
        var withBurst = Render(script, 0.4f);
        var withoutBurst = Render(script, 0.4f, p => p with { ControlSplines = p.ControlSplines.Where(s => !s.SurfacePath.EndsWith("/release_flow", StringComparison.Ordinal)).ToArray() });
        // Everything but the burst's noise is deterministic and shared, so the difference of the two renders is the burst alone.
        var burst = withBurst.Zip(withoutBurst, (x, y) => x - y).ToArray();
        const double vowelFrom = 0.095;
        var releaseFrom = ReleaseAt(0, vowelFrom);
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

    // The burst is noise whose amplitude is release_flow times the stored pressure through the opening, and release_flow was held at its
    // gain from the plosive's gesture on, so it never ended: every later contact state made noise too. In /bin/ the "burst" alone (the
    // difference of the renders with and without release_flow) peaked at 0.0302 in the final /n/ and the tail, above its 0.0226 at the
    // release itself, and in /nibu/ the leading /n/ and /i/ carried it backward (a spline holds its first value before its first point).
    [FaustFact]
    public void PlosiveBurstExistsOnlyAroundItsOwnRelease()
    {
        IpaGestureExperimentTarget Phone(string label, string ipa, float seconds) => new($"w_{label}", ipa, "", seconds);
        var script = IpaGestureExperiment.BuildWordScript(
            [(Phone("0_n", "n", 0.095f), 1f), (Phone("1_a", "a", 0.18f), 1f), (Phone("2_b", "b", 0.095f), 1f), (Phone("3_a", "a", 0.18f), 1f), (Phone("4_n", "n", 0.095f), 1f)], 140);
        var withBurst = Render(script, 0.77f);
        var withoutBurst = Render(script, 0.77f, p => p with { ControlSplines = p.ControlSplines.Where(s => !s.SurfacePath.EndsWith("/release_flow", StringComparison.Ordinal)).ToArray() });
        var burst = withBurst.Zip(withoutBurst, (x, y) => x - y).ToArray();
        const double bFrom = 0.275, bTo = 0.37;
        var atRelease = Peak(burst, ReleaseAt(bFrom, bTo) - 0.003, bTo + 0.02);
        var leading = Peak(burst, 0, bFrom);
        var trailing = Peak(burst, bTo + 0.12, 0.77);
        output.WriteLine($"burst peak {atRelease:F4} at the release, {leading:F4} before the plosive (n, a), {trailing:F4} after it (a, n, tail)");
        Assert.True(atRelease > 0.004, "the burst is still there at the release");
        Assert.True(leading < atRelease / 20, "no burst noise before the plosive's own gesture");
        Assert.True(trailing < atRelease / 10, "no burst noise in the vowel, the nasal and the tail after the plosive");
    }

    // The burst gain's window, read off the spline itself: it exists only from the stop's release start, one ramp (0.15 of the release window)
    // before it and one after the stop's end. A gain held through the closure made the closing ramp a hiss (ClosingIntoAStopMakesNoHighFrequencyNoise).
    [Fact]
    public void PlosiveBurstGainWindowIsOneRampEitherSideOfTheRelease()
    {
        IpaGestureExperimentTarget Phone(string label, string ipa, float seconds) => new($"w_{label}", ipa, "", seconds);
        var script = IpaGestureExperiment.BuildWordScript(
            [(Phone("0_n", "n", 0.095f), 1f), (Phone("1_a", "a", 0.18f), 1f), (Phone("2_b", "b", 0.095f), 1f), (Phone("3_a", "a", 0.18f), 1f), (Phone("4_n", "n", 0.095f), 1f)], 140);
        var window = PatchScript.Parse(script).ControlSplines.Single(s => s.SurfacePath.EndsWith("/release_flow", StringComparison.Ordinal) && s.Points.Any(p => p.Value > 0));
        const double start = 0.275, duration = 0.095;
        var release = start + PatchScript.StopReleaseStartSeconds((float)duration);
        var ramp = 0.15 * (start + duration - release);
        Assert.Equal((release - ramp, start + duration + ramp), (window.Points.Min(p => (double)p.TimeSeconds), window.Points.Max(p => (double)p.TimeSeconds)), new TupleTolerance(1e-4));
    }

    // /ɑ C ɑ/ at the scaffold's own durations (vowel 0.18 s, consonant 0.095 s). Returns the render and the consonant's [start, end).
    private (float[]? Samples, double From, double To) VowelConsonantVowel(string ipa, float frequency = 140, string descriptor = "", Func<SynthPatch, SynthPatch>? edit = null)
    {
        var script = IpaGestureExperiment.BuildWordScript(
            [(new IpaGestureExperimentTarget("w_0_a", "a", "", 0.18f), 1f), (new IpaGestureExperimentTarget("w_1_c", ipa, descriptor, 0.095f), 1f), (new IpaGestureExperimentTarget("w_2_a", "a", "", 0.18f), 1f)], frequency);
        return (Render(script, 0.5f, edit), 0.18, 0.275);
    }

    // Level of the consonant's middle 60 percent against the mean power of the steady parts of the two flanking vowels (the 70 ms before the
    // consonant and 90 ms from 30 ms after it), in dB.
    private static double ConsonantDbReVowels(float[] x, double from, double to)
    {
        var v1 = Rms(x, from - 0.08, from - 0.01);
        var v2 = Rms(x, to + 0.03, to + 0.12);
        var vowels = Math.Sqrt((v1 * v1 + v2 * v2) / 2);
        var span = to - from;
        return 20 * Math.Log10(Math.Max(1e-9, Rms(x, from + 0.2 * span, to - 0.2 * span)) / vowels);
    }

    // Fujimura 1962 and later accounts put the nasal murmur well under the flanking vowels; the target band is 8 to 16 dB under (a DESIGN
    // value: no primary level was retrieved, docs/research/articulatory-vocal-tract-sources.md). Two defects held it out of the band: /n/
    // read -3.8 dB while its alveolar closure began to open at half the gesture, a third before the velum and the contact did (now held to
    // 0.8: /n/ -8.8, /m/ -12.8), and once the nasal junction sat on its own interface the nose took its share and both read -2.2 and -4.2 dB
    // until the nasal path carried its real damping (loss .975 per cell; /m/ -11.8, /n/ -9.6).
    [FaustTheory]
    [InlineData("m")]
    [InlineData("n")]
    public void NasalMurmurIsWellBelowTheFlankingVowels(string ipa)
    {
        var (x, from, to) = VowelConsonantVowel(ipa);
        var db = ConsonantDbReVowels(x, from, to);
        output.WriteLine($"/{ipa}/ murmur {db:F1} dB re the flanking vowels");
        Assert.InRange(db, -16, -8);
    }

    // The primary-stress cue as the plan applies it (amplitude and f0 ratio, same vowel length) against the unstressed vowel, over five vowels:
    // the f0 rise alone moves the harmonics against F1 and the level by -1.3 to +1.8 dB by vowel, so the amplitude factor is held to the mean
    // rendered result, 2 dB (Sluijter and van Heuven 1996: 1 to 3 dB overall). Measured 1.7 dB with the amplitude at 1.26 (outside the band), 2.0 dB at 1.30.
    [FaustFact]
    public void PrimaryStressRendersAboutTwoDecibelsLouderOnAverageOverVowels()
    {
        var cue = WeksaWordPlan.CueFor(WeksaStress.Primary);
        var diffs = new List<double>();
        foreach (var vowel in new[] { "i", "a", "u", "e", "o" })
        {
            double Level(float ratio, float intensity)
            {
                var script = IpaGestureExperiment.BuildWordScript([(new IpaGestureExperimentTarget($"w_0_{vowel}", vowel, "", 0.25f, PitchRatio: ratio), intensity)], 140, 0.2f);
                var x = Render(script, 0.45f);
                return Rms(x, 0.12, 0.22);
            }

            var flat = Level(1f, 1f);
            var db = 20 * Math.Log10(Level(cue.PitchRatio, cue.Amplitude) / flat);
            output.WriteLine($"/{vowel}/ stressed against unstressed: {db:F2} dB");
            diffs.Add(db);
        }

        output.WriteLine($"mean {diffs.Average():F2} dB");
        Assert.InRange(diffs.Average(), 1.8, 2.3);
    }

    // Standard deviation, so the slow offset a shutting tract leaves behind does not count as sound.
    private static double Deviation(float[] x, double from, double to)
    {
        var seg = x.Skip((int)(from * Rate)).Take((int)((to - from) * Rate)).Select(v => (double)v).ToArray();
        var mean = seg.Average();
        return Math.Sqrt(seg.Sum(v => (v - mean) * (v - mean)) / seg.Length);
    }

    // A word-initial plosive's closure (0.03 to 0.058 s of a 0.095 s consonant shut to 0.062 s) against the vowel after it (0.15 to 0.25 s). The
    // mouth is shut, so all that gets out is the tract wall radiation of the voiced source: the voice bar, about 30 dB under the vowel for /b/
    // (a DESIGN value, no primary level retrieved; Stevens 1998 per secondary sources has voicing weaker as transglottal pressure falls), and
    // near-silent for /p/, whose source is weak. The closure was 0.0000 (below 16 bits) before the wall radiation.
    [FaustTheory]
    [InlineData("b", -34, -26)]
    [InlineData("p", -60, -38)]
    [InlineData("d", -34, -26)]
    public void PlosiveClosureCarriesTheVoiceBarOnlyWhenVoiced(string ipa, double lowDb, double highDb)
    {
        var db = VoiceBarDb(ipa);
        if (double.IsNaN(db)) return;
        output.WriteLine($"/{ipa}/ closure {db:F1} dB re the following vowel");
        Assert.InRange(db, lowDb, highDb);
    }

    // The closure's deviation (the slow offset of a shutting tract is not sound) against the following vowel's, for a word-initial consonant.
    private double VoiceBarDb(string ipa)
    {
        var script = IpaGestureExperiment.BuildWordScript(
            [(new IpaGestureExperimentTarget("w_0_c", ipa, "", 0.095f), 1f), (new IpaGestureExperimentTarget("w_1_a", "a", "", 0.18f), 1f)], 140);
        var x = Render(script, 0.4f);
        return x is null ? double.NaN : 20 * Math.Log10(Math.Max(1e-9, Deviation(x, 0.03, 0.058)) / Deviation(x, 0.15, 0.25));
    }

    private static SynthPatch WithoutBurst(SynthPatch p) =>
        p with { ControlSplines = p.ControlSplines.Where(s => !s.SurfacePath.EndsWith("/release_flow", StringComparison.Ordinal)).ToArray() };

    // The closure of a stop is the part after the boundary ramp into it and before its release, without the burst (the ramp is the tract still
    // shutting on the vowel's decaying voicing; its noise is measured by ClosingIntoAStopMakesNoHighFrequencyNoise).
    // A tongue closure at the nasal's 0.03 cm aperture leaked: /t/ read -14.3 dB re the flanking vowels against /p/ -22.1 (the lips seal /p/);
    // completely closed it reads -24.3.
    [Fact]
    public void ATongueSealedStopClosureIsAsQuietAsTheLipSealedOne()
    {
        double Closure(string ipa, string descriptor)
        {
            var (x, from, to) = VowelConsonantVowel(ipa, descriptor: descriptor, edit: WithoutBurst);
            if (x is null) return double.NaN;
            var vowels = Math.Sqrt((Math.Pow(Rms(x, from - 0.08, from - 0.01), 2) + Math.Pow(Rms(x, to + 0.03, to + 0.12), 2)) / 2);
            return 20 * Math.Log10(Rms(x, ClosureFrom(from), ReleaseAt(from, to)) / vowels);
        }

        var p = Closure("p", "voiceless_bilabial_plosive");
        var t = Closure("t", "voiceless_alveolar_plosive");
        output.WriteLine($"closure re vowels: /p/ {p:F1} dB, /t/ {t:F1} dB");
        if (double.IsNaN(p)) return;
        Assert.True(t <= p + 1, $"the /t/ closure ({t:F1} dB) is as quiet as /p/ ({p:F1} dB)");
    }

    [Fact]
    public void ATongueSealedStopBurstStaysUnderHalfTheFollowingVowel()
    {
        var (x, from, to) = VowelConsonantVowel("t", descriptor: "voiceless_alveolar_plosive");
        var (nb, _, _) = VowelConsonantVowel("t", descriptor: "voiceless_alveolar_plosive", edit: WithoutBurst);
        if (x is null || nb is null) return;
        var burst = x.Zip(nb, (a, b) => a - b).ToArray();
        var peak = Peak(burst, ReleaseAt(from, to) - 0.003, to + 0.01);
        var vowel = Peak(x, to + 0.06, to + 0.17);
        output.WriteLine($"/t/ burst peak {peak:F4}, vowel peak {vowel:F4}, ratio {peak / vowel:F2}");
        Assert.True(peak > 0.004, "audible");
        Assert.True(peak < vowel / 2, "under half the following vowel's peak, as /b/");
    }

    // The /b/ test over the alveolar stop, by its table entry (before the entry the phone was unsupported, and a hand script approximated it).
    [Fact]
    public void AlveolarStopBurstExistsOnlyAroundItsOwnRelease_t()
    {
        IpaGestureExperimentTarget Phone(string label, string ipa, float seconds) => new($"w_{label}", ipa, "", seconds);
        var script = IpaGestureExperiment.BuildWordScript(
            [(Phone("0_n", "n", 0.095f), 1f), (Phone("1_a", "a", 0.18f), 1f), (Phone("2_t", "t", 0.095f), 1f), (Phone("3_a", "a", 0.18f), 1f), (Phone("4_n", "n", 0.095f), 1f)], 140);
        var withBurst = Render(script, 0.77f);
        var withoutBurst = Render(script, 0.77f, WithoutBurst);
        if (withBurst is null || withoutBurst is null) return;
        var burst = withBurst.Zip(withoutBurst, (x, y) => x - y).ToArray();
        const double tFrom = 0.275, tTo = 0.37;
        var atRelease = Peak(burst, ReleaseAt(tFrom, tTo) - 0.003, tTo + 0.02);
        var leading = Peak(burst, 0, tFrom);
        var trailing = Peak(burst, tTo + 0.12, 0.77);
        output.WriteLine($"/t/ burst peak {atRelease:F4} at the release, {leading:F5} before the plosive, {trailing:F5} after it");
        Assert.True(atRelease > 0.004, "the burst is a transient of audible size");
        Assert.True(leading < atRelease / 20, "no burst before the plosive's own gesture");
        Assert.True(trailing < atRelease / 10, "no burst after the plosive");
    }

    // Chodroff and Wilson 2014: /t/ burst peak 5216 Hz, /k/ 2868. The band 3.5 to 7 kHz is a DESIGN tolerance around the /t/ figure.
    // Peak of the isolated burst's spectrum from 1 kHz up (the weak_spots.burst_spectrum definition: 20 ms Hann window from the release, 400 Hz smoothing).
    [Fact]
    public void StopTBurstSpectrumIsInTheLiteratureBand()
    {
        var (x, from, to) = VowelConsonantVowel("t");
        var (nb, _, _) = VowelConsonantVowel("t", edit: WithoutBurst);
        if (x is null || nb is null) return;
        var start = (int)(ReleaseAt(from, to) * Rate);
        const int n = 960;
        var seg = Enumerable.Range(0, n).Select(i => (x[start + i] - nb[start + i]) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)))).ToArray();
        double Power(double hz)
        {
            double re = 0, im = 0;
            for (var i = 0; i < n; i++) { var a = 2 * Math.PI * hz * i / Rate; re += seg[i] * Math.Cos(a); im -= seg[i] * Math.Sin(a); }
            return re * re + im * im;
        }

        var grid = Enumerable.Range(0, 180).Select(k => 1000.0 + 50 * k).ToArray();
        var power = grid.Select(Power).ToArray();
        var smooth = power.Select((_, k) => Enumerable.Range(Math.Max(0, k - 4), Math.Min(9, power.Length - Math.Max(0, k - 4))).Average(j => power[j])).ToArray();
        var peak = grid[Array.IndexOf(smooth, smooth.Max())];
        output.WriteLine($"/t/ burst spectral peak {peak:F0} Hz");
        Assert.InRange(peak, 3500, 7000);
    }

    // /k/ over the /t/ tests: its own place (the velar distance in the place table), closure sealed by the tongue, burst under half the vowel.
    // The spec asks /k/'s closure to be at or below /p/'s. It is not: with the voiceless source as /p/ and /t/ have it the closure reads
    // -14.9 dB re the vowels (/p/ -20.4, /t/ -24.9). The tract itself seals: with the glottal source's flow scale held at 0 over the gesture the
    // same closure reads -22.7 dB, under /p/; with the source on, the 150 Hz voicing is what reaches the output through a closure that leaves
    // only a short rear cavity (the glottal pressure that the tract wall radiates is higher the smaller that cavity is). Silencing voicing in
    // voiceless stops would change /p/ and /t/ (pinned byte for byte), so the criterion is reported as unmet (fork), not met by a /k/ special case.
    // The second assertion is a regression bound on the measured leak, not the criterion.
    [Fact]
    public void AVelarStopClosureSealsAndItsLeakIsTheGlottalSource()
    {
        static SynthPatch WithoutSource(SynthPatch p) =>
            WithoutBurst(p) with
            {
                ControlSplines = WithoutBurst(p).ControlSplines
                    .Select(s => s.Name.StartsWith("w_1_c_source_level", StringComparison.Ordinal)
                        ? s with { Points = s.Points.Select(q => q with { Value = 0, OutValue = 0, InValue = 0 }).ToArray() }
                        : s).ToArray()
            };

        double Closure(string ipa, string descriptor, Func<SynthPatch, SynthPatch> edit)
        {
            var (x, from, to) = VowelConsonantVowel(ipa, descriptor: descriptor, edit: edit);
            if (x is null) return double.NaN;
            var vowels = Math.Sqrt((Math.Pow(Rms(x, from - 0.08, from - 0.01), 2) + Math.Pow(Rms(x, to + 0.03, to + 0.12), 2)) / 2);
            return 20 * Math.Log10(Rms(x, ClosureFrom(from), ReleaseAt(from, to)) / vowels);
        }

        var p = Closure("p", "voiceless_bilabial_plosive", WithoutBurst);
        var k = Closure("k", "voiceless_velar_plosive", WithoutBurst);
        var kSilent = Closure("k", "voiceless_velar_plosive", WithoutSource);
        output.WriteLine($"closure re vowels: /p/ {p:F1} dB, /k/ {k:F1} dB, /k/ with the glottal source off {kSilent:F1} dB");
        if (double.IsNaN(p)) return;
        Assert.True(kSilent <= p, $"the /k/ closure seals: without the glottal source it is {kSilent:F1} dB, at or under /p/ ({p:F1} dB)");
        Assert.True(k <= -12, $"the /k/ closure with the source is {k:F1} dB (regression bound; the criterion, at or under /p/ {p:F1} dB, is not met)");
    }

    [Fact]
    public void AVelarStopBurstStaysUnderHalfTheFollowingVowel()
    {
        var (x, from, to) = VowelConsonantVowel("k", descriptor: "voiceless_velar_plosive");
        var (nb, _, _) = VowelConsonantVowel("k", descriptor: "voiceless_velar_plosive", edit: WithoutBurst);
        if (x is null || nb is null) return;
        var burst = x.Zip(nb, (a, b) => a - b).ToArray();
        var peak = Peak(burst, ReleaseAt(from, to) - 0.003, to + 0.01);
        var vowel = Peak(x, to + 0.06, to + 0.17);
        output.WriteLine($"/k/ burst peak {peak:F4}, vowel peak {vowel:F4}, ratio {peak / vowel:F2}");
        Assert.True(peak > 0.004, "audible");
        Assert.True(peak < vowel / 2, "under half the following vowel's peak, as /t/ and /b/");
    }

    [Fact]
    public void VelarStopBurstExistsOnlyAroundItsOwnRelease_k()
    {
        IpaGestureExperimentTarget Phone(string label, string ipa, float seconds) => new($"w_{label}", ipa, "", seconds);
        var script = IpaGestureExperiment.BuildWordScript(
            [(Phone("0_n", "n", 0.095f), 1f), (Phone("1_a", "a", 0.18f), 1f), (Phone("2_k", "k", 0.095f), 1f), (Phone("3_a", "a", 0.18f), 1f), (Phone("4_n", "n", 0.095f), 1f)], 140);
        var withBurst = Render(script, 0.77f);
        var withoutBurst = Render(script, 0.77f, WithoutBurst);
        if (withBurst is null || withoutBurst is null) return;
        var burst = withBurst.Zip(withoutBurst, (x, y) => x - y).ToArray();
        const double kFrom = 0.275, kTo = 0.37;
        var atRelease = Peak(burst, ReleaseAt(kFrom, kTo) - 0.003, kTo + 0.02);
        var leading = Peak(burst, 0, kFrom);
        var trailing = Peak(burst, kTo + 0.12, 0.77);
        output.WriteLine($"/k/ burst peak {atRelease:F4} at the release, {leading:F5} before the plosive, {trailing:F5} after it");
        Assert.True(atRelease > 0.004, "the burst is a transient of audible size");
        Assert.True(leading < atRelease / 20, "no burst before the plosive's own gesture");
        Assert.True(trailing < atRelease / 10, "no burst after the plosive");
    }

    // Chodroff and Wilson 2014: /k/ burst peak 2868 Hz. The band 2 to 4 kHz is a DESIGN tolerance around it, fixed before the first measurement.
    // The peak is judged against the literature and against /t/ measured the same way (a velar burst enters further back, so its peak is lower).
    // The centroid is reported, not asserted: over this 1 to 10 kHz grid /k/ reads 4626 Hz against /t/ 3629 Hz, higher though its peak is lower,
    // because the /k/ burst has broadband energy above its peak, and no literature centroid for /k/ was retrieved to judge it by.
    private (double Peak, double Centroid) BurstSpectrum(string ipa, string descriptor)
    {
        var (x, from, to) = VowelConsonantVowel(ipa, descriptor: descriptor);
        var (nb, _, _) = VowelConsonantVowel(ipa, descriptor: descriptor, edit: WithoutBurst);
        if (x is null || nb is null) return (double.NaN, double.NaN);
        var start = (int)(ReleaseAt(from, to) * Rate);
        const int n = 960;
        var seg = Enumerable.Range(0, n).Select(i => (x[start + i] - nb[start + i]) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)))).ToArray();
        double Power(double hz)
        {
            double re = 0, im = 0;
            for (var i = 0; i < n; i++) { var a = 2 * Math.PI * hz * i / Rate; re += seg[i] * Math.Cos(a); im -= seg[i] * Math.Sin(a); }
            return re * re + im * im;
        }

        var grid = Enumerable.Range(0, 180).Select(k => 1000.0 + 50 * k).ToArray();
        var power = grid.Select(Power).ToArray();
        var smooth = power.Select((_, k) => Enumerable.Range(Math.Max(0, k - 4), Math.Min(9, power.Length - Math.Max(0, k - 4))).Average(j => power[j])).ToArray();
        return (grid[Array.IndexOf(smooth, smooth.Max())], grid.Zip(power, (f, w) => f * w).Sum() / power.Sum());
    }

    [Fact]
    public void StopKBurstPeakIsInTheLiteratureBandAndLowerThanT()
    {
        var (peak, centroid) = BurstSpectrum("k", "voiceless_velar_plosive");
        if (double.IsNaN(peak)) return;
        var (tPeak, tCentroid) = BurstSpectrum("t", "voiceless_alveolar_plosive");
        output.WriteLine($"/k/ burst spectral peak {peak:F0} Hz, centroid {centroid:F0} Hz; /t/ peak {tPeak:F0} Hz, centroid {tCentroid:F0} Hz");
        Assert.InRange(peak, 2000, 4000);
        Assert.True(peak < tPeak, $"/k/ peak {peak:F0} Hz is under /t/ {tPeak:F0} Hz");
    }

    // /d/ is /t/ with voicing, as /b/ is /p/ with voicing: the same gesture, so the splines that shape the tract are identical and the two differ
    // in the voice bar through the closure and in nothing else the burst carries. Chodroff and Wilson 2014: /d/ burst peak 4878 Hz, /t/ 5216.
    [Fact]
    public void StopDDiffersFromStopTOnlyByVoicing()
    {
        static SynthPatch Word(string ipa) => PatchScript.Parse(IpaGestureExperiment.BuildWordScript(
            [(new IpaGestureExperimentTarget("w_0_a", "a", "", 0.18f), 1f), (new IpaGestureExperimentTarget("w_1_c", ipa, "", 0.095f), 1f), (new IpaGestureExperimentTarget("w_2_a", "a", "", 0.18f), 1f)], 140));
        static string[] Shape(SynthPatch p) => p.ControlSplines
            .Where(s => s.Name.StartsWith("w_1_c", StringComparison.Ordinal) && !s.Name.StartsWith("w_1_c_source", StringComparison.Ordinal))
            .Select(s => s.SurfacePath + "|" + s.Name[5..] + "|" + string.Join(";", s.Points.Select(q => $"{q.TimeSeconds:R}:{q.Value:R}")))
            .Order().ToArray();
        var t = Shape(Word("t"));
        var d = Shape(Word("d"));
        output.WriteLine($"/t/ {t.Length} consonant splines, /d/ {d.Length}");
        Assert.NotEmpty(d);
        Assert.Equal(t, d);

        var tDb = VoiceBarDb("t");
        var dDb = VoiceBarDb("d");
        output.WriteLine($"closure re the following vowel: /t/ {tDb:F1} dB, /d/ {dDb:F1} dB");
        if (double.IsNaN(tDb)) return;
        Assert.True(dDb >= tDb + 8, $"the voice bar lifts the /d/ closure ({dDb:F1} dB) at least 8 dB over /t/ ({tDb:F1} dB)");
        var (dPeak, _) = BurstSpectrum("d", "");
        var (tPeak, _) = BurstSpectrum("t", "");
        output.WriteLine($"burst spectral peak: /t/ {tPeak:F0} Hz, /d/ {dPeak:F0} Hz");
        Assert.True(Math.Abs(dPeak - tPeak) <= 1000, $"/d/ burst peak {dPeak:F0} Hz within 1 kHz of /t/ {tPeak:F0} Hz");
    }

    [Fact]
    public void StopDBurstExistsOnlyAroundItsOwnRelease()
    {
        IpaGestureExperimentTarget Phone(string label, string ipa, float seconds) => new($"w_{label}", ipa, "", seconds);
        var script = IpaGestureExperiment.BuildWordScript(
            [(Phone("0_n", "n", 0.095f), 1f), (Phone("1_a", "a", 0.18f), 1f), (Phone("2_d", "d", 0.095f), 1f), (Phone("3_a", "a", 0.18f), 1f), (Phone("4_n", "n", 0.095f), 1f)], 140);
        var withBurst = Render(script, 0.77f);
        var withoutBurst = Render(script, 0.77f, WithoutBurst);
        if (withBurst is null || withoutBurst is null) return;
        var burst = withBurst.Zip(withoutBurst, (x, y) => x - y).ToArray();
        const double dFrom = 0.275, dTo = 0.37;
        var atRelease = Peak(burst, ReleaseAt(dFrom, dTo) - 0.003, dTo + 0.02);
        var leading = Peak(burst, 0, dFrom);
        var trailing = Peak(burst, dTo + 0.12, 0.77);
        output.WriteLine($"/d/ burst peak {atRelease:F4} at the release, {leading:F5} before the plosive, {trailing:F5} after it");
        Assert.True(atRelease > 0.004, "the burst is a transient of audible size");
        Assert.True(leading < atRelease / 20, "no burst before the plosive's own gesture");
        Assert.True(trailing < atRelease / 10, "no burst after the plosive");
    }

    // /l/ over /ɑ _ ɑ/ at the scaffold's durations. Envelope of the consonant's middle 60 percent at the harmonics of f0 (the largest power within a
    // quarter of f0 of k * f0), in dB: the spectrum of a periodic source is a line spectrum, so extrema are read from the harmonic envelope.
    private (double[] Hz, double[] Db) HarmonicEnvelope(float[] x, double from, double to, double f0 = 140, double maxHz = 5000)
    {
        var a = (int)((from + 0.2 * (to - from)) * Rate);
        var n = (int)(0.6 * (to - from) * Rate);
        var seg = Enumerable.Range(0, n).Select(i => x[a + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)))).ToArray();
        double Power(double hz)
        {
            double re = 0, im = 0;
            for (var i = 0; i < n; i++) { var w = 2 * Math.PI * hz * i / Rate; re += seg[i] * Math.Cos(w); im -= seg[i] * Math.Sin(w); }
            return re * re + im * im;
        }

        var hz = new List<double>();
        var db = new List<double>();
        for (var k = 1; k * f0 < maxHz; k++)
        {
            hz.Add(k * f0);
            db.Add(10 * Math.Log10(Math.Max(1e-30, Enumerable.Range(-3, 7).Max(j => Power(k * f0 + j * f0 / 12)))));
        }

        return (hz.ToArray(), db.ToArray());
    }

    // The lateral's own dip: the envelope with the side stub open less the same render with it shut, at its lowest between 2 and 3 kHz. The
    // difference isolates the stub's antiresonance from the valleys between the vowel's formants, which the shut render has as deep.
    private static (double DipHz, double Depth) LateralDip(double[] hz, double[] db, double[] shutDb)
    {
        var i = Enumerable.Range(0, hz.Length).Where(j => hz[j] >= 2000 && hz[j] <= 3000).MinBy(j => db[j] - shutDb[j]);
        return (hz[i], shutDb[i] - db[i]);
    }

    // The lateral's antiresonance: the closed side stub beside the oral path (sublingual cavity). With the stub open the 2 to 3 kHz harmonics fall at
    // least 6 dB under the same render with it shut (a DESIGN criterion; no /l/ spectrum was retrieved). The lateral is continuous: the render with
    // and without release_flow is the same render.
    [Fact]
    public void LateralHasAnAntiresonanceAndNoBurst()
    {
        var (x, from, to) = VowelConsonantVowel("l");
        if (x is null) return;
        var (hz, db) = HarmonicEnvelope(x, from, to);
        var (shut, _, _) = VowelConsonantVowel("l", edit: WithShutSideStub);
        var (_, shutDb) = HarmonicEnvelope(shut!, from, to);
        var (dipHz, depth) = LateralDip(hz, db, shutDb);
        var (nb, _, _) = VowelConsonantVowel("l", edit: WithoutBurst);
        output.WriteLine($"/l/ stub open {depth:F1} dB under stub shut at {dipHz:F0} Hz (2-3 kHz); max |with - without release_flow| {x.Zip(nb!, (a, b) => Math.Abs(a - b)).Max():G3}");
        Assert.True(depth >= 6, $"the 2-3 kHz dip is {depth:F1} dB under the shut-stub render");
        Assert.Equal(x, nb);
    }

    private static SynthPatch WithShutSideStub(SynthPatch p) =>
        p with { ControlSplines = p.ControlSplines.Where(s => s.SurfacePath != "/vocal/branches/1/opening").ToArray() };

    // Between the vowels and the nasals in level: -4 to -10 dB re the flanking vowels (a DESIGN band; /n/ reads -9.6, /m/ -11.8 in the same measure).
    [Fact]
    public void LateralLevelIsBetweenTheVowelsAndTheNasals()
    {
        var (x, from, to) = VowelConsonantVowel("l");
        if (x is null) return;
        var db = ConsonantDbReVowels(x, from, to);
        output.WriteLine($"/l/ {db:F1} dB re the flanking vowels");
        Assert.InRange(db, -10, -4);
    }
}

// Reports Skipped, not Passed, when the Faust toolchain is absent: a test that returns early reads as a pass nobody earned.
public sealed class FaustFactAttribute : FactAttribute
{
    public FaustFactAttribute()
    {
        if (AquaSynthFaustToolchain.Find() is null) Skip = "Faust toolchain not found";
    }
}

public sealed class FaustTheoryAttribute : TheoryAttribute
{
    public FaustTheoryAttribute()
    {
        if (AquaSynthFaustToolchain.Find() is null) Skip = "Faust toolchain not found";
    }
}

public sealed class FaustSkipTests
{
    [Fact]
    public void StressTestsSkipWithoutFaust()
    {
        var toolchain = AquaSynthFaustToolchain.Find();
        Assert.Equal(toolchain is null, new FaustFactAttribute().Skip is not null);
        Assert.Equal(toolchain is null, new FaustTheoryAttribute().Skip is not null);
    }
}

file sealed class TupleTolerance(double eps) : IEqualityComparer<(double, double)>
{
    public bool Equals((double, double) x, (double, double) y) => Math.Abs(x.Item1 - y.Item1) < eps && Math.Abs(x.Item2 - y.Item2) < eps;
    public int GetHashCode((double, double) value) => 0;
}
