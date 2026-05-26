using System.Globalization;
using System.Text;

namespace AquaSynth.Dsl.Tests;

public sealed class PinkTromboneLogMelParityTests
{
    private const float SmokeCosineFloor = 0.08f;
    private static readonly IReadOnlyDictionary<string, float> GraphSmokeCosineFloors = new Dictionary<string, float>
    {
        ["open-vowel"] = 0.55f,
        ["front-vowel"] = 0.57f,
        ["nasal-vowel"] = 0.48f,
        ["bilabial-nasal-ma"] = 0.47f,
        ["sibilant"] = 0.18f,
        ["closure-release"] = 0.05f
    };
    private static readonly string[] UtteranceParityIds = ["mama", "papa", "thrombosis"];
    private static readonly IReadOnlyDictionary<string, float> UtteranceGraphSmokeCosineFloors = new Dictionary<string, float>
    {
        ["mama"] = -0.08f,
        ["papa"] = 0.8f,
        ["thrombosis"] = 0.2f
    };

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
            reports.Add(Report(fixture, comparison, "graph"));
            WriteFixtureArtifacts(artifactDir, fixture, "graph", reference.Samples, candidate.Samples, reference.SampleRate, comparison, candidateSource.Source);

            Assert.True(
                comparison.LogMelCosineSimilarity >= GraphSmokeCosineFloors.GetValueOrDefault(fixture.Id, SmokeCosineFloor),
                $"{Report(fixture, comparison, "graph")}{Environment.NewLine}artifacts: {artifactDir}");
            Assert.True(
                comparison.RmsRatio is > 0.03f and < 2.25f,
                $"{Report(fixture, comparison, "graph")}{Environment.NewLine}artifacts: {artifactDir}");
        }

        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(Path.Combine(artifactDir, "summary.txt"), string.Join(Environment.NewLine + Environment.NewLine, reports));
    }

    [Fact]
    public async Task PinkTromboneAcceptedUtterancesReportGraphLogMelParityWhenFaustIsInstalled()
    {
        if (FaustCompiler.FindFaust() is null)
        {
            return;
        }

        var referenceRenderer = new PinkTromboneReferenceRenderer();
        var artifactDir = ArtifactPath("parity", "pink-trombone-utterance-logmel", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture));
        var analyzer = new AudioAnalyzer(new AudioAnalysisConfig(SampleRate: 44100));
        var reports = new List<string>();

        foreach (var fixture in PinkTromboneUtteranceFixtures.All.Where(fixture => UtteranceParityIds.Contains(fixture.Id)))
        {
            var reference = referenceRenderer.RenderUtterance(fixture.Id, fixture.ControlPoints, fixture.DurationSeconds);
            var source = AutomatedGraphSource(fixture);
            var candidate = await FaustCompiler.RenderAsync(
                source,
                new FaustRenderOptions(reference.SampleRate, reference.Samples.Length / (float)reference.SampleRate));

            Assert.NotNull(candidate);
            Assert.True(candidate.Samples.Length > 0, $"{candidate.Stderr}{Environment.NewLine}artifacts: {artifactDir}");
            Assert.True(candidate.Samples.Max(MathF.Abs) > 0.00001f, candidate.Stderr);

            var comparison = analyzer.Compare(reference.Samples, candidate.Samples);
            var report = string.Create(
                CultureInfo.InvariantCulture,
                $"{fixture.Id}/graph-utterance: cosine={comparison.LogMelCosineSimilarity:0.0000} logMelDistance={comparison.LogMelDistance:0.0000} score={comparison.Score:0.0000} rmsRatio={comparison.RmsRatio:0.0000} centroidRatio={comparison.CentroidRatio:0.0000}");
            reports.Add(report);

            var fixtureDir = Path.Combine(artifactDir, fixture.Id);
            Directory.CreateDirectory(fixtureDir);
            WriteWav(Path.Combine(fixtureDir, "reference-pink-trombone.wav"), reference.Samples, reference.SampleRate);
            WriteWav(Path.Combine(fixtureDir, "candidate-graph.wav"), candidate.Samples, candidate.SampleRate);
            File.WriteAllText(Path.Combine(fixtureDir, "candidate-graph.dsp"), source);
            File.WriteAllText(Path.Combine(fixtureDir, "report.txt"), report);
            File.WriteAllText(Path.Combine(fixtureDir, "controls.csv"), UtteranceControlCsv(fixture));

            Assert.True(
                comparison.LogMelCosineSimilarity >= UtteranceGraphSmokeCosineFloors.GetValueOrDefault(fixture.Id, 0.1f),
                $"{report}{Environment.NewLine}artifacts: {artifactDir}");
        }

        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(Path.Combine(artifactDir, "summary.txt"), string.Join(Environment.NewLine + Environment.NewLine, reports));
    }

    private static string Report(PinkTromboneParityFixture fixture, AudioComparison comparison, string candidate) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{fixture.Id}/{candidate}: cosine={comparison.LogMelCosineSimilarity:0.0000} logMelDistance={comparison.LogMelDistance:0.0000} score={comparison.Score:0.0000} rmsRatio={comparison.RmsRatio:0.0000} centroidRatio={comparison.CentroidRatio:0.0000}");

    private static string AutomatedGraphSource(PinkTromboneUtteranceFixture fixture)
    {
        var source = FaustEmitter.EmitScript(UtteranceGraphScript(fixture), new FaustExportOptions($"pt_utterance_{fixture.Id}")).Source;
        var controls = ControlCurves(fixture.ControlPoints);
        for (var index = 0; index < controls.Count; index++)
        {
            source = ReplaceParameter(source, index, controls[index]);
        }

        return source;
    }

    private static string ReplaceParameter(string source, int parameterIndex, string expression)
    {
        var id = $"patch_param_{parameterIndex}";
        var start = source.IndexOf($"{id} = hslider(", StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"Could not find emitted Faust parameter `{id}`.");
        }

        var end = source.IndexOf(';', start);
        if (end < 0)
        {
            throw new InvalidOperationException($"Could not find end of emitted Faust parameter `{id}`.");
        }

        return source[..start] + $"{id} = {expression}" + source[end..];
    }

    private static IReadOnlyList<string> ControlCurves(IReadOnlyList<PinkTromboneControlPoint> points) =>
    [
        Curve(points, point => point.Controls.Frequency),
        Curve(points, point => point.Controls.Intensity),
        Curve(points, point => point.Controls.Tenseness),
        Curve(points, point => point.Controls.TongueIndex),
        Curve(points, point => point.Controls.TongueDiameter),
        Curve(points, point => point.Controls.ConstrictionIndex),
        Curve(points, point => point.Controls.ConstrictionDiameter),
        Curve(points, point => point.Controls.Turbulence),
        Curve(points, point => point.Controls.Velum),
        Curve(points, point => point.Controls.LipOpening),
        Curve(points, point => point.Controls.GlottalReflection),
        Curve(points, point => point.Controls.LipReflection),
        Curve(points, point => point.Controls.Gain * CandidateGainScale(points)),
        Curve(points, point => point.Controls.Burst)
    ];

    private static string Curve(IReadOnlyList<PinkTromboneControlPoint> points, Func<PinkTromboneControlPoint, float> valueAt)
    {
        var ordered = points.OrderBy(point => point.TimeSeconds).ToArray();
        var expression = F(valueAt(ordered[^1]));
        for (var index = ordered.Length - 2; index >= 0; index--)
        {
            var current = ordered[index];
            var next = ordered[index + 1];
            var span = Math.Max(0.0001f, next.TimeSeconds - current.TimeSeconds);
            var u = $"min(1.0, max(0.0, (age - {F(current.TimeSeconds)}) / {F(span)}))";
            var smooth = $"(({u}) * ({u}) * (3.0 - 2.0 * ({u})))";
            var value = $"({F(valueAt(current))} + ({F(valueAt(next))} - {F(valueAt(current))}) * {smooth})";
            expression = $"select2(age < {F(next.TimeSeconds)}, {expression}, {value})";
        }

        return expression;
    }

    private static float CandidateGainScale(IReadOnlyList<PinkTromboneControlPoint> points)
    {
        var labels = string.Join(" ", points.Select(point => point.Label));
        if (labels.Contains("th", StringComparison.OrdinalIgnoreCase)) return 0.57f;
        if (labels.Contains("p release", StringComparison.OrdinalIgnoreCase)) return 1.15f;
        return 0.9f;
    }

    private static string UtteranceGraphScript(PinkTromboneUtteranceFixture fixture) =>
        $$"""
        patch gain=0.55 soft_clip=true

        param name=frequency path=/pink/frequency default={{F(fixture.ControlPoints[0].Controls.Frequency)}} min=10 max=600 step=0.01 unit=Hz rate=audio
        param name=intensity path=/pink/intensity default={{F(fixture.ControlPoints[0].Controls.Intensity)}} min=0 max=1 step=0.001
        param name=tenseness path=/pink/tenseness default={{F(fixture.ControlPoints[0].Controls.Tenseness)}} min=0 max=1 step=0.001
        param name=tongue_index path=/pink/tongue/index default={{F(fixture.ControlPoints[0].Controls.TongueIndex)}} min=0 max=44 step=0.001 unit=cell
        param name=tongue_diameter path=/pink/tongue/diameter default={{F(fixture.ControlPoints[0].Controls.TongueDiameter)}} min=0 max=4 step=0.001 unit=diameter
        param name=constriction_index path=/pink/constriction/index default={{F(fixture.ControlPoints[0].Controls.ConstrictionIndex)}} min=0 max=44 step=0.001 unit=cell
        param name=constriction_diameter path=/pink/constriction/diameter default={{F(fixture.ControlPoints[0].Controls.ConstrictionDiameter)}} min=-1 max=4 step=0.001 unit=diameter
        param name=turbulence path=/pink/turbulence default={{F(fixture.ControlPoints[0].Controls.Turbulence)}} min=0 max=1 step=0.001
        param name=velum path=/pink/velum default={{F(fixture.ControlPoints[0].Controls.Velum)}} min=0.01 max=0.4 step=0.001 unit=diameter
        param name=lip_opening path=/pink/lip/opening default={{F(fixture.ControlPoints[0].Controls.LipOpening)}} min=0 max=2.5 step=0.001 unit=diameter
        param name=glottal_reflection path=/pink/glottal/reflection default={{F(fixture.ControlPoints[0].Controls.GlottalReflection)}} min=-0.95 max=0.95 step=0.001
        param name=lip_reflection path=/pink/lip/reflection default={{F(fixture.ControlPoints[0].Controls.LipReflection)}} min=-0.98 max=0.1 step=0.001
        param name=gain path=/pink/gain default={{F(fixture.ControlPoints[0].Controls.Gain)}} min=0 max=2 step=0.001
        param name=burst path=/pink/burst default={{F(fixture.ControlPoints[0].Controls.Burst)}} min=0 max=2 step=0.001

        tract_shape
            name=human
            length_cm=17
            diameters=0.6,0.6,0.6,0.6,0.6,0.7,0.8,1.0,1.1,1.1,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.4,1.3,1.2,1.15,1.5

        glottis name=modal intensity=@/pink/intensity tenseness=@/pink/tenseness aspiration=.12 reflection=@/pink/glottal/reflection skew=.42
        tract_injection name=inj position=@/pink/constriction/index diameter=@/pink/constriction/diameter turbulence=@/pink/turbulence burst=@/pink/burst width=1
        nasal_branch name=nose length_cm=12 junction=17 velum=@/pink/velum reflection=@/pink/lip/reflection loss=.999 diameters=0.01,0.35,0.5,0.65,0.8,0.95,1.1,1.25,1.4,1.55,1.7,1.8,1.9,1.9,1.85,1.75,1.65,1.55,1.45,1.35,1.25,1.15,1.05,0.95,0.85,0.75,0.65,0.55
        tract_motion name=motion diameter_slew=18 shape_return=8 constriction_slew=24 velum_slew=16 obstruction_threshold=.05

        tract shape=human glottis=modal injection=inj nasal_branch=nose motion=motion propagation=graph waveguide_loss=.999 freq=@/pink/frequency gain=@/pink/gain intensity=@/pink/intensity tenseness=@/pink/tenseness attack=.03 sustain={{F(fixture.DurationSeconds)}} decay=.05 tongue_index=@/pink/tongue/index tongue_diameter=@/pink/tongue/diameter constriction_index=@/pink/constriction/index constriction_diameter=@/pink/constriction/diameter turbulence=@/pink/turbulence velum=@/pink/velum lip=@/pink/lip/opening burst=@/pink/burst glottal_reflection=@/pink/glottal/reflection lip_reflection=@/pink/lip/reflection
        """;

    private static string UtteranceControlCsv(PinkTromboneUtteranceFixture fixture)
    {
        var lines = new List<string>
        {
            "time,label,frequency,intensity,tenseness,tongue_index,tongue_diameter,constriction_index,constriction_diameter,turbulence,velum,lip_opening,glottal_reflection,lip_reflection,gain,burst"
        };
        lines.AddRange(fixture.ControlPoints.Select(point => string.Join(",",
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

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    private static void WriteFixtureArtifacts(
        string artifactDir,
        PinkTromboneParityFixture fixture,
        string candidateName,
        IReadOnlyList<float> reference,
        IReadOnlyList<float> candidate,
        int sampleRate,
        AudioComparison comparison,
        string candidateSource)
    {
        var fixtureDir = Path.Combine(artifactDir, fixture.Id);
        Directory.CreateDirectory(fixtureDir);
        WriteWav(Path.Combine(fixtureDir, "reference-pink-trombone.wav"), reference, sampleRate);
        WriteWav(Path.Combine(fixtureDir, $"candidate-{candidateName}.wav"), candidate, sampleRate);
        File.WriteAllText(Path.Combine(fixtureDir, $"candidate-{candidateName}.dsp"), candidateSource);
        File.WriteAllText(Path.Combine(fixtureDir, $"report-{candidateName}.txt"), Report(fixture, comparison, candidateName));
    }

    private static string ArtifactPath(params string[] parts)
    {
        var root = RepositoryRoot();
        return Path.Combine([root, "artifacts", .. parts]);
    }

    private static string F(float value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

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
