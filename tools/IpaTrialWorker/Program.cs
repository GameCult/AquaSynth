using System.Globalization;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using AquaSynth.Dsl;
using AquaSynth.Faust;
using GameCult.Caching.MessagePack;
using NAudio.Wave;

var command = args.FirstOrDefault();
if (string.IsNullOrWhiteSpace(command) || command is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

var options = ParseOptions(args.Skip(1).ToArray());
try
{
    switch (command)
    {
        case "seed":
            await SeedAsync(options);
            return 0;
        case "score":
            await ScoreAsync(options);
            return 0;
        case "song-prepare":
            await SongPrepareAsync(options);
            return 0;
        case "song-score":
            await SongScoreAsync(options);
            return 0;
        case "dump":
            await DumpAsync(options);
            return 0;
        case "distill":
            await DistillAsync(options);
            return 0;
        case "music-distill":
            await MusicDistillAsync(options);
            return 0;
        case "music-search":
            await MusicSearchAsync(options);
            return 0;
        case "music-show":
            await MusicShowAsync(options);
            return 0;
        case "search":
            await SearchAsync(options);
            return 0;
        case "index":
            await IndexAsync(options);
            return 0;
        case "show":
            await ShowAsync(options);
            return 0;
        default:
            Console.Error.WriteLine($"Unknown command `{command}`.");
            PrintHelp();
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

static async Task SeedAsync(Dictionary<string, string> options)
{
    var artifactRoot = Required(options, "artifact-root");
    var batchId = Value(options, "batch-id", "five-seed-trials");
    var store = Value(options, "store", Path.Combine(artifactRoot, "ipa-trial-results.cc"));
    var result = await IpaTrialOrchestrator.RunAsync(
        artifactRoot,
        options: new IpaTrialOrchestrationOptions(BatchId: batchId));
    await IpaTrialResultCultCacheStore.UpsertResultsAsync(store, result.TrialResults);
    Console.WriteLine(result.ArtifactDirectory);
    Console.WriteLine(store);
}

static async Task ScoreAsync(Dictionary<string, string> options)
{
    var patchRoot = Required(options, "patch-root");
    var artifactRoot = Required(options, "artifact-root");
    var batchId = Value(options, "batch-id", $"agent-candidates-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}");
    var store = Value(options, "store", Path.Combine(artifactRoot, "ipa-trial-results.cc"));
    var hypothesizer = Value(options, "hypothesizer", "external-codex-hypothesis-worker");
    var candidates = CandidateScripts(patchRoot, hypothesizer);
    var result = await IpaTrialOrchestrator.RunCandidateScriptsAsync(
        artifactRoot,
        candidates,
        options: new IpaTrialOrchestrationOptions(BatchId: batchId, HypothesizerId: hypothesizer));
    await IpaTrialResultCultCacheStore.UpsertResultsAsync(store, result.TrialResults);
    Console.WriteLine(result.ArtifactDirectory);
    Console.WriteLine(store);
}

static async Task SongPrepareAsync(Dictionary<string, string> options)
{
    var source = Required(options, "source");
    var artifactRoot = Required(options, "artifact-root");
    var durationSeconds = FloatValue(options, "duration-seconds", 10f);
    var sampleRate = IntValue(options, "sample-rate", 44100);
    var seed = IntValue(options, "seed", RandomNumberGenerator.GetInt32(1, int.MaxValue));
    var challengeId = Value(options, "challenge-id", $"song-snippet-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}");
    var output = Value(options, "output", Path.Combine(artifactRoot, "challenge.json"));

    if (!File.Exists(source))
    {
        throw new FileNotFoundException("Song source file was not found.", source);
    }

    Directory.CreateDirectory(artifactRoot);
    var decoded = DecodeMono(source);
    var resampled = Resample(decoded.Samples, decoded.SampleRate, sampleRate);
    var fullSong = durationSeconds <= 0;
    var clipLength = fullSong
        ? resampled.Length
        : Math.Min(resampled.Length, Math.Max(1, (int)MathF.Round(durationSeconds * sampleRate)));
    var maxStart = Math.Max(0, resampled.Length - clipLength);
    var random = new Random(seed);
    var startSample = fullSong || maxStart == 0 ? 0 : random.Next(0, maxStart + 1);
    var clip = new float[clipLength];
    Array.Copy(resampled, startSample, clip, 0, clipLength);
    NormalizePeak(clip, .9f);

    var referenceWav = Path.Combine(artifactRoot, "reference.wav");
    WriteWav(referenceWav, clip, sampleRate);
    var analyzer = new AudioAnalyzer(new AudioAnalysisConfig(SampleRate: sampleRate));
    var analysis = analyzer.Analyze(clip);
    var tempo = EstimateTempo(clip, sampleRate);
    var register = EstimateRegister(clip, sampleRate, analysis.Features.SpectralCentroidHz);
    var spectrogramPath = Path.Combine(artifactRoot, "logmel-spectrogram.csv");
    var bandStatsPath = Path.Combine(artifactRoot, "logmel-band-stats.csv");
    var envelopePath = Path.Combine(artifactRoot, "rms-envelope.csv");
    var envelopeAutocorrPath = Path.Combine(artifactRoot, "rms-envelope-autocorr.csv");
    var whitenedSpectralAutocorrPath = Path.Combine(artifactRoot, "whitened-spectral-autocorr.csv");
    var analysisReportPath = Path.Combine(artifactRoot, "analysis.md");
    await File.WriteAllTextAsync(spectrogramPath, SpectrogramCsv(analysis.LogMelSpectrogram), Encoding.UTF8);
    await File.WriteAllTextAsync(bandStatsPath, SpectrogramBandStatsCsv(analysis.LogMelSpectrogram), Encoding.UTF8);
    await File.WriteAllTextAsync(envelopePath, EnvelopeCsv(analysis.RmsEnvelope, sampleRate, analyzer.Config.HopSize), Encoding.UTF8);
    await File.WriteAllTextAsync(envelopeAutocorrPath, AutocorrelationCsv(analysis.RmsEnvelope, sampleRate / (float)Math.Max(1, analyzer.Config.HopSize)), Encoding.UTF8);
    await File.WriteAllTextAsync(whitenedSpectralAutocorrPath, WhitenedSpectralAutocorrelationCsv(analysis.LogMelSpectrogram, sampleRate / (float)Math.Max(1, analyzer.Config.HopSize)), Encoding.UTF8);
    var features = new SongChallengeFeatures(
        analysis.Features.DurationSeconds,
        analysis.Features.Peak,
        analysis.Features.Rms,
        analysis.Features.ZeroCrossingRate,
        analysis.Features.SpectralCentroidHz,
        analysis.Features.SpectralRolloffHz,
        ActiveDuty(clip, sampleRate),
        SpectralFlux(clip, sampleRate),
        tempo.Bpm,
        tempo.BeatSeconds,
        tempo.Confidence,
        register.DominantHz,
        register.LowHz,
        register.HighHz,
        register.RootNote,
        register.SuggestedScale,
        string.Join(",", register.ScaleFrequencies.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture))));
    var artifacts = new SongChallengeAnalysisArtifacts(
        spectrogramPath,
        bandStatsPath,
        envelopePath,
        envelopeAutocorrPath,
        whitenedSpectralAutocorrPath,
        analysisReportPath);
    var challenge = new SongChallenge(
        challengeId,
        source,
        Path.GetFileName(source),
        seed,
        startSample / (float)sampleRate,
        clipLength / (float)sampleRate,
        sampleRate,
        referenceWav,
        features,
        artifacts);
    await File.WriteAllTextAsync(analysisReportPath, SongAnalysisReport(challenge, analysis), Encoding.UTF8);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(challenge, JsonOptions()), Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(artifactRoot, "challenge.md"), SongChallengeReport(challenge), Encoding.UTF8);
    Console.WriteLine(output);
}

static async Task SongScoreAsync(Dictionary<string, string> options)
{
    var patchRoot = Required(options, "patch-root");
    var challengePath = Required(options, "challenge");
    var artifactRoot = Required(options, "artifact-root");
    var batchId = Value(options, "batch-id", $"song-round-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}");
    var store = Value(options, "store", Path.Combine(artifactRoot, "song-trial-results.cc"));
    var hypothesizer = Value(options, "hypothesizer", "external-codex-song-worker");
    var challenge = JsonSerializer.Deserialize<SongChallenge>(
        await File.ReadAllTextAsync(challengePath, Encoding.UTF8),
        JsonOptions()) ?? throw new InvalidDataException($"Could not read song challenge `{challengePath}`.");
    var referenceSamples = ReadMonoPcm16Wav(challenge.ReferenceWavPath);
    var candidates = SongCandidateScripts(patchRoot, hypothesizer);
    if (candidates.Count == 0)
    {
        throw new InvalidDataException($"No .aqua song candidates found under `{patchRoot}`.");
    }

    var batchDirectory = Path.Combine(artifactRoot, batchId);
    var candidateRoot = Path.Combine(batchDirectory, "song-candidates");
    var timelineRoot = Path.Combine(batchDirectory, "song-timelines");
    Directory.CreateDirectory(candidateRoot);
    Directory.CreateDirectory(timelineRoot);
    var analyzer = new AudioAnalyzer(new AudioAnalysisConfig(SampleRate: challenge.SampleRate));
    var challengeEvidence = SongChallengeEvidenceDocuments(challenge).ToArray();
    await IpaTrialResultCultCacheStore.UpsertSongChallengeEvidenceAsync(store, challengeEvidence);
    var results = new List<IpaTrialResult>();

    foreach (var candidate in candidates)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var startedStamp = Stopwatch.GetTimestamp();
        var safeCandidateId = SafeName(candidate.CandidateId);
        var candidateDir = Path.Combine(candidateRoot, safeCandidateId);
        var audioDir = Path.Combine(candidateDir, "audio");
        Directory.CreateDirectory(audioDir);
        var scriptCopy = Path.Combine(candidateDir, "candidate.aqua");
        File.Copy(candidate.ScriptPath, scriptCopy, overwrite: true);
        var script = await File.ReadAllTextAsync(scriptCopy, Encoding.UTF8);
        var timelinePath = Path.Combine(timelineRoot, $"{safeCandidateId}.csv");
        var dspPath = Path.Combine(candidateDir, "candidate.dsp");

        var candidateWav = Path.Combine(audioDir, "candidate.wav");
        var metrics = new List<SpeechScoreMetric>();
        var instrumentProfile = AnalyzeSongInstrumentProfile(script);
        var productionProfile = AnalyzeSongProductionProfile(script, scriptCopy, artifactRoot);
        var artifacts = new List<SpeechRenderArtifact>
        {
            Artifact("song-challenge", challengePath),
            Artifact("reference-wav", challenge.ReferenceWavPath),
            Artifact("candidate-script", scriptCopy),
            Artifact("primitive-timeline", timelinePath)
        };
        metrics.AddRange(SongTargetMetrics(challenge, challengeEvidence));
        metrics.AddRange(SongInstrumentMetrics(instrumentProfile));
        metrics.AddRange(SongProductionMetrics(productionProfile));
        artifacts.AddRange(SongChallengeArtifacts(challenge));
        artifacts.AddRange(challengeEvidence.Select(SongChallengeEvidenceArtifact));
        var timelineFacts = Array.Empty<PrimitiveTimelineFact>();
        string verdict;
        string evaluation;
        try
        {
            var patch = PatchScript.Parse(script);
            var timelineNetwork = patch.VocalNetworks.FirstOrDefault();
            var timeline = timelineNetwork is null
                ? Array.Empty<ProbeTimelineSample>()
                : ProbeTimelineReport.Build(patch, timelineNetwork.Name, 12);
            await File.WriteAllTextAsync(timelinePath, ProbeTimelineReport.ToCsv(timeline), Encoding.UTF8);
            timelineFacts = PrimitiveTimelineFactExtractor.Extract(timeline).ToArray();
            var source = FaustEmitter.EmitScript(script, new FaustExportOptions(safeCandidateId)).Source;
            await File.WriteAllTextAsync(dspPath, source, Encoding.UTF8);
            artifacts.Add(Artifact("candidate-dsp", dspPath));

            var render = await FaustCompiler.RenderAsync(
                source,
                new FaustRenderOptions(challenge.SampleRate, challenge.DurationSeconds));
            if (render is null || render.Samples.Length == 0)
            {
                verdict = "render-failed";
                evaluation = render is null
                    ? "Faust was not available to render this song candidate."
                    : $"Faust render produced no samples. stderr: {render.Stderr}";
                metrics.Add(new SpeechScoreMetric("render_failed", 1, 1));
            }
            else
            {
                var matched = MatchLength(render.Samples, referenceSamples.Length);
                NormalizePeak(matched, .9f);
                WriteWav(candidateWav, matched, challenge.SampleRate);
                artifacts.Add(Artifact("candidate-wav", candidateWav));
                var candidateAnalysisArtifacts = await WriteSongRenderAnalysisAsync(
                    matched,
                    challenge.SampleRate,
                    audioDir,
                    "candidate",
                    analyzer);
                artifacts.AddRange(candidateAnalysisArtifacts.Select(ArtifactFromAnalysis));
                var comparison = analyzer.Compare(referenceSamples, matched);
                var continuity = AnalyzeSongContinuity(comparison);
                metrics.AddRange(SongComparisonMetrics(comparison));
                metrics.AddRange(SongContinuityMetrics(continuity));
                verdict = SongVerdict(comparison, instrumentProfile, productionProfile, continuity);
                evaluation = SongEvaluationSentence(challenge, comparison, verdict, instrumentProfile, continuity) +
                             $" Production profile: {productionProfile.Summary}.";
                var comparisonPath = Path.Combine(audioDir, "comparison.txt");
                await File.WriteAllTextAsync(comparisonPath, SongComparisonReport(challenge, candidate, comparison, verdict, instrumentProfile, continuity) + Environment.NewLine + $"productionSummary={productionProfile.Summary}" + Environment.NewLine, Encoding.UTF8);
                artifacts.Add(Artifact("comparison-report", comparisonPath));
            }
        }
        catch (Exception ex) when (ex is PatchScriptException or InvalidOperationException or InvalidDataException)
        {
            await File.WriteAllTextAsync(timelinePath, ProbeTimelineReport.ToCsv([]), Encoding.UTF8);
            verdict = "render-failed";
            evaluation = $"Candidate could not be parsed, lowered, or prepared for render: {ex.GetType().Name}: {ex.Message}";
            metrics.Add(new SpeechScoreMetric("render_failed", 1, 1));
            metrics.Add(new SpeechScoreMetric("candidate_invalid", 1, 1));
        }

        var latency = Stopwatch.GetElapsedTime(startedStamp).TotalMilliseconds;
        results.Add(new IpaTrialResult(
            $"{batchId}:song-snippet:{safeCandidateId}",
            batchId,
            startedAt.ToString("O", CultureInfo.InvariantCulture),
            "song-snippet",
            ["song", "snippet", "alien-gibberish"],
            challenge.ChallengeId,
            safeCandidateId,
            candidate.HypothesizerId,
            candidate.Hypothesis,
            scriptCopy,
            challenge.ReferenceWavPath,
            File.Exists(candidateWav) ? candidateWav : "",
            timelinePath,
            metrics.ToArray(),
            artifacts.ToArray(),
            "song-snippet-audio-evaluator",
            evaluation,
            verdict,
            [
                "This is a local-only song clip challenge; reference audio is not redistributed by the repo.",
                challenge.StartSeconds <= 0.001f
                    ? $"The target starts at the source file beginning and lasts {challenge.DurationSeconds:0.###} seconds; in full-song runs this is the whole decoded song, not IPA articulation truth."
                    : $"The target is a randomly selected {challenge.DurationSeconds:0.###}-second scene-audio snippet, not IPA articulation truth.",
                "Challenge spectrogram, derivative, envelope, and autocorrelation artifacts are stored as typed CultMesh/CultCache evidence documents in the .cc database.",
                "Full-patch FM/AM/noise/scene modeling is allowed; primitive timeline facts remain diagnostic when present.",
                instrumentProfile.ChipDistressRisk >= .6f
                    ? "Instrument profile is chip-distress-risky: simple oscillator/noise roles are not enough curriculum evidence for the music generator."
                    : "Instrument profile uses at least some owned musical role evidence instead of only simple oscillator distress.",
                productionProfile.MusicianshipScore < .45f
                    ? "Production profile is weak: the agent did not leave enough producer brief, listening journal, gap ledger, section form, or anti-template evidence to teach future runs."
                    : "Production profile includes studio evidence and arrangement/form pressure, not just syntax tokens."
            ],
            [
                new SpeechTimingReceipt(
                    "song-snippet-render-score",
                    startedAt.ToString("O", CultureInfo.InvariantCulture),
                    latency,
                    0,
                    SongConfidence(metrics),
                    "Local worker rendered an AquaSynth candidate against a frozen song snippet and wrote CultCache trial result data.")
            ],
            timelineFacts));
    }

    try
    {
        await IpaTrialResultCultCacheStore.UpsertResultsAsync(store, results);
    }
    catch (UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"warning: could not flush CultCache store `{store}` in this environment; continuing with filesystem artifacts only.");
    }
    var summaryPath = Path.Combine(batchDirectory, "summary.csv");
    await File.WriteAllTextAsync(summaryPath, SongSummaryCsv(results), Encoding.UTF8);
    var reportPath = Path.Combine(batchDirectory, "evaluator-report.md");
    await File.WriteAllTextAsync(reportPath, SongEvaluatorReport(challenge, results), Encoding.UTF8);
    Console.WriteLine(batchDirectory);
    Console.WriteLine(store);
}

static async Task DumpAsync(Dictionary<string, string> options)
{
    var store = Required(options, "store");
    var output = Required(options, "output");
    var results = await IpaTrialResultCultCacheStore.ReadResultsAsync(store);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, StoreReport(store, results), Encoding.UTF8);
    Console.WriteLine(output);
}

static async Task DistillAsync(Dictionary<string, string> options)
{
    var store = Required(options, "store");
    var outputStore = Required(options, "output-store");
    var output = Required(options, "output");
    var maxResults = int.TryParse(Value(options, "max-results", "40"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMax)
        ? Math.Clamp(parsedMax, 1, 500)
        : 40;
    var minCosine = FloatValue(options, "min-cosine", .35f);
    var results = await IpaTrialResultCultCacheStore.ReadResultsAsync(store);
    var evidence = await IpaTrialResultCultCacheStore.ReadSongChallengeEvidenceAsync(store);
    var selected = DistillResults(results, maxResults, minCosine).ToArray();
    var selectedReferenceIds = selected.Select(result => result.ReferenceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var selectedEvidence = evidence
        .Where(document => selectedReferenceIds.Contains(document.ChallengeId))
        .ToArray();
    var distillation = SongTrialDistillation(store, results, selected, minCosine);

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputStore))!);
    if (File.Exists(outputStore))
    {
        File.Delete(outputStore);
    }

    await IpaTrialResultCultCacheStore.UpsertResultsAsync(outputStore, selected);
    await IpaTrialResultCultCacheStore.UpsertSongChallengeEvidenceAsync(outputStore, selectedEvidence);
    await IpaTrialResultCultCacheStore.UpsertSongTrialDistillationsAsync(outputStore, [distillation]);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, DistillationReport(store, outputStore, results, selected, evidence, selectedEvidence, distillation, minCosine), Encoding.UTF8);
    Console.WriteLine(outputStore);
    Console.WriteLine(output);
}

static async Task MusicDistillAsync(Dictionary<string, string> options)
{
    var artifactRoot = Required(options, "artifact-root");
    var outputStore = Required(options, "output-store");
    var output = Required(options, "output");
    var maxCandidates = int.TryParse(Value(options, "max-candidates", "16"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? Math.Clamp(parsed, 1, 100)
        : 16;

    var documents = BuildMusicKnowledgeDocuments(artifactRoot, maxCandidates).ToArray();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputStore))!);
    if (File.Exists(outputStore))
    {
        File.Delete(outputStore);
    }

    var recordDirectory = DirectoryMessagePackBackingStore.DefaultRecordDirectoryPath(outputStore);
    if (Directory.Exists(recordDirectory))
    {
        Directory.Delete(recordDirectory, recursive: true);
    }

    await IpaTrialResultCultCacheStore.UpsertMusicProductionKnowledgeAsync(outputStore, documents);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, MusicKnowledgeDistillationReport(artifactRoot, outputStore, documents), Encoding.UTF8);
    Console.WriteLine(outputStore);
    Console.WriteLine(output);
}

static async Task MusicSearchAsync(Dictionary<string, string> options)
{
    var store = Required(options, "store");
    var query = Required(options, "query");
    var output = Required(options, "output");
    var limit = int.TryParse(Value(options, "limit", "12"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? Math.Clamp(parsed, 1, 100)
        : 12;
    var documents = await IpaTrialResultCultCacheStore.ReadMusicProductionKnowledgeAsync(store);
    var hits = RankMusicKnowledge(documents, query, limit);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, MusicKnowledgeSearchReport(store, query, hits), Encoding.UTF8);
    Console.WriteLine(output);
}

static async Task MusicShowAsync(Dictionary<string, string> options)
{
    var store = Required(options, "store");
    var id = Required(options, "knowledge-id");
    var output = Required(options, "output");
    var documents = await IpaTrialResultCultCacheStore.ReadMusicProductionKnowledgeAsync(store);
    var document = documents.FirstOrDefault(item =>
        item.KnowledgeId.Equals(id, StringComparison.OrdinalIgnoreCase) ||
        item.Topic.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"No music knowledge document found for `{id}`.");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, MusicKnowledgeDetailReport(store, document), Encoding.UTF8);
    Console.WriteLine(output);
}

static async Task SearchAsync(Dictionary<string, string> options)
{
    var store = Required(options, "store");
    var query = Required(options, "query");
    var output = Required(options, "output");
    var limit = int.TryParse(Value(options, "limit", "12"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? Math.Clamp(parsed, 1, 100)
        : 12;
    var results = await IpaTrialResultCultCacheStore.ReadResultsAsync(store);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    var searchOptions = SearchOptionsFrom(options);
    var ranked = await SearchRankAsync(results, query, limit, store, searchOptions);
    await File.WriteAllTextAsync(output, SearchReport(store, query, results, ranked, searchOptions), Encoding.UTF8);
    Console.WriteLine(output);
}

static async Task IndexAsync(Dictionary<string, string> options)
{
    var store = Required(options, "store");
    var output = Value(options, "output", "");
    var results = await IpaTrialResultCultCacheStore.ReadResultsAsync(store);
    var searchOptions = SearchOptionsFrom(options);
    var index = await EnsureVectorIndexAsync(results, store, searchOptions, force: BoolValue(options, "force"));
    var report = new StringBuilder();
    report.AppendLine("# IPA Trial Vector Index");
    report.AppendLine();
    report.AppendLine($"store: `{store}`");
    report.AppendLine($"collection: `{searchOptions.Collection}`");
    report.AppendLine($"qdrant: `{searchOptions.QdrantUrl}`");
    report.AppendLine($"ollama: `{searchOptions.OllamaUrl}`");
    report.AppendLine($"embedder: `{searchOptions.EmbedModel}`");
    report.AppendLine($"chunks: {index.ChunkCount}");
    report.AppendLine($"vector_dimensions: {index.VectorSize}");
    report.AppendLine($"store_key: `{StoreKey(store)}`");
    if (!string.IsNullOrWhiteSpace(output))
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, report.ToString(), Encoding.UTF8);
        Console.WriteLine(output);
    }
    else
    {
        Console.Write(report.ToString());
    }
}

static async Task<IReadOnlyList<SearchHit>> SearchRankAsync(
    IReadOnlyList<IpaTrialResult> results,
    string query,
    int limit,
    string store,
    SearchOptions options)
{
    if (!options.UseVector)
    {
        return Rank(results, query, limit, store, "lexical-disabled");
    }

    try
    {
        if (!options.SkipIndex)
        {
            await EnsureVectorIndexAsync(results, store, options, force: false);
        }

        var vectorHits = await QueryVectorIndexAsync(query, limit * 3, store, options);
        if (vectorHits.Count > 0)
        {
            return MergeVectorAndLexicalHits(results, query, limit, store, vectorHits);
        }
    }
    catch (Exception ex) when (!options.RequireVector)
    {
        return Rank(results, query, limit, store, $"vector-fallback:{ex.GetType().Name}:{ex.Message}");
    }

    return Rank(results, query, limit, store, "vector-empty-fallback");
}

