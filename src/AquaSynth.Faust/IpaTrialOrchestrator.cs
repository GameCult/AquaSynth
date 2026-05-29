using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using AquaSynth.Dsl;

namespace AquaSynth.Faust;

public sealed record IpaTrialReferenceTarget(
    IpaGestureExperimentTarget Target,
    string ReferenceFixtureId);

public sealed record IpaTrialTargetSet(
    string Id,
    string Description,
    IReadOnlyList<IpaTrialReferenceTarget> Targets,
    IReadOnlyList<IpaGestureExperimentVariant> Variants)
{
    public IReadOnlyList<IpaTrialReferenceTarget> Targets { get; init; } = Targets ?? Array.Empty<IpaTrialReferenceTarget>();
    public IReadOnlyList<IpaGestureExperimentVariant> Variants { get; init; } = Variants ?? Array.Empty<IpaGestureExperimentVariant>();
}

public sealed record IpaTrialOrchestrationOptions(
    string BatchId = "ipa-seed-trials",
    string HypothesizerId = "local-hypothesis-worker",
    string EvaluatorId = "local-science-evaluator",
    int TimelineBlocks = 12,
    int SampleRate = 44100,
    string RendererToolchain = "local-faust-render",
    string ReferenceRenderer = "pink-trombone-local-fixture",
    string TrialResultStoreFileName = "ipa-trial-results.cc");

public sealed record IpaTrialOrchestrationResult(
    string BatchId,
    string ArtifactDirectory,
    string TrialResultStorePath,
    string SummaryPath,
    string EvaluatorReportPath,
    IReadOnlyList<IpaTrialResult> TrialResults)
{
    public IReadOnlyList<IpaTrialResult> TrialResults { get; init; } = TrialResults ?? Array.Empty<IpaTrialResult>();
}

public static class IpaTrialOrchestrator
{
    public static IReadOnlyList<IpaTrialTargetSet> DefaultFiveSeedTrialSets { get; } =
    [
        new(
            "trial-001-vowels",
            "Open and front vowel hypotheses against static tract references.",
            [
                Target("a", "a", "voiced_open_back_unrounded_vowel", "open-vowel", .20f),
                Target("i", "i", "voiced_close_front_unrounded_vowel", "front-vowel", .20f)
            ],
            [Variant("baseline", 1, 1, "vowel-seed")]),
        new(
            "trial-002-nasal",
            "Bilabial nasal velum/contact hypothesis against the ma pressure fixture.",
            [Target("m", "m", "voiced_bilabial_nasal", "bilabial-nasal-ma", .18f)],
            [Variant("nasal-base", 1, 1, "nasal-seed")]),
        new(
            "trial-003-sibilant",
            "Voiceless alveolar fricative hypothesis against the high-turbulence sibilant fixture.",
            [Target("s", "s", "voiceless_alveolar_fricative", "sibilant", .16f)],
            [Variant("hiss-base", 1.15f, .9f, "fricative-seed")]),
        new(
            "trial-004-plosive",
            "Bilabial stop closure/release hypothesis against obstruction-history pressure.",
            [Target("p", "p", "voiceless_bilabial_plosive", "closure-release", .14f)],
            [Variant("stop-base", 1.2f, .85f, "plosive-seed")]),
        new(
            "trial-005-mixed",
            "Small mixed set used to check whether the same DSL abstractions separate vowel, nasal, and fricative pressure.",
            [
                Target("mix-a", "a", "voiced_open_back_unrounded_vowel", "open-vowel", .18f),
                Target("mix-m", "m", "voiced_bilabial_nasal", "bilabial-nasal-ma", .16f),
                Target("mix-s", "s", "voiceless_alveolar_fricative", "sibilant", .14f)
            ],
            [Variant("mixed-base", 1, 1, "mixed-seed")])
    ];

