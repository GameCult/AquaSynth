using AquaSynth.Dsl;
using AquaSynth.Faust;

namespace AquaSynth.Dsl.Tests;

// What Meta heard in tract-weak-spots-1 (2026-11-13) while every formant matched: clicks at the phone boundaries of nibu, a /b/ burst the
// length of a fricative, and a first 100 ms that climbed from silence. These pin the three causes at the control-spline and DSP-source
// level (docs/listening/tract-weak-spots-1-analysis-20261113.md); the renders are measured by tools/formant-check/ear_metrics.py.
public sealed class PhoneTransitionTests
{
    private static SynthPatch Nibu() => PatchScript.Parse(IpaGestureExperiment.BuildWordScript(
    [
        (new IpaGestureExperimentTarget("w_n", "n", "", .095f), 1f),
        (new IpaGestureExperimentTarget("w_i", "i", "", .18f), 1f),
        (new IpaGestureExperimentTarget("w_b", "b", "", .095f), 1f),
        (new IpaGestureExperimentTarget("w_u", "u", "", .18f), 1f)
    ], frequency: 140));

    [Fact]
    public void NoSurfaceStepsAtAPhoneBoundary()
    {
        var timeline = new ControlSplineTimeline(Nibu().ControlSplines);
        var worst = (Surface: "", Boundary: 0f, Step: 0f);
        foreach (var boundary in new[] { .095f, .275f, .37f })
        {
            foreach (var surface in timeline.SurfacePaths)
            {
                var step = MathF.Abs(timeline.ValueAt(surface, boundary + .0005f) - timeline.ValueAt(surface, boundary - .0005f));
                if (step > worst.Step) worst = (surface, boundary, step);
            }
        }

        // 0.5 ms either side of a boundary; a 30 ms ramp across the full 0..1 range moves 0.017 in that time, a step moves it by the
        // whole difference (the pre-fix nibu stepped /vocal/areas/0/area/constriction_diameter by 0.77 at the 95 ms boundary).
        Assert.True(worst.Step < .03f, $"{worst.Surface} steps {worst.Step:0.000} at {worst.Boundary} s");
    }

    [Fact]
    public void ThePhoneAfterABoundaryArrivesAtItsFirstValueWithinTheTransition()
    {
        var timeline = new ControlSplineTimeline(Nibu().ControlSplines);
        const string diameter = "/vocal/areas/0/area/constriction_diameter";
        var closedN = timeline.ValueAt(diameter, .05f);
        Assert.True(closedN < .1f, "the /n/ constriction is shut");
        Assert.True(timeline.ValueAt(diameter, .095f + .002f) < .5f, "still closing into the vowel two ms after the boundary");
        Assert.Equal(1f, timeline.ValueAt(diameter, .095f + .035f), 2);
    }

    [Fact]
    public void TheFirstPhoneOfAWordStartsAtItsOwnValue()
    {
        var timeline = new ControlSplineTimeline(Nibu().ControlSplines);
        Assert.Equal(timeline.ValueAt("/vocal/areas/0/area/constriction_diameter", 0f), timeline.ValueAt("/vocal/areas/0/area/constriction_diameter", .02f), 3);
    }

    [Fact]
    public void AStopOpensInTwelveMillisecondsNotAThirdOfItsGesture()
    {
        var timeline = new ControlSplineTimeline(Nibu().ControlSplines);
        const string contact = "/vocal/contacts/0/opening";
        const float start = .275f, duration = .095f;
        Assert.True(timeline.ValueAt(contact, start + duration - .013f) < .1f, "closed until 12 ms before the end");
        Assert.True(timeline.ValueAt(contact, start + duration) > .8f, "open at the end");
        var opening = 0f;
        for (var t = start + .03f; t <= start + duration; t += .0005f)
        {
            if (timeline.ValueAt(contact, t) > .3f) { opening = t; break; }
        }

        Assert.True(start + duration - opening < .012f, $"the opening crosses 0.3 only {start + duration - opening:0.000} s before the end");
    }

    [Fact]
    public void ControlSmoothersStartAtTheirInputNotAtZero()
    {
        var source = FaustEmitter.Emit(Nibu(), new FaustExportOptions("transition_probe")).Source;
        Assert.Contains("smoo_init(x) =", source);
        Assert.Contains("smoo_init;", source);
        // Every control surface and spline-point slider uses it; patch parameters keep si.smoo.
        var remaining = source.Split('\n').Where(line => line.Contains("hslider", StringComparison.Ordinal) && line.Contains(": si.smoo;", StringComparison.Ordinal)
            && (line.Contains("\"/vocal/", StringComparison.Ordinal) || line.Contains("\"/splines/", StringComparison.Ordinal))).ToArray();
        Assert.Empty(remaining);
    }
}
