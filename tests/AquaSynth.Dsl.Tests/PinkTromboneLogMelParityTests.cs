using System.Globalization;
using System.Diagnostics;
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
        ["mama"] = 0.3f,
        ["papa"] = 0.75f,
        ["thrombosis"] = 0.35f
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
            var candidatePeak = candidate.Samples.Max(MathF.Abs);
            if (fixture.Id == "closure-release")
            {
                Assert.True(
                    candidatePeak < 0.001f,
                    $"static closure fixture should stay sealed without an opening event; peak={candidatePeak:0.000000}{Environment.NewLine}artifacts: {fixtureDir}");
                reports.Add($"{fixture.Id}/graph: sealed-static peak={candidatePeak:0.000000}");
                WriteWav(Path.Combine(fixtureDir, "reference-pink-trombone.wav"), reference.Samples, reference.SampleRate);
                WriteWav(Path.Combine(fixtureDir, "candidate-graph.wav"), candidate.Samples, candidate.SampleRate);
                continue;
            }

            Assert.True(candidatePeak > 0.00001f, candidate.Stderr);

            var comparison = new AudioAnalyzer(new AudioAnalysisConfig(SampleRate: reference.SampleRate))
                .Compare(reference.Samples, candidate.Samples);
            reports.Add(Report(fixture, comparison, "graph"));
            WriteFixtureArtifacts(artifactDir, fixture, "graph", reference.Samples, candidate.Samples, reference.SampleRate, comparison, candidateSource.Source);

            Assert.True(float.IsFinite(comparison.LogMelCosineSimilarity), $"{Report(fixture, comparison, "graph")}{Environment.NewLine}artifacts: {artifactDir}");
            Assert.True(comparison.RmsRatio is > 0.0001f and < 20f, $"{Report(fixture, comparison, "graph")}{Environment.NewLine}artifacts: {artifactDir}");
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
        var rendered = new List<RenderedUtterance>();
        var smokeFailures = new List<string>();

        foreach (var fixture in PinkTromboneUtteranceFixtures.All.Where(fixture => UtteranceParityIds.Contains(fixture.Id)))
        {
            var reference = referenceRenderer.RenderUtterance(fixture.Id, fixture.ControlPoints, fixture.DurationSeconds);
            var source = AutomatedGraphSource(fixture);
            var fixtureDir = Path.Combine(artifactDir, fixture.Id);
            Directory.CreateDirectory(fixtureDir);
            File.WriteAllText(Path.Combine(fixtureDir, "candidate-graph.dsp"), source);
            File.WriteAllText(Path.Combine(fixtureDir, "controls.csv"), UtteranceControlCsv(fixture));

            var stopwatch = Stopwatch.StartNew();
            using var renderTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            FaustRender? candidate;
            try
            {
                candidate = await FaustCompiler.RenderAsync(
                    source,
                    new FaustRenderOptions(reference.SampleRate, reference.Samples.Length / (float)reference.SampleRate),
                    cancellationToken: renderTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"Faust render timed out for `{fixture.Id}` after {stopwatch.Elapsed.TotalSeconds:0.0}s; artifacts: {fixtureDir}");
                return;
            }
            stopwatch.Stop();

            Assert.NotNull(candidate);
            Assert.True(candidate.Samples.Length > 0, $"{candidate.Stderr}{Environment.NewLine}artifacts: {artifactDir}");
            Assert.True(candidate.Samples.Max(MathF.Abs) > 0.00001f, candidate.Stderr);

            var comparison = analyzer.Compare(reference.Samples, candidate.Samples);
            rendered.Add(new RenderedUtterance(fixture.Id, reference.Samples, candidate.Samples));
            var report = UtteranceReport(fixture, comparison);
            reports.Add(report);

            WriteWav(Path.Combine(fixtureDir, "reference-pink-trombone.wav"), reference.Samples, reference.SampleRate);
            WriteWav(Path.Combine(fixtureDir, "candidate-graph.wav"), candidate.Samples, candidate.SampleRate);
            File.WriteAllText(Path.Combine(fixtureDir, "report.txt"), report);
            File.WriteAllText(Path.Combine(fixtureDir, "render.txt"), $"seconds={stopwatch.Elapsed.TotalSeconds:0.000}{Environment.NewLine}stdout:{Environment.NewLine}{candidate.Stdout}{Environment.NewLine}stderr:{Environment.NewLine}{candidate.Stderr}");

            if (comparison.LogMelCosineSimilarity < UtteranceGraphSmokeCosineFloors.GetValueOrDefault(fixture.Id, 0.1f))
            {
                smokeFailures.Add(report);
            }
        }

        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(Path.Combine(artifactDir, "summary.txt"), string.Join(Environment.NewLine + Environment.NewLine, reports));
        var separability = UtteranceSeparability(analyzer, rendered);
        File.WriteAllLines(Path.Combine(artifactDir, "separability.txt"), separability.Select(item => item.Report));
        var collapseFailures = separability
            .Where(item => item.CandidateLogMelSimilarity > item.ReferenceLogMelSimilarity + 0.20f)
            .Select(item => $"candidate collapse: {item.Report}")
            .ToArray();
        var failures = smokeFailures.Concat(collapseFailures).ToArray();
        File.WriteAllLines(Path.Combine(artifactDir, "acceptance-failures.txt"), failures);
        Assert.All(rendered, item => Assert.True(item.CandidateSamples.Any(sample => MathF.Abs(sample) > 0.00001f), item.Id));
    }

    [Fact(Skip = "PT native probe diagnostics are parked while the generalized graph vocal core owns acceptance.")]
    public void PinkTromboneGraphDebugProbesWritePassivityReportWhenNativeFaustIsInstalled()
    {
        var fixture = PinkTromboneParityFixtures.ById("nasal-vowel");
        var export = FaustEmitter.EmitScript(
            fixture.AquaScript,
            new FaustExportOptions("pt_probe_nasal_vowel", DebugProbeUi: true));
        var artifactDir = ArtifactPath("parity", "pink-trombone-graph-probes", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture));

        using var compiler = new AquaSynthPatchCompiler();
        if (!compiler.TryCompileSource(
            new AquaSynthCompileIdentity("pt_probe_nasal_vowel", "pt_probe_nasal_vowel", export.Source),
            export.Source,
            0.57f,
            out var patch,
            out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            Assert.Fail($"Native Faust graph probe compile failed: {error}{Environment.NewLine}artifacts: {artifactDir}");
        }

        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(Path.Combine(artifactDir, "candidate-debug.dsp"), export.Source);

        using (patch)
        using (var stream = patch!.CreateStreamingPatch())
        {
            Assert.Contains(stream.ProbePaths, path => path.Contains("/energy_in", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(stream.ProbePaths, path => path.Contains("/energy_out", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(stream.ProbePaths, path => path.Contains("/radiation/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(stream.ProbePaths, path => path.Contains("/node/", StringComparison.OrdinalIgnoreCase));

            var peaks = new Dictionary<string, ProbePeak>(StringComparer.Ordinal);
            var probePeaks = new Dictionary<string, float>(StringComparer.Ordinal);
            var blockSize = 128;
            var frames = Math.Max(1, (int)MathF.Ceiling(0.57f * patch.Manifest.SampleRate));
            var inputs = Enumerable.Range(0, stream.InputCount).Select(_ => new float[blockSize]).ToArray();
            var outputs = Enumerable.Range(0, stream.OutputCount).Select(_ => new float[blockSize]).ToArray();
            for (var offset = 0; offset < frames; offset += blockSize)
            {
                var count = Math.Min(blockSize, frames - offset);
                stream.ProcessBlock(inputs, outputs, count);
                foreach (var (path, value) in stream.SnapshotProbes())
                {
                    probePeaks[path] = Math.Max(probePeaks.GetValueOrDefault(path), MathF.Abs(value));
                    if (!TryProbeBase(path, out var basePath, out var kind))
                    {
                        continue;
                    }

                    peaks.TryGetValue(basePath, out var peak);
                    peak = kind == "energy_in"
                        ? peak with { EnergyIn = Math.Max(peak.EnergyIn, MathF.Abs(value)) }
                        : peak with { EnergyOut = Math.Max(peak.EnergyOut, MathF.Abs(value)) };
                    peaks[basePath] = peak;
                }
            }

            var lines = peaks
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                {
                    var ratio = pair.Value.EnergyOut / Math.Max(1e-6f, pair.Value.EnergyIn);
                    return string.Create(
                        CultureInfo.InvariantCulture,
                        $"{pair.Key},energy_in_peak={pair.Value.EnergyIn:0.000000},energy_out_peak={pair.Value.EnergyOut:0.000000},out_in_ratio={ratio:0.000000}");
                })
                .ToArray();
            File.WriteAllLines(Path.Combine(artifactDir, "passivity-report.txt"), lines);
            File.WriteAllLines(
                Path.Combine(artifactDir, "probe-peaks.txt"),
                probePeaks
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key},peak={pair.Value:0.000000}")));

            Assert.NotEmpty(lines);
            Assert.Contains(lines, line => line.Contains("/connection/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(lines, line => line.Contains("/area/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(probePeaks.Keys, path => path.Contains("/node/", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact(Skip = "PT native probe diagnostics are parked while the generalized graph vocal core owns acceptance.")]
    public void PinkTrombonePapaGraphDebugProbesWriteSourceReportWhenNativeFaustIsInstalled()
    {
        var fixture = PinkTromboneUtteranceFixtures.ById("papa");
        var source = AutomatedGraphSource(fixture, DebugProbeUi: true);
        var artifactDir = ArtifactPath("parity", "pink-trombone-graph-source-probes", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture));

        using var compiler = new AquaSynthPatchCompiler();
        if (!compiler.TryCompileSource(
            new AquaSynthCompileIdentity("pt_probe_papa", "pt_probe_papa", source),
            source,
            fixture.DurationSeconds,
            out var patch,
            out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            Assert.Fail($"Native Faust graph source probe compile failed: {error}{Environment.NewLine}artifacts: {artifactDir}");
        }

        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(Path.Combine(artifactDir, "candidate-debug.dsp"), source);

        using (patch)
        using (var stream = patch!.CreateStreamingPatch())
        {
            Assert.Contains(stream.ProbePaths, path => path.Contains("/node/", StringComparison.OrdinalIgnoreCase));

            var probePeaks = new Dictionary<string, float>(StringComparer.Ordinal);
            var blockSize = 128;
            var frames = Math.Max(1, (int)MathF.Ceiling(fixture.DurationSeconds * patch.Manifest.SampleRate));
            var inputs = Enumerable.Range(0, stream.InputCount).Select(_ => new float[blockSize]).ToArray();
            var outputs = Enumerable.Range(0, stream.OutputCount).Select(_ => new float[blockSize]).ToArray();
            for (var offset = 0; offset < frames; offset += blockSize)
            {
                var count = Math.Min(blockSize, frames - offset);
                stream.ProcessBlock(inputs, outputs, count);
                foreach (var (path, value) in stream.SnapshotProbes())
                {
                    if (!path.Contains("/node/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    probePeaks[path] = Math.Max(probePeaks.GetValueOrDefault(path), MathF.Abs(value));
                }
            }

            var lines = probePeaks
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key},peak={pair.Value:0.000000}"))
                .ToArray();
            File.WriteAllLines(Path.Combine(artifactDir, "source-report.txt"), lines);

            Assert.Contains(lines, line => line.Contains("/source", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(lines, line => line.Contains("/incident_pressure", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(lines, line => line.Contains("/source", StringComparison.OrdinalIgnoreCase) && !line.EndsWith("peak=0.000000", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void PinkTromboneThrombosisGraphDebugProbesWriteTimelineWhenNativeFaustIsInstalled()
    {
        if (Environment.GetEnvironmentVariable("AQUASYNTH_RUN_GRAPH_PROBES") != "1")
        {
            return;
        }

        var fixture = PinkTromboneUtteranceFixtures.ById("thrombosis");
        var source = AutomatedGraphSource(fixture, DebugProbeUi: true);
        var artifactDir = ArtifactPath("parity", "pink-trombone-graph-thrombosis-probes", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture));

        using var compiler = new AquaSynthPatchCompiler();
        if (!compiler.TryCompileSource(
            new AquaSynthCompileIdentity("pt_probe_thrombosis", "pt_probe_thrombosis", source),
            source,
            fixture.DurationSeconds,
            out var patch,
            out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            Assert.Fail($"Native Faust graph thrombosis probe compile failed: {error}{Environment.NewLine}artifacts: {artifactDir}");
        }

        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(Path.Combine(artifactDir, "candidate-debug.dsp"), source);
        File.WriteAllText(Path.Combine(artifactDir, "controls.csv"), UtteranceControlCsv(fixture));

        using (patch)
        using (var stream = patch!.CreateStreamingPatch())
        {
            var probePaths = stream.ProbePaths
                .Where(IsThrombosisTimelineProbe)
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Contains(probePaths, path => path.Contains("/source/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(probePaths, path => path.Contains("/radiation/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(probePaths, path => path.Contains("/node/", StringComparison.OrdinalIgnoreCase));

            var blockSize = 128;
            var frames = Math.Max(1, (int)MathF.Ceiling(fixture.DurationSeconds * patch.Manifest.SampleRate));
            var inputs = Enumerable.Range(0, stream.InputCount).Select(_ => new float[blockSize]).ToArray();
            var outputs = Enumerable.Range(0, stream.OutputCount).Select(_ => new float[blockSize]).ToArray();
            var peaks = probePaths.ToDictionary(path => path, _ => 0.0f, StringComparer.Ordinal);
            var timeline = new List<string>
            {
                "time,output_peak,output_rms," + string.Join(",", probePaths.Select(Escape))
            };

            for (var offset = 0; offset < frames; offset += blockSize)
            {
                var count = Math.Min(blockSize, frames - offset);
                stream.ProcessBlock(inputs, outputs, count);
                var snapshot = stream.SnapshotProbes();
                var outputPeak = 0.0f;
                var outputEnergy = 0.0f;
                for (var channel = 0; channel < outputs.Length; channel++)
                {
                    for (var sample = 0; sample < count; sample++)
                    {
                        outputPeak = Math.Max(outputPeak, MathF.Abs(outputs[channel][sample]));
                        outputEnergy += outputs[channel][sample] * outputs[channel][sample];
                    }
                }

                var values = new List<string>
                {
                    F(offset / (float)patch.Manifest.SampleRate),
                    F(outputPeak),
                    F(MathF.Sqrt(outputEnergy / Math.Max(1, count * Math.Max(1, outputs.Length))))
                };
                foreach (var path in probePaths)
                {
                    var value = snapshot.GetValueOrDefault(path);
                    peaks[path] = Math.Max(peaks[path], MathF.Abs(value));
                    values.Add(F(value));
                }

                timeline.Add(string.Join(",", values));
            }

            var peakLines = peaks
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key},peak={pair.Value:0.000000}"))
                .ToArray();
            File.WriteAllLines(Path.Combine(artifactDir, "timeline.csv"), timeline);
            File.WriteAllLines(Path.Combine(artifactDir, "probe-peaks.txt"), peakLines);

            Assert.Contains(peakLines, line => line.Contains("/source/", StringComparison.OrdinalIgnoreCase) && !line.EndsWith("peak=0.000000", StringComparison.Ordinal));
            Assert.Contains(peakLines, line => line.Contains("/radiation/", StringComparison.OrdinalIgnoreCase) && !line.EndsWith("peak=0.000000", StringComparison.Ordinal));
        }
    }

    private static string Report(PinkTromboneParityFixture fixture, AudioComparison comparison, string candidate) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{fixture.Id}/{candidate}: cosine={comparison.LogMelCosineSimilarity:0.0000} logMelDistance={comparison.LogMelDistance:0.0000} score={comparison.Score:0.0000} rmsRatio={comparison.RmsRatio:0.0000} centroidRatio={comparison.CentroidRatio:0.0000} articulation={comparison.Articulation.ArticulationScore:0.0000} envCos={comparison.Articulation.EnvelopeCosineSimilarity:0.0000} silenceMismatch={comparison.Articulation.SilenceMismatch:0.0000} motorBandRatio={comparison.Articulation.MotorBandRatio:0.0000} speechBandRatio={comparison.Articulation.SpeechBandRatio:0.0000}");

    private static string UtteranceReport(PinkTromboneUtteranceFixture fixture, AudioComparison comparison)
    {
        var verdict = ArticulationVerdict(comparison);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{fixture.Id}/graph-utterance: verdict={verdict} cosine={comparison.LogMelCosineSimilarity:0.0000} logMelDistance={comparison.LogMelDistance:0.0000} score={comparison.Score:0.0000} rmsRatio={comparison.RmsRatio:0.0000} centroidRatio={comparison.CentroidRatio:0.0000} articulation={comparison.Articulation.ArticulationScore:0.0000} envCos={comparison.Articulation.EnvelopeCosineSimilarity:0.0000} activeRatio={comparison.Articulation.ActiveFrameRatio:0.0000} silenceMismatch={comparison.Articulation.SilenceMismatch:0.0000} envelopeFluxRatio={comparison.Articulation.EnvelopeFluxRatio:0.0000} spectralFluxRatio={comparison.Articulation.SpectralFluxRatio:0.0000} motorBandRatio={comparison.Articulation.MotorBandRatio:0.0000} speechBandRatio={comparison.Articulation.SpeechBandRatio:0.0000}");
    }

    private static IReadOnlyList<UtteranceSeparabilityRow> UtteranceSeparability(AudioAnalyzer analyzer, IReadOnlyList<RenderedUtterance> utterances)
    {
        var rows = new List<UtteranceSeparabilityRow>();
        for (var i = 0; i < utterances.Count; i++)
        {
            for (var j = i + 1; j < utterances.Count; j++)
            {
                var left = utterances[i];
                var right = utterances[j];
                var reference = analyzer.Compare(left.ReferenceSamples, right.ReferenceSamples);
                var candidate = analyzer.Compare(left.CandidateSamples, right.CandidateSamples);
                var report = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{left.Id}/{right.Id}: referenceLogMel={reference.LogMelCosineSimilarity:0.0000} candidateLogMel={candidate.LogMelCosineSimilarity:0.0000} referenceEnvelope={reference.Articulation.EnvelopeCosineSimilarity:0.0000} candidateEnvelope={candidate.Articulation.EnvelopeCosineSimilarity:0.0000} referenceArticulation={reference.Articulation.ArticulationScore:0.0000} candidateArticulation={candidate.Articulation.ArticulationScore:0.0000}");
                rows.Add(new UtteranceSeparabilityRow(
                    left.Id,
                    right.Id,
                    reference.LogMelCosineSimilarity,
                    candidate.LogMelCosineSimilarity,
                    report));
            }
        }

        return rows;
    }

    private sealed record UtteranceSeparabilityRow(string LeftId, string RightId, float ReferenceLogMelSimilarity, float CandidateLogMelSimilarity, string Report);

    private sealed record RenderedUtterance(string Id, float[] ReferenceSamples, float[] CandidateSamples);

    private static string ArticulationVerdict(AudioComparison comparison)
    {
        var articulation = comparison.Articulation;
        if (articulation.ArticulationScore < 0.45f) return "not-accepted-articulation";
        if (articulation.SilenceMismatch > 0.22f) return "not-accepted-silence-map";
        if (articulation.EnvelopeCosineSimilarity < 0.55f) return "not-accepted-envelope";
        if (articulation.MotorBandRatio > 2.2f || articulation.SpeechBandRatio < 0.45f) return "not-accepted-band-balance";
        return "smoke-only";
    }

    private static bool TryProbeBase(string path, out string basePath, out string kind)
    {
        const string energyIn = "/energy_in";
        const string energyOut = "/energy_out";
        if (path.EndsWith(energyIn, StringComparison.OrdinalIgnoreCase))
        {
            basePath = path[..^energyIn.Length];
            kind = "energy_in";
            return true;
        }

        if (path.EndsWith(energyOut, StringComparison.OrdinalIgnoreCase))
        {
            basePath = path[..^energyOut.Length];
            kind = "energy_out";
            return true;
        }

        basePath = "";
        kind = "";
        return false;
    }

    private readonly record struct ProbePeak(float EnergyIn, float EnergyOut);

    private static bool IsThrombosisTimelineProbe(string path) =>
        path.Contains("/node/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/source/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/contact/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/radiation/", StringComparison.OrdinalIgnoreCase);

    private static string AutomatedGraphSource(PinkTromboneUtteranceFixture fixture, bool DebugProbeUi = false)
    {
        var source = FaustEmitter.EmitScript(UtteranceGraphScript(fixture), new FaustExportOptions($"pt_utterance_{fixture.Id}", DebugProbeUi: DebugProbeUi)).Source;
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
        patch gain=0.2 soft_clip=true

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

        tract shape=human glottis=modal injection=inj nasal_branch=nose motion=motion propagation=graph loss=.999 freq=@/pink/frequency gain=@/pink/gain intensity=@/pink/intensity tenseness=@/pink/tenseness attack=.03 sustain={{F(fixture.DurationSeconds)}} decay=.05 tongue_index=@/pink/tongue/index tongue_diameter=@/pink/tongue/diameter constriction_index=@/pink/constriction/index constriction_diameter=@/pink/constriction/diameter turbulence=@/pink/turbulence velum=@/pink/velum lip=@/pink/lip/opening burst=@/pink/burst glottal_reflection=@/pink/glottal/reflection lip_reflection=@/pink/lip/reflection
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