    public static async Task<IpaTrialOrchestrationResult> RunAsync(
        string artifactRoot,
        IReadOnlyList<IpaTrialTargetSet>? trialSets = null,
        IpaTrialOrchestrationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        options ??= new IpaTrialOrchestrationOptions();
        trialSets ??= DefaultFiveSeedTrialSets;
        if (trialSets.Count == 0)
        {
            throw new ArgumentException("At least one IPA trial target set is required.", nameof(trialSets));
        }

        var batchDirectory = Path.Combine(artifactRoot, options.BatchId);
        Directory.CreateDirectory(batchDirectory);
        var storePath = Path.Combine(batchDirectory, options.TrialResultStoreFileName);
        var referenceRenderer = new PinkTromboneReferenceRenderer();
        var analyzer = new AudioAnalyzer(new AudioAnalysisConfig(SampleRate: options.SampleRate));
        var results = new List<IpaTrialResult>();

        foreach (var targetSet in trialSets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var round = IpaGestureExperiment.WriteRound(
                batchDirectory,
                targetSet.Id,
                targetSet.Targets.Select(target => target.Target),
                targetSet.Variants,
                options.TimelineBlocks);
            var analysis = IpaGestureExperiment.AnalyzeRound(round);
            var targetById = targetSet.Targets.ToDictionary(target => target.Target.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in round.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var startedAt = DateTimeOffset.UtcNow;
                var startedStamp = Stopwatch.GetTimestamp();
                if (!targetById.TryGetValue(candidate.TargetId, out var target))
                {
                    continue;
                }

                var candidateDir = Path.Combine(round.RoundDirectory, "audio", candidate.Id);
                Directory.CreateDirectory(candidateDir);
                var script = await File.ReadAllTextAsync(candidate.ScriptPath, cancellationToken).ConfigureAwait(false);
                var source = FaustEmitter.EmitScript(script, new FaustExportOptions(SafeName(candidate.Id))).Source;
                var dspPath = Path.Combine(candidateDir, "candidate.dsp");
                await File.WriteAllTextAsync(dspPath, source, cancellationToken).ConfigureAwait(false);

                var referenceFixture = PinkTromboneParityFixtures.ById(target.ReferenceFixtureId);
                var reference = referenceRenderer.Render(referenceFixture.Controls, target.Target.DurationSeconds + .18f);
                var candidateRender = await FaustCompiler.RenderAsync(
                    source,
                    new FaustRenderOptions(reference.SampleRate, reference.Samples.Length / (float)reference.SampleRate),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var referenceWav = Path.Combine(candidateDir, "reference.wav");
                var candidateWav = Path.Combine(candidateDir, "candidate.wav");
                WriteWav(referenceWav, reference.Samples, reference.SampleRate);
                var metrics = new List<SpeechScoreMetric>();
                string verdict;
                string evaluation;
                var artifacts = new List<SpeechRenderArtifact>
                {
                    Artifact("candidate-script", candidate.ScriptPath),
                    Artifact("candidate-dsp", dspPath),
                    Artifact("primitive-timeline", candidate.TimelinePath),
                    Artifact("reference-wav", referenceWav),
                    Artifact("round-science-brief", analysis.ScienceBriefPath)
                };

                if (candidateRender is null || candidateRender.Samples.Length == 0)
                {
                    verdict = "render-failed";
                    evaluation = candidateRender is null
                        ? "Faust was not available to render this candidate."
                        : $"Faust render produced no samples. stderr: {candidateRender.Stderr}";
                    metrics.Add(new SpeechScoreMetric("render_failed", 1, 1));
                }
                else
                {
                    WriteWav(candidateWav, candidateRender.Samples, candidateRender.SampleRate);
                    artifacts.Add(Artifact("candidate-wav", candidateWav));
                    var comparison = analyzer.Compare(reference.Samples, candidateRender.Samples);
                    metrics.AddRange(Metrics(candidate, round, comparison));
                    verdict = Verdict(comparison);
                    evaluation = EvaluationSentence(target.Target.Ipa, target.ReferenceFixtureId, comparison, verdict);
                    await WriteComparisonReportAsync(
                        Path.Combine(candidateDir, "comparison.txt"),
                        target,
                        candidate,
                        comparison,
                        verdict,
                        cancellationToken).ConfigureAwait(false);
                    artifacts.Add(Artifact("comparison-report", Path.Combine(candidateDir, "comparison.txt")));
                }

                var latency = Stopwatch.GetElapsedTime(startedStamp).TotalMilliseconds;
                results.Add(new IpaTrialResult(
                    $"{targetSet.Id}:{candidate.Id}",
                    options.BatchId,
                    startedAt.ToString("O", CultureInfo.InvariantCulture),
                    targetSet.Id,
                    targetSet.Targets.Select(item => item.Target.Ipa).Distinct(StringComparer.Ordinal).ToArray(),
                    target.ReferenceFixtureId,
                    candidate.Id,
                    options.HypothesizerId,
                    Hypothesis(targetSet, target, candidate),
                    candidate.ScriptPath,
                    referenceWav,
                    File.Exists(candidateWav) ? candidateWav : "",
                    candidate.TimelinePath,
                    metrics.ToArray(),
                    artifacts.ToArray(),
                    options.EvaluatorId,
                    evaluation,
                    verdict,
                    KnownLies(options, target.ReferenceFixtureId),
                    [
                        new SpeechTimingReceipt(
                            "ipa-trial-render-score",
                            startedAt.ToString("O", CultureInfo.InvariantCulture),
                            latency,
                            0,
                            Confidence(metrics),
                            "Local worker generated the candidate patch, rendered Faust audio, compared log-mel/articulation evidence, and wrote CultCache trial result data.")
                    ]));
            }
        }

        await IpaTrialResultCultCacheStore.UpsertResultsAsync(storePath, results).ConfigureAwait(false);
        var summaryPath = Path.Combine(batchDirectory, "summary.csv");
        await File.WriteAllTextAsync(summaryPath, SummaryCsv(results), cancellationToken).ConfigureAwait(false);
        var evaluatorReport = Path.Combine(batchDirectory, "evaluator-report.md");
        await File.WriteAllTextAsync(evaluatorReport, EvaluatorReport(options, results), cancellationToken).ConfigureAwait(false);
        await IpaTrialResultCultCacheStore.UpsertResultsAsync(storePath, results).ConfigureAwait(false);

        return new IpaTrialOrchestrationResult(
            options.BatchId,
            batchDirectory,
            storePath,
            summaryPath,
            evaluatorReport,
            results);
    }

    private static IpaTrialReferenceTarget Target(string id, string ipa, string descriptor, string fixture, float duration) =>
        new(new IpaGestureExperimentTarget(id, ipa, descriptor, duration), fixture);

    private static IpaGestureExperimentVariant Variant(string id, float intensity, float duration, string tag) =>
        new(id, intensity, duration, [tag]);

    private static IReadOnlyList<SpeechScoreMetric> Metrics(
        IpaGestureExperimentCandidate candidate,
        IpaGestureExperimentResult round,
        AudioComparison comparison)
    {
        var gesture = round.Metrics.FirstOrDefault(metric =>
            metric.CandidateId.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase) &&
            metric.Metric.Equals("gesture_score", StringComparison.OrdinalIgnoreCase))?.Value ?? 0;
        return
        [
            new("gesture_score", gesture, .25f),
            new("log_mel_cosine", comparison.LogMelCosineSimilarity, .25f),
            new("log_mel_distance", comparison.LogMelDistance, .15f),
            new("audio_score", comparison.Score, .15f),
            new("articulation_score", comparison.Articulation.ArticulationScore, .10f),
            new("rms_ratio", comparison.RmsRatio, .05f),
            new("speech_band_ratio", comparison.Articulation.SpeechBandRatio, .05f)
        ];
    }