static async Task<VectorIndexResult> EnsureVectorIndexAsync(
    IReadOnlyList<IpaTrialResult> results,
    string store,
    SearchOptions options,
    bool force)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
    var chunks = BuildEvidenceChunks(results, store).ToArray();
    if (chunks.Length == 0)
    {
        return new VectorIndexResult(0, 0);
    }

    if (force)
    {
        await DeleteCollectionIfExistsAsync(http, options);
    }

    var sampleVector = await EmbedOneAsync(http, options, chunks[0].Text);
    await EnsureQdrantCollectionAsync(http, options, sampleVector.Length);

    const int batchSize = 32;
    for (var index = 0; index < chunks.Length; index += batchSize)
    {
        var batch = chunks.Skip(index).Take(batchSize).ToArray();
        var vectors = await EmbedManyAsync(http, options, batch.Select(chunk => chunk.Text).ToArray());
        if (vectors.Length != batch.Length)
        {
            throw new InvalidDataException($"Ollama returned {vectors.Length} vectors for {batch.Length} evidence chunks.");
        }

        var points = batch.Select((chunk, offset) => new QdrantPoint(
            StableUuid(chunk.Id),
            vectors[offset],
            QdrantPayloadFrom(chunk, options.EmbedModel))).ToArray();
        var upsert = new QdrantUpsertRequest(points);
        var response = await http.PutAsJsonAsync(
            $"{options.QdrantUrl.TrimEnd('/')}/collections/{Uri.EscapeDataString(options.Collection)}/points?wait=true",
            upsert,
            JsonOptions());
        response.EnsureSuccessStatusCode();
    }

    await EnsurePayloadIndexesAsync(http, options);
    return new VectorIndexResult(chunks.Length, sampleVector.Length);
}

static async Task<IReadOnlyList<VectorSearchHit>> QueryVectorIndexAsync(
    string query,
    int limit,
    string store,
    SearchOptions options)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
    var queryVector = await EmbedOneAsync(http, options, query);
    var request = new QdrantSearchRequest(
        queryVector,
        Math.Clamp(limit, 1, 100),
        true,
        false,
        QdrantFilter.Store(StoreKey(store), options.EmbedModel));
    var response = await http.PostAsJsonAsync(
        $"{options.QdrantUrl.TrimEnd('/')}/collections/{Uri.EscapeDataString(options.Collection)}/points/search",
        request,
        JsonOptions());
    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(JsonOptions())
        ?? new QdrantSearchResponse([]);
    return result.Result
        .Select(VectorSearchHitFrom)
        .Where(hit => !string.IsNullOrWhiteSpace(hit.TrialId))
        .ToArray();
}

static IReadOnlyList<SearchHit> MergeVectorAndLexicalHits(
    IReadOnlyList<IpaTrialResult> results,
    string query,
    int limit,
    string store,
    IReadOnlyList<VectorSearchHit> vectorHits)
{
    var lexical = Rank(results, query, Math.Max(limit, results.Count), store, "hybrid-lexical");
    var lexicalByTrial = lexical.ToDictionary(hit => hit.Result.TrialId, StringComparer.OrdinalIgnoreCase);
    var vectorByTrial = vectorHits
        .GroupBy(hit => hit.TrialId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.OrderByDescending(hit => hit.Score).First(), StringComparer.OrdinalIgnoreCase);
    var resultsByTrial = results.ToDictionary(result => result.TrialId, StringComparer.OrdinalIgnoreCase);
    return vectorByTrial.Values
        .Where(hit => resultsByTrial.ContainsKey(hit.TrialId))
        .Select(hit =>
        {
            var result = resultsByTrial[hit.TrialId];
            var lexicalHit = lexicalByTrial.GetValueOrDefault(result.TrialId);
            var matchedByField = lexicalHit?.MatchedByField ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var matchedTerms = lexicalHit?.MatchedTerms ?? [];
            var tags = EvidenceTags(result, SearchDocument.From(result, DocumentFields(result, store)));
            var lexicalScore = lexicalHit?.Score ?? 0;
            var queryTerms = ExpandTerms(Tokenize(query)).ToArray();
            var score = hit.Score * 10f
                + MathF.Min(lexicalScore, 8f)
                + MetricBias(result, queryTerms)
                + ClassAffinityBias(result, queryTerms);
            return new SearchHit(
                result,
                score,
                matchedTerms,
                matchedByField,
                tags,
                "qdrant-ollama",
                hit.Score,
                hit.ChunkId,
                hit.Text);
        })
        .OrderByDescending(hit => hit.Score)
        .ThenByDescending(hit => ScoreSort(hit.Result))
        .Take(limit)
        .ToArray();
}

static async Task DeleteCollectionIfExistsAsync(HttpClient http, SearchOptions options)
{
    var response = await http.GetAsync($"{options.QdrantUrl.TrimEnd('/')}/collections/{Uri.EscapeDataString(options.Collection)}");
    if (!response.IsSuccessStatusCode)
    {
        return;
    }

    var delete = await http.DeleteAsync($"{options.QdrantUrl.TrimEnd('/')}/collections/{Uri.EscapeDataString(options.Collection)}");
    delete.EnsureSuccessStatusCode();
}

static async Task EnsureQdrantCollectionAsync(HttpClient http, SearchOptions options, int vectorSize)
{
    var url = $"{options.QdrantUrl.TrimEnd('/')}/collections/{Uri.EscapeDataString(options.Collection)}";
    var existing = await http.GetAsync(url);
    if (existing.IsSuccessStatusCode)
    {
        return;
    }

    var create = new QdrantCreateCollectionRequest(
        new QdrantVectorConfig(vectorSize, "Cosine", true),
        true,
        new Dictionary<string, object>
        {
            ["managedBy"] = "aquasynth",
            ["corpusKind"] = "ipa_trial_result",
            ["embedderId"] = options.EmbedModel,
            ["schemaVersion"] = 1
        });
    var response = await http.PutAsJsonAsync(url, create, JsonOptions());
    response.EnsureSuccessStatusCode();
}

static async Task EnsurePayloadIndexesAsync(HttpClient http, SearchOptions options)
{
    foreach (var field in new[] { "storeKey", "trialId", "candidateId", "targetSetId", "chunkKind", "embedderId" })
    {
        var response = await http.PutAsJsonAsync(
            $"{options.QdrantUrl.TrimEnd('/')}/collections/{Uri.EscapeDataString(options.Collection)}/index?wait=true",
            new QdrantPayloadIndexRequest(field, "keyword"),
            JsonOptions());
        if (!response.IsSuccessStatusCode)
        {
            // Existing indexes may be reported as an error by older Qdrant builds.
            continue;
        }
    }
}

static async Task<float[]> EmbedOneAsync(HttpClient http, SearchOptions options, string text) =>
    (await EmbedManyAsync(http, options, [text])).FirstOrDefault()
    ?? throw new InvalidDataException("Ollama returned no embedding.");

static async Task<float[][]> EmbedManyAsync(HttpClient http, SearchOptions options, string[] texts)
{
    var response = await http.PostAsJsonAsync(
        $"{options.OllamaUrl.TrimEnd('/')}/api/embed",
        new OllamaEmbedRequest(options.EmbedModel, texts),
        JsonOptions());
    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(JsonOptions())
        ?? throw new InvalidDataException("Ollama returned an empty embedding response.");
    if (body.Embeddings is { Length: > 0 })
    {
        return body.Embeddings;
    }

    if (body.Embedding is { Length: > 0 })
    {
        return [body.Embedding];
    }

    throw new InvalidDataException("Ollama returned no embeddings.");
}

static async Task ShowAsync(Dictionary<string, string> options)
{
    var store = Required(options, "store");
    var trialId = Required(options, "trial-id");
    var output = Required(options, "output");
    var results = await IpaTrialResultCultCacheStore.ReadResultsAsync(store);
    var result = results.FirstOrDefault(item =>
        item.TrialId.Equals(trialId, StringComparison.OrdinalIgnoreCase) ||
        item.CandidateId.Equals(trialId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"No trial or candidate found for `{trialId}`.");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, DetailReport(store, result), Encoding.UTF8);
    Console.WriteLine(output);
}

static IReadOnlyList<IpaTrialScriptCandidate> CandidateScripts(string patchRoot, string hypothesizer)
{
    if (!Directory.Exists(patchRoot))
    {
        throw new DirectoryNotFoundException(patchRoot);
    }

    var targetIds = IpaTrialOrchestrator.DefaultFiveSeedTrialSets
        .SelectMany(set => set.Targets)
        .Select(target => target.Target.Id)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(id => id.Length)
        .ToArray();
    return Directory.EnumerateFiles(patchRoot, "*.aqua", SearchOption.AllDirectories)
        .Order(StringComparer.OrdinalIgnoreCase)
        .Select(path =>
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            var target = TargetIdFromStem(stem, targetIds);
            return new IpaTrialScriptCandidate(
                target,
                stem,
                path,
                hypothesizer,
                $"External Codex-authored IPA patch candidate `{stem}` for target `{target}`.");
        })
        .ToArray();
}

static IReadOnlyList<IpaTrialScriptCandidate> SongCandidateScripts(string patchRoot, string hypothesizer)
{
    if (!Directory.Exists(patchRoot))
    {
        throw new DirectoryNotFoundException(patchRoot);
    }

    return Directory.EnumerateFiles(patchRoot, "*.aqua", SearchOption.AllDirectories)
        .Order(StringComparer.OrdinalIgnoreCase)
        .Select(path =>
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            return new IpaTrialScriptCandidate(
                "song-snippet",
                stem,
                path,
                hypothesizer,
                $"External Codex-authored song-snippet patch candidate `{stem}`.");
        })
        .ToArray();
}

static string TargetIdFromStem(string stem, IReadOnlyList<string> targetIds)
{
    var explicitSplit = stem.Split(["__"], StringSplitOptions.None);
    if (explicitSplit.Length > 1 && targetIds.Contains(explicitSplit[0], StringComparer.OrdinalIgnoreCase))
    {
        return explicitSplit[0];
    }

    var matched = targetIds.FirstOrDefault(id =>
        stem.Equals(id, StringComparison.OrdinalIgnoreCase) ||
        stem.StartsWith($"{id}-", StringComparison.OrdinalIgnoreCase) ||
        stem.StartsWith($"{id}_", StringComparison.OrdinalIgnoreCase));
    if (!string.IsNullOrWhiteSpace(matched))
    {
        return matched;
    }

    throw new InvalidDataException(
        $"Could not infer IPA target id from `{stem}`. Name files as `<targetId>__candidate-name.aqua`; known target ids: {string.Join(", ", targetIds)}.");
}

static IReadOnlyList<SearchHit> Rank(
    IReadOnlyList<IpaTrialResult> results,
    string query,
    int limit,
    string store,
    string retrievalMode = "lexical")
{
    var queryTerms = ExpandTerms(Tokenize(query)).ToArray();
    var documents = results
        .Select(result => SearchDocument.From(result, DocumentFields(result, store)))
        .ToArray();
    var documentFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var document in documents)
    {
        foreach (var term in document.AllTerms.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            documentFrequency[term] = documentFrequency.TryGetValue(term, out var existingCount) ? existingCount + 1 : 1;
        }
    }

    var count = Math.Max(1, documents.Length);
    return results
        .Zip(documents)
        .Select(pair =>
        {
            var (result, document) = pair;
            var matchedByField = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var lexical = 0f;
            foreach (var (field, weightedTerms) in document.Fields)
            {
                var termCounts = weightedTerms.Terms.GroupBy(term => term, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
                var matched = queryTerms.Where(term => termCounts.ContainsKey(term))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (matched.Length == 0)
                {
                    continue;
                }

                matchedByField[field] = matched;
                foreach (var term in matched)
                {
                    var df = documentFrequency.TryGetValue(term, out var value) ? value : 1;
                    var idf = MathF.Log(1 + ((count - df + 0.5f) / (df + 0.5f)));
                    lexical += weightedTerms.Weight * (1f + MathF.Log(1 + termCounts[term])) * Math.Max(0.1f, idf);
                }
            }

            var matchedTerms = matchedByField.Values.SelectMany(term => term)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var metricBias = MetricBias(result, queryTerms);
            var evidenceBias = EvidenceBias(result, queryTerms, document);
            var classBias = ClassAffinityBias(result, queryTerms);
            var score = lexical + metricBias + evidenceBias + classBias;
            return new SearchHit(result, score, matchedTerms, matchedByField, EvidenceTags(result, document), retrievalMode);
        })
        .Where(item => item.Score > 0)
        .OrderByDescending(item => item.Score)
        .ThenByDescending(item => ScoreSort(item.Result))
        .ThenBy(item => item.Result.TrialId, StringComparer.Ordinal)
        .Take(limit)
        .ToArray();
}

static IEnumerable<string> ExpandTerms(IEnumerable<string> terms)
{
    var set = new HashSet<string>(terms, StringComparer.OrdinalIgnoreCase);
    var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["vowel"] = ["a", "i", "u", "e", "o", "formant", "tongue", "lip", "radiation"],
        ["nasal"] = ["m", "n", "ng", "ŋ", "velum", "branch", "velopharynx", "admittance"],
        ["sonorant"] = ["nasal", "approximant", "l", "r", "voiced"],
        ["fricative"] = ["s", "z", "f", "v", "th", "θ", "turbulence", "noise", "constriction"],
        ["stop"] = ["p", "b", "t", "d", "k", "plosive", "closure", "release", "reservoir"],
        ["plosive"] = ["stop", "closure", "release", "reservoir", "stored", "pressure"],
        ["weak"] = ["render-failed", "weak", "low", "failed", "silent"],
        ["promising"] = ["pressure", "promising", "high", "improved"],
        ["articulation"] = ["gesture", "primitive", "timeline", "opening", "flow"],
        ["dressing"] = ["fm", "am", "modulator", "envelope", "noise", "patch"],
        ["owner"] = ["source", "lowering", "tract", "radiation", "gesture", "orchestration"]
    };

    var queue = set.ToArray();
    foreach (var term in queue)
    {
        if (!aliases.TryGetValue(term, out var expanded))
        {
            continue;
        }

        foreach (var alias in expanded)
        {
            set.Add(alias);
        }
    }

    return set;
}

static float MetricBias(IpaTrialResult result, IReadOnlyCollection<string> terms)
{
    var bias = 0f;
    var cosine = result.Metrics.FirstOrDefault(metric => metric.Name == "log_mel_cosine")?.Value ?? 0;
    var articulation = result.Metrics.FirstOrDefault(metric => metric.Name == "articulation_score")?.Value ?? 0;
    var gesture = result.Metrics.FirstOrDefault(metric => metric.Name == "gesture_score")?.Value ?? 0;
    if (terms.Contains("weak", StringComparer.OrdinalIgnoreCase))
    {
        bias += Math.Clamp(1 - cosine, 0, 1);
        bias += Math.Clamp(1 - articulation, 0, 1);
    }

    if (terms.Contains("promising", StringComparer.OrdinalIgnoreCase))
    {
        bias += Math.Clamp(cosine, 0, 1);
        bias += Math.Clamp(articulation, 0, 1);
    }

    if (terms.Contains("gesture", StringComparer.OrdinalIgnoreCase) ||
        terms.Contains("articulation", StringComparer.OrdinalIgnoreCase))
    {
        bias += Math.Clamp(gesture, 0, 1);
    }

    return bias;
}

static float EvidenceBias(IpaTrialResult result, IReadOnlyCollection<string> terms, SearchDocument document)
{
    var bias = 0f;
    if (terms.Contains("timeline", StringComparer.OrdinalIgnoreCase) &&
        document.Fields.TryGetValue("artifacts", out var artifacts) &&
        artifacts.Terms.Contains("timeline", StringComparer.OrdinalIgnoreCase))
    {
        bias += 1.25f;
    }

    if (terms.Contains("contrast", StringComparer.OrdinalIgnoreCase) ||
        terms.Contains("pair", StringComparer.OrdinalIgnoreCase))
    {
        bias += result.Phonemes.Length > 1 ? 0.25f : 0.5f;
    }

    if (terms.Contains("source", StringComparer.OrdinalIgnoreCase) &&
        result.Hypothesis.Contains("source", StringComparison.OrdinalIgnoreCase))
    {
        bias += 0.75f;
    }

    return bias;
}

