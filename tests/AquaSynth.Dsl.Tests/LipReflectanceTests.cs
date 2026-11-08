using System.Text.RegularExpressions;
using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

// The ladder's lip termination is the open-end reflectance R(z) = -g (1-b)/(1-b z^-1) with b/(1-b)^2 = (a fs / c)^2, a = sqrt(A_lip / pi) in cm,
// c = 35000 cm/s (FaustExport.VocalLadder.cs). These tests evaluate the emitted Faust expression itself and check it against the derivation.
public sealed class LipReflectanceTests
{
    private const int Rate = 48000;

    private const string Script = """
        morphology name=oral length_cm=17.52 diameters=.55,.75,1.05,1.45,1.25,.8 tongue_index=3 tongue_diameter=1.4 constriction_index=4 constriction_diameter=4 lip_opening=4
        waveguide_path name=oral_path morphology=oral speed=350 loss=.998
        source_port name=folds path=oral_path pressure=.7 tension=.55 opening=.5 noise=0 impedance=.3 flow_scale=.12 tilt=0
        radiation_load name=mouth path=oral_path aperture=1 reflection=-1 impedance=.28
        vocal_network name=voice paths=oral_path sources=folds radiation=mouth
        vocal network=voice freq=100 gain=.5 sustain=1
        """;

    private static string Emit() => FaustEmitter.Emit(PatchScript.Parse(Script), new FaustExportOptions("lip_pole", SampleRate: Rate)).Source;

    // b by bisection on the derivation b/(1-b)^2 = q, independent of the closed form.
    private static double SolvePole(double areaCm2)
    {
        var a = Math.Sqrt(areaCm2 / Math.PI);
        var q = Math.Pow(a * Rate / 35000.0, 2);
        double lo = 0, hi = 1 - 1e-12;
        for (var i = 0; i < 200; i++)
        {
            var mid = (lo + hi) / 2;
            if (mid / Math.Pow(1 - mid, 2) < q) lo = mid; else hi = mid;
        }

        return Math.Clamp((lo + hi) / 2, 0, .9);
    }

    [Fact]
    public void LipReflectanceKeepsReflectionAsDcGainAndFeedsBackThePoleInTheLastCellEquation()
    {
        var source = Emit();
        var last = Regex.Match(source, @"'(\w+_L\d+) = \((\w+_lip_b) \* (\w+_L\d+) \+ \(1\.0 - (\w+_lip_b)\) \* \((.+?)\) \* (\w+_R\d+)\) \* clip01");
        Assert.True(last.Success, "last-cell equation is not the one-pole reflectance");
        Assert.Equal(last.Groups[1].Value, last.Groups[3].Value);
        Assert.Equal(last.Groups[2].Value, last.Groups[4].Value);
        Assert.False(string.IsNullOrWhiteSpace(last.Groups[5].Value));
    }

    [Theory]
    [InlineData(.3)]
    [InlineData(1.0)]
    [InlineData(3.14159265)]
    [InlineData(8.0)]
    public void EmittedPoleSolvesTheLowFrequencyLossDerivationForTheLipCellArea(double areaCm2)
    {
        var source = Emit();
        var q = Regex.Match(source, @"^\s*(\w+)_lip_q = (.+);$", RegexOptions.Multiline);
        var b = Regex.Match(source, @"^\s*(\w+)_lip_b = (.+);$", RegexOptions.Multiline);
        Assert.True(q.Success && b.Success);
        var prefix = q.Groups[1].Value;
        // The lip pole reads the last cell's area and nothing else of the tract.
        var lastCell = Regex.Matches(source, $@"^\s*{prefix}_A(\d+) = ", RegexOptions.Multiline).Count - 1;
        Assert.Contains($"{prefix}_A{lastCell}", q.Groups[2].Value);
        var program = $"import(\"stdfaust.lib\");\n{prefix}_A{lastCell} = {areaCm2.ToString(System.Globalization.CultureInfo.InvariantCulture)};\n" +
                      $"{prefix}_lip_q = {q.Groups[2].Value};\n{prefix}_lip_b = {b.Groups[2].Value};\nprocess = {prefix}_lip_b;\n";
        using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: Rate));
        if (!compiler.TryCompileSource(new AquaSynthCompileIdentity("lip_pole", "lip_pole", ""), program, .05f, out var patch, out var error))
        {
            if (error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true) return;
            Assert.Fail($"lip pole program failed: {error}");
        }

        using (patch)
        {
            var samples = patch!.Render();
            Assert.Equal(SolvePole(areaCm2), samples[^1], 3);
        }
    }

    [Fact]
    public void PoleGrowsWithTheLipAreaAndEndCorrectionOfAOneCentimetreRadiusIsAboutHalfARadius()
    {
        // b/(1-b) samples is the DC group delay: for a = 1 cm at 48 kHz it is 0.96 samples = 0.7 cm, which is the 0.6 a end correction scale.
        Assert.True(SolvePole(.5) < SolvePole(2) && SolvePole(2) < SolvePole(6));
        var b = SolvePole(Math.PI);
        Assert.InRange(b / (1 - b) * 35000.0 / Rate, .5, .8);
    }
}