    private static string Verdict(AudioComparison comparison)
    {
        if (comparison.LogMelCosineSimilarity >= .70f && comparison.Articulation.ArticulationScore >= .45f)
        {
            return "promising";
        }

        if (comparison.LogMelCosineSimilarity >= .35f)
        {
            return "pressure";
        }

        return "weak";
    }

    private static string EvaluationSentence(string ipa, string referenceId, AudioComparison comparison, string verdict) =>
        $"IPA `{ipa}` against `{referenceId}` is `{verdict}`: logMelCosine={comparison.LogMelCosineSimilarity:0.0000}, logMelDistance={comparison.LogMelDistance:0.0000}, articulation={comparison.Articulation.ArticulationScore:0.0000}, rmsRatio={comparison.RmsRatio:0.0000}.";

    private static string Hypothesis(IpaTrialTargetSet set, IpaTrialReferenceTarget target, IpaGestureExperimentCandidate candidate) =>
        $"{set.Description} Candidate `{candidate.Id}` tests whether descriptor `{target.Target.Descriptor}` drives the public anatomical control surfaces enough to approach `{target.ReferenceFixtureId}` spectrogram pressure.";

    private static string[] KnownLies(IpaTrialOrchestrationOptions options, string referenceId) =>
    [
        $"Ground truth is `{referenceId}` from {options.ReferenceRenderer}, not an open IPA recording dataset.",
        "Log-mel evidence is normalized shape pressure and must be read with RMS/articulation witnesses.",
        "The first seed trials render one candidate per hypothesis set; they explore the orchestration surface before optimizer scale-out."
    ];

    private static float Confidence(IReadOnlyList<SpeechScoreMetric> metrics)
    {
        var cosine = metrics.FirstOrDefault(metric => metric.Name == "log_mel_cosine")?.Value ?? 0;
        var gesture = metrics.FirstOrDefault(metric => metric.Name == "gesture_score")?.Value ?? 0;
        return Math.Clamp((cosine + gesture) * .5f, 0, 1);
    }

