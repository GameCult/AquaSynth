using System.Globalization;
using System.Text.RegularExpressions;
using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

// The nasal side branch: the cited nasal tract, the velopharyngeal port area, and where the scaffold's nasal and bilabial/alveolar gestures
// close the mouth. The murmur itself is measured in VocalTractFormantTests (renders).
public sealed class NasalCouplingTests
{
    // Birkholz 2005 Table 2.3 after Dang & Honda 1994, copied here independently of NasalTractAreas: 19 sections of 0.6 cm, cm^2,
    // velopharyngeal end first.
    private static readonly float[] CitedAreas =
    [
        1.63f, 2.07f, 2.72f, 3.59f, 4.24f, 3.26f, 3.04f, 3.04f, 2.72f, 2.5f, 2.39f, 2.39f, 1.85f, 0.76f, 1.41f, 1.74f, 1.30f, 1.74f, 0.76f,
    ];

    [Fact]
    public void TheNasalTractTableIsTheCitedNineteenSections()
    {
        Assert.Equal(CitedAreas, NasalTractAreas.Areas.ToArray());
        Assert.Equal(11.4f, NasalTractAreas.LengthCentimeters, 4);
        Assert.Equal(NasalTractAreas.LengthCentimeters, CitedAreas.Length * NasalTractAreas.SectionLengthCentimeters, 3);
    }

    [Fact]
    public void TheScaffoldNasalMorphologyIsTheCitedTableAtItsLength()
    {
        var patch = PatchScript.Parse(IpaGestureExperiment.BuildWordScript([(new IpaGestureExperimentTarget("v_m", "m", "", .2f), 1f)]));
        var nasal = patch.AreaFunctions.Single(area => area.Name == "nasal").Shape;

        Assert.Equal(11.4f, nasal.LengthCentimeters, 3);
        Assert.Equal(CitedAreas.Length, nasal.Diameters.Count);
        for (var i = 0; i < CitedAreas.Length; i++)
        {
            Assert.Equal(CitedAreas[i], nasal.Diameters[i] * nasal.Diameters[i], 2);
        }
    }

    // The emitted velum area expression, evaluated with the admittance (opening * coupling) it reads set to a constant: the port area is
    // linear in the opening with the cited maximum, 0.8 cm^2.
    [Theory]
    [InlineData(1.0, 0.8)]
    [InlineData(0.7, 0.56)]
    [InlineData(0.25, 0.2)]
    public void TheVelopharyngealPortAreaIsTheOpeningTimesTheMaximumPortArea(double opening, double expectedAreaSquareCentimeters)
    {
        var source = FaustEmitter.Emit(
            PatchScript.Parse(IpaGestureExperiment.BuildWordScript([(new IpaGestureExperimentTarget("v_m", "m", "", .2f), 1f)])),
            new FaustExportOptions("velum", SampleRate: 48000)).Source;
        var area = Regex.Match(source, @"^\s*(\w+)_velum_area = (.+);$", RegexOptions.Multiline);
        Assert.True(area.Success);
        var admittance = Regex.Match(area.Groups[2].Value, @"\b(\w+_admittance)\b");
        Assert.True(admittance.Success, area.Groups[2].Value);
        var program = $"import(\"stdfaust.lib\");\n{admittance.Groups[1].Value} = {opening.ToString(CultureInfo.InvariantCulture)};\n" +
                      $"area = {area.Groups[2].Value};\nprocess = area;\n";
        using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: 48000));
        if (!compiler.TryCompileSource(new AquaSynthCompileIdentity("velum", "velum", ""), program, .05f, out var compiled, out var error))
        {
            if (error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true) return;
            Assert.Fail($"velum program failed: {error}");
        }

        using (compiled)
        {
            Assert.Equal(expectedAreaSquareCentimeters, compiled!.Render()[^1], 3);
        }
    }

    private static float Spline(SynthPatch patch, string namePrefix, string surfacePath) =>
        patch.ControlSplines.Single(spline => spline.Name.StartsWith(namePrefix, StringComparison.Ordinal) && spline.SurfacePath == surfacePath).Points[0].Value;

    // Distance of a vowel_set consonant constriction from the lip plane in cm, from the constriction_index spline: the surface is a section
    // index in [0, sections] and the area model reads position = index / (sections - 1).
    private static double CentimetresFromLips(string ipa)
    {
        var patch = PatchScript.Parse(IpaGestureExperiment.BuildWordScript([(new IpaGestureExperimentTarget("v_c", ipa, "", .2f), 1f)]));
        var oral = patch.AreaFunctions.Single(area => area.Name == "oral");
        var index = Spline(patch, "v_c_constriction_index", "/vocal/areas/0/area/constriction_index") * oral.Shape.Sections;
        return (1 - index / (oral.Shape.Sections - 1)) * oral.Shape.LengthCentimeters;
    }

    [Fact]
    public void UnderVowelSetMClosesAtTheLipsAndNAtTheAlveolarRidgeInCentimetres()
    {
        Assert.InRange(CentimetresFromLips("m"), -.05, .05);
        Assert.InRange(CentimetresFromLips("n"), 2.4, 2.7);
        Assert.True(CentimetresFromLips("n") > CentimetresFromLips("m") + 2);
    }

    [Fact]
    public void WithoutVowelSetTheConstrictionStaysOnTheNormalizedPlaceFraction()
    {
        // The earlier surface semantics are kept for hand-written scripts: the place fraction is the normalized spline value itself. A place
        // with a distance in the place table is that distance from the lips over the morphology's length (1 - 2.555 / 17 for /n/, 1 for /m/).
        var patch = PatchScript.Parse("""
            morphology name=oral length_cm=17 diameters=.6,.8,1.2,1.6,1.3,.9 tongue_index=3 tongue_diameter=1.4 constriction_index=4 constriction_diameter=1 lip_opening=1.5
            morphology name=nasal length_cm=12 diameters=.05,.35,.6,.8
            waveguide_path name=oral_path morphology=oral loss=.998
            waveguide_path name=nasal_path morphology=nasal loss=.997
            source_port name=folds path=oral_path pressure=.7 tension=.55 opening=.45 noise=.05 impedance=.3
            branch_port name=velopharynx from=oral_path from_position=.45 to=nasal_path opening=0 coupling=1
            constriction_contact name=contact path=oral_path position=.92 opening=.5 resistance=.4 stored_pressure=.1
            radiation_load name=mouth path=oral_path aperture=.8 reflection=-.82 impedance=.28
            vocal_network name=voice paths=oral_path,nasal_path sources=folds contacts=contact branches=velopharynx radiation=mouth
            phoneme_gesture name=en ipa=n start=0 dur=.16 intensity=.8
            phoneme_gesture name=em ipa=m start=.2 dur=.16 intensity=.8
            vocal network=voice freq=150 gain=.2 sustain=.6
            """);

        Assert.Equal(1 - 2.555f / 17, Spline(patch, "en_constriction_index", "/vocal/areas/0/area/constriction_index"), 5);
        Assert.Equal(1f, Spline(patch, "em_constriction_index", "/vocal/areas/0/area/constriction_index"), 5);
    }
}
