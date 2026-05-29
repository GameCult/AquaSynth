using AquaSynth.Dsl;
using AquaSynth.Faust;

namespace AquaSynth.Dsl.Tests;

public sealed class NativeFaustRuntimeTests
{
    [Fact]
    public void CompileKeyTracksRevisionNameAndScript()
    {
        var first = new AquaSynthCompileIdentity("patch", "tone", "voice wave=sine freq=440 gain=.1", 1);
        var same = new AquaSynthCompileIdentity("patch", "tone", "voice wave=sine freq=440 gain=.1", 1);
        var changed = new AquaSynthCompileIdentity("patch", "tone", "voice wave=sine freq=441 gain=.1", 1);

        Assert.Equal(first.CompileKey, same.CompileKey);
        Assert.NotEqual(first.CompileKey, changed.CompileKey);
    }

    [Fact]
    public void NativeFaustPatchCompilerRendersAudiblePatchWhenToolchainIsAvailable()
    {
        const string script = """
            voice
                wave=sine
                freq=440
                gain=0.2
                attack=0.001
                sustain=0.06
                decay=0.12
            """;

        using var compiler = new AquaSynthPatchCompiler();
        if (!compiler.TryCompileScript(new AquaSynthCompileIdentity("native_smoke", "native_smoke", script), out var patch, out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            Assert.Fail($"AquaSynth native Faust render failed: {error}");
        }

        using (patch)
        {
            var samples = patch!.Render(1.0f);
            Assert.True(samples.Length > 2048, $"Rendered too few samples: {samples.Length}.");
            Assert.Contains(samples, sample => MathF.Abs(sample) > 0.001f);
            Assert.InRange(samples.Max(sample => MathF.Abs(sample)), 0.001f, 1.0f);
        }
    }

    [Fact]
    public void NativeFaustStreamingPatchProcessesInputBlocksWhenToolchainIsAvailable()
    {
        const string source = """
            import("stdfaust.lib");
            gain = hslider("gain", 1.0, 0.0, 2.0, 0.001);
            process = _ * gain;
            """;

        using var compiler = new AquaSynthPatchCompiler();
        if (!compiler.TryCompileSource(
            new AquaSynthCompileIdentity("native_streaming_smoke", "native_streaming_smoke", source),
            source,
            0.05f,
            out var patch,
            out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            Assert.Fail($"AquaSynth native Faust streaming compile failed: {error}");
        }

        using (patch)
        using (var stream = patch!.CreateStreamingPatch())
        {
            Assert.Equal(1, stream.InputCount);
            Assert.Equal(1, stream.OutputCount);

            var input = new[] { Enumerable.Repeat(0.25f, 128).ToArray() };
            var output = new[] { new float[128] };
            stream.ProcessBlock(input, output, 128, new Dictionary<string, float>
            {
                ["gain"] = 0.5f
            });

            Assert.All(output[0], sample => Assert.InRange(sample, 0.124f, 0.126f));
        }
    }

    [Fact]
    public void NativeFaustStreamingPatchReadsDebugProbeBargraphsWhenToolchainIsAvailable()
    {
        const string source = """
            import("stdfaust.lib");
            tone = os.osc(440);
            process = attach(tone * 0.1, abs(tone) : vbargraph("/debug/level", 0.0, 1.0));
            """;

        using var compiler = new AquaSynthPatchCompiler();
        if (!compiler.TryCompileSource(
            new AquaSynthCompileIdentity("native_probe_smoke", "native_probe_smoke", source),
            source,
            0.05f,
            out var patch,
            out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            Assert.Fail($"AquaSynth native Faust probe compile failed: {error}");
        }

        using (patch)
        using (var stream = patch!.CreateStreamingPatch())
        {
            Assert.Contains("debug/level", patch.ProbePaths);
            Assert.Contains("debug/level", stream.ProbePaths);

            var output = new[] { new float[256] };
            stream.ProcessBlock([], output, 256);

            var level = stream.ReadProbe("/debug/level");
            Assert.InRange(level, 0.0f, 1.0f);
            Assert.Contains(stream.SnapshotProbes(), pair => pair.Key == "debug/level" && pair.Value >= 0.0f);
        }
    }

    [Fact]
    public void NativeFaustCompiledPatchExposesPrimitiveControlSurfaceCatalogWhenToolchainIsAvailable()
    {
        const string script = """
            morphology name=oral length_cm=17 diameters=.6,.8,1.2,1.5,.9
            waveguide_path name=oral_path morphology=oral loss=.998
            source_port name=folds path=oral_path pressure=.7 tension=.55 opening=.45 noise=.05 impedance=.3
            radiation_load name=mouth path=oral_path aperture=.8 reflection=-.82 impedance=.28
            vocal_network name=voice paths=oral_path sources=folds radiation=mouth
            vocal network=voice freq=150 gain=.2 sustain=.05 decay=.04
            """;

        using var compiler = new AquaSynthPatchCompiler();
        if (!compiler.TryCompileScript(new AquaSynthCompileIdentity("native_surface_smoke", "native_surface_smoke", script), out var patch, out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            Assert.Fail($"AquaSynth native Faust surface compile failed: {error}");
        }

        using (patch)
        {
            Assert.Contains("/vocal/sources/0/pressure", patch!.ControlSurfaces.SurfacePaths);
            Assert.Contains("vocal/sources/0/pressure", patch.ControlPaths);

            var timeline = patch.ControlSurfaces.CreateTimeline(includePatchSplines: false);
            timeline.SetFuturePoint("/vocal/sources/0/pressure", timeSeconds: .01f, normalizedValue: .9f, nowSeconds: 0);
            var controls = patch.ControlValuesAt(timeline, .01f);
            var samples = patch.Render(controls);

            Assert.Contains("/vocal/sources/0/pressure", controls.Keys);
            Assert.DoesNotContain("/vocal/radiation/0/reflection", controls.Keys);
            Assert.True(samples.Length > 1024);
            Assert.Contains(samples, sample => MathF.Abs(sample) > 0.0001f);
        }
    }
}