    private static SpeechRenderArtifact Artifact(string kind, string path) =>
        new(kind, path, File.Exists(path) ? Sha256(path) : "");

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task WriteComparisonReportAsync(
        string path,
        IpaTrialReferenceTarget target,
        IpaGestureExperimentCandidate candidate,
        AudioComparison comparison,
        string verdict,
        CancellationToken cancellationToken)
    {
        var text =
            $"candidate={candidate.Id}{Environment.NewLine}" +
            $"ipa={target.Target.Ipa}{Environment.NewLine}" +
            $"descriptor={target.Target.Descriptor}{Environment.NewLine}" +
            $"reference={target.ReferenceFixtureId}{Environment.NewLine}" +
            $"verdict={verdict}{Environment.NewLine}" +
            $"logMelCosine={comparison.LogMelCosineSimilarity:0.######}{Environment.NewLine}" +
            $"logMelDistance={comparison.LogMelDistance:0.######}{Environment.NewLine}" +
            $"score={comparison.Score:0.######}{Environment.NewLine}" +
            $"rmsRatio={comparison.RmsRatio:0.######}{Environment.NewLine}" +
            $"centroidRatio={comparison.CentroidRatio:0.######}{Environment.NewLine}" +
            $"articulation={comparison.Articulation.ArticulationScore:0.######}{Environment.NewLine}" +
            $"speechBandRatio={comparison.Articulation.SpeechBandRatio:0.######}{Environment.NewLine}";
        await File.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(false);
    }

    private static string SummaryCsv(IReadOnlyList<IpaTrialResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("trial_id,target_set_id,candidate_id,reference_id,verdict,gesture_score,log_mel_cosine,log_mel_distance,audio_score,articulation_score,rms_ratio");
        foreach (var result in results)
        {
            builder.Append(Escape(result.TrialId));
            builder.Append(',');
            builder.Append(Escape(result.TargetSetId));
            builder.Append(',');
            builder.Append(Escape(result.CandidateId));
            builder.Append(',');
            builder.Append(Escape(result.ReferenceId));
            builder.Append(',');
            builder.Append(Escape(result.Verdict));
            builder.Append(',');
            builder.Append(Metric(result, "gesture_score"));
            builder.Append(',');
            builder.Append(Metric(result, "log_mel_cosine"));
            builder.Append(',');
            builder.Append(Metric(result, "log_mel_distance"));
            builder.Append(',');
            builder.Append(Metric(result, "audio_score"));
            builder.Append(',');
            builder.Append(Metric(result, "articulation_score"));
            builder.Append(',');
            builder.AppendLine(Metric(result, "rms_ratio"));
        }

        return builder.ToString();
    }

    private static string EvaluatorReport(IpaTrialOrchestrationOptions options, IReadOnlyList<IpaTrialResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# IPA Trial Evaluator Report: {options.BatchId}");
        builder.AppendLine();
        builder.AppendLine("Authority: this evaluator reads generated candidate patches, primitive timelines, rendered audio comparisons, and CultCache trial records. It does not mutate optimizer checkpoints.");
        builder.AppendLine();
        foreach (var group in results.GroupBy(result => result.TargetSetId).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"## {group.Key}");
            foreach (var result in group.OrderBy(result => result.TrialId, StringComparer.Ordinal))
            {
                builder.Append("- ");
                builder.Append(result.CandidateId);
                builder.Append(": ");
                builder.Append(result.EvaluationSummary);
                builder.Append(" Known contamination: ");
                builder.AppendLine(string.Join(" / ", result.KnownLies));
            }
            builder.AppendLine();
        }

        builder.AppendLine("## Next Hypothesis Pressure");
        foreach (var result in results
            .OrderBy(result => float.Parse(Metric(result, "log_mel_cosine"), CultureInfo.InvariantCulture))
            .ThenBy(result => result.TrialId, StringComparer.Ordinal)
            .Take(5))
        {
            builder.Append("- ");
            builder.Append(result.TrialId);
            builder.Append(": inspect ");
            builder.Append(result.PrimitiveTimelineUri);
            builder.Append(" and ");
            builder.Append(result.CandidatePatchUri);
            builder.AppendLine(" before adding more synthesis dressing.");
        }

        return builder.ToString();
    }

    private static string Metric(IpaTrialResult result, string name) =>
        (result.Metrics.FirstOrDefault(metric => metric.Name == name)?.Value ?? 0)
        .ToString("0.######", CultureInfo.InvariantCulture);

    private static string SafeName(string value) =>
        new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static void WriteWav(string path, IReadOnlyList<float> samples, int sampleRate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        var dataLength = samples.Count * sizeof(short);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        foreach (var sample in samples)
        {
            writer.Write((short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue));
        }
    }
}
