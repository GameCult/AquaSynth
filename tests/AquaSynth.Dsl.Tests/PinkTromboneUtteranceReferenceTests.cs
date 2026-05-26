using System.Globalization;
using System.Text;

namespace AquaSynth.Dsl.Tests;

public sealed class PinkTromboneUtteranceReferenceTests
{
    [Fact]
    public void PinkTromboneUtteranceFixturesWriteReferenceWavs()
    {
        var renderer = new PinkTromboneReferenceRenderer();
        var artifactDir = ArtifactPath("parity", "pink-trombone-utterances", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(artifactDir);

        var summary = new List<string>
        {
            "Pink Trombone utterance reference fixtures",
            "These are PT-rendered control-curve sketches for listening before Aqua parity golf.",
            ""
        };

        foreach (var fixture in PinkTromboneUtteranceFixtures.All)
        {
            var render = renderer.RenderUtterance(fixture.Id, fixture.ControlPoints, fixture.DurationSeconds);
            var fixtureDir = Path.Combine(artifactDir, fixture.Id);
            Directory.CreateDirectory(fixtureDir);

            WriteWav(Path.Combine(fixtureDir, "reference-pink-trombone.wav"), render.Samples, render.SampleRate);
            File.WriteAllText(Path.Combine(fixtureDir, "controls.csv"), ControlCsv(fixture));
            File.WriteAllText(Path.Combine(fixtureDir, "summary.txt"), FixtureSummary(fixture, render));

            var analysis = AudioAnalyzer.AnalyzeAudio(render.Samples, new AudioAnalysisConfig(SampleRate: render.SampleRate));
            Assert.True(render.Samples.Length > render.SampleRate * 0.5f, fixture.Id);
            Assert.True(analysis.Features.Peak > 0.001f, fixture.Id);
            Assert.True(analysis.Features.Rms > 0.0001f, fixture.Id);

            summary.Add($"{fixture.Id}: `{fixture.Text}` duration={fixture.DurationSeconds:0.000}s peak={analysis.Features.Peak:0.0000} rms={analysis.Features.Rms:0.0000}");
        }

        File.WriteAllText(Path.Combine(artifactDir, "summary.txt"), string.Join(Environment.NewLine, summary));
    }

    private static string FixtureSummary(PinkTromboneUtteranceFixture fixture, PinkTromboneUtteranceRender render)
    {
        var lines = new List<string>
        {
            $"id: {fixture.Id}",
            $"text: {fixture.Text}",
            $"duration_seconds: {fixture.DurationSeconds.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"sample_rate: {render.SampleRate}",
            $"samples: {render.Samples.Length}",
            $"intended_phones: {string.Join(" ", fixture.IntendedPhones)}",
            "",
            "control_points:",
        };
        lines.AddRange(fixture.ControlPoints.Select(point => $"  {point.TimeSeconds:0.000}s {point.Label}: {ControlLine(point.Controls)}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string ControlCsv(PinkTromboneUtteranceFixture fixture)
    {
        var lines = new List<string>
        {
            "time,label,frequency,intensity,tenseness,tongue_index,tongue_diameter,constriction_index,constriction_diameter,turbulence,velum,lip_opening,glottal_reflection,lip_reflection,gain,burst"
        };
        lines.AddRange(fixture.ControlPoints.Select(point =>
            string.Join(",",
                F(point.TimeSeconds),
                Escape(point.Label),
                F(point.Controls.Frequency),
                F(point.Controls.Intensity),
                F(point.Controls.Tenseness),
                F(point.Controls.TongueIndex),
                F(point.Controls.TongueDiameter),
                F(point.Controls.ConstrictionIndex),
                F(point.Controls.ConstrictionDiameter),
                F(point.Controls.Turbulence),
                F(point.Controls.Velum),
                F(point.Controls.LipOpening),
                F(point.Controls.GlottalReflection),
                F(point.Controls.LipReflection),
                F(point.Controls.Gain),
                F(point.Controls.Burst))));
        return string.Join(Environment.NewLine, lines);
    }

    private static string ControlLine(PinkTromboneFixtureControls controls) =>
        $"freq={F(controls.Frequency)} intensity={F(controls.Intensity)} tense={F(controls.Tenseness)} tongue={F(controls.TongueIndex)}/{F(controls.TongueDiameter)} constriction={F(controls.ConstrictionIndex)}/{F(controls.ConstrictionDiameter)} turbulence={F(controls.Turbulence)} velum={F(controls.Velum)} lip={F(controls.LipOpening)}";

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    private static string F(float value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string ArtifactPath(params string[] parts)
    {
        var root = RepositoryRoot();
        return Path.Combine([root, "artifacts", .. parts]);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AquaSynth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private static void WriteWav(string path, IReadOnlyList<float> samples, int sampleRate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        var dataSize = samples.Count * sizeof(short);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        foreach (var sample in samples)
        {
            writer.Write((short)Math.Clamp(MathF.Round(sample * short.MaxValue), short.MinValue, short.MaxValue));
        }
    }
}
