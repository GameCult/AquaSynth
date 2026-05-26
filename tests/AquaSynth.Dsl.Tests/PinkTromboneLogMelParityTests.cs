using System.Globalization;
using System.Text;

namespace AquaSynth.Dsl.Tests;

public sealed class PinkTromboneLogMelParityTests
{
    private const float SmokeCosineFloor = 0.08f;

    [Fact]
    public void PinkTromboneReferenceRendererProducesAudibleFixtureAudio()
    {
        var renderer = new PinkTromboneReferenceRenderer();

        foreach (var fixture in PinkTromboneParityFixtures.All)
        {
            var render = renderer.Render(fixture.Controls);
            var analysis = AudioAnalyzer.AnalyzeAudio(render.Samples, new AudioAnalysisConfig(SampleRate: render.SampleRate));

            Assert.True(render.Samples.Length > 10000, fixture.Id);
            Assert.True(analysis.Features.Peak > 0.001f, fixture.Id);
            Assert.True(analysis.Features.Rms > 0.0001f, fixture.Id);
            Assert.Equal(32, analysis.LogMelSpectrogram.Bands);
        }
    }

    [Fact]
    public async Task PinkTromboneFixturesReportLogMelParityWhenFaustIsInstalled()
    {
        if (FaustCompiler.FindFaust() is null)
        {
            return;
        }

        var referenceRenderer = new PinkTromboneReferenceRenderer();
        var artifactDir = ArtifactPath("parity", "pink-trombone-logmel", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture));
        var reports = new List<string>();

        foreach (var fixture in PinkTromboneParityFixtures.All)
        {
            var reference = referenceRenderer.Render(fixture.Controls);
            var candidateSource = FaustEmitter.EmitScript(fixture.AquaScript, new FaustExportOptions($"pt_{fixture.Id.Replace('-', '_')}"));
            var fixtureDir = Path.Combine(artifactDir, fixture.Id);
            Directory.CreateDirectory(fixtureDir);
            File.WriteAllText(Path.Combine(fixtureDir, "candidate.dsp"), candidateSource.Source);
            var candidate = await FaustCompiler.RenderAsync(
                candidateSource.Source,
                new FaustRenderOptions(reference.SampleRate, reference.Samples.Length / (float)reference.SampleRate));

            Assert.NotNull(candidate);
            Assert.True(candidate.Samples.Length > 0, $"{candidate.Stderr}{Environment.NewLine}artifacts: {fixtureDir}");
            Assert.True(candidate.Samples.Max(MathF.Abs) > 0.00001f, candidate.Stderr);

            var comparison = new AudioAnalyzer(new AudioAnalysisConfig(SampleRate: reference.SampleRate))
                .Compare(reference.Samples, candidate.Samples);
            reports.Add(Report(fixture, comparison));
            WriteFixtureArtifacts(artifactDir, fixture, reference.Samples, candidate.Samples, reference.SampleRate, comparison, candidateSource.Source);

            Assert.True(
                comparison.LogMelCosineSimilarity >= SmokeCosineFloor,
                $"{Report(fixture, comparison)}{Environment.NewLine}artifacts: {artifactDir}");
        }

        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(Path.Combine(artifactDir, "summary.txt"), string.Join(Environment.NewLine + Environment.NewLine, reports));
    }

    private static string Report(PinkTromboneParityFixture fixture, AudioComparison comparison) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{fixture.Id}: cosine={comparison.LogMelCosineSimilarity:0.0000} logMelDistance={comparison.LogMelDistance:0.0000} score={comparison.Score:0.0000} rmsRatio={comparison.RmsRatio:0.0000} centroidRatio={comparison.CentroidRatio:0.0000}");

    private static void WriteFixtureArtifacts(
        string artifactDir,
        PinkTromboneParityFixture fixture,
        IReadOnlyList<float> reference,
        IReadOnlyList<float> candidate,
        int sampleRate,
        AudioComparison comparison,
        string candidateSource)
    {
        var fixtureDir = Path.Combine(artifactDir, fixture.Id);
        Directory.CreateDirectory(fixtureDir);
        WriteWav(Path.Combine(fixtureDir, "reference-pink-trombone.wav"), reference, sampleRate);
        WriteWav(Path.Combine(fixtureDir, "candidate-aquasynth.wav"), candidate, sampleRate);
        File.WriteAllText(Path.Combine(fixtureDir, "candidate.dsp"), candidateSource);
        File.WriteAllText(Path.Combine(fixtureDir, "report.txt"), Report(fixture, comparison));
    }

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