static float ClassAffinityBias(IpaTrialResult result, IReadOnlyCollection<string> terms)
{
    var intents = QueryClasses(terms).ToArray();
    if (intents.Length == 0)
    {
        return 0;
    }

    if (intents.Contains("dressing", StringComparer.OrdinalIgnoreCase))
    {
        return MentionsDressing(result) ? 2.0f : -0.75f;
    }

    var classes = ResultClasses(result).ToArray();
    var primaryIntents = intents
        .Where(intent => !intent.Equals("mixed", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    if (primaryIntents.Length > 0)
    {
        if (primaryIntents.Intersect(classes, StringComparer.OrdinalIgnoreCase).Any())
        {
            return classes.Contains("mixed", StringComparer.OrdinalIgnoreCase) ? 2.0f : 2.5f;
        }

        return classes.Contains("mixed", StringComparer.OrdinalIgnoreCase) ? -1.0f : -1.75f;
    }

    return intents.Intersect(classes, StringComparer.OrdinalIgnoreCase).Any() ? 1.25f : -0.75f;
}

static IEnumerable<string> QueryClasses(IEnumerable<string> terms)
{
    var set = new HashSet<string>(terms, StringComparer.OrdinalIgnoreCase);
    if (set.Overlaps(["stop", "plosive", "closure", "release", "reservoir"]))
    {
        yield return "stop";
    }
    if (set.Overlaps(["fricative", "sibilant", "constriction", "turbulence", "labiodental", "dental"]))
    {
        yield return "fricative";
    }
    if (set.Overlaps(["nasal", "velum", "veloparynx", "velopharynx", "branch", "admittance"]))
    {
        yield return "nasal";
    }
    if (set.Overlaps(["vowel", "formant", "tongue", "body", "rounded", "unrounded"]))
    {
        yield return "vowel";
    }
    if (set.Overlaps(["mixed", "transfer", "generalization"]))
    {
        yield return "mixed";
    }
    if (set.Overlaps(["dressing", "fm", "am", "modulator", "envelope", "helper"]))
    {
        yield return "dressing";
    }
}

static IEnumerable<string> ResultClasses(IpaTrialResult result)
{
    var candidateClasses = CandidateSpecificClasses(result.CandidateId).ToArray();
    var target = result.TargetSetId.ToLowerInvariant();
    if (candidateClasses.Length > 0)
    {
        foreach (var candidateClass in candidateClasses)
        {
            yield return candidateClass;
        }

        if (target.Contains("mixed", StringComparison.Ordinal))
        {
            yield return "mixed";
        }

        yield break;
    }

    if (target.Contains("mixed", StringComparison.Ordinal))
    {
        yield return "mixed";
        yield break;
    }
    if (target.Contains("stop", StringComparison.Ordinal))
    {
        yield return "stop";
        yield break;
    }
    if (target.Contains("fricative", StringComparison.Ordinal))
    {
        yield return "fricative";
        yield break;
    }
    if (target.Contains("nasal", StringComparison.Ordinal))
    {
        yield return "nasal";
        yield break;
    }
    if (target.Contains("vowel", StringComparison.Ordinal))
    {
        yield return "vowel";
        yield break;
    }

    foreach (var phoneme in result.Phonemes)
    {
        var normalized = phoneme.ToLowerInvariant();
        if (normalized is "p" or "b" or "t" or "d" or "k")
        {
            yield return "stop";
        }
        if (normalized is "s" or "z" or "f" or "v" or "th" or "θ")
        {
            yield return "fricative";
        }
        if (normalized is "m" or "n" or "ng" or "ŋ")
        {
            yield return "nasal";
        }
        if (normalized is "a" or "i" or "u" or "e" or "o")
        {
            yield return "vowel";
        }
    }
}

static IEnumerable<string> CandidateSpecificClasses(string candidateId)
{
    var normalized = candidateId.ToLowerInvariant();
    var prefix = normalized;
    if (prefix.StartsWith("mix-", StringComparison.Ordinal))
    {
        prefix = prefix[4..];
    }

    prefix = prefix.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? prefix;
    if (prefix is "p" or "b" or "t" or "d" or "k")
    {
        yield return "stop";
    }
    if (prefix is "s" or "z" or "f" or "v" or "th" or "θ")
    {
        yield return "fricative";
    }
    if (prefix is "m" or "n" or "ng" or "ŋ")
    {
        yield return "nasal";
    }
    if (prefix is "a" or "i" or "u" or "e" or "o")
    {
        yield return "vowel";
    }
    if (prefix is "l" or "r")
    {
        yield return "approximant";
    }
}

static bool MentionsDressing(IpaTrialResult result)
{
    var text = string.Join(' ', [
        result.Hypothesis,
        result.EvaluationSummary,
        string.Join(' ', result.KnownLies)
    ]);
    return Tokenize(text).Any(term => term is "dressing" or "fm" or "am" or "modulator" or "envelope" or "helper");
}

static Dictionary<string, WeightedTerms> DocumentFields(IpaTrialResult result, string store)
{
    var fields = new Dictionary<string, WeightedTerms>(StringComparer.OrdinalIgnoreCase)
    {
        ["identity"] = new(4.0f, Tokenize(string.Join(' ', [
            result.TrialId,
            result.BatchId,
            result.TargetSetId,
            result.ReferenceId,
            result.CandidateId,
            result.HypothesizerId,
            result.Verdict
        ]))),
        ["phonetics"] = new(4.0f, Tokenize(string.Join(' ', result.Phonemes))),
        ["hypothesis"] = new(2.5f, Tokenize(result.Hypothesis)),
        ["evaluation"] = new(2.5f, Tokenize(result.EvaluationSummary)),
        ["known_lies"] = new(2.0f, Tokenize(string.Join(' ', result.KnownLies))),
        ["metrics"] = new(2.0f, Tokenize(MetricText(result))),
        ["timeline_facts"] = new(2.5f, Tokenize(TimelineFactText(result))),
        ["artifacts"] = new(1.75f, Tokenize(ArtifactText(result, store))),
        ["timing"] = new(1.25f, Tokenize(TimingText(result)))
    };
    return fields;
}

static string MetricText(IpaTrialResult result)
{
    var builder = new StringBuilder();
    foreach (var metric in result.Metrics)
    {
        builder.Append(metric.Name).Append(' ');
        builder.Append(metric.Value.ToString("0.######", CultureInfo.InvariantCulture)).Append(' ');
        if (metric.Name.Contains("cosine", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(metric.Value < 0.2f ? "weak low failed " : "promising high ");
        }
        if (metric.Name.Contains("articulation", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(metric.Value < 0.3f ? "weak articulation failed " : "promising articulation ");
        }
        if (metric.Name.Contains("gesture", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(metric.Value < 0.6f ? "weak gesture " : "promising gesture ");
        }
    }

    return builder.ToString();
}

static string TimelineFactText(IpaTrialResult result)
{
    var builder = new StringBuilder();
    foreach (var fact in result.TimelineFacts ?? [])
    {
        builder.Append(fact.Name).Append(' ');
        builder.Append(fact.Primitive).Append(' ');
        builder.Append(fact.Signal).Append(' ');
        builder.Append(fact.Value.ToString("0.######", CultureInfo.InvariantCulture)).Append(' ');
        builder.Append(fact.Unit).Append(' ');
        builder.Append("blocks ").Append(fact.BlockStart).Append(' ').Append(fact.BlockEnd).Append(' ');
        builder.Append(fact.Summary).Append(' ');
        if (fact.Name.Contains("release", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("closure release burst plosive stop ");
        }
        if (fact.Name.Contains("branch", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("nasal velum admittance branch ");
        }
        if (fact.Name.Contains("radiation", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("radiation lip output flow ");
        }
        if (fact.Name.Contains("passivity", StringComparison.OrdinalIgnoreCase) ||
            fact.Name.Contains("energy", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("passivity energy check ");
        }
    }

    return builder.ToString();
}

static string TimingText(IpaTrialResult result) =>
    string.Join(' ', result.TimingReceipts.Select(receipt =>
        $"{receipt.StageId} {receipt.Notes} latency {receipt.LatencyMilliseconds:0.###} budget {receipt.BudgetMilliseconds:0.###} confidence {receipt.Confidence:0.###}"));

static string ArtifactText(IpaTrialResult result, string store)
{
    var builder = new StringBuilder();
    builder.Append(result.CandidatePatchUri).Append(' ');
    builder.Append(result.ReferenceArtifactUri).Append(' ');
    builder.Append(result.CandidateArtifactUri).Append(' ');
    builder.Append(result.PrimitiveTimelineUri).Append(' ');
    foreach (var artifact in result.Artifacts)
    {
        builder.Append(artifact.Kind).Append(' ').Append(artifact.Uri).Append(' ').Append(artifact.ContentHash).Append(' ');
        builder.Append(ReadArtifactSnippet(artifact.Uri, store)).Append(' ');
    }

    builder.Append(ReadArtifactSnippet(result.CandidatePatchUri, store)).Append(' ');
    builder.Append(ReadArtifactSnippet(result.PrimitiveTimelineUri, store)).Append(' ');
    return builder.ToString();
}

static string ReadArtifactSnippet(string uri, string store)
{
    var path = ResolveArtifactPath(uri, store);
    if (path is null || !File.Exists(path))
    {
        return "";
    }

    var extension = Path.GetExtension(path);
    if (!new[] { ".aqua", ".md", ".txt", ".csv", ".jsonl", ".yaml", ".yml", ".dsp" }
        .Contains(extension, StringComparer.OrdinalIgnoreCase))
    {
        return "";
    }

    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    var buffer = new char[32768];
    var read = reader.Read(buffer, 0, buffer.Length);
    return new string(buffer, 0, read);
}

static string? ResolveArtifactPath(string uri, string store)
{
    if (string.IsNullOrWhiteSpace(uri))
    {
        return null;
    }

    if (File.Exists(uri))
    {
        return uri;
    }

    if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile)
    {
        return parsed.LocalPath;
    }

    if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
        Uri.TryCreate(uri, UriKind.Absolute, out var fileUri))
    {
        return fileUri.LocalPath;
    }

    var storeDirectory = Path.GetDirectoryName(Path.GetFullPath(store));
    if (!string.IsNullOrWhiteSpace(storeDirectory))
    {
        var relative = Path.GetFullPath(Path.Combine(storeDirectory, uri));
        if (File.Exists(relative))
        {
            return relative;
        }
    }

    return null;
}

static IEnumerable<EvidenceChunk> BuildEvidenceChunks(IReadOnlyList<IpaTrialResult> results, string store)
{
    var songEvidenceByChallenge = IpaTrialResultCultCacheStore.ReadSongChallengeEvidenceAsync(store)
        .GetAwaiter()
        .GetResult()
        .GroupBy(document => document.ChallengeId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
    var distillations = IpaTrialResultCultCacheStore.ReadSongTrialDistillationsAsync(store)
        .GetAwaiter()
        .GetResult();

    foreach (var document in distillations)
    {
        var linkedTrialIds = document.KeptTrialIds.Length == 0
            ? [document.DistillationId]
            : document.KeptTrialIds;
        foreach (var linkedTrialId in linkedTrialIds)
        {
            yield return new EvidenceChunk(
                $"song-trial-distillation:{StoreKey(store)}:{document.DistillationId}:{linkedTrialId}",
                linkedTrialId,
                "distillation",
                "song-trial-distillation",
                "distillation",
                $"""
                song trial distillation
                id {document.DistillationId}
                linked_trial {linkedTrialId}
                source_store {document.SourceStore}
                input_trials {document.InputTrialCount}
                kept_trials {document.KeptTrialCount}
                summary {document.Summary}
                reusable_scene_roles
                {string.Join(Environment.NewLine, document.ReusableSceneRoles)}
                transfer_rules
                {string.Join(Environment.NewLine, document.TransferRules)}
                failure_patterns
                {string.Join(Environment.NewLine, document.FailurePatterns)}
                aggregate_metrics
                {string.Join(Environment.NewLine, document.AggregateMetrics.Select(metric => $"{metric.Name} {metric.Value.ToString("0.######", CultureInfo.InvariantCulture)} weight {metric.Weight.ToString("0.######", CultureInfo.InvariantCulture)}"))}
                """,
                store);
        }
    }

    foreach (var result in results)
    {
        var text = new StringBuilder();
        text.AppendLine($"trial {result.TrialId}");
        text.AppendLine($"candidate {result.CandidateId}");
        text.AppendLine($"target {result.TargetSetId}");
        text.AppendLine($"phonemes {string.Join(' ', result.Phonemes)}");
        text.AppendLine($"reference {result.ReferenceId}");
        text.AppendLine($"verdict {result.Verdict}");
        text.AppendLine("metrics");
        foreach (var metric in result.Metrics.OrderBy(metric => metric.Name, StringComparer.OrdinalIgnoreCase))
        {
            text.AppendLine($"{metric.Name} {metric.Value.ToString("0.######", CultureInfo.InvariantCulture)} weight {metric.Weight.ToString("0.######", CultureInfo.InvariantCulture)}");
        }

        text.AppendLine("timeline_facts");
        foreach (var fact in (result.TimelineFacts ?? []).OrderBy(fact => fact.Name, StringComparer.OrdinalIgnoreCase))
        {
            text.AppendLine($"{fact.Name} {fact.Primitive} {fact.Signal} {fact.Value.ToString("0.######", CultureInfo.InvariantCulture)} {fact.Unit} blocks {fact.BlockStart}-{fact.BlockEnd} {fact.Summary}");
        }

        text.AppendLine("hypothesis");
        text.AppendLine(result.Hypothesis);
        text.AppendLine("evaluation");
        text.AppendLine(result.EvaluationSummary);
        text.AppendLine("known_lies");
        text.AppendLine(string.Join(Environment.NewLine, result.KnownLies));
        text.AppendLine("artifacts");
        text.AppendLine(ArtifactText(result, store));
        yield return new EvidenceChunk(
            $"ipa-trial:{StoreKey(store)}:{result.TrialId}:summary",
            result.TrialId,
            result.CandidateId,
            result.TargetSetId,
            "summary",
            text.ToString(),
            store);

        if (songEvidenceByChallenge.TryGetValue(result.ReferenceId, out var evidenceDocuments))
        {
            foreach (var document in evidenceDocuments)
            {
                yield return new EvidenceChunk(
                    $"song-challenge-evidence:{StoreKey(store)}:{result.TrialId}:{document.Kind}",
                    result.TrialId,
                    result.CandidateId,
                    result.TargetSetId,
                    $"song-challenge-{document.Kind}",
                    $"""
                    song challenge evidence
                    challenge {document.ChallengeId}
                    kind {document.Kind}
                    content_type {document.ContentType}
                    content_hash {document.ContentHash}
                    source {document.SourcePath}
                    {EvidenceIndexText(document)}
                    """,
                    store);
            }
        }
    }
}

static string EvidenceIndexText(SongChallengeEvidenceDocument document)
{
    const int maxIndexedChars = 32768;
    if (document.Content.Length <= maxIndexedChars)
    {
        return document.Content;
    }

    var builder = new StringBuilder();
    builder.AppendLine($"indexed_excerpt_only true");
    builder.AppendLine($"full_content_hash {document.ContentHash}");
    builder.AppendLine($"full_content_chars {document.Content.Length.ToString(CultureInfo.InvariantCulture)}");
    if (document.Kind.Contains("spectrogram", StringComparison.OrdinalIgnoreCase))
    {
        builder.AppendLine("spectrogram evidence is stored in full in CultCache; vector index carries a bounded CSV excerpt.");
    }

    builder.Append(document.Content.AsSpan(0, maxIndexedChars));
    return builder.ToString();
}

static string[] EvidenceTags(IpaTrialResult result, SearchDocument document)
{
    var tags = new List<string>();
    var cosine = result.Metrics.FirstOrDefault(metric => metric.Name == "log_mel_cosine")?.Value;
    var articulation = result.Metrics.FirstOrDefault(metric => metric.Name == "articulation_score")?.Value;
    var gesture = result.Metrics.FirstOrDefault(metric => metric.Name == "gesture_score")?.Value;
    if (cosine is < 0.2f || articulation is < 0.25f)
    {
        tags.Add("weak");
    }
    if (cosine is > 0.5f || articulation is > 0.4f)
    {
        tags.Add("promising");
    }
    if (gesture is > 0.7f && articulation is < 0.3f)
    {
        tags.Add("gesture-audio-gap");
    }
    if (document.AllTerms.Contains("timeline", StringComparer.OrdinalIgnoreCase))
    {
        tags.Add("timeline");
    }
    if ((result.TimelineFacts ?? []).Any(fact => fact.Name.Contains("release", StringComparison.OrdinalIgnoreCase)))
    {
        tags.Add("release-facts");
    }
    if ((result.TimelineFacts ?? []).Any(fact => fact.Name.Contains("passivity", StringComparison.OrdinalIgnoreCase)))
    {
        tags.Add("passivity-facts");
    }
    if (MentionsDressing(result))
    {
        tags.Add("dressing");
    }

    return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

static string[] Tokenize(string text) =>
    text.ToLowerInvariant()
        .Split([' ', '\t', '\r', '\n', ',', '.', ':', ';', '/', '\\', '`', '"', '\'', '[', ']', '(', ')', '{', '}', '<', '>', '|', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(term => term.Length > 1 || "aeioubdklmnprstvzθŋ".Contains(term, StringComparison.Ordinal))
        .ToArray();

static string SearchReport(
    string store,
    string query,
    IReadOnlyList<IpaTrialResult> allResults,
    IReadOnlyList<SearchHit> ranked,
    SearchOptions options)
{
    var builder = new StringBuilder();
    builder.AppendLine("# IPA Trial Semantic Search");
    builder.AppendLine();
    builder.AppendLine($"store: `{store}`");
    builder.AppendLine($"query: `{query}`");
    builder.AppendLine($"retrieval: `{(ranked.Any(hit => hit.RetrievalMode == "qdrant-ollama") ? "qdrant-ollama" : "lexical-fallback")}`");
    builder.AppendLine($"collection: `{options.Collection}`");
    builder.AppendLine($"embedder: `{options.EmbedModel}`");
    builder.AppendLine($"class_focus: `{string.Join(", ", QueryClasses(ExpandTerms(Tokenize(query))).Distinct(StringComparer.OrdinalIgnoreCase))}`");
    builder.AppendLine($"matches: {ranked.Count}");
    builder.AppendLine();
    foreach (var hit in ranked)
    {
        var result = hit.Result;
        var contrast = ContrastCandidate(result, allResults);
        builder.Append("- ");
        builder.Append(result.TrialId);
        builder.Append(" / ");
        builder.Append(result.CandidateId);
        builder.Append(" / ");
        builder.Append(result.Verdict);
        builder.Append(" / score=");
        builder.Append(hit.Score.ToString("0.###", CultureInfo.InvariantCulture));
        if (hit.VectorScore is not null)
        {
            builder.Append(" / vector=");
            builder.Append(hit.VectorScore.Value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        builder.Append(" / matched=");
        builder.AppendLine(string.Join('|', hit.MatchedTerms.Take(12)));
        builder.Append("  tags: ");
        builder.AppendLine(string.Join(", ", hit.Tags));
        builder.Append("  class_match: ");
        builder.AppendLine(string.Join(", ", ResultClasses(result).Distinct(StringComparer.OrdinalIgnoreCase)));
        builder.Append("  metrics: gesture=");
        builder.Append(Metric(result, "gesture_score"));
        builder.Append(", logMelCosine=");
        builder.Append(Metric(result, "log_mel_cosine"));
        builder.Append(", articulation=");
        builder.Append(Metric(result, "articulation_score"));
        builder.Append(", rmsRatio=");
        builder.AppendLine(Metric(result, "rms_ratio"));
        builder.Append("  timeline: releasePeak=");
        builder.Append(TimelineFact(result, "contact_release_peak"));
        builder.Append(", releaseBlock=");
        builder.Append(TimelineFact(result, "contact_release_peak_block"));
        builder.Append(", branchAdmittance=");
        builder.Append(TimelineFact(result, "branch_admittance_peak"));
        builder.Append(", radiationOutput=");
        builder.Append(TimelineFact(result, "radiation_output_peak"));
        builder.Append(", passivityMax=");
        builder.AppendLine(TimelineFact(result, "path_passivity_max"));
        builder.Append("  hypothesis: ");
        builder.AppendLine(result.Hypothesis);
        builder.Append("  evaluation: ");
        builder.AppendLine(result.EvaluationSummary);
        if (contrast is not null)
        {
            builder.Append("  contrast: ");
            builder.Append(contrast.TrialId);
            builder.Append(" / ");
            builder.Append(contrast.CandidateId);
            builder.Append(" / logMelCosine=");
            builder.Append(Metric(contrast, "log_mel_cosine"));
            builder.Append(" / articulation=");
            builder.AppendLine(Metric(contrast, "articulation_score"));
        }
        if (!string.IsNullOrWhiteSpace(hit.VectorChunkId))
        {
            builder.Append("  vector_chunk: ");
            builder.AppendLine(hit.VectorChunkId);
        }
        if (!string.IsNullOrWhiteSpace(hit.VectorText))
        {
            builder.Append("  vector_excerpt: ");
            builder.AppendLine(OneLine(hit.VectorText, 280));
        }
        builder.Append("  patch: ");
        builder.AppendLine(result.CandidatePatchUri);
        builder.Append("  show: dotnet run --project tools/IpaTrialWorker/IpaTrialWorker.csproj -- show --store \"");
        builder.Append(store);
        builder.Append("\" --trial-id \"");
        builder.Append(result.TrialId);
        builder.AppendLine("\" --output <detail.md>");
    }

    return builder.ToString();
}

static string DetailReport(string store, IpaTrialResult result)
{
    var builder = new StringBuilder();
    builder.AppendLine($"# IPA Trial Detail: {result.TrialId}");
    builder.AppendLine();
    builder.AppendLine($"store: `{store}`");
    builder.AppendLine($"batch: `{result.BatchId}`");
    builder.AppendLine($"target_set: `{result.TargetSetId}`");
    builder.AppendLine($"phonemes: `{string.Join(", ", result.Phonemes)}`");
    builder.AppendLine($"reference: `{result.ReferenceId}`");
    builder.AppendLine($"candidate: `{result.CandidateId}`");
    builder.AppendLine($"verdict: `{result.Verdict}`");
    builder.AppendLine();
    builder.AppendLine("## Hypothesis");
    builder.AppendLine(result.Hypothesis);
    builder.AppendLine();
    builder.AppendLine("## Evaluation");
    builder.AppendLine(result.EvaluationSummary);
    builder.AppendLine();
    builder.AppendLine("## Metrics");
    foreach (var metric in result.Metrics.OrderBy(metric => metric.Name, StringComparer.OrdinalIgnoreCase))
    {
        builder.Append("- ");
        builder.Append(metric.Name);
        builder.Append(": ");
        builder.Append(metric.Value.ToString("0.######", CultureInfo.InvariantCulture));
        builder.Append(" weight=");
        builder.AppendLine(metric.Weight.ToString("0.######", CultureInfo.InvariantCulture));
    }

    builder.AppendLine();
    builder.AppendLine("## Primitive Timeline Facts");
    foreach (var fact in (result.TimelineFacts ?? []).OrderBy(fact => fact.Name, StringComparer.OrdinalIgnoreCase))
    {
        builder.Append("- ");
        builder.Append(fact.Name);
        builder.Append(": ");
        builder.Append(fact.Value.ToString("0.######", CultureInfo.InvariantCulture));
        builder.Append(' ');
        builder.Append(fact.Unit);
        builder.Append(" / ");
        builder.Append(fact.Primitive);
        builder.Append(' ');
        builder.Append(fact.Signal);
        builder.Append(" / blocks ");
        builder.Append(fact.BlockStart);
        builder.Append('-');
        builder.Append(fact.BlockEnd);
        if (!string.IsNullOrWhiteSpace(fact.Summary))
        {
            builder.Append(" / ");
            builder.Append(fact.Summary);
        }

        builder.AppendLine();
    }

    builder.AppendLine();
    builder.AppendLine("## Artifacts");
    foreach (var artifact in result.Artifacts)
    {
        builder.Append("- ");
        builder.Append(artifact.Kind);
        builder.Append(": ");
        builder.Append(artifact.Uri);
        if (!string.IsNullOrWhiteSpace(artifact.ContentHash))
        {
            builder.Append(" ");
            builder.Append(artifact.ContentHash);
        }

        builder.AppendLine();
    }

    builder.AppendLine();
    builder.AppendLine("## Known Lies");
    foreach (var lie in result.KnownLies)
    {
        builder.Append("- ");
        builder.AppendLine(lie);
    }

    return builder.ToString();
}

static IEnumerable<MusicProductionKnowledgeDocument> BuildMusicKnowledgeDocuments(string artifactRoot, int maxCandidates)
{
    if (!Directory.Exists(artifactRoot))
    {
        throw new DirectoryNotFoundException(artifactRoot);
    }

    var created = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    var rows = Directory.EnumerateFiles(artifactRoot, "summary.csv", SearchOption.AllDirectories)
        .Where(IsOfficialMusicArtifactPath)
        .SelectMany(ReadSongSummaryRows)
        .GroupBy(row => row.TrialId, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(row => row.AudioScore).First())
        .ToArray();
    var rendered = rows
        .Where(row => !row.Verdict.Equals("render-failed", StringComparison.OrdinalIgnoreCase) && row.AudioScore > 0)
        .OrderByDescending(row => MusicSignalScore(row))
        .ToArray();
    var admissible = rendered.Where(IsMusicCurriculumCandidate).ToArray();
    var failed = rows.Where(row => row.Verdict.Equals("render-failed", StringComparison.OrdinalIgnoreCase)).ToArray();
    var collapsed = rows.Where(row => row.ModeCollapseRisk >= .45f).ToArray();
    var slopRejected = rows.Where(row => row.TemplateLoopRisk >= .55f || row.NoisePercussionRisk >= .55f).ToArray();
    var missingProducerEvidence = rows.Where(row => row.RequiredStudioDocsPresent < .5f || row.ProducerMusicianshipScore < .45f).ToArray();

    yield return new MusicProductionKnowledgeDocument(
        "music-production-quality-standard-v1",
        "quality-standard",
        "future song curriculum admission standard",
        "master-standard",
        "The music curriculum store owns distilled production knowledge; raw trials and giant feature dumps are only evidence, not curriculum.",
        "Fresh agents should retrieve role owners, transfer rules, failure modes, and AquaSynth patterns before writing patches. New runs must add compact, evidence-backed documents, not whole spectrogram dumps or undigested trial chatter.",
        [
            "Admit a candidate as reusable knowledge only when it renders, has explicit instrument-role ownership, includes producer/listening/gap evidence, and states what production job it owns.",
            "Reject one-phrase-plus-texture arrangements as failure pressure; continuity across the clip is part of the production contract.",
            "Require an explicit composition map: meter, progression or tonal center, instrument lanes, section events, composition-scale automation, and mix movement.",
            "Prefer compact role abstractions over one-off target mimicry; a reusable owner sentence is more valuable than a small metric bump.",
            "Reject stock loop skeletons and raw-noise percussion as failure pressure unless the listening journal proves target-specific role behavior.",
            "Keep failed renders as failure-mode pressure only; do not let syntax wreckage dominate retrieval.",
            "Store full artifacts on disk and cite them; CultCache curriculum records should carry bounded summaries, metrics, and transfer rules."
        ],
        [
            "voice-like lead -> syrinx/acoustic source ports with pressure, opening, and radiation motion",
            "drums/transients -> subtractive body plus filtered noise skin plus pattern gate",
            "pads/beds -> additive/PAD layer, harmonics, and spectrum banks",
            "recording color -> texture role with band limits and gates, not full-duration raw noise",
            "composition form -> `meter`, `sequence`, `chords`, `scale`, `pattern`, and `mix` sugar lowering to visible `param`/`curve` owners"
        ],
        [
            "Single-file stores or raw spectrogram payloads are not curriculum; distill to paged CultCache records.",
            "Metric winners with unclear role ownership should be pressure, not doctrine.",
            "High cosine with high mode-collapse risk is a trap: it copies a moment, not a song.",
            "Instrument timbre without phrase, progression, lane entrances, and mix movement is sound design, not composition."
        ],
        rows.Select(row => row.TrialId).Take(24).ToArray(),
        admissible.Select(row => row.CandidateId).Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray(),
        [
            new SpeechScoreMetric("input_trial_count", rows.Length, 0),
            new SpeechScoreMetric("rendered_trial_count", rendered.Length, 0),
            new SpeechScoreMetric("admissible_curriculum_candidate_count", admissible.Length, 0),
            new SpeechScoreMetric("render_failed_count", failed.Length, 0),
            new SpeechScoreMetric("mode_collapsed_count", collapsed.Length, 0),
            new SpeechScoreMetric("slop_template_rejected_count", slopRejected.Length, 0),
            new SpeechScoreMetric("missing_producer_evidence_count", missingProducerEvidence.Length, 0)
        ],
        [artifactRoot],
        created);

    foreach (var role in MusicRoleDocuments(admissible, created))
    {
        yield return role;
    }

    foreach (var document in MusicFailureDocuments(failed, rows, created, artifactRoot))
    {
        yield return document;
    }

    foreach (var row in admissible.Take(maxCandidates))
    {
        var patchPath = CandidatePatchPath(row);
        var analysisPath = CandidateAnalysisPath(row);
        var patchExcerpt = File.Exists(patchPath) ? ReadBoundedText(patchPath, 2400) : "";
        var analysisExcerpt = File.Exists(analysisPath) ? ReadBoundedText(analysisPath, 1800) : "";
        var profile = AnalyzeSongInstrumentProfile(patchExcerpt);
        yield return new MusicProductionKnowledgeDocument(
            $"music-candidate-{SafeName(row.CandidateId)}-{StableUuid(row.TrialId)[..8]}",
            "candidate-pattern",
            row.CandidateId,
            row.ModeCollapseRisk >= .45f ? "failure-pressure" : row.LogMelCosine >= .12f || row.AudioScore >= .22f ? "strong-pressure" : "weak-pressure",
            $"Candidate `{row.CandidateId}` owns a reusable rendered song-pattern witness for `{row.ReferenceId}`.",
            CandidateKnowledgeSummary(row, profile, analysisExcerpt),
            CandidateTransferRules(row, profile),
            CandidateAquaSynthPatterns(patchExcerpt, profile),
            CandidateFailureModes(row),
            [row.TrialId],
            [row.CandidateId],
            RowMetrics(row),
            ExistingPaths([row.SummaryPath, patchPath, analysisPath, EvaluatorReportPath(row)]),
            created);
    }

    foreach (var ledgerDocument in MusicAbstractionLedgerDocuments(artifactRoot, created))
    {
        yield return ledgerDocument;
    }
}

static IEnumerable<MusicProductionKnowledgeDocument> MusicRoleDocuments(IReadOnlyList<SongSummaryRow> rendered, string created)
{
    yield return RoleDocument(
        "music-role-composition-form",
        "composition form, lanes, and mix motion",
        "The composition role owns meter, chord/progression movement, note sampling, instrument-lane gates, section events, automation, and mix motion so a target behaves like arranged music rather than a timbre demo.",
        [
            "Start every song candidate with `meter` and at least one progression or tonal-center declaration.",
            "Use `sequence`/`pattern` for lane gates and `scale`/`chords` for pitch material before hand-writing isolated frequencies.",
            "Add section events after the opening and use `mix` or ordinary curves for lane entrances, drops, swells, and exits."
        ],
        ["meter", "sequence", "pattern", "scale", "chords", "progression", "mix", "curve"],
        rendered.Where(row => row.ModeCollapseRisk < .45f).ToArray(),
        created);

    yield return RoleDocument(
        "music-role-syrinx-voice",
        "syrinx/acoustic voice lead",
        "The syrinx/acoustic voice role owns singing, creature, alien, and vowel-like lead identity through pressure/opening motion, acoustic source ports, formant/radiation filtering, and register-bounded pitch control.",
        [
            "Start voice-like leads with syrinx/acoustic topology before ordinary oscillator stacks.",
            "Move pressure and opening over time; a static syrinx is just a badge.",
            "Use formant or radiation filtering to put vocal peaks in the target register."
        ],
        ["source_port", "radiation_port", "acoustic_network", "syrinx", "formant_mix", "vowels=", "curve ... loop=true"],
        rendered.Where(row => row.InstrumentVoiceSyrinx > 0).ToArray(),
        created);

    yield return RoleDocument(
        "music-role-subtractive-drums",
        "subtractive drums and transient bodies",
        "The drum role owns rhythmic impact through pitched sine/triangle body envelopes, filtered noise skins, and pattern gates. It is not a naked click track.",
        [
            "Build kick/snare/hat as separate body/skin owners when possible.",
            "Use short gated high-band noise only for skins and dust, not broadband beds.",
            "Align pattern gates to target tempo and autocorrelation peaks before changing timbre."
        ],
        ["pattern", "texture role=dust", "wave=noise with hpf/lpf", "env=ad", "curve interp=hold loop=true"],
        rendered.Where(row => row.InstrumentDrumSubtractive > 0).ToArray(),
        created);

    yield return RoleDocument(
        "music-role-additive-pad",
        "additive and PAD harmonic beds",
        "The pad role owns sustained harmonic beds through authored layers, harmonic banks, PAD spectra, slow gain/filter motion, and restrained register placement.",
        [
            "Use layer/harmonics/spectrum for beds before stacking ordinary simple waves.",
            "Keep pads below the lead or widen them with slow filter motion; do not fill every band all the time.",
            "Treat PAD/additive beds as harmony owners and texture as recording color, not the same job."
        ],
        ["layer", "harmonics", "spectrum", "pad_bandwidth", "pad_profile", "lpf=@/macro/brightness"],
        rendered.Where(row => row.InstrumentPadAdditive > 0).ToArray(),
        created);

    yield return RoleDocument(
        "music-role-texture-air-dust-codec",
        "shaped texture, air, dust, and codec color",
        "Texture owns air, dust, tape, room, and codec coloration as band-limited moving material. Raw full-duration noise is a failure mode unless it is deliberately tiny and shaped.",
        [
            "Use `texture` roles with band limits and gates for recording color.",
            "Clock dust/hat transients; drift room/air slowly.",
            "Keep codec grit narrow and modulated so it supports scene identity instead of becoming hiss."
        ],
        ["texture role=air", "texture role=dust", "texture role=codec", "hpf", "lpf", "gain curves"],
        rendered.Where(row => row.CandidateId.Contains("dust", StringComparison.OrdinalIgnoreCase) || row.CandidateId.Contains("air", StringComparison.OrdinalIgnoreCase) || row.CandidateId.Contains("codec", StringComparison.OrdinalIgnoreCase)).ToArray(),
        created);
}

static MusicProductionKnowledgeDocument RoleDocument(
    string id,
    string topic,
    string summary,
    string[] rules,
    string[] patterns,
    IReadOnlyList<SongSummaryRow> evidence,
    string created) =>
    new(
        id,
        "scene-role",
        topic,
        "role-doctrine",
        $"{topic} owns one reusable production role so arrangements stay inspectable instead of collapsing into oscillator soup.",
        summary,
        rules,
        patterns,
        ["Do not admit candidates that name the role but implement it with unrelated static simple waves."],
        evidence.Select(row => row.TrialId).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
        evidence.Select(row => row.CandidateId).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
        [
            new SpeechScoreMetric("evidence_count", evidence.Count, 0),
            new SpeechScoreMetric("best_audio_score", evidence.Select(row => row.AudioScore).DefaultIfEmpty(0).Max(), 0),
            new SpeechScoreMetric("best_log_mel_cosine", evidence.Select(row => row.LogMelCosine).DefaultIfEmpty(0).Max(), 0)
        ],
        evidence.Select(row => row.SummaryPath).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
        created);

static IEnumerable<MusicProductionKnowledgeDocument> MusicFailureDocuments(
    IReadOnlyList<SongSummaryRow> failed,
    IReadOnlyList<SongSummaryRow> all,
    string created,
    string artifactRoot)
{
    var collapsed = all.Where(row => row.ModeCollapseRisk >= .45f).ToArray();
    yield return new MusicProductionKnowledgeDocument(
        "music-failure-rendered-syntax-pressure",
        "failure-mode",
        "render-failed candidates are pressure, not training exemplars",
        "failure-pressure",
        "The failure-mode record owns why failed candidates are kept out of the main curriculum while still teaching future agents what to avoid.",
        $"This run produced {failed.Count} render-failed official candidates out of {all.Count} scored rows. They should not transfer as musical examples; they transfer only as syntax/lowering quality pressure.",
        [
            "A candidate that cannot render is not a music-production exemplar.",
            "Keep invalid syntax in failure records with candidate ids and artifact paths; do not mix it with role doctrine.",
            "Future agents should run local `song-score` attempts before publishing official patches."
        ],
        ["parse before score", "publish one final .aqua per target", "use existing DSL roles instead of imaginary wave names"],
        ["illegal or unsupported DSL syntax", "render preparation failures", "publishing before local iteration"],
        failed.Select(row => row.TrialId).Take(20).ToArray(),
        failed.Select(row => row.CandidateId).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray(),
        [
            new SpeechScoreMetric("failed_count", failed.Count, 0),
            new SpeechScoreMetric("input_trial_count", all.Count, 0)
        ],
        [artifactRoot],
        created);

    if (collapsed.Length > 0)
    {
        yield return new MusicProductionKnowledgeDocument(
            "music-failure-mode-collapse-pressure",
            "failure-mode",
            "one-phrase-plus-texture candidates are not song exemplars",
            "failure-pressure",
            "The mode-collapse failure record owns evidence that a rendered candidate can satisfy static audio metrics while failing song continuity.",
            $"This run produced {collapsed.Length} official candidates with mode-collapse risk >= 0.45. These candidates transfer only as negative pressure unless their abstractions explicitly add later motifs, section changes, or eventful motion.",
            [
                "Do not promote candidates that spend most musical energy in the first second and coast on low-motion texture.",
                "Require motif mutations or distinct musical events across the target duration, with checkpoints scaled to the assigned clip length.",
                "Use motion coverage and first-second energy share beside cosine; similarity to a moment is not similarity to the song."
            ],
            ["mode_collapse_risk", "candidate_motion_coverage", "candidate_first_second_energy_share", "motif mutation", "section event"],
            [
                "front-loaded musical energy",
                "low-motion pink-noise or pad tail",
                "high cosine that copies only the opening phrase"
            ],
            collapsed.Select(row => row.TrialId).Take(20).ToArray(),
            collapsed.Select(row => row.CandidateId).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray(),
            [
                new SpeechScoreMetric("mode_collapsed_count", collapsed.Length, 0),
                new SpeechScoreMetric("input_trial_count", all.Count, 0),
                new SpeechScoreMetric("max_mode_collapse_risk", collapsed.Select(row => row.ModeCollapseRisk).DefaultIfEmpty(0).Max(), 0),
                new SpeechScoreMetric("mean_first_second_energy_share", collapsed.Select(row => row.CandidateFirstSecondEnergyShare).DefaultIfEmpty(0).Average(), 0)
            ],
            ExistingPaths(collapsed.Select(row => row.SummaryPath).Concat([artifactRoot]).Take(24)),
            created);
    }

    var slop = all.Where(row => row.TemplateLoopRisk >= .55f || row.NoisePercussionRisk >= .55f || row.Verdict.Equals("weak-slop-template", StringComparison.OrdinalIgnoreCase)).ToArray();
    if (slop.Length > 0)
    {
        yield return new MusicProductionKnowledgeDocument(
            "music-failure-template-noise-slop",
            "failure-mode",
            "stock loops and noise percussion are not production knowledge",
            "failure-pressure",
            "Template/noise failures own the lesson that naming kick, snare, hat, dust, or texture does not prove the patch learned the reference artist.",
            $"This run produced {slop.Length} candidates with high template_loop_risk, high noise_percussion_risk, or weak-slop-template verdicts. Keep them as negative pressure against the voiced-burst-plus-static-noise attractor.",
            [
                "A drum lane needs a pitched/body owner, a filtered skin owner, a gate, and target-specific timing evidence.",
                "A texture lane needs band limits and musical motion; a full-duration noise wash is not a mix.",
                "A reusable pattern must cite the producer brief or listening journal that proves why it belongs to this reference."
            ],
            ["template_loop_risk", "noise_percussion_risk", "producer_musicianship_score", "required_studio_docs_present"],
            ["four-on-floor skeleton copied across targets", "raw noise pretending to be hats or room", "syntax sugar mined from unlistened patches"],
            slop.Select(row => row.TrialId).Take(20).ToArray(),
            slop.Select(row => row.CandidateId).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray(),
            [
                new SpeechScoreMetric("slop_candidate_count", slop.Length, 0),
                new SpeechScoreMetric("worst_template_loop_risk", slop.Select(row => row.TemplateLoopRisk).DefaultIfEmpty(0).Max(), 0),
                new SpeechScoreMetric("worst_noise_percussion_risk", slop.Select(row => row.NoisePercussionRisk).DefaultIfEmpty(0).Max(), 0)
            ],
            ExistingPaths(slop.Select(row => row.SummaryPath).Concat([artifactRoot]).Take(24)),
            created);
    }
}

static IEnumerable<MusicProductionKnowledgeDocument> MusicAbstractionLedgerDocuments(string artifactRoot, string created)
{
    foreach (var ledger in Directory.EnumerateFiles(artifactRoot, "abstraction-ledger.md", SearchOption.AllDirectories).Where(IsOfficialMusicArtifactPath).Order(StringComparer.OrdinalIgnoreCase))
    {
        var text = ReadBoundedText(ledger, 7000);
        if (string.IsNullOrWhiteSpace(text))
        {
            continue;
        }

        var agent = Directory.GetParent(ledger)?.Name ?? "agent";
        yield return new MusicProductionKnowledgeDocument(
            $"music-abstraction-ledger-{SafeName(agent)}-{StableUuid(ledger)[..8]}",
            "abstraction-ledger",
            $"{agent} reusable abstraction ledger",
            "syntax-sugar-pressure",
            "Abstraction ledgers own future syntax-sugar pressure; they do not by themselves prove runtime or lowering correctness.",
            text,
            LinesContaining(text, "transfer", "rule", "reuse", "verdict").Take(12).ToArray(),
            LinesContaining(text, "sugar", "lower", "DSL", "pattern", "scale", "texture").Take(12).ToArray(),
            LinesContaining(text, "cut", "fail", "weak", "risk").Take(12).ToArray(),
            [],
            [],
            [new SpeechScoreMetric("ledger_chars", text.Length, 0)],
            [ledger],
            created);
    }
}

static IReadOnlyList<MusicKnowledgeHit> RankMusicKnowledge(IReadOnlyList<MusicProductionKnowledgeDocument> documents, string query, int limit)
{
    var terms = ExpandTerms(Tokenize(query)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    return documents
        .Select(document =>
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = document.KnowledgeId,
                ["kind"] = document.Kind,
                ["topic"] = document.Topic,
                ["tier"] = document.QualityTier,
                ["owner"] = document.Owner,
                ["summary"] = document.Summary,
                ["rules"] = string.Join('\n', document.TransferRules),
                ["patterns"] = string.Join('\n', document.AquaSynthPatterns),
                ["failures"] = string.Join('\n', document.FailureModes)
            };
            var matched = fields
                .Select(pair => (pair.Key, Terms: terms.Where(term => Tokenize(pair.Value).Contains(term, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
                .Where(pair => pair.Terms.Length > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Terms, StringComparer.OrdinalIgnoreCase);
            var score = matched.Sum(pair => pair.Key switch
            {
                "topic" => pair.Value.Length * 4f,
                "owner" => pair.Value.Length * 3f,
                "rules" => pair.Value.Length * 2.5f,
                "patterns" => pair.Value.Length * 2.25f,
                "summary" => pair.Value.Length * 2f,
                _ => pair.Value.Length
            }) + KnowledgeTierBias(document);
            return new MusicKnowledgeHit(document, score, matched);
        })
        .Where(hit => hit.Score > 0 || terms.Length == 0)
        .OrderByDescending(hit => hit.Score)
        .ThenBy(hit => hit.Document.Topic, StringComparer.OrdinalIgnoreCase)
        .Take(limit)
        .ToArray();
}

static float KnowledgeTierBias(MusicProductionKnowledgeDocument document) =>
    document.QualityTier switch
    {
        "master-standard" => 4f,
        "role-doctrine" => 3f,
        "strong-pressure" => 2f,
        "syntax-sugar-pressure" => 1.5f,
        _ => 0.5f
    };

static string MusicKnowledgeDistillationReport(string artifactRoot, string outputStore, IReadOnlyList<MusicProductionKnowledgeDocument> documents)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Music Knowledge CultCache Distillation");
    builder.AppendLine();
    builder.AppendLine($"artifact_root: `{artifactRoot}`");
    builder.AppendLine($"output_store: `{outputStore}`");
    builder.AppendLine($"knowledge_documents: `{documents.Count}`");
    builder.AppendLine();
    foreach (var group in documents.GroupBy(document => document.Kind).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
    {
        builder.AppendLine($"- {group.Key}: {group.Count()}");
    }

    builder.AppendLine();
    builder.AppendLine("## Documents");
    foreach (var document in documents.OrderBy(document => document.Kind, StringComparer.OrdinalIgnoreCase).ThenBy(document => document.Topic, StringComparer.OrdinalIgnoreCase))
    {
        builder.AppendLine($"- `{document.KnowledgeId}` / {document.Kind} / {document.QualityTier} / {document.Topic}");
    }

    return builder.ToString();
}

static string MusicKnowledgeSearchReport(string store, string query, IReadOnlyList<MusicKnowledgeHit> hits)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Music Knowledge Search");
    builder.AppendLine();
    builder.AppendLine($"store: `{store}`");
    builder.AppendLine($"query: `{query}`");
    builder.AppendLine($"matches: {hits.Count}");
    builder.AppendLine();
    foreach (var hit in hits)
    {
        var document = hit.Document;
        builder.AppendLine($"- `{document.KnowledgeId}` score={hit.Score:0.###} kind={document.Kind} tier={document.QualityTier}");
        builder.AppendLine($"  topic: {document.Topic}");
        builder.AppendLine($"  summary: {TrimForReport(document.Summary, 260)}");
        builder.AppendLine($"  matched: {string.Join(", ", hit.MatchedByField.Select(pair => $"{pair.Key}=[{string.Join('|', pair.Value)}]"))}");
    }

    return builder.ToString();
}

static string MusicKnowledgeDetailReport(string store, MusicProductionKnowledgeDocument document)
{
    var builder = new StringBuilder();
    builder.AppendLine($"# Music Knowledge: {document.Topic}");
    builder.AppendLine();
    builder.AppendLine($"store: `{store}`");
    builder.AppendLine($"id: `{document.KnowledgeId}`");
    builder.AppendLine($"kind: `{document.Kind}`");
    builder.AppendLine($"tier: `{document.QualityTier}`");
    builder.AppendLine($"created: `{document.CreatedAtUtc}`");
    builder.AppendLine();
    builder.AppendLine("## Owner");
    builder.AppendLine(document.Owner);
    builder.AppendLine();
    builder.AppendLine("## Summary");
    builder.AppendLine(document.Summary);
    AppendList(builder, "Transfer Rules", document.TransferRules);
    AppendList(builder, "AquaSynth Patterns", document.AquaSynthPatterns);
    AppendList(builder, "Failure Modes", document.FailureModes);
    AppendList(builder, "Evidence Trials", document.EvidenceTrialIds);
    AppendList(builder, "Evidence Candidates", document.EvidenceCandidateIds);
    AppendList(builder, "Source Artifacts", document.SourceArtifactUris);
    builder.AppendLine("## Metrics");
    foreach (var metric in document.Metrics)
    {
        builder.AppendLine($"- {metric.Name}: {metric.Value:0.######}");
    }

    return builder.ToString();
}

static void AppendList(StringBuilder builder, string title, IEnumerable<string> values)
{
    builder.AppendLine();
    builder.AppendLine($"## {title}");
    foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
    {
        builder.AppendLine($"- {value}");
    }
}

static IEnumerable<SongSummaryRow> ReadSongSummaryRows(string path)
{
    var lines = File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
    if (lines.Length < 2)
    {
        yield break;
    }

    var header = lines[0].Split(',');
    for (var index = 1; index < lines.Length; index++)
    {
        var parts = lines[index].Split(',');
        if (parts.Length < header.Length)
        {
            continue;
        }

        string Value(string name)
        {
            var column = Array.IndexOf(header, name);
            return column >= 0 && column < parts.Length ? parts[column] : "";
        }

        float Float(string name, float fallback = 0) => float.TryParse(Value(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        yield return new SongSummaryRow(
            Value("trial_id"),
            Value("candidate_id"),
            Value("reference_id"),
            Value("verdict"),
            Float("log_mel_cosine"),
            Float("log_mel_distance"),
            Float("audio_score"),
            Float("envelope_distance"),
            Float("rms_ratio"),
            Float("centroid_ratio"),
            Float("zero_crossing_ratio"),
            Float("articulation_score"),
            Float("musical_instrument_score"),
            Float("chip_distress_risk"),
            Float("instrument_voice_syrinx"),
            Float("instrument_drum_subtractive"),
            Float("instrument_pad_additive"),
            Float("producer_musicianship_score"),
            Float("required_studio_docs_present"),
            Float("required_studio_doc_coverage"),
            Float("template_loop_risk"),
            Float("noise_percussion_risk"),
            Float("composition_section_score"),
            Float("aqua_gap_count"),
            Float("candidate_active_coverage", 1),
            Float("active_coverage_ratio", 1),
            Float("candidate_motion_coverage", .75f),
            Float("motion_coverage_ratio", 1),
            Float("candidate_first_second_energy_share"),
            Float("first_second_energy_excess"),
            Float("candidate_tail_energy_share", .2f),
            Float("tail_energy_ratio", 1),
            Float("mode_collapse_risk"),
            path);
    }
}

static float MusicSignalScore(SongSummaryRow row) =>
    row.LogMelCosine * 2.5f
    + row.AudioScore
    + row.MusicalInstrumentScore * .35f
    + row.ProducerMusicianshipScore * .75f
    - row.ChipDistressRisk * .5f
    - row.ModeCollapseRisk * .9f
    - row.TemplateLoopRisk * .8f
    - row.NoisePercussionRisk * .8f
    - (row.RequiredStudioDocsPresent < .5f ? .35f : 0)
    - MathF.Max(0, .55f - row.CandidateMotionCoverage) * .35f
    - (row.Verdict.Equals("render-failed", StringComparison.OrdinalIgnoreCase) ? 100f : 0f);

static bool IsMusicCurriculumCandidate(SongSummaryRow row) =>
    !row.Verdict.Equals("render-failed", StringComparison.OrdinalIgnoreCase) &&
    !row.Verdict.StartsWith("weak-", StringComparison.OrdinalIgnoreCase) &&
    row.ModeCollapseRisk < .45f &&
    row.TemplateLoopRisk < .55f &&
    row.NoisePercussionRisk < .55f &&
    row.ProducerMusicianshipScore >= .45f;

static bool IsOfficialMusicArtifactPath(string path)
{
    var segments = Path.GetFullPath(path).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
    return !segments.Any(segment =>
        segment.Equals("iterations", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("smoke", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("rerun", StringComparison.OrdinalIgnoreCase));
}

static string CandidatePatchPath(SongSummaryRow row) =>
    Path.Combine(Path.GetDirectoryName(row.SummaryPath)!, "song-candidates", SafeName(row.CandidateId), "candidate.aqua");

static string CandidateAnalysisPath(SongSummaryRow row) =>
    Path.Combine(Path.GetDirectoryName(row.SummaryPath)!, "song-candidates", SafeName(row.CandidateId), "candidate-analysis.md");

static string EvaluatorReportPath(SongSummaryRow row) =>
    Path.Combine(Path.GetDirectoryName(row.SummaryPath)!, "evaluator-report.md");

static string[] ExistingPaths(IEnumerable<string> paths) =>
    paths.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

static SpeechScoreMetric[] RowMetrics(SongSummaryRow row) =>
[
    new("log_mel_cosine", row.LogMelCosine, 1),
    new("audio_score", row.AudioScore, 1),
    new("articulation_score", row.ArticulationScore, 1),
    new("musical_instrument_score", row.MusicalInstrumentScore, 1),
    new("chip_distress_risk", row.ChipDistressRisk, 1),
    new("producer_musicianship_score", row.ProducerMusicianshipScore, 1),
    new("required_studio_docs_present", row.RequiredStudioDocsPresent, 1),
    new("required_studio_doc_coverage", row.RequiredStudioDocCoverage, 1),
    new("template_loop_risk", row.TemplateLoopRisk, 1),
    new("noise_percussion_risk", row.NoisePercussionRisk, 1),
    new("composition_section_score", row.CompositionSectionScore, 1),
    new("aqua_gap_count", row.AquaGapCount, 1),
    new("candidate_active_coverage", row.CandidateActiveCoverage, 1),
    new("active_coverage_ratio", row.ActiveCoverageRatio, 1),
    new("candidate_motion_coverage", row.CandidateMotionCoverage, 1),
    new("motion_coverage_ratio", row.MotionCoverageRatio, 1),
    new("candidate_first_second_energy_share", row.CandidateFirstSecondEnergyShare, 1),
    new("first_second_energy_excess", row.FirstSecondEnergyExcess, 1),
    new("candidate_tail_energy_share", row.CandidateTailEnergyShare, 1),
    new("tail_energy_ratio", row.TailEnergyRatio, 1),
    new("mode_collapse_risk", row.ModeCollapseRisk, 1),
    new("rms_ratio", row.RmsRatio, 0),
    new("centroid_ratio", row.CentroidRatio, 0),
    new("zero_crossing_ratio", row.ZeroCrossingRatio, 0)
];

static string CandidateKnowledgeSummary(SongSummaryRow row, SongInstrumentProfile profile, string analysisExcerpt) =>
    $"Rendered candidate `{row.CandidateId}` against `{row.ReferenceId}` with cosine {row.LogMelCosine:0.######}, score {row.AudioScore:0.######}, instrument score {row.MusicalInstrumentScore:0.###}, producer musicianship {row.ProducerMusicianshipScore:0.###}, studio doc coverage {row.RequiredStudioDocCoverage:0.###}, chip risk {row.ChipDistressRisk:0.###}, template risk {row.TemplateLoopRisk:0.###}, noise-percussion risk {row.NoisePercussionRisk:0.###}, motion coverage {row.CandidateMotionCoverage:0.###}, first-second energy share {row.CandidateFirstSecondEnergyShare:0.###}, collapse risk {row.ModeCollapseRisk:0.###}. Instrument profile: {profile.Summary}. Candidate analysis excerpt: {TrimForReport(analysisExcerpt, 900)}";

static string[] CandidateTransferRules(SongSummaryRow row, SongInstrumentProfile profile)
{
    var rules = new List<string>();
    if (profile.HasSyrinxVoice) rules.Add("Transfer the voice role as pressure/opening/radiation motion, not as static oscillator timbre.");
    if (profile.HasSubtractiveDrums) rules.Add("Transfer the drum role as separate pitched body and filtered-noise skin gates.");
    if (profile.HasAdditivePad) rules.Add("Transfer the bed role as additive/PAD harmonic material with slow control motion.");
    if (profile.HasTexture) rules.Add("Transfer texture as a shaped role with band limits, gates, and motion.");
    if (row.RequiredStudioDocsPresent >= .5f) rules.Add("Transfer only the lesson tied to this candidate's producer brief, listening journal, gap ledger, and studio lesson.");
    if (row.RmsRatio < .35f) rules.Add("Candidate is quiet against the reference; normalize loudness after role selection.");
    if (row.ModeCollapseRisk >= .45f) rules.Add("Do not transfer the arrangement as-is: it concentrates too much musical action near the opening and must be rewritten with distributed sections.");
    if (row.TemplateLoopRisk >= .55f) rules.Add("Do not transfer the stock loop skeleton; rewrite the arrangement form before mining syntax sugar.");
    if (row.NoisePercussionRisk >= .55f) rules.Add("Do not transfer the noise-percussion layer unless it is rebuilt as body plus filtered skin with evidence.");
    if (rules.Count == 0) rules.Add("Use as weak pressure only; demand a clearer role owner before promoting this pattern.");
    return rules.ToArray();
}

static string[] CandidateAquaSynthPatterns(string patchExcerpt, SongInstrumentProfile profile)
{
    var patterns = LinesContaining(patchExcerpt, "syrinx", "source_port", "texture", "pattern", "scale", "layer", "harmonics", "spectrum", "curve", "env=", "lpf", "hpf")
        .Take(16)
        .ToArray();
    return patterns.Length > 0 ? patterns : [profile.Summary];
}

static string[] CandidateFailureModes(SongSummaryRow row)
{
    var modes = new List<string>();
    if (row.LogMelCosine < .1f) modes.Add("low spectral similarity; treat as role pressure, not target-copy proof");
    if (row.RmsRatio < .35f) modes.Add("under-loud candidate can hide arrangement ideas behind weak level");
    if (row.ZeroCrossingRatio > 8f) modes.Add("excess zero-crossing ratio suggests noisy or too-bright material");
    if (row.ChipDistressRisk > 0) modes.Add("chip-distress risk must be justified as deliberate style before transfer");
    if (row.RequiredStudioDocsPresent < .5f) modes.Add("missing producer apprenticeship evidence; syntax alone cannot become curriculum");
    if (row.ProducerMusicianshipScore < .45f) modes.Add("low producer musicianship score; candidate did not carry enough arrangement, listening, and gap evidence");
    if (row.TemplateLoopRisk >= .55f) modes.Add("template loop risk: stock drum/lane skeleton is masquerading as composition");
    if (row.NoisePercussionRisk >= .55f) modes.Add("noise-percussion risk: raw or under-shaped noise is carrying the drum job");
    if (row.ModeCollapseRisk >= .45f) modes.Add("mode collapse: short opening phrase followed by low-motion texture or noise");
    if (row.CandidateFirstSecondEnergyShare >= .35f) modes.Add("front-loaded energy; require later motifs or section changes before promotion");
    if (row.CandidateMotionCoverage > 0 && row.CandidateMotionCoverage < .45f) modes.Add("low musical motion coverage across the target duration");
    return modes.Count == 0 ? ["still pressure, not mastered parity"] : modes.ToArray();
}

static string ReadBoundedText(string path, int maxChars)
{
    if (!File.Exists(path))
    {
        return "";
    }

    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    var buffer = new char[maxChars];
    var read = reader.Read(buffer, 0, buffer.Length);
    return new string(buffer, 0, read);
}

static IEnumerable<string> LinesContaining(string text, params string[] needles) =>
    text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => needles.Any(needle => line.Contains(needle, StringComparison.OrdinalIgnoreCase)))
        .Select(line => TrimForReport(line, 320))
        .Distinct(StringComparer.OrdinalIgnoreCase);

static string TrimForReport(string text, int maxChars)
{
    text = text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
    return text.Length <= maxChars ? text : text[..Math.Max(0, maxChars - 3)] + "...";
}

static string StoreReport(string store, IReadOnlyList<IpaTrialResult> results)
{
    var builder = new StringBuilder();
    builder.AppendLine($"# IPA Trial Results Store");
    builder.AppendLine();
    builder.AppendLine($"store: `{store}`");
    builder.AppendLine($"records: {results.Count}");
    builder.AppendLine();
    foreach (var group in results.GroupBy(result => result.TargetSetId).OrderBy(group => group.Key, StringComparer.Ordinal))
    {
        builder.AppendLine($"## {group.Key}");
        foreach (var result in group.OrderByDescending(ScoreSort).ThenBy(result => result.TrialId, StringComparer.Ordinal))
        {
            builder.Append("- ");
            builder.Append(result.TrialId);
            builder.Append(" / ");
            builder.Append(result.CandidateId);
            builder.Append(" / ");
            builder.Append(result.Verdict);
            builder.Append(": ");
            builder.Append("gesture=");
            builder.Append(Metric(result, "gesture_score"));
            builder.Append(", logMelCosine=");
            builder.Append(Metric(result, "log_mel_cosine"));
            builder.Append(", articulation=");
            builder.Append(Metric(result, "articulation_score"));
            builder.Append(", rmsRatio=");
            builder.Append(Metric(result, "rms_ratio"));
            builder.Append(", releasePeak=");
            builder.Append(TimelineFact(result, "contact_release_peak"));
            builder.Append(", radiationOutput=");
            builder.Append(TimelineFact(result, "radiation_output_peak"));
            builder.Append(", passivityMax=");
            builder.Append(TimelineFact(result, "path_passivity_max"));
            builder.AppendLine();
            builder.Append("  patch: ");
            builder.AppendLine(result.CandidatePatchUri);
            builder.Append("  timeline: ");
            builder.AppendLine(result.PrimitiveTimelineUri);
            builder.Append("  evaluation: ");
            builder.AppendLine(result.EvaluationSummary);
        }

        builder.AppendLine();
    }

    return builder.ToString();
}

static IReadOnlyList<IpaTrialResult> DistillResults(
    IReadOnlyList<IpaTrialResult> results,
    int maxResults,
    float minCosine)
{
    var keep = new Dictionary<string, IpaTrialResult>(StringComparer.OrdinalIgnoreCase);
    foreach (var result in results
        .Where(result => result.Verdict.Equals("promising", StringComparison.OrdinalIgnoreCase) ||
                         result.Verdict.Equals("pressure", StringComparison.OrdinalIgnoreCase) ||
                         MetricValue(result, "log_mel_cosine") >= minCosine)
        .OrderByDescending(ScoreSort))
    {
        keep.TryAdd(result.TrialId, result);
    }

    foreach (var result in results
        .GroupBy(result => result.ReferenceId, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(ScoreSort).First()))
    {
        keep.TryAdd(result.TrialId, result);
    }

    foreach (var result in results
        .GroupBy(result => result.CandidateId, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(ScoreSort).First()))
    {
        keep.TryAdd(result.TrialId, result);
    }

    return keep.Values
        .OrderByDescending(ScoreSort)
        .ThenByDescending(result => MetricValue(result, "audio_score"))
        .ThenBy(result => result.TrialId, StringComparer.Ordinal)
        .Take(maxResults)
        .ToArray();
}

static string DistillationReport(
    string sourceStore,
    string outputStore,
    IReadOnlyList<IpaTrialResult> allResults,
    IReadOnlyList<IpaTrialResult> selected,
    IReadOnlyList<SongChallengeEvidenceDocument> allEvidence,
    IReadOnlyList<SongChallengeEvidenceDocument> selectedEvidence,
    SongTrialDistillationDocument distillation,
    float minCosine)
{
    var selectedIds = selected.Select(result => result.TrialId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var dropped = allResults.Where(result => !selectedIds.Contains(result.TrialId)).ToArray();
    var builder = new StringBuilder();
    builder.AppendLine("# Song Trial Distillation");
    builder.AppendLine();
    builder.AppendLine($"source_store: `{sourceStore}`");
    builder.AppendLine($"output_store: `{outputStore}`");
    builder.AppendLine($"min_cosine: `{minCosine.ToString("0.######", CultureInfo.InvariantCulture)}`");
    builder.AppendLine($"input_trials: `{allResults.Count}`");
    builder.AppendLine($"kept_trials: `{selected.Count}`");
    builder.AppendLine($"dropped_trials: `{dropped.Length}`");
    builder.AppendLine($"input_evidence_docs: `{allEvidence.Count}`");
    builder.AppendLine($"kept_evidence_docs: `{selectedEvidence.Count}`");
    builder.AppendLine($"distillation_document: `{distillation.DistillationId}`");
    builder.AppendLine();
    builder.AppendLine("## Distilled Signal");
    builder.AppendLine(distillation.Summary);
    builder.AppendLine();
    builder.AppendLine("### Reusable Scene Roles");
    foreach (var role in distillation.ReusableSceneRoles)
    {
        builder.Append("- ");
        builder.AppendLine(role);
    }

    builder.AppendLine();
    builder.AppendLine("### Transfer Rules");
    foreach (var rule in distillation.TransferRules)
    {
        builder.Append("- ");
        builder.AppendLine(rule);
    }

    builder.AppendLine();
    builder.AppendLine("### Failure Patterns");
    foreach (var pattern in distillation.FailurePatterns)
    {
        builder.Append("- ");
        builder.AppendLine(pattern);
    }

    builder.AppendLine();
    builder.AppendLine("## Kept Trials");
    foreach (var result in selected)
    {
        builder.Append("- ");
        builder.Append(result.TrialId);
        builder.Append(" / ");
        builder.Append(result.CandidateId);
        builder.Append(" / ");
        builder.Append(result.Verdict);
        builder.Append(" / cosine=");
        builder.Append(Metric(result, "log_mel_cosine"));
        builder.Append(" / score=");
        builder.Append(Metric(result, "audio_score"));
        builder.Append(" / articulation=");
        builder.Append(Metric(result, "articulation_score"));
        builder.Append(" / reference=");
        builder.AppendLine(result.ReferenceId);
    }

    builder.AppendLine();
    builder.AppendLine("## Candidate Aggregates");
    foreach (var group in selected.GroupBy(result => result.CandidateId, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(group => group.Average(result => MetricValue(result, "log_mel_cosine"))))
    {
        builder.Append("- ");
        builder.Append(group.Key);
        builder.Append(": trials=");
        builder.Append(group.Count());
        builder.Append(", mean_cosine=");
        builder.Append(group.Average(result => MetricValue(result, "log_mel_cosine")).ToString("0.######", CultureInfo.InvariantCulture));
        builder.Append(", min_cosine=");
        builder.Append(group.Min(result => MetricValue(result, "log_mel_cosine")).ToString("0.######", CultureInfo.InvariantCulture));
        builder.Append(", max_cosine=");
        builder.Append(group.Max(result => MetricValue(result, "log_mel_cosine")).ToString("0.######", CultureInfo.InvariantCulture));
        builder.AppendLine();
    }

    builder.AppendLine();
    builder.AppendLine("## Dropped Noise");
    foreach (var result in dropped.OrderBy(ScoreSort).Take(30))
    {
        builder.Append("- ");
        builder.Append(result.TrialId);
        builder.Append(" / ");
        builder.Append(result.CandidateId);
        builder.Append(" / ");
        builder.Append(result.Verdict);
        builder.Append(" / cosine=");
        builder.Append(Metric(result, "log_mel_cosine"));
        builder.AppendLine();
    }

    return builder.ToString();
}

static SongTrialDistillationDocument SongTrialDistillation(
    string sourceStore,
    IReadOnlyList<IpaTrialResult> allResults,
    IReadOnlyList<IpaTrialResult> selected,
    float minCosine)
{
    var droppedIds = allResults
        .Where(result => !selected.Any(kept => kept.TrialId.Equals(result.TrialId, StringComparison.OrdinalIgnoreCase)))
        .Select(result => result.TrialId)
        .ToArray();
    var top = selected.OrderByDescending(ScoreSort).Take(5).ToArray();
    var bestCosine = selected.Select(result => MetricValue(result, "log_mel_cosine")).DefaultIfEmpty(0).Max();
    var meanCosine = selected.Select(result => MetricValue(result, "log_mel_cosine")).DefaultIfEmpty(0).Average();
    var meanScore = selected.Select(result => MetricValue(result, "audio_score")).DefaultIfEmpty(0).Average();
    var text = string.Join(' ', selected.Select(result =>
        $"{result.CandidateId} {result.Hypothesis} {result.EvaluationSummary} {string.Join(' ', result.KnownLies)}"));
    var roles = SceneRolesFrom(text).ToArray();
    var failures = FailurePatternsFrom(allResults, selected).ToArray();
    var transferRules = TransferRulesFrom(selected, roles).ToArray();
    var summary = selected.Count == 0
        ? $"No high-signal trials met min cosine {minCosine:0.###}; keep only best-per-reference scaffolding."
        : $"Kept {selected.Count} of {allResults.Count} trials. Best cosine {bestCosine:0.######}; kept mean cosine {meanCosine:0.######}; best candidates: {string.Join(", ", top.Select(result => result.CandidateId).Distinct(StringComparer.OrdinalIgnoreCase).Take(5))}.";
    return new SongTrialDistillationDocument(
        $"song-distill-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{StableUuid(sourceStore)[..8]}",
        sourceStore,
        DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        allResults.Count,
        selected.Count,
        summary,
        roles,
        transferRules,
        failures,
        selected.Select(result => result.TrialId).ToArray(),
        droppedIds,
        [
            new SpeechScoreMetric("distilled_best_log_mel_cosine", bestCosine, 0),
            new SpeechScoreMetric("distilled_mean_log_mel_cosine", meanCosine, 0),
            new SpeechScoreMetric("distilled_mean_audio_score", meanScore, 0),
            new SpeechScoreMetric("distilled_kept_ratio", allResults.Count == 0 ? 0 : selected.Count / (float)allResults.Count, 0)
        ]);
}

static IEnumerable<string> SceneRolesFrom(string text)
{
    var lower = text.ToLowerInvariant();
    if (lower.Contains("syrinx", StringComparison.Ordinal) || lower.Contains("labium", StringComparison.Ordinal) || lower.Contains("beak", StringComparison.Ordinal))
    {
        yield return "syrinx voice: owns singing/creature/alien lead material through acoustic source ports, pressure/opening motion, and radiation filtering.";
    }
    if (lower.Contains("additive", StringComparison.Ordinal) || lower.Contains("spectrum", StringComparison.Ordinal) || lower.Contains("pad", StringComparison.Ordinal) || lower.Contains("harmonics", StringComparison.Ordinal))
    {
        yield return "additive/PAD texture: owns sustained harmonic beds through `layer`, `harmonics`, and `spectrum` banks with slow filter or gain motion.";
    }
    if (lower.Contains("subtractive", StringComparison.Ordinal) || lower.Contains("kick", StringComparison.Ordinal) || lower.Contains("snare", StringComparison.Ordinal))
    {
        yield return "subtractive drums: own rhythmic bodies through pitched sine/triangle envelopes plus filtered noise skins, not generic blips.";
    }
    if (lower.Contains("formant", StringComparison.Ordinal) || lower.Contains("vowel", StringComparison.Ordinal))
    {
        yield return "bright formant/vowel lead: owns pitched synthetic body and vocal-ish spectral peaks.";
    }
    if (lower.Contains("dust", StringComparison.Ordinal) || lower.Contains("hat", StringComparison.Ordinal))
    {
        yield return "clocked dust/hat texture: owns short gated high-band transients through `texture role=dust`, not static white hiss.";
    }
    if (lower.Contains("codec", StringComparison.Ordinal) || lower.Contains("bit", StringComparison.Ordinal))
    {
        yield return "codec/bit bed: owns narrow mid-high grit through shaped `texture role=codec` with slow motion.";
    }
    if (lower.Contains("bass", StringComparison.Ordinal) || lower.Contains("sub", StringComparison.Ordinal))
    {
        yield return "rubber/sub bass: owns low rhythmic support and should stay register-bounded.";
    }
    if (lower.Contains("air", StringComparison.Ordinal) || lower.Contains("room", StringComparison.Ordinal) || lower.Contains("bed", StringComparison.Ordinal))
    {
        yield return "air/room bed: owns low-level recording color through shaped moving texture, not broad full-duration noise.";
    }
}

static IEnumerable<string> TransferRulesFrom(IReadOnlyList<IpaTrialResult> selected, IReadOnlyList<string> roles)
{
    if (roles.Count == 0)
    {
        yield return "Prefer target analysis artifacts over memorized song names; choose roles from tempo, register, band deltas, and envelope autocorr.";
    }
    else
    {
        yield return "Start zero-shot production by mapping target analysis to reusable scene roles, then instantiate one owner per role.";
    }

    if (selected.Any(result => MetricValue(result, "rms_ratio") is > 1.35f or < .7f))
    {
        yield return "Normalize loudness after role selection; high cosine can still hide bad RMS balance.";
    }

    if (selected.Any(result => MetricValue(result, "mode_collapse_risk") >= .45f))
    {
        yield return "Reject one-phrase-plus-texture arrangements as reusable song knowledge; distribute motifs, rhythmic events, and harmonic motion across the full clip.";
    }

    yield return "Use `texture` for background/recording noise and reserve raw `wave=noise` for short gated transients with narrow filters.";
    yield return "When the scene wants a singing or creature voice, start with a syrinx/acoustic voice role and modulate pressure/opening/radiation before falling back to ordinary formant oscillators.";
    yield return "When the scene wants a bed or pad, start with additive/PAD banks (`layer`, `harmonics`, `spectrum`) instead of stacked simple waves.";
    yield return "When the scene wants percussion, use subtractive drum ownership: pitched body, filtered noise skin, envelope, and pattern gate.";
    yield return "Treat repeated pressure verdicts as reusable direction, not acceptance; keep the patch family but mutate timing/register/noise shaping.";
}

static IEnumerable<string> FailurePatternsFrom(IReadOnlyList<IpaTrialResult> allResults, IReadOnlyList<IpaTrialResult> selected)
{
    var weakCount = allResults.Count(result => result.Verdict.Equals("weak", StringComparison.OrdinalIgnoreCase));
    if (weakCount > 0)
    {
        yield return $"{weakCount} weak trials are dropped from retrieval pressure unless they were best for their reference.";
    }

    if (allResults.Any(result => MetricValue(result, "log_mel_cosine") < 0))
    {
        yield return "Negative cosine candidates are curriculum noise unless preserved as contrast; they should not steer zero-shot production.";
    }

    if (selected.Any(result => result.KnownLies.Any(lie => lie.Contains("noise", StringComparison.OrdinalIgnoreCase))))
    {
        yield return "Scene/noise helper voices are allowed, but only shaped role owners should transfer.";
    }

    if (allResults.Any(result => MetricValue(result, "chip_distress_risk") >= .6f))
    {
        yield return "Chip-distress-risk candidates may be useful contrast, but they should not train the music generator unless the target explicitly calls for chip/SFX vocabulary.";
    }

    if (selected.Any(result => MetricValue(result, "musical_instrument_score") < .34f))
    {
        yield return "Low instrument-role coverage is a scoring smell: require at least one owned role such as syrinx voice, subtractive drums, additive/PAD bed, or shaped texture.";
    }

    var collapsed = allResults.Count(result => MetricValue(result, "mode_collapse_risk") >= .45f);
    if (collapsed > 0)
    {
        yield return $"{collapsed} trials show mode-collapse risk: a short opening phrase followed by low-motion texture/noise. Keep them as negative pressure unless later sections carry motifs or eventful motion.";
    }
}

static float ScoreSort(IpaTrialResult result)
{
    var cosine = MetricValue(result, "log_mel_cosine");
    var instrument = MetricValue(result, "musical_instrument_score");
    var chipRisk = MetricValue(result, "chip_distress_risk");
    var collapseRisk = MetricValue(result, "mode_collapse_risk");
    var motionCoverage = MetricValue(result, "candidate_motion_coverage");
    return cosine + instrument * .10f + motionCoverage * .08f - chipRisk * .15f - collapseRisk * .25f;
}

static string Metric(IpaTrialResult result, string name) =>
    (result.Metrics.FirstOrDefault(metric => metric.Name == name)?.Value ?? 0)
    .ToString("0.######", CultureInfo.InvariantCulture);

static string TimelineFact(IpaTrialResult result, string name) =>
    (result.TimelineFacts?
        .Where(fact => fact.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(fact => fact.Value)
        .DefaultIfEmpty(0)
        .Max() ?? 0)
    .ToString("0.######", CultureInfo.InvariantCulture);

static SpeechRenderArtifact Artifact(string kind, string path) =>
    new(kind, path, File.Exists(path) ? Sha256(path) : "");

static IEnumerable<SpeechRenderArtifact> SongChallengeArtifacts(SongChallenge challenge)
{
    if (challenge.Artifacts is null)
    {
        yield break;
    }

    yield return Artifact("target-analysis-report", challenge.Artifacts.AnalysisReportMarkdown);
    yield return Artifact("target-logmel-spectrogram", challenge.Artifacts.LogMelSpectrogramCsv);
    yield return Artifact("target-logmel-band-stats", challenge.Artifacts.LogMelBandStatsCsv);
    yield return Artifact("target-rms-envelope", challenge.Artifacts.RmsEnvelopeCsv);
    yield return Artifact("target-rms-envelope-autocorr", challenge.Artifacts.RmsEnvelopeAutocorrCsv);
    if (!string.IsNullOrWhiteSpace(challenge.Artifacts.WhitenedSpectralAutocorrCsv))
    {
        yield return Artifact("target-whitened-spectral-autocorr", challenge.Artifacts.WhitenedSpectralAutocorrCsv);
    }
}

static SpeechRenderArtifact SongChallengeEvidenceArtifact(SongChallengeEvidenceDocument document) =>
    new(
        $"cultmesh-song-challenge-{document.Kind}",
        $"cultmesh://aquasynth/song-challenge-evidence/{Uri.EscapeDataString(document.EvidenceId)}",
        document.ContentHash);

static SpeechRenderArtifact ArtifactFromAnalysis(SongRenderAnalysisArtifacts artifact) =>
    Artifact($"candidate-{artifact.Kind}", artifact.Path);

static IReadOnlyList<SongChallengeEvidenceDocument> SongChallengeEvidenceDocuments(SongChallenge challenge)
{
    var documents = new List<SongChallengeEvidenceDocument>();
    if (challenge.Artifacts is null)
    {
        return documents;
    }

    AddSongChallengeEvidence(documents, challenge, "analysis-report", "text/markdown", challenge.Artifacts.AnalysisReportMarkdown);
    AddSongChallengeEvidence(documents, challenge, "logmel-spectrogram", "text/csv", challenge.Artifacts.LogMelSpectrogramCsv);
    AddSongChallengeEvidence(documents, challenge, "logmel-band-stats", "text/csv", challenge.Artifacts.LogMelBandStatsCsv);
    AddSongChallengeEvidence(documents, challenge, "rms-envelope", "text/csv", challenge.Artifacts.RmsEnvelopeCsv);
    AddSongChallengeEvidence(documents, challenge, "rms-envelope-autocorr", "text/csv", challenge.Artifacts.RmsEnvelopeAutocorrCsv);
    AddSongChallengeEvidence(documents, challenge, "whitened-spectral-autocorr", "text/csv", challenge.Artifacts.WhitenedSpectralAutocorrCsv);
    return documents;
}

static void AddSongChallengeEvidence(
    List<SongChallengeEvidenceDocument> documents,
    SongChallenge challenge,
    string kind,
    string contentType,
    string path)
{
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
        return;
    }

    documents.Add(new SongChallengeEvidenceDocument(
        $"{challenge.ChallengeId}:{kind}",
        challenge.ChallengeId,
        kind,
        contentType,
        File.ReadAllText(path, Encoding.UTF8),
        Sha256(path),
        challenge.SourcePath,
        DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
}

static async Task<IReadOnlyList<SongRenderAnalysisArtifacts>> WriteSongRenderAnalysisAsync(
    IReadOnlyList<float> samples,
    int sampleRate,
    string artifactRoot,
    string prefix,
    AudioAnalyzer analyzer)
{
    Directory.CreateDirectory(artifactRoot);
    var analysis = analyzer.Analyze(samples.ToArray());
    var frameRate = sampleRate / (float)Math.Max(1, analyzer.Config.HopSize);
    var artifacts = new[]
    {
        new SongRenderAnalysisArtifacts("analysis-report", Path.Combine(artifactRoot, $"{prefix}-analysis.md")),
        new SongRenderAnalysisArtifacts("logmel-spectrogram", Path.Combine(artifactRoot, $"{prefix}-logmel-spectrogram.csv")),
        new SongRenderAnalysisArtifacts("logmel-band-stats", Path.Combine(artifactRoot, $"{prefix}-logmel-band-stats.csv")),
        new SongRenderAnalysisArtifacts("rms-envelope", Path.Combine(artifactRoot, $"{prefix}-rms-envelope.csv")),
        new SongRenderAnalysisArtifacts("rms-envelope-autocorr", Path.Combine(artifactRoot, $"{prefix}-rms-envelope-autocorr.csv")),
        new SongRenderAnalysisArtifacts("whitened-spectral-autocorr", Path.Combine(artifactRoot, $"{prefix}-whitened-spectral-autocorr.csv"))
    };

    await File.WriteAllTextAsync(artifacts[0].Path, SongRenderAnalysisReport(prefix, analysis, frameRate), Encoding.UTF8);
    await File.WriteAllTextAsync(artifacts[1].Path, SpectrogramCsv(analysis.LogMelSpectrogram), Encoding.UTF8);
    await File.WriteAllTextAsync(artifacts[2].Path, SpectrogramBandStatsCsv(analysis.LogMelSpectrogram), Encoding.UTF8);
    await File.WriteAllTextAsync(artifacts[3].Path, EnvelopeCsv(analysis.RmsEnvelope, sampleRate, analyzer.Config.HopSize), Encoding.UTF8);
    await File.WriteAllTextAsync(artifacts[4].Path, AutocorrelationCsv(analysis.RmsEnvelope, frameRate), Encoding.UTF8);
    await File.WriteAllTextAsync(artifacts[5].Path, WhitenedSpectralAutocorrelationCsv(analysis.LogMelSpectrogram, frameRate), Encoding.UTF8);
    return artifacts;
}

static AutocorrelationPoint ReadAutocorrPeak(string? path)
{
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
        return new AutocorrelationPoint(0, 0, 0);
    }

    return File.ReadLines(path, Encoding.UTF8)
        .Skip(1)
        .Select(line => line.Split(','))
        .Where(parts => parts.Length >= 3)
        .Select(parts => new AutocorrelationPoint(
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lagFrames) ? lagFrames : 0,
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lagSeconds) ? lagSeconds : 0,
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var correlation) ? correlation : 0))
        .OrderByDescending(point => point.Correlation)
        .FirstOrDefault() ?? new AutocorrelationPoint(0, 0, 0);
}

static string Sha256(string path)
{
    using var stream = File.OpenRead(path);
    var hash = SHA256.HashData(stream);
    return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
}

static string SafeName(string value) =>
    new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

static string Escape(string value) =>
    value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
        ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
        : value;

static IpaTrialResult? ContrastCandidate(IpaTrialResult result, IReadOnlyList<IpaTrialResult> allResults)
{
    var sameTarget = allResults
        .Where(item => !ReferenceEquals(item, result) && item.TargetSetId.Equals(result.TargetSetId, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(item => Math.Abs(ScoreSort(item) - ScoreSort(result)))
        .ThenByDescending(item => item.Metrics.FirstOrDefault(metric => metric.Name == "articulation_score")?.Value ?? 0)
        .FirstOrDefault();
    if (sameTarget is not null)
    {
        return sameTarget;
    }

    return allResults
        .Where(item => !ReferenceEquals(item, result) && item.Phonemes.Intersect(result.Phonemes, StringComparer.OrdinalIgnoreCase).Any())
        .OrderByDescending(item => Math.Abs(ScoreSort(item) - ScoreSort(result)))
        .FirstOrDefault();
}

static string OneLine(string text, int maxLength)
{
    var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
    return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
}

static string StoreKey(string store)
{
    var fullPath = Path.GetFullPath(store).ToLowerInvariant();
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath));
    return Convert.ToHexString(hash)[..16].ToLowerInvariant();
}

static string StableUuid(string id)
{
    var digest = SHA256.HashData(Encoding.UTF8.GetBytes(id));
    digest[6] = (byte)((digest[6] & 0x0f) | 0x50);
    digest[8] = (byte)((digest[8] & 0x3f) | 0x80);
    var hex = Convert.ToHexString(digest[..16]).ToLowerInvariant();
    return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
}

static DecodedAudio DecodeMono(string path)
{
    using var reader = new AudioFileReader(path);
    var interleaved = new float[4096 * reader.WaveFormat.Channels];
    var mono = new List<float>();
    int read;
    while ((read = reader.Read(interleaved, 0, interleaved.Length)) > 0)
    {
        for (var index = 0; index < read; index += reader.WaveFormat.Channels)
        {
            var sum = 0f;
            for (var channel = 0; channel < reader.WaveFormat.Channels && index + channel < read; channel++)
            {
                sum += interleaved[index + channel];
            }

            mono.Add(sum / reader.WaveFormat.Channels);
        }
    }

    return new DecodedAudio(reader.WaveFormat.SampleRate, mono.ToArray());
}

static float[] Resample(IReadOnlyList<float> samples, int sourceRate, int targetRate)
{
    if (sourceRate == targetRate)
    {
        return samples.ToArray();
    }

    var result = new float[Math.Max(1, (int)Math.Round(samples.Count * (double)targetRate / sourceRate))];
    for (var index = 0; index < result.Length; index++)
    {
        var position = index * (sourceRate / (double)targetRate);
        var left = Math.Clamp((int)Math.Floor(position), 0, samples.Count - 1);
        var right = Math.Clamp(left + 1, 0, samples.Count - 1);
        var t = (float)(position - left);
        result[index] = samples[left] * (1 - t) + samples[right] * t;
    }

    return result;
}

static float[] MatchLength(IReadOnlyList<float> samples, int length)
{
    var result = new float[length];
    for (var index = 0; index < result.Length && index < samples.Count; index++)
    {
        result[index] = samples[index];
    }

    return result;
}

static void NormalizePeak(IList<float> samples, float peak)
{
    var current = samples.Select(MathF.Abs).DefaultIfEmpty(0).Max();
    if (current <= 0)
    {
        return;
    }

    var gain = peak / current;
    for (var index = 0; index < samples.Count; index++)
    {
        samples[index] *= gain;
    }
}

static float[] ReadMonoPcm16Wav(string path)
{
    using var stream = File.OpenRead(path);
    using var reader = new BinaryReader(stream, Encoding.ASCII);
    if (new string(reader.ReadChars(4)) != "RIFF")
    {
        throw new InvalidDataException($"`{path}` is not a RIFF WAV file.");
    }

    reader.ReadInt32();
    if (new string(reader.ReadChars(4)) != "WAVE")
    {
        throw new InvalidDataException($"`{path}` is not a WAVE file.");
    }

    short channels = 1;
    short bitsPerSample = 16;
    byte[]? data = null;
    while (stream.Position < stream.Length)
    {
        var chunkId = new string(reader.ReadChars(4));
        var chunkSize = reader.ReadInt32();
        if (chunkId == "fmt ")
        {
            var format = reader.ReadInt16();
            channels = reader.ReadInt16();
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt16();
            bitsPerSample = reader.ReadInt16();
            if (chunkSize > 16)
            {
                reader.ReadBytes(chunkSize - 16);
            }

            if (format != 1 || bitsPerSample != 16)
            {
                throw new InvalidDataException($"`{path}` must be PCM16.");
            }
        }
        else if (chunkId == "data")
        {
            data = reader.ReadBytes(chunkSize);
        }
        else
        {
            reader.ReadBytes(chunkSize);
        }
    }

    if (data is null)
    {
        throw new InvalidDataException($"`{path}` has no data chunk.");
    }

    var samples = new List<float>(data.Length / Math.Max(1, channels * sizeof(short)));
    for (var offset = 0; offset + sizeof(short) * channels <= data.Length; offset += sizeof(short) * channels)
    {
        var sum = 0f;
        for (var channel = 0; channel < channels; channel++)
        {
            sum += BitConverter.ToInt16(data, offset + channel * sizeof(short)) / (float)short.MaxValue;
        }

        samples.Add(sum / channels);
    }

    return samples.ToArray();
}

static void WriteWav(string path, IReadOnlyList<float> samples, int sampleRate)
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

static float ActiveDuty(IReadOnlyList<float> samples, int sampleRate)
{
    var frameSize = Math.Max(64, sampleRate / 100);
    var peak = samples.Select(MathF.Abs).DefaultIfEmpty(0).Max();
    var threshold = Math.Max(.01f, peak * .04f);
    var active = 0;
    var frames = 0;
    for (var start = 0; start < samples.Count; start += frameSize)
    {
        var end = Math.Min(samples.Count, start + frameSize);
        var rms = MathF.Sqrt(samples.Skip(start).Take(end - start).Select(sample => sample * sample).DefaultIfEmpty(0).Average());
        if (rms >= threshold)
        {
            active++;
        }

        frames++;
    }

    return active / (float)Math.Max(1, frames);
}

static float SpectralFlux(IReadOnlyList<float> samples, int sampleRate)
{
    var frameSize = Math.Max(64, sampleRate / 100);
    var previous = 0f;
    var flux = 0f;
    var frames = 0;
    for (var start = 0; start < samples.Count; start += frameSize)
    {
        var end = Math.Min(samples.Count, start + frameSize);
        var rms = MathF.Sqrt(samples.Skip(start).Take(end - start).Select(sample => sample * sample).DefaultIfEmpty(0).Average());
        if (frames > 0)
        {
            flux += MathF.Abs(rms - previous);
        }

        previous = rms;
        frames++;
    }

    var peak = samples.Select(MathF.Abs).DefaultIfEmpty(0).Max();
    return frames <= 1 || peak <= 0 ? 0 : Math.Clamp(flux / (frames * peak), 0, 1);
}

static TempoEstimate EstimateTempo(IReadOnlyList<float> samples, int sampleRate)
{
    var hopSize = Math.Max(64, sampleRate / 100);
    var frameRate = sampleRate / (float)hopSize;
    var envelope = RmsEnvelope(samples, hopSize)
        .Select(value => MathF.Log(1e-7f + value))
        .ToArray();
    if (envelope.Length < 4)
    {
        return new TempoEstimate(0, 0, 0);
    }

    var onset = new float[envelope.Length];
    for (var index = 1; index < envelope.Length; index++)
    {
        onset[index] = MathF.Max(0, envelope[index] - envelope[index - 1]);
    }

    var mean = onset.Average();
    for (var index = 0; index < onset.Length; index++)
    {
        onset[index] = MathF.Max(0, onset[index] - mean);
    }

    var minBpm = 60f;
    var maxBpm = 200f;
    var minLag = Math.Max(1, (int)MathF.Floor(frameRate * 60f / maxBpm));
    var maxLag = Math.Min(onset.Length - 1, (int)MathF.Ceiling(frameRate * 60f / minBpm));
    var bestLag = 0;
    var best = 0f;
    var energy = onset.Sum(value => value * value);
    if (energy <= 1e-9f)
    {
        return new TempoEstimate(0, 0, 0);
    }

    for (var lag = minLag; lag <= maxLag; lag++)
    {
        var sum = 0f;
        for (var index = 0; index + lag < onset.Length; index++)
        {
            sum += onset[index] * onset[index + lag];
        }

        var normalized = sum / energy;
        if (normalized > best)
        {
            best = normalized;
            bestLag = lag;
        }
    }

    if (bestLag <= 0)
    {
        return new TempoEstimate(0, 0, 0);
    }

    var beatSeconds = bestLag / frameRate;
    var bpm = 60f / Math.Max(1e-6f, beatSeconds);
    return new TempoEstimate(bpm, beatSeconds, Math.Clamp(best, 0, 1));
}

static SongRegister EstimateRegister(IReadOnlyList<float> samples, int sampleRate, float spectralCentroidHz)
{
    var dominantHz = DominantFrequency(samples, sampleRate);
    if (dominantHz <= 0)
    {
        var fallback = Math.Clamp(spectralCentroidHz * .25f, 55f, 880f);
        dominantHz = float.IsFinite(fallback) ? fallback : 220f;
    }

    var midi = (int)MathF.Round(69f + 12f * MathF.Log2(dominantHz / 440f));
    var rootMidi = midi;
    while (rootMidi > 72)
    {
        rootMidi -= 12;
    }

    while (rootMidi < 36)
    {
        rootMidi += 12;
    }

    var rootHz = MidiToFrequency(rootMidi);
    var lowHz = rootHz;
    while (lowHz > 220f)
    {
        lowHz *= .5f;
    }

    while (lowHz < 55f)
    {
        lowHz *= 2f;
    }

    var highHz = Math.Min(4000f, lowHz * 8f);
    var scaleName = spectralCentroidHz > 1800f ? "minor-pentatonic-plus-tritone" : "minor-pentatonic";
    var intervals = scaleName == "minor-pentatonic-plus-tritone"
        ? new[] { 0, 3, 5, 6, 7, 10, 12, 15, 17, 18, 19, 22, 24 }
        : new[] { 0, 3, 5, 7, 10, 12, 15, 17, 19, 22, 24 };
    var scaleFrequencies = intervals
        .Select(interval => MidiToFrequency(rootMidi + interval))
        .Where(value => value >= lowHz * .9f && value <= highHz * 1.1f)
        .ToArray();

    return new SongRegister(
        dominantHz,
        lowHz,
        highHz,
        NoteName(rootMidi),
        scaleName,
        scaleFrequencies);
}

static float DominantFrequency(IReadOnlyList<float> samples, int sampleRate)
{
    if (samples.Count < 128)
    {
        return 0;
    }

    var windowSize = Math.Min(samples.Count, Math.Max(2048, sampleRate / 5));
    var hopSize = Math.Max(256, windowSize / 4);
    var bestStart = 0;
    var bestEnergy = 0f;
    for (var start = 0; start + windowSize <= samples.Count; start += hopSize)
    {
        var energy = 0f;
        for (var index = start; index < start + windowSize; index++)
        {
            energy += samples[index] * samples[index];
        }

        if (energy > bestEnergy)
        {
            bestEnergy = energy;
            bestStart = start;
        }
    }

    if (bestEnergy <= 1e-9f)
    {
        return 0;
    }

    var minLag = Math.Max(1, sampleRate / 2000);
    var maxLag = Math.Min(windowSize - 1, sampleRate / 40);
    var bestLag = 0;
    var bestScore = 0f;
    for (var lag = minLag; lag <= maxLag; lag++)
    {
        var sum = 0f;
        var left = 0f;
        var right = 0f;
        for (var offset = 0; offset + lag < windowSize; offset++)
        {
            var a = samples[bestStart + offset];
            var b = samples[bestStart + offset + lag];
            sum += a * b;
            left += a * a;
            right += b * b;
        }

        var normalized = sum / MathF.Sqrt(Math.Max(1e-12f, left * right));
        if (normalized > bestScore)
        {
            bestScore = normalized;
            bestLag = lag;
        }
    }

    return bestLag <= 0 || bestScore < .08f ? 0 : sampleRate / (float)bestLag;
}

static float MidiToFrequency(int midi) =>
    440f * MathF.Pow(2f, (midi - 69) / 12f);

static string NoteName(int midi)
{
    ReadOnlySpan<string> names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    var octave = midi / 12 - 1;
    var pitchClass = ((midi % 12) + 12) % 12;
    return $"{names[pitchClass]}{octave}";
}

static float[] RmsEnvelope(IReadOnlyList<float> samples, int frameSize)
{
    var envelope = new List<float>();
    for (var start = 0; start < samples.Count; start += frameSize)
    {
        var end = Math.Min(samples.Count, start + frameSize);
        var sum = 0f;
        for (var index = start; index < end; index++)
        {
            sum += samples[index] * samples[index];
        }

        envelope.Add(MathF.Sqrt(sum / Math.Max(1, end - start)));
    }

    return envelope.ToArray();
}

static string SpectrogramCsv(Spectrogram spectrogram)
{
    var builder = new StringBuilder();
    builder.AppendLine("frame,band,value");
    for (var frame = 0; frame < spectrogram.Frames; frame++)
    {
        for (var band = 0; band < spectrogram.Bands; band++)
        {
            builder.Append(frame).Append(',');
            builder.Append(band).Append(',');
            builder.AppendLine(spectrogram.At(frame, band).ToString("0.########", CultureInfo.InvariantCulture));
        }
    }

    return builder.ToString();
}

static string SpectrogramBandStatsCsv(Spectrogram spectrogram)
{
    var builder = new StringBuilder();
    builder.AppendLine("band,mean,min,max,stddev,delta_mean,abs_delta_mean,delta2_mean,abs_delta2_mean");
    foreach (var band in SpectrogramBandStats(spectrogram))
    {
        builder.Append(band.Band).Append(',');
        builder.Append(band.Mean.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        builder.Append(band.Min.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        builder.Append(band.Max.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        builder.Append(band.StdDev.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        builder.Append(band.DeltaMean.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        builder.Append(band.AbsDeltaMean.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        builder.Append(band.Delta2Mean.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        builder.AppendLine(band.AbsDelta2Mean.ToString("0.########", CultureInfo.InvariantCulture));
    }

    return builder.ToString();
}

static IReadOnlyList<SpectrogramBandSummary> SpectrogramBandStats(Spectrogram spectrogram)
{
    var summaries = new List<SpectrogramBandSummary>();
    for (var band = 0; band < spectrogram.Bands; band++)
    {
        var values = new float[Math.Max(0, spectrogram.Frames)];
        for (var frame = 0; frame < values.Length; frame++)
        {
            values[frame] = spectrogram.At(frame, band);
        }

        var deltas = new float[Math.Max(0, values.Length - 1)];
        for (var index = 1; index < values.Length; index++)
        {
            deltas[index - 1] = values[index] - values[index - 1];
        }

        var delta2 = new float[Math.Max(0, deltas.Length - 1)];
        for (var index = 1; index < deltas.Length; index++)
        {
            delta2[index - 1] = deltas[index] - deltas[index - 1];
        }

        summaries.Add(new SpectrogramBandSummary(
            band,
            Mean(values),
            values.Length == 0 ? 0 : values.Min(),
            values.Length == 0 ? 0 : values.Max(),
            StdDev(values),
            Mean(deltas),
            MeanAbs(deltas),
            Mean(delta2),
            MeanAbs(delta2)));
    }

    return summaries;
}

static string EnvelopeCsv(IReadOnlyList<float> envelope, float sampleRate, int hopSize)
{
    var builder = new StringBuilder();
    builder.AppendLine("frame,time_seconds,rms");
    var frameRate = sampleRate / Math.Max(1, hopSize);
    for (var frame = 0; frame < envelope.Count; frame++)
    {
        builder.Append(frame).Append(',');
        builder.Append((frame / Math.Max(1e-6f, frameRate)).ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        builder.AppendLine(envelope[frame].ToString("0.########", CultureInfo.InvariantCulture));
    }

    return builder.ToString();
}

static string AutocorrelationCsv(IReadOnlyList<float> values, float frameRate)
{
    var builder = new StringBuilder();
    builder.AppendLine("lag_frames,lag_seconds,correlation");
    foreach (var point in Autocorrelation(values, frameRate, Math.Min(Math.Max(1, values.Count - 1), 512)))
    {
        builder.Append(point.LagFrames).Append(',');
        builder.Append(point.LagSeconds.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        builder.AppendLine(point.Correlation.ToString("0.########", CultureInfo.InvariantCulture));
    }

    return builder.ToString();
}

static string WhitenedSpectralAutocorrelationCsv(Spectrogram spectrogram, float frameRate)
{
    var series = WhitenedSpectralFluxSeries(spectrogram);
    var builder = new StringBuilder();
    builder.AppendLine("lag_frames,lag_seconds,correlation");
    foreach (var point in Autocorrelation(series, frameRate, Math.Min(Math.Max(1, series.Length - 1), 512)))
    {
        builder.Append(point.LagFrames).Append(',');
        builder.Append(point.LagSeconds.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        builder.AppendLine(point.Correlation.ToString("0.########", CultureInfo.InvariantCulture));
    }

    return builder.ToString();
}

static float[] WhitenedSpectralFluxSeries(Spectrogram spectrogram)
{
    if (spectrogram.Frames <= 1 || spectrogram.Bands <= 0)
    {
        return [];
    }

    var means = new float[spectrogram.Bands];
    var stddevs = new float[spectrogram.Bands];
    for (var band = 0; band < spectrogram.Bands; band++)
    {
        var values = new float[spectrogram.Frames];
        for (var frame = 0; frame < spectrogram.Frames; frame++)
        {
            values[frame] = spectrogram.At(frame, band);
        }

        means[band] = Mean(values);
        stddevs[band] = Math.Max(1e-4f, StdDev(values));
    }

    var series = new float[spectrogram.Frames - 1];
    for (var frame = 1; frame < spectrogram.Frames; frame++)
    {
        var sum = 0f;
        for (var band = 0; band < spectrogram.Bands; band++)
        {
            var current = (spectrogram.At(frame, band) - means[band]) / stddevs[band];
            var previous = (spectrogram.At(frame - 1, band) - means[band]) / stddevs[band];
            sum += MathF.Abs(current - previous);
        }

        series[frame - 1] = sum / Math.Max(1, spectrogram.Bands);
    }

    return series;
}

static IReadOnlyList<AutocorrelationPoint> Autocorrelation(IReadOnlyList<float> values, float frameRate, int maxLags)
{
    if (values.Count < 2)
    {
        return [];
    }

    var mean = Mean(values);
    var centered = values.Select(value => value - mean).ToArray();
    var energy = centered.Sum(value => value * value);
    if (energy <= 1e-12f)
    {
        return [];
    }

    var limit = Math.Min(maxLags, values.Count - 1);
    var points = new List<AutocorrelationPoint>(limit);
    for (var lag = 1; lag <= limit; lag++)
    {
        var sum = 0f;
        for (var index = 0; index + lag < centered.Length; index++)
        {
            sum += centered[index] * centered[index + lag];
        }

        points.Add(new AutocorrelationPoint(
            lag,
            lag / Math.Max(1e-6f, frameRate),
            sum / energy));
    }

    return points;
}

static float Mean(IReadOnlyList<float> values) =>
    values.Count == 0 ? 0 : values.Sum() / values.Count;

static float MeanAbs(IReadOnlyList<float> values) =>
    values.Count == 0 ? 0 : values.Sum(value => MathF.Abs(value)) / values.Count;

static float StdDev(IReadOnlyList<float> values)
{
    if (values.Count == 0)
    {
        return 0;
    }

    var mean = Mean(values);
    var variance = values.Sum(value => (value - mean) * (value - mean)) / values.Count;
    return MathF.Sqrt(Math.Max(0, variance));
}

static IReadOnlyList<SpeechScoreMetric> SongTargetMetrics(
    SongChallenge challenge,
    IReadOnlyList<SongChallengeEvidenceDocument> evidence)
{
    var metrics = new List<SpeechScoreMetric>
    {
        new("target_duration_seconds", challenge.DurationSeconds, 0),
        new("target_rms", challenge.Features.Rms, 0),
        new("target_active_duty", challenge.Features.ActiveDuty, 0),
        new("target_spectral_centroid_hz", challenge.Features.SpectralCentroidHz, 0),
        new("target_spectral_flux", challenge.Features.SpectralFlux, 0),
        new("target_tempo_bpm", challenge.Features.TempoBpm, 0),
        new("target_tempo_confidence", challenge.Features.TempoConfidence, 0),
        new("target_dominant_hz", challenge.Features.DominantHz, 0),
        new("target_register_low_hz", challenge.Features.RegisterLowHz, 0),
        new("target_register_high_hz", challenge.Features.RegisterHighHz, 0),
        new("target_evidence_document_count", evidence.Count, 0)
    };

    var autocorrPeak = ReadAutocorrPeak(challenge.Artifacts?.RmsEnvelopeAutocorrCsv);
    metrics.Add(new SpeechScoreMetric("target_envelope_autocorr_peak", autocorrPeak.Correlation, 0));
    metrics.Add(new SpeechScoreMetric("target_envelope_autocorr_peak_lag_seconds", autocorrPeak.LagSeconds, 0));
    var whitenedPeak = ReadAutocorrPeak(challenge.Artifacts?.WhitenedSpectralAutocorrCsv);
    metrics.Add(new SpeechScoreMetric("target_whitened_spectral_autocorr_peak", whitenedPeak.Correlation, 0));
    metrics.Add(new SpeechScoreMetric("target_whitened_spectral_autocorr_peak_lag_seconds", whitenedPeak.LagSeconds, 0));
    return metrics;
}

static IReadOnlyList<SpeechScoreMetric> SongComparisonMetrics(AudioComparison comparison) =>
[
    new("log_mel_cosine", comparison.LogMelCosineSimilarity, .25f),
    new("log_mel_distance", comparison.LogMelDistance, .15f),
    new("audio_score", comparison.Score, .15f),
    new("envelope_distance", comparison.EnvelopeDistance, .10f),
    new("rms_ratio", comparison.RmsRatio, .10f),
    new("centroid_ratio", comparison.CentroidRatio, .10f),
    new("zero_crossing_ratio", comparison.ZeroCrossingRatio, .05f),
    new("articulation_score", comparison.Articulation.ArticulationScore, .05f),
    new("speech_band_ratio", comparison.Articulation.SpeechBandRatio, .05f)
];

static IReadOnlyList<SpeechScoreMetric> SongContinuityMetrics(SongContinuityProfile profile) =>
[
    new("candidate_active_coverage", profile.CandidateActiveCoverage, .08f),
    new("target_active_coverage", profile.TargetActiveCoverage, 0),
    new("active_coverage_ratio", profile.ActiveCoverageRatio, .08f),
    new("candidate_motion_coverage", profile.CandidateMotionCoverage, .12f),
    new("target_motion_coverage", profile.TargetMotionCoverage, 0),
    new("motion_coverage_ratio", profile.MotionCoverageRatio, .12f),
    new("candidate_first_second_energy_share", profile.CandidateFirstSecondEnergyShare, .10f),
    new("target_first_second_energy_share", profile.TargetFirstSecondEnergyShare, 0),
    new("first_second_energy_excess", profile.FirstSecondEnergyExcess, .10f),
    new("candidate_tail_energy_share", profile.CandidateTailEnergyShare, .05f),
    new("tail_energy_ratio", profile.TailEnergyRatio, .05f),
    new("mode_collapse_risk", profile.ModeCollapseRisk, .15f)
];

static IReadOnlyList<SpeechScoreMetric> SongInstrumentMetrics(SongInstrumentProfile profile) =>
[
    new("instrument_voice_syrinx", profile.HasSyrinxVoice ? 1 : 0, .05f),
    new("instrument_drum_subtractive", profile.HasSubtractiveDrums ? 1 : 0, .05f),
    new("instrument_pad_additive", profile.HasAdditivePad ? 1 : 0, .05f),
    new("musical_instrument_score", profile.MusicalInstrumentScore, .10f),
    new("chip_distress_risk", profile.ChipDistressRisk, .10f)
];

static IReadOnlyList<SpeechScoreMetric> SongProductionMetrics(SongProductionProfile profile) =>
[
    new("producer_musicianship_score", profile.MusicianshipScore, .15f),
    new("required_studio_docs_present", profile.RequiredStudioDocsPresent ? 1 : 0, .10f),
    new("required_studio_doc_coverage", profile.RequiredStudioDocCoverage, .08f),
    new("template_loop_risk", profile.TemplateLoopRisk, .12f),
    new("noise_percussion_risk", profile.NoisePercussionRisk, .12f),
    new("composition_section_score", profile.CompositionSectionScore, .08f),
    new("aqua_gap_count", profile.AquaGapCount, .03f)
];

static SongInstrumentProfile AnalyzeSongInstrumentProfile(string script)
{
    var lower = script.ToLowerInvariant();
    var voiceCount = CountOccurrences(lower, "voice");
    var hasTexture = lower.Contains("texture ", StringComparison.Ordinal) ||
                     lower.Contains("noise_texture", StringComparison.Ordinal);
    var hasSyrinxVoice = lower.Contains("kind=syrinx", StringComparison.Ordinal) ||
                         lower.Contains("kind = syrinx", StringComparison.Ordinal) ||
                         lower.Contains("syrinx", StringComparison.Ordinal) && lower.Contains("acoustic_voice", StringComparison.Ordinal);
    var hasAdditivePad = lower.Contains("spectrum ", StringComparison.Ordinal) ||
                         lower.Contains("harmonics ", StringComparison.Ordinal) ||
                         lower.Contains("engine=pad", StringComparison.Ordinal) ||
                         lower.Contains("engine = pad", StringComparison.Ordinal) ||
                         lower.Contains("engine=add", StringComparison.Ordinal) ||
                         lower.Contains("engine = add", StringComparison.Ordinal);
    var hasSubtractiveDrums = lower.Contains("role=dust", StringComparison.Ordinal) ||
                              lower.Contains("dust_hat", StringComparison.Ordinal) ||
                              lower.Contains("kick", StringComparison.Ordinal) ||
                              lower.Contains("snare", StringComparison.Ordinal) ||
                              lower.Contains("hat", StringComparison.Ordinal) ||
                              (lower.Contains("pitch_ramp", StringComparison.Ordinal) &&
                               (lower.Contains("wave=sine", StringComparison.Ordinal) || lower.Contains("wave=triangle", StringComparison.Ordinal)) &&
                               (lower.Contains("wave=noise", StringComparison.Ordinal) || lower.Contains("noise=", StringComparison.Ordinal)) &&
                               (lower.Contains("hpf=", StringComparison.Ordinal) || lower.Contains("bpf=", StringComparison.Ordinal)));
    var hasSimpleChipTone = lower.Contains("wave=square", StringComparison.Ordinal) ||
                            lower.Contains("wave=sine", StringComparison.Ordinal) ||
                            lower.Contains("wave=saw", StringComparison.Ordinal);
    var lacksOwnedRoles = !hasSyrinxVoice && !hasAdditivePad && !hasSubtractiveDrums && !hasTexture;
    var chipRisk = lacksOwnedRoles && hasSimpleChipTone
        ? Math.Clamp(.45f + Math.Max(0, 4 - voiceCount) * .12f, 0, 1)
        : 0f;
    if (lower.Contains("laser", StringComparison.Ordinal) ||
        lower.Contains("blip", StringComparison.Ordinal) ||
        lower.Contains("sfxr", StringComparison.Ordinal))
    {
        chipRisk = Math.Max(chipRisk, .65f);
    }

    var score = 0f;
    if (hasSyrinxVoice) score += .34f;
    if (hasSubtractiveDrums) score += .33f;
    if (hasAdditivePad) score += .33f;
    if (hasTexture) score = Math.Min(1, score + .10f);

    var summary =
        $"syrinx_voice={(hasSyrinxVoice ? "yes" : "no")}; " +
        $"subtractive_drums={(hasSubtractiveDrums ? "yes" : "no")}; " +
        $"additive_pad={(hasAdditivePad ? "yes" : "no")}; " +
        $"texture={(hasTexture ? "yes" : "no")}; " +
        $"chip_distress_risk={chipRisk:0.###}";
    return new SongInstrumentProfile(hasSyrinxVoice, hasSubtractiveDrums, hasAdditivePad, hasTexture, score, chipRisk, summary);
}

static SongProductionProfile AnalyzeSongProductionProfile(string script, string scriptPath, string artifactRoot)
{
    var lower = script.ToLowerInvariant();
    var evidenceRoots = EvidenceSearchRoots(scriptPath, artifactRoot).ToArray();
    var producerBrief = FindEvidenceFile(evidenceRoots, "producer-brief.md", "hypotheses.md");
    var listeningJournal = FindEvidenceFile(evidenceRoots, "listening-journal.md");
    var aquaGapLedger = FindEvidenceFile(evidenceRoots, "aqua-gap-ledger.md");
    var studioLesson = FindEvidenceFile(evidenceRoots, "studio-lesson.md", "producer-lesson.md", "abstraction-ledger.md", "instrument-conventions.md");
    var docCoverage =
        (FileExistsWithText(producerBrief) ? .25f : 0) +
        (FileExistsWithText(listeningJournal) ? .25f : 0) +
        (FileExistsWithText(aquaGapLedger) ? .25f : 0) +
        (FileExistsWithText(studioLesson) ? .25f : 0);

    var curveCount = CountOccurrences(lower, "curve ");
    var mixCount = CountOccurrences(lower, "mix ");
    var sequenceCount = CountOccurrences(lower, "sequence ");
    var sectionWordCount =
        CountOccurrences(lower, "section") +
        CountOccurrences(lower, "verse") +
        CountOccurrences(lower, "chorus") +
        CountOccurrences(lower, "bridge") +
        CountOccurrences(lower, "break") +
        CountOccurrences(lower, "drop") +
        CountOccurrences(lower, "automation");
    var lateEventHints =
        CountOccurrences(lower, "bar=2") +
        CountOccurrences(lower, "bar=4") +
        CountOccurrences(lower, "bar=8") +
        CountOccurrences(lower, "2.") +
        CountOccurrences(lower, "4.") +
        CountOccurrences(lower, "8.");
    var sectionScore = Math.Clamp(
        sequenceCount * .08f +
        curveCount * .035f +
        mixCount * .08f +
        sectionWordCount * .08f +
        lateEventHints * .04f,
        0,
        1);

    var commonPatternCount =
        CountOccurrences(lower, "pattern=x..x") +
        CountOccurrences(lower, "pattern=x...") +
        CountOccurrences(lower, "pattern=..x.") +
        CountOccurrences(lower, "pattern=....x") +
        CountOccurrences(lower, "x..x") +
        CountOccurrences(lower, "x.x.");
    var stockDrumWords = lower.Contains("kick", StringComparison.Ordinal) &&
                         lower.Contains("snare", StringComparison.Ordinal) &&
                         (lower.Contains("hat", StringComparison.Ordinal) || lower.Contains("dust", StringComparison.Ordinal));
    var templateRisk = Math.Clamp(
        (stockDrumWords ? .30f : 0) +
        MathF.Min(.30f, commonPatternCount * .08f) +
        (sectionScore < .35f ? .25f : 0) +
        (sequenceCount <= 2 ? .15f : 0),
        0,
        1);

    var rawNoiseCount = CountOccurrences(lower, "wave=noise") + CountOccurrences(lower, "wave = noise");
    var hasNoiseBed = rawNoiseCount > 0 &&
                      (lower.Contains("sustain=30", StringComparison.Ordinal) ||
                       lower.Contains("sustain = 30", StringComparison.Ordinal) ||
                       lower.Contains("duration_seconds", StringComparison.Ordinal) ||
                       lower.Contains("air_wash", StringComparison.Ordinal));
    var hasDrumNoise = rawNoiseCount > 0 &&
                       (lower.Contains("snare", StringComparison.Ordinal) ||
                        lower.Contains("hat", StringComparison.Ordinal) ||
                        lower.Contains("dust", StringComparison.Ordinal));
    var hasPitchedBody = lower.Contains("pitch_ramp", StringComparison.Ordinal) ||
                         lower.Contains("body", StringComparison.Ordinal) &&
                         (lower.Contains("wave=sine", StringComparison.Ordinal) ||
                          lower.Contains("wave=triangle", StringComparison.Ordinal) ||
                          lower.Contains("wave = sine", StringComparison.Ordinal) ||
                          lower.Contains("wave = triangle", StringComparison.Ordinal));
    var hasBandLimits = lower.Contains("hpf", StringComparison.Ordinal) ||
                        lower.Contains("lpf", StringComparison.Ordinal) ||
                        lower.Contains("bpf", StringComparison.Ordinal);
    var noiseRisk = Math.Clamp(
        rawNoiseCount * .12f +
        (hasNoiseBed ? .35f : 0) +
        (hasDrumNoise && !hasPitchedBody ? .30f : 0) -
        (hasBandLimits ? .12f : 0),
        0,
        1);

    var aquaGapCount = CountEvidenceItems(aquaGapLedger);
    var musicianship = Math.Clamp(
        docCoverage * .35f +
        sectionScore * .25f +
        (1 - templateRisk) * .20f +
        (1 - noiseRisk) * .20f,
        0,
        1);
    var summary =
        $"studio_docs={(docCoverage >= .99f ? "complete" : docCoverage > 0 ? "partial" : "missing")}; " +
        $"doc_coverage={docCoverage:0.###}; section_score={sectionScore:0.###}; " +
        $"template_loop_risk={templateRisk:0.###}; noise_percussion_risk={noiseRisk:0.###}; " +
        $"aqua_gap_count={aquaGapCount}; producer_musicianship_score={musicianship:0.###}";
    return new SongProductionProfile(
        FileExistsWithText(producerBrief),
        FileExistsWithText(listeningJournal),
        FileExistsWithText(aquaGapLedger),
        FileExistsWithText(studioLesson),
        docCoverage,
        sectionScore,
        templateRisk,
        noiseRisk,
        aquaGapCount,
        musicianship,
        summary);
}

static IEnumerable<string> EvidenceSearchRoots(string scriptPath, string artifactRoot)
{
    foreach (var root in new[] { Path.GetDirectoryName(Path.GetFullPath(scriptPath)), Path.GetFullPath(artifactRoot) })
    {
        var current = string.IsNullOrWhiteSpace(root) ? null : new DirectoryInfo(root);
        for (var depth = 0; current is not null && depth < 7; depth++)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}

static string FindEvidenceFile(IEnumerable<string> roots, params string[] names)
{
    foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        foreach (var name in names)
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path))
            {
                return path;
            }
        }
    }

    return "";
}

static bool FileExistsWithText(string path) =>
    !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 16;

static int CountEvidenceItems(string path)
{
    if (!FileExistsWithText(path))
    {
        return 0;
    }

    return File.ReadLines(path)
        .Count(line => line.TrimStart().StartsWith("-", StringComparison.Ordinal) ||
                       line.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
                       line.Contains("gap", StringComparison.OrdinalIgnoreCase));
}

static int CountOccurrences(string haystack, string needle)
{
    var count = 0;
    var index = 0;
    while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += needle.Length;
    }

    return count;
}

static string SongVerdict(
    AudioComparison comparison,
    SongInstrumentProfile profile,
    SongProductionProfile production,
    SongContinuityProfile continuity)
{
    if (continuity.ModeCollapseRisk >= .60f)
    {
        return "weak-mode-collapse";
    }

    if (production.NoisePercussionRisk >= .65f || production.TemplateLoopRisk >= .70f)
    {
        return "weak-slop-template";
    }

    if (!production.RequiredStudioDocsPresent && production.MusicianshipScore < .45f)
    {
        return "weak-producer-missing";
    }

    if (profile.ChipDistressRisk >= .75f && comparison.LogMelCosineSimilarity < .60f)
    {
        return "weak";
    }

    if (comparison.LogMelCosineSimilarity >= .70f && comparison.Score >= .45f && production.MusicianshipScore >= .55f)
    {
        return "promising";
    }

    if ((comparison.LogMelCosineSimilarity >= .35f || comparison.Score >= .30f) && production.MusicianshipScore >= .40f)
    {
        return "pressure";
    }

    return "weak";
}

static SongContinuityProfile AnalyzeSongContinuity(AudioComparison comparison)
{
    var durationSeconds = Math.Max(1e-3f, comparison.Candidate.Features.DurationSeconds);
    var targetActive = ActiveCoverage(comparison.Reference.RmsEnvelope);
    var candidateActive = ActiveCoverage(comparison.Candidate.RmsEnvelope);
    var targetMotion = MotionCoverage(comparison.Reference.RmsEnvelope, comparison.Reference.LogMelSpectrogram);
    var candidateMotion = MotionCoverage(comparison.Candidate.RmsEnvelope, comparison.Candidate.LogMelSpectrogram);
    var targetFirst = EnergyShare(comparison.Reference.RmsEnvelope, comparison.Reference.Features.DurationSeconds, 0, 1);
    var candidateFirst = EnergyShare(comparison.Candidate.RmsEnvelope, durationSeconds, 0, 1);
    var targetTail = EnergyShare(comparison.Reference.RmsEnvelope, comparison.Reference.Features.DurationSeconds, MathF.Min(1, comparison.Reference.Features.DurationSeconds * .20f), comparison.Reference.Features.DurationSeconds);
    var candidateTail = EnergyShare(comparison.Candidate.RmsEnvelope, durationSeconds, MathF.Min(1, durationSeconds * .20f), durationSeconds);
    var activeRatio = SafeMetricRatio(candidateActive, targetActive);
    var motionRatio = SafeMetricRatio(candidateMotion, targetMotion);
    var firstExcess = MathF.Max(0, candidateFirst - MathF.Max(.20f, targetFirst * 1.6f));
    var tailRatio = SafeMetricRatio(candidateTail, targetTail);
    var collapseRisk = Math.Clamp(
        firstExcess * 1.6f +
        MathF.Max(0, .72f - candidateMotion) * .85f +
        MathF.Max(0, .65f - motionRatio) * .55f +
        MathF.Max(0, .55f - candidateActive) * .40f +
        MathF.Max(0, .50f - tailRatio) * .25f,
        0,
        1);
    return new SongContinuityProfile(
        targetActive,
        candidateActive,
        activeRatio,
        targetMotion,
        candidateMotion,
        motionRatio,
        targetFirst,
        candidateFirst,
        firstExcess,
        candidateTail,
        tailRatio,
        collapseRisk);
}

static float ActiveCoverage(IReadOnlyList<float> envelope)
{
    if (envelope.Count == 0)
    {
        return 0;
    }

    var peak = envelope.Max();
    var threshold = Math.Max(peak * .08f, 1e-5f);
    return envelope.Count(value => value >= threshold) / (float)envelope.Count;
}

static float MotionCoverage(IReadOnlyList<float> envelope, Spectrogram spectrogram)
{
    var envelopeFlux = FrameFlux(envelope);
    var spectralFlux = SpectralFluxSeries(spectrogram);
    var frames = Math.Max(envelopeFlux.Length, spectralFlux.Length);
    if (frames == 0)
    {
        return 0;
    }

    var envelopeThreshold = AdaptiveFluxThreshold(envelopeFlux);
    var spectralThreshold = AdaptiveFluxThreshold(spectralFlux);
    var moving = 0;
    for (var index = 0; index < frames; index++)
    {
        var env = ResampledAt(envelopeFlux, index, frames);
        var spec = ResampledAt(spectralFlux, index, frames);
        if (env >= envelopeThreshold || spec >= spectralThreshold)
        {
            moving++;
        }
    }

    return moving / (float)frames;
}

static float[] FrameFlux(IReadOnlyList<float> values)
{
    if (values.Count < 2)
    {
        return [0];
    }

    var flux = new float[values.Count - 1];
    for (var index = 1; index < values.Count; index++)
    {
        flux[index - 1] = MathF.Abs(values[index] - values[index - 1]);
    }

    return flux;
}

static float[] SpectralFluxSeries(Spectrogram spectrogram)
{
    if (spectrogram.Frames < 2)
    {
        return [0];
    }

    var flux = new float[spectrogram.Frames - 1];
    for (var frame = 1; frame < spectrogram.Frames; frame++)
    {
        var sum = 0f;
        for (var band = 0; band < spectrogram.Bands; band++)
        {
            sum += MathF.Abs(spectrogram.At(frame, band) - spectrogram.At(frame - 1, band));
        }

        flux[frame - 1] = sum / Math.Max(1, spectrogram.Bands);
    }

    return flux;
}

static float AdaptiveFluxThreshold(IReadOnlyList<float> values)
{
    if (values.Count == 0)
    {
        return float.PositiveInfinity;
    }

    var sorted = values.Order().ToArray();
    var median = sorted[sorted.Length / 2];
    var high = sorted[Math.Clamp((int)MathF.Round((sorted.Length - 1) * .75f), 0, sorted.Length - 1)];
    return Math.Max(1e-6f, median + (high - median) * .35f);
}

static float EnergyShare(IReadOnlyList<float> envelope, float durationSeconds, float startSeconds, float endSeconds)
{
    if (envelope.Count == 0 || durationSeconds <= 0)
    {
        return 0;
    }

    var total = envelope.Sum(value => value * value);
    if (total <= float.Epsilon)
    {
        return 0;
    }

    var start = Math.Clamp((int)MathF.Floor(startSeconds / durationSeconds * envelope.Count), 0, envelope.Count - 1);
    var end = Math.Clamp((int)MathF.Ceiling(endSeconds / durationSeconds * envelope.Count), start + 1, envelope.Count);
    var segment = 0f;
    for (var index = start; index < end; index++)
    {
        segment += envelope[index] * envelope[index];
    }

    return segment / total;
}

static float SafeMetricRatio(float candidate, float reference) =>
    reference <= float.Epsilon ? (candidate <= float.Epsilon ? 1 : 10) : candidate / reference;

static float ResampledAt(IReadOnlyList<float> values, int index, int outputLength)
{
    if (values.Count == 0)
    {
        return 0;
    }

    if (values.Count == 1 || outputLength <= 1)
    {
        return values[0];
    }

    var position = index / (float)(outputLength - 1) * (values.Count - 1);
    var left = Math.Clamp((int)MathF.Floor(position), 0, values.Count - 1);
    var right = Math.Clamp(left + 1, 0, values.Count - 1);
    var t = position - left;
    return values[left] * (1 - t) + values[right] * t;
}

static float SongConfidence(IReadOnlyList<SpeechScoreMetric> metrics)
{
    var cosine = metrics.FirstOrDefault(metric => metric.Name == "log_mel_cosine")?.Value ?? 0;
    var score = metrics.FirstOrDefault(metric => metric.Name == "audio_score")?.Value ?? 0;
    return Math.Clamp((cosine + score) * .5f, 0, 1);
}

static string SongEvaluationSentence(SongChallenge challenge, AudioComparison comparison, string verdict, SongInstrumentProfile profile, SongContinuityProfile continuity) =>
    $"Song snippet `{challenge.ChallengeId}` is `{verdict}`: logMelCosine={comparison.LogMelCosineSimilarity:0.0000}, logMelDistance={comparison.LogMelDistance:0.0000}, score={comparison.Score:0.0000}, rmsRatio={comparison.RmsRatio:0.0000}, centroidRatio={comparison.CentroidRatio:0.0000}, activeCoverage={continuity.CandidateActiveCoverage:0.0000}, motionCoverage={continuity.CandidateMotionCoverage:0.0000}, firstSecondEnergyShare={continuity.CandidateFirstSecondEnergyShare:0.0000}, modeCollapseRisk={continuity.ModeCollapseRisk:0.0000}. Instrument profile: {profile.Summary}.";

static string SongAnalysisReport(SongChallenge challenge, AudioAnalysis analysis)
{
    var bands = SpectrogramBandStats(analysis.LogMelSpectrogram);
    var brightest = bands.OrderByDescending(band => band.Mean).Take(5).Select(band => band.Band).ToArray();
    var mostMoving = bands.OrderByDescending(band => band.AbsDeltaMean).Take(5).Select(band => band.Band).ToArray();
    var mostAccelerating = bands.OrderByDescending(band => band.AbsDelta2Mean).Take(5).Select(band => band.Band).ToArray();
    var autocorr = Autocorrelation(analysis.RmsEnvelope, analysis.RmsEnvelope.Length / Math.Max(1f, challenge.DurationSeconds), maxLags: 64)
        .OrderByDescending(point => point.Correlation)
        .Take(5)
        .ToArray();
    var builder = new StringBuilder();
    builder.AppendLine($"# Song Challenge Analysis: {challenge.ChallengeId}");
    builder.AppendLine();
    builder.AppendLine($"spectrogram_frames: `{analysis.LogMelSpectrogram.Frames}`");
    builder.AppendLine($"spectrogram_bands: `{analysis.LogMelSpectrogram.Bands}`");
    builder.AppendLine($"rms_envelope_frames: `{analysis.RmsEnvelope.Length}`");
    builder.AppendLine($"brightest_bands: `{string.Join(",", brightest)}`");
    builder.AppendLine($"highest_delta_bands: `{string.Join(",", mostMoving)}`");
    builder.AppendLine($"highest_delta2_bands: `{string.Join(",", mostAccelerating)}`");
    builder.AppendLine();
    builder.AppendLine("## Strong Envelope Autocorrelation Lags");
    foreach (var point in autocorr)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"- lag_frames `{point.LagFrames}`, lag_seconds `{point.LagSeconds:0.######}`, correlation `{point.Correlation:0.######}`");
    }

    builder.AppendLine();
    builder.AppendLine("## Artifact Paths");
    builder.AppendLine($"- log_mel_spectrogram_csv: `{challenge.Artifacts?.LogMelSpectrogramCsv}`");
    builder.AppendLine($"- log_mel_band_stats_csv: `{challenge.Artifacts?.LogMelBandStatsCsv}`");
    builder.AppendLine($"- rms_envelope_csv: `{challenge.Artifacts?.RmsEnvelopeCsv}`");
    builder.AppendLine($"- rms_envelope_autocorr_csv: `{challenge.Artifacts?.RmsEnvelopeAutocorrCsv}`");
    builder.AppendLine($"- whitened_spectral_autocorr_csv: `{challenge.Artifacts?.WhitenedSpectralAutocorrCsv}`");
    return builder.ToString();
}

static string SongRenderAnalysisReport(string label, AudioAnalysis analysis, float frameRate)
{
    var bands = SpectrogramBandStats(analysis.LogMelSpectrogram);
    var brightest = bands.OrderByDescending(band => band.Mean).Take(5).Select(band => band.Band).ToArray();
    var mostMoving = bands.OrderByDescending(band => band.AbsDeltaMean).Take(5).Select(band => band.Band).ToArray();
    var spectralAutocorr = Autocorrelation(WhitenedSpectralFluxSeries(analysis.LogMelSpectrogram), frameRate, maxLags: 64)
        .OrderByDescending(point => point.Correlation)
        .Take(5)
        .ToArray();
    var builder = new StringBuilder();
    builder.AppendLine($"# Song Render Analysis: {label}");
    builder.AppendLine();
    builder.AppendLine($"duration_seconds: `{analysis.Features.DurationSeconds:0.######}`");
    builder.AppendLine($"peak: `{analysis.Features.Peak:0.######}`");
    builder.AppendLine($"rms: `{analysis.Features.Rms:0.######}`");
    builder.AppendLine($"zero_crossing_rate: `{analysis.Features.ZeroCrossingRate:0.######}`");
    builder.AppendLine($"spectral_centroid_hz: `{analysis.Features.SpectralCentroidHz:0.######}`");
    builder.AppendLine($"spectral_rolloff_hz: `{analysis.Features.SpectralRolloffHz:0.######}`");
    builder.AppendLine($"brightest_bands: `{string.Join(",", brightest)}`");
    builder.AppendLine($"highest_delta_bands: `{string.Join(",", mostMoving)}`");
    builder.AppendLine();
    builder.AppendLine("## Whitened Spectral Autocorrelation Peaks");
    foreach (var point in spectralAutocorr)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"- lag_frames `{point.LagFrames}`, lag_seconds `{point.LagSeconds:0.######}`, correlation `{point.Correlation:0.######}`");
    }

    return builder.ToString();
}

static string SongChallengeReport(SongChallenge challenge) =>
    $"""
    # Song Snippet Challenge: {challenge.ChallengeId}

    source: `{challenge.SourcePath}`
    file: `{challenge.SourceFileName}`
    seed: `{challenge.Seed}`
    start_seconds: `{challenge.StartSeconds.ToString("0.######", CultureInfo.InvariantCulture)}`
    duration_seconds: `{challenge.DurationSeconds.ToString("0.######", CultureInfo.InvariantCulture)}`
    sample_rate: `{challenge.SampleRate}`
    reference_wav: `{challenge.ReferenceWavPath}`

    ## Reference Features
    peak: `{challenge.Features.Peak.ToString("0.######", CultureInfo.InvariantCulture)}`
    rms: `{challenge.Features.Rms.ToString("0.######", CultureInfo.InvariantCulture)}`
    active_duty: `{challenge.Features.ActiveDuty.ToString("0.######", CultureInfo.InvariantCulture)}`
    zero_crossing_rate: `{challenge.Features.ZeroCrossingRate.ToString("0.######", CultureInfo.InvariantCulture)}`
    spectral_centroid_hz: `{challenge.Features.SpectralCentroidHz.ToString("0.######", CultureInfo.InvariantCulture)}`
    spectral_rolloff_hz: `{challenge.Features.SpectralRolloffHz.ToString("0.######", CultureInfo.InvariantCulture)}`
    spectral_flux: `{challenge.Features.SpectralFlux.ToString("0.######", CultureInfo.InvariantCulture)}`
    tempo_bpm: `{challenge.Features.TempoBpm.ToString("0.######", CultureInfo.InvariantCulture)}`
    beat_seconds: `{challenge.Features.BeatSeconds.ToString("0.######", CultureInfo.InvariantCulture)}`
    tempo_confidence: `{challenge.Features.TempoConfidence.ToString("0.######", CultureInfo.InvariantCulture)}`
    dominant_hz: `{challenge.Features.DominantHz.ToString("0.######", CultureInfo.InvariantCulture)}`
    register_low_hz: `{challenge.Features.RegisterLowHz.ToString("0.######", CultureInfo.InvariantCulture)}`
    register_high_hz: `{challenge.Features.RegisterHighHz.ToString("0.######", CultureInfo.InvariantCulture)}`
    root_note: `{challenge.Features.RootNote}`
    suggested_scale: `{challenge.Features.SuggestedScale}`
    scale_frequencies_hz: `{challenge.Features.ScaleFrequenciesHz}`

    ## Analysis Artifacts
    analysis_report: `{challenge.Artifacts?.AnalysisReportMarkdown ?? ""}`
    log_mel_spectrogram_csv: `{challenge.Artifacts?.LogMelSpectrogramCsv ?? ""}`
    log_mel_band_stats_csv: `{challenge.Artifacts?.LogMelBandStatsCsv ?? ""}`
    rms_envelope_csv: `{challenge.Artifacts?.RmsEnvelopeCsv ?? ""}`
    rms_envelope_autocorr_csv: `{challenge.Artifacts?.RmsEnvelopeAutocorrCsv ?? ""}`
    whitened_spectral_autocorr_csv: `{challenge.Artifacts?.WhitenedSpectralAutocorrCsv ?? ""}`
    """;

static string SongComparisonReport(SongChallenge challenge, IpaTrialScriptCandidate candidate, AudioComparison comparison, string verdict, SongInstrumentProfile profile, SongContinuityProfile continuity) =>
    $"""
    candidate={candidate.CandidateId}
    challenge={challenge.ChallengeId}
    verdict={verdict}
    logMelCosine={comparison.LogMelCosineSimilarity:0.######}
    logMelDistance={comparison.LogMelDistance:0.######}
    score={comparison.Score:0.######}
    envelopeDistance={comparison.EnvelopeDistance:0.######}
    rmsRatio={comparison.RmsRatio:0.######}
    centroidRatio={comparison.CentroidRatio:0.######}
    zeroCrossingRatio={comparison.ZeroCrossingRatio:0.######}
    articulation={comparison.Articulation.ArticulationScore:0.######}
    speechBandRatio={comparison.Articulation.SpeechBandRatio:0.######}
    candidateActiveCoverage={continuity.CandidateActiveCoverage:0.######}
    activeCoverageRatio={continuity.ActiveCoverageRatio:0.######}
    candidateMotionCoverage={continuity.CandidateMotionCoverage:0.######}
    motionCoverageRatio={continuity.MotionCoverageRatio:0.######}
    candidateFirstSecondEnergyShare={continuity.CandidateFirstSecondEnergyShare:0.######}
    firstSecondEnergyExcess={continuity.FirstSecondEnergyExcess:0.######}
    candidateTailEnergyShare={continuity.CandidateTailEnergyShare:0.######}
    tailEnergyRatio={continuity.TailEnergyRatio:0.######}
    modeCollapseRisk={continuity.ModeCollapseRisk:0.######}
    instrumentVoiceSyrinx={(profile.HasSyrinxVoice ? 1 : 0)}
    instrumentDrumSubtractive={(profile.HasSubtractiveDrums ? 1 : 0)}
    instrumentPadAdditive={(profile.HasAdditivePad ? 1 : 0)}
    musicalInstrumentScore={profile.MusicalInstrumentScore:0.######}
    chipDistressRisk={profile.ChipDistressRisk:0.######}
    instrumentSummary={profile.Summary}
    """;

static string SongSummaryCsv(IReadOnlyList<IpaTrialResult> results)
{
    var builder = new StringBuilder();
    builder.AppendLine("trial_id,candidate_id,reference_id,verdict,log_mel_cosine,log_mel_distance,audio_score,envelope_distance,rms_ratio,centroid_ratio,zero_crossing_ratio,articulation_score,musical_instrument_score,chip_distress_risk,instrument_voice_syrinx,instrument_drum_subtractive,instrument_pad_additive,producer_musicianship_score,required_studio_docs_present,required_studio_doc_coverage,template_loop_risk,noise_percussion_risk,composition_section_score,aqua_gap_count,candidate_active_coverage,active_coverage_ratio,candidate_motion_coverage,motion_coverage_ratio,candidate_first_second_energy_share,first_second_energy_excess,candidate_tail_energy_share,tail_energy_ratio,mode_collapse_risk");
    foreach (var result in results)
    {
        builder.Append(Escape(result.TrialId)).Append(',');
        builder.Append(Escape(result.CandidateId)).Append(',');
        builder.Append(Escape(result.ReferenceId)).Append(',');
        builder.Append(Escape(result.Verdict)).Append(',');
        builder.Append(Metric(result, "log_mel_cosine")).Append(',');
        builder.Append(Metric(result, "log_mel_distance")).Append(',');
        builder.Append(Metric(result, "audio_score")).Append(',');
        builder.Append(Metric(result, "envelope_distance")).Append(',');
        builder.Append(Metric(result, "rms_ratio")).Append(',');
        builder.Append(Metric(result, "centroid_ratio")).Append(',');
        builder.Append(Metric(result, "zero_crossing_ratio")).Append(',');
        builder.Append(Metric(result, "articulation_score")).Append(',');
        builder.Append(Metric(result, "musical_instrument_score")).Append(',');
        builder.Append(Metric(result, "chip_distress_risk")).Append(',');
        builder.Append(Metric(result, "instrument_voice_syrinx")).Append(',');
        builder.Append(Metric(result, "instrument_drum_subtractive")).Append(',');
        builder.Append(Metric(result, "instrument_pad_additive")).Append(',');
        builder.Append(Metric(result, "producer_musicianship_score")).Append(',');
        builder.Append(Metric(result, "required_studio_docs_present")).Append(',');
        builder.Append(Metric(result, "required_studio_doc_coverage")).Append(',');
        builder.Append(Metric(result, "template_loop_risk")).Append(',');
        builder.Append(Metric(result, "noise_percussion_risk")).Append(',');
        builder.Append(Metric(result, "composition_section_score")).Append(',');
        builder.Append(Metric(result, "aqua_gap_count")).Append(',');
        builder.Append(Metric(result, "candidate_active_coverage")).Append(',');
        builder.Append(Metric(result, "active_coverage_ratio")).Append(',');
        builder.Append(Metric(result, "candidate_motion_coverage")).Append(',');
        builder.Append(Metric(result, "motion_coverage_ratio")).Append(',');
        builder.Append(Metric(result, "candidate_first_second_energy_share")).Append(',');
        builder.Append(Metric(result, "first_second_energy_excess")).Append(',');
        builder.Append(Metric(result, "candidate_tail_energy_share")).Append(',');
        builder.Append(Metric(result, "tail_energy_ratio")).Append(',');
        builder.AppendLine(Metric(result, "mode_collapse_risk"));
    }

    return builder.ToString();
}

static string SongEvaluatorReport(SongChallenge challenge, IReadOnlyList<IpaTrialResult> results)
{
    var builder = new StringBuilder();
    builder.AppendLine($"# Song Snippet Evaluator Report: {challenge.ChallengeId}");
    builder.AppendLine();
    builder.AppendLine(SongChallengeReport(challenge));
    builder.AppendLine();
    builder.AppendLine("| candidate | verdict | logMelCosine | score | instrument | producer | docs | template | noise | motion | firstSecond | collapse | rmsRatio | centroidRatio |");
    builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
    foreach (var result in results.OrderByDescending(result => MetricValue(result, "log_mel_cosine")))
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"| {result.CandidateId} | {result.Verdict} | {MetricValue(result, "log_mel_cosine"):0.######} | {MetricValue(result, "audio_score"):0.######} | {MetricValue(result, "musical_instrument_score"):0.######} | {MetricValue(result, "producer_musicianship_score"):0.######} | {MetricValue(result, "required_studio_docs_present"):0.######} | {MetricValue(result, "template_loop_risk"):0.######} | {MetricValue(result, "noise_percussion_risk"):0.######} | {MetricValue(result, "candidate_motion_coverage"):0.######} | {MetricValue(result, "candidate_first_second_energy_share"):0.######} | {MetricValue(result, "mode_collapse_risk"):0.######} | {MetricValue(result, "rms_ratio"):0.######} | {MetricValue(result, "centroid_ratio"):0.######} |");
    }

    return builder.ToString();
}

static float MetricValue(IpaTrialResult result, string name) =>
    result.Metrics.FirstOrDefault(metric => metric.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? 0;

static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web)
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true
};

static SearchOptions SearchOptionsFrom(Dictionary<string, string> options) => new(
    Value(options, "qdrant-url", "http://127.0.0.1:6333"),
    Value(options, "ollama-url", "http://127.0.0.1:11434"),
    Value(options, "embed-model", "qwen3-embedding:0.6b"),
    Value(options, "collection", "aquasynth_ipa_trial_results"),
    int.TryParse(Value(options, "timeout-seconds", "120"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout)
        ? Math.Clamp(timeout, 5, 600)
        : 120,
    !BoolValue(options, "no-vector"),
    BoolValue(options, "require-vector"),
    BoolValue(options, "skip-index"));

static QdrantPayload QdrantPayloadFrom(EvidenceChunk chunk, string embedderId) => new(
    chunk.Id,
    chunk.TrialId,
    "ipa_trial_result",
    "ipa_trial_result",
    StoreKey(chunk.Store),
    chunk.TrialId,
    chunk.CandidateId,
    chunk.TargetSetId,
    chunk.ChunkKind,
    embedderId,
    chunk.Text);

static VectorSearchHit VectorSearchHitFrom(QdrantScoredPoint point)
{
    var payload = point.Payload ?? new Dictionary<string, JsonElement>();
    return new VectorSearchHit(
        ReadPayloadString(payload, "chunkId"),
        ReadPayloadString(payload, "trialId"),
        ReadPayloadString(payload, "candidateId"),
        ReadPayloadString(payload, "targetSetId"),
        ReadPayloadString(payload, "text"),
        point.Score);
}

static Dictionary<string, string> ParseOptions(IReadOnlyList<string> args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Count; i++)
    {
        var arg = args[i];
        if (!arg.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Expected option, got `{arg}`.");
        }

        var key = arg[2..];
        if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            options[key] = "true";
        }
        else
        {
            options[key] = args[++i];
        }
    }

    return options;
}

static string Required(Dictionary<string, string> options, string key) =>
    options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required --{key} option.");

static string Value(Dictionary<string, string> options, string key, string fallback) =>
    options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

static bool BoolValue(Dictionary<string, string> options, string key) =>
    options.TryGetValue(key, out var value) &&
    (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
     value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
     value.Equals("yes", StringComparison.OrdinalIgnoreCase));

static int IntValue(Dictionary<string, string> options, string key, int fallback) =>
    int.TryParse(Value(options, key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : fallback;

static float FloatValue(Dictionary<string, string> options, string key, float fallback) =>
    float.TryParse(Value(options, key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : fallback;

static void PrintHelp()
{
    Console.WriteLine("""
        IPA trial worker

        Commands:
          seed  --artifact-root <dir> [--batch-id five-seed-trials] [--store <trial-results.cc>]
          score --patch-root <dir> --artifact-root <dir> [--batch-id round-001] [--store <trial-results.cc>] [--hypothesizer id]
          song-prepare --source <audio-file> --artifact-root <dir> [--duration-seconds 10] [--seed n] [--output challenge.json]
          song-score --patch-root <dir> --challenge <challenge.json> --artifact-root <dir> [--batch-id round-001] [--store <trial-results.cc>] [--hypothesizer id]
          dump  --store <trial-results.cc> --output <report.md>
          distill --store <trial-results.cc> --output-store <distilled.cc> --output <report.md> [--min-cosine .35] [--max-results 40]
          music-distill --artifact-root <song-swarm-dir> --output-store <music-knowledge.cc> --output <report.md> [--max-candidates 16]
          music-search --store <music-knowledge.cc> --query <text> --output <report.md> [--limit 12]
          music-show --store <music-knowledge.cc> --knowledge-id <id-or-topic> --output <detail.md>
          index --store <trial-results.cc> [--output <report.md>] [--force true]
          search --store <trial-results.cc> --query <text> --output <report.md> [--limit 12] [--no-vector true] [--require-vector true] [--skip-index true]
          show  --store <trial-results.cc> --trial-id <id-or-candidate> --output <detail.md>

        Vector search defaults:
          --qdrant-url http://127.0.0.1:6333
          --ollama-url http://127.0.0.1:11434
          --embed-model qwen3-embedding:0.6b
          --collection aquasynth_ipa_trial_results

        Use song-prepare --duration-seconds 0 to freeze the full decoded source file from sample zero.
        Agent-authored patch files must be named <targetId>__candidate-name.aqua.
        Known seed target ids: a, i, u, e, o, m, n, ng, l, r, s, z, f, v, th, p, b, t, d, k, mix-a, mix-m, mix-s, mix-p, mix-u.
        """);
}

static string ReadPayloadString(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value))
    {
        return "";
    }

    return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
}

sealed record DecodedAudio(int SampleRate, float[] Samples);

sealed record TempoEstimate(float Bpm, float BeatSeconds, float Confidence);

sealed record SongChallenge(
    string ChallengeId,
    string SourcePath,
    string SourceFileName,
    int Seed,
    float StartSeconds,
    float DurationSeconds,
    int SampleRate,
    string ReferenceWavPath,
    SongChallengeFeatures Features,
    SongChallengeAnalysisArtifacts? Artifacts = null);

sealed record SongChallengeAnalysisArtifacts(
    string LogMelSpectrogramCsv,
    string LogMelBandStatsCsv,
    string RmsEnvelopeCsv,
    string RmsEnvelopeAutocorrCsv,
    string WhitenedSpectralAutocorrCsv,
    string AnalysisReportMarkdown);

sealed record SongRenderAnalysisArtifacts(string Kind, string Path);

sealed record SongChallengeFeatures(
    float DurationSeconds,
    float Peak,
    float Rms,
    float ZeroCrossingRate,
    float SpectralCentroidHz,
    float SpectralRolloffHz,
    float ActiveDuty,
    float SpectralFlux,
    float TempoBpm,
    float BeatSeconds,
    float TempoConfidence,
    float DominantHz,
    float RegisterLowHz,
    float RegisterHighHz,
    string RootNote,
    string SuggestedScale,
    string ScaleFrequenciesHz);

sealed record SongInstrumentProfile(
    bool HasSyrinxVoice,
    bool HasSubtractiveDrums,
    bool HasAdditivePad,
    bool HasTexture,
    float MusicalInstrumentScore,
    float ChipDistressRisk,
    string Summary);

sealed record SongProductionProfile(
    bool HasProducerBrief,
    bool HasListeningJournal,
    bool HasAquaGapLedger,
    bool HasStudioLesson,
    float RequiredStudioDocCoverage,
    float CompositionSectionScore,
    float TemplateLoopRisk,
    float NoisePercussionRisk,
    int AquaGapCount,
    float MusicianshipScore,
    string Summary)
{
    public bool RequiredStudioDocsPresent => HasProducerBrief && HasListeningJournal && HasAquaGapLedger && HasStudioLesson;
}

sealed record SongContinuityProfile(
    float TargetActiveCoverage,
    float CandidateActiveCoverage,
    float ActiveCoverageRatio,
    float TargetMotionCoverage,
    float CandidateMotionCoverage,
    float MotionCoverageRatio,
    float TargetFirstSecondEnergyShare,
    float CandidateFirstSecondEnergyShare,
    float FirstSecondEnergyExcess,
    float CandidateTailEnergyShare,
    float TailEnergyRatio,
    float ModeCollapseRisk);

sealed record SongSummaryRow(
    string TrialId,
    string CandidateId,
    string ReferenceId,
    string Verdict,
    float LogMelCosine,
    float LogMelDistance,
    float AudioScore,
    float EnvelopeDistance,
    float RmsRatio,
    float CentroidRatio,
    float ZeroCrossingRatio,
    float ArticulationScore,
    float MusicalInstrumentScore,
    float ChipDistressRisk,
    float InstrumentVoiceSyrinx,
    float InstrumentDrumSubtractive,
    float InstrumentPadAdditive,
    float ProducerMusicianshipScore,
    float RequiredStudioDocsPresent,
    float RequiredStudioDocCoverage,
    float TemplateLoopRisk,
    float NoisePercussionRisk,
    float CompositionSectionScore,
    float AquaGapCount,
    float CandidateActiveCoverage,
    float ActiveCoverageRatio,
    float CandidateMotionCoverage,
    float MotionCoverageRatio,
    float CandidateFirstSecondEnergyShare,
    float FirstSecondEnergyExcess,
    float CandidateTailEnergyShare,
    float TailEnergyRatio,
    float ModeCollapseRisk,
    string SummaryPath);

sealed record MusicKnowledgeHit(
    MusicProductionKnowledgeDocument Document,
    float Score,
    IReadOnlyDictionary<string, string[]> MatchedByField);

sealed record SongRegister(
    float DominantHz,
    float LowHz,
    float HighHz,
    string RootNote,
    string SuggestedScale,
    float[] ScaleFrequencies);

sealed record SpectrogramBandSummary(
    int Band,
    float Mean,
    float Min,
    float Max,
    float StdDev,
    float DeltaMean,
    float AbsDeltaMean,
    float Delta2Mean,
    float AbsDelta2Mean);

sealed record AutocorrelationPoint(
    int LagFrames,
    float LagSeconds,
    float Correlation);

sealed record SearchOptions(
    string QdrantUrl,
    string OllamaUrl,
    string EmbedModel,
    string Collection,
    int TimeoutSeconds,
    bool UseVector,
    bool RequireVector,
    bool SkipIndex);

sealed record WeightedTerms(float Weight, string[] Terms);

sealed record SearchDocument(IpaTrialResult Result, Dictionary<string, WeightedTerms> Fields)
{
    public string[] AllTerms { get; } = Fields.Values
        .SelectMany(field => field.Terms)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static SearchDocument From(IpaTrialResult result, Dictionary<string, WeightedTerms> fields) => new(result, fields);
}

sealed record SearchHit(
    IpaTrialResult Result,
    float Score,
    string[] MatchedTerms,
    IReadOnlyDictionary<string, string[]> MatchedByField,
    string[] Tags,
    string RetrievalMode = "lexical",
    float? VectorScore = null,
    string? VectorChunkId = null,
    string? VectorText = null);

sealed record EvidenceChunk(
    string Id,
    string TrialId,
    string CandidateId,
    string TargetSetId,
    string ChunkKind,
    string Text,
    string Store);

sealed record VectorIndexResult(int ChunkCount, int VectorSize);

sealed record VectorSearchHit(
    string ChunkId,
    string TrialId,
    string CandidateId,
    string TargetSetId,
    string Text,
    float Score);

sealed record OllamaEmbedRequest(string Model, string[] Input);

sealed record OllamaEmbedResponse(
    [property: JsonPropertyName("embedding")] float[]? Embedding,
    [property: JsonPropertyName("embeddings")] float[][]? Embeddings);

sealed record QdrantVectorConfig(
    [property: JsonPropertyName("size")] int Size,
    [property: JsonPropertyName("distance")] string Distance,
    [property: JsonPropertyName("on_disk")] bool OnDisk);

sealed record QdrantCreateCollectionRequest(
    [property: JsonPropertyName("vectors")] QdrantVectorConfig Vectors,
    [property: JsonPropertyName("on_disk_payload")] bool OnDiskPayload,
    [property: JsonPropertyName("metadata")] Dictionary<string, object> Metadata);

sealed record QdrantPayloadIndexRequest(
    [property: JsonPropertyName("field_name")] string FieldName,
    [property: JsonPropertyName("field_schema")] string FieldSchema);

sealed record QdrantPoint(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("vector")] float[] Vector,
    [property: JsonPropertyName("payload")] QdrantPayload Payload);

sealed record QdrantUpsertRequest([property: JsonPropertyName("points")] QdrantPoint[] Points);

sealed record QdrantPayload(
    [property: JsonPropertyName("chunkId")] string ChunkId,
    [property: JsonPropertyName("sourceId")] string SourceId,
    [property: JsonPropertyName("sourceKind")] string SourceKind,
    [property: JsonPropertyName("corpusKind")] string CorpusKind,
    [property: JsonPropertyName("storeKey")] string StoreKey,
    [property: JsonPropertyName("trialId")] string TrialId,
    [property: JsonPropertyName("candidateId")] string CandidateId,
    [property: JsonPropertyName("targetSetId")] string TargetSetId,
    [property: JsonPropertyName("chunkKind")] string ChunkKind,
    [property: JsonPropertyName("embedderId")] string EmbedderId,
    [property: JsonPropertyName("text")] string Text);

sealed record QdrantMatch([property: JsonPropertyName("value")] string Value);

sealed record QdrantCondition(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("match")] QdrantMatch Match);

sealed record QdrantFilter([property: JsonPropertyName("must")] QdrantCondition[] Must)
{
    public static QdrantFilter Store(string storeKey, string embedderId) => new([
        new QdrantCondition("storeKey", new QdrantMatch(storeKey)),
        new QdrantCondition("embedderId", new QdrantMatch(embedderId))
    ]);
}

sealed record QdrantSearchRequest(
    [property: JsonPropertyName("vector")] float[] Vector,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("with_payload")] bool WithPayload,
    [property: JsonPropertyName("with_vector")] bool WithVector,
    [property: JsonPropertyName("filter")] QdrantFilter Filter);

sealed record QdrantSearchResponse([property: JsonPropertyName("result")] QdrantScoredPoint[] Result);

sealed record QdrantScoredPoint(
    [property: JsonPropertyName("score")] float Score,
    [property: JsonPropertyName("payload")] Dictionary<string, JsonElement>? Payload);
