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

public sealed record IpaTrialScriptCandidate(
    string TargetId,
    string CandidateId,
    string ScriptPath,
    string HypothesizerId,
    string Hypothesis);

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
            "Five vowel-space hypotheses against static tract references.",
            [
                Target("a", "a", "voiced_open_back_unrounded_vowel", "open-vowel", .20f),
                Target("i", "i", "voiced_close_front_unrounded_vowel", "front-vowel", .20f),
                Target("u", "u", "voiced_close_back_rounded_vowel", "nasal-vowel", .20f),
                Target("e", "e", "voiced_close_mid_front_unrounded_vowel", "front-vowel", .20f),
                Target("o", "o", "voiced_close_mid_back_rounded_vowel", "open-vowel", .20f)
            ],
            [Variant("baseline", 1, 1, "vowel-seed")]),
        new(
            "trial-002-nasals-approximants",
            "Five sonorant hypotheses that should generalize voicing, velum, lips, and lateral/rhotic shaping.",
            [
                Target("m", "m", "voiced_bilabial_nasal", "bilabial-nasal-ma", .18f),
                Target("n", "n", "voiced_alveolar_nasal", "bilabial-nasal-ma", .18f),
                Target("ng", "ŋ", "voiced_velar_nasal", "nasal-vowel", .18f),
                Target("l", "l", "voiced_alveolar_lateral_approximant", "front-vowel", .18f),
                Target("r", "r", "voiced_alveolar_rhotic_approximant", "open-vowel", .18f)
            ],
            [Variant("nasal-base", 1, 1, "nasal-seed")]),
        new(
            "trial-003-fricatives",
            "Five fricative hypotheses that should generalize narrow constriction, turbulence, place, and voicing.",
            [
                Target("s", "s", "voiceless_alveolar_fricative", "sibilant", .16f),
                Target("z", "z", "voiced_alveolar_fricative", "sibilant", .16f),
                Target("f", "f", "voiceless_labiodental_fricative", "sibilant", .16f),
                Target("v", "v", "voiced_labiodental_fricative", "sibilant", .16f),
                Target("th", "θ", "voiceless_dental_fricative", "sibilant", .16f)
            ],
            [Variant("hiss-base", 1.15f, .9f, "fricative-seed")]),
        new(
            "trial-004-stops",
            "Five stop hypotheses that should generalize closure, reservoir pressure, release, place, and voicing.",
            [
                Target("p", "p", "voiceless_bilabial_plosive", "closure-release", .14f),
                Target("b", "b", "voiced_bilabial_plosive", "closure-release", .14f),
                Target("t", "t", "voiceless_alveolar_plosive", "closure-release", .14f),
                Target("d", "d", "voiced_alveolar_plosive", "closure-release", .14f),
                Target("k", "k", "voiceless_velar_plosive", "closure-release", .14f)
            ],
            [Variant("stop-base", 1.2f, .85f, "plosive-seed")]),
        new(
            "trial-005-mixed-generalization",
            "Five mixed phones used to check whether one DSL/control vocabulary transfers across vowel, nasal, fricative, stop, and rounded classes.",
            [
                Target("mix-a", "a", "voiced_open_back_unrounded_vowel", "open-vowel", .18f),
                Target("mix-m", "m", "voiced_bilabial_nasal", "bilabial-nasal-ma", .16f),
                Target("mix-s", "s", "voiceless_alveolar_fricative", "sibilant", .14f),
                Target("mix-p", "p", "voiceless_bilabial_plosive", "closure-release", .14f),
                Target("mix-u", "u", "voiced_close_back_rounded_vowel", "nasal-vowel", .18f)
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
                    ],
                    PrimitiveTimelineFactExtractor.Extract(ReadTimeline(candidate.TimelinePath)).ToArray()));
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

    public static async Task<IpaTrialOrchestrationResult> RunCandidateScriptsAsync(
        string artifactRoot,
        IReadOnlyList<IpaTrialScriptCandidate> candidates,
        IReadOnlyList<IpaTrialTargetSet>? targetSets = null,
        IpaTrialOrchestrationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        ArgumentNullException.ThrowIfNull(candidates);
        options ??= new IpaTrialOrchestrationOptions(BatchId: "ipa-agent-candidate-trials");
        targetSets ??= DefaultFiveSeedTrialSets;
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one IPA script candidate is required.", nameof(candidates));
        }

        var batchDirectory = Path.Combine(artifactRoot, options.BatchId);
        var candidateRoot = Path.Combine(batchDirectory, "agent-candidates");
        var timelineRoot = Path.Combine(batchDirectory, "agent-timelines");
        Directory.CreateDirectory(candidateRoot);
        Directory.CreateDirectory(timelineRoot);
        var storePath = Path.Combine(batchDirectory, options.TrialResultStoreFileName);
        var targetById = targetSets
            .SelectMany(set => set.Targets.Select(target => (set, target)))
            .GroupBy(pair => pair.target.Target.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var referenceRenderer = new PinkTromboneReferenceRenderer();
        var analyzer = new AudioAnalyzer(new AudioAnalysisConfig(SampleRate: options.SampleRate));
        var results = new List<IpaTrialResult>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!targetById.TryGetValue(candidate.TargetId, out var mapped))
            {
                throw new ArgumentException($"Candidate `{candidate.CandidateId}` names unknown IPA target `{candidate.TargetId}`.", nameof(candidates));
            }

            var startedAt = DateTimeOffset.UtcNow;
            var startedStamp = Stopwatch.GetTimestamp();
            var safeCandidateId = SafeName(candidate.CandidateId);
            var candidateDir = Path.Combine(candidateRoot, safeCandidateId);
            Directory.CreateDirectory(candidateDir);
            var audioDir = Path.Combine(candidateDir, "audio");
            Directory.CreateDirectory(audioDir);
            var scriptCopy = Path.Combine(candidateDir, "candidate.aqua");
            File.Copy(candidate.ScriptPath, scriptCopy, overwrite: true);
            var script = await File.ReadAllTextAsync(scriptCopy, cancellationToken).ConfigureAwait(false);
            var patch = PatchScript.Parse(script);
            var timeline = ProbeTimelineReport.Build(patch, "voice", Math.Max(1, options.TimelineBlocks));
            var timelinePath = Path.Combine(timelineRoot, $"{safeCandidateId}.csv");
            await File.WriteAllTextAsync(timelinePath, ProbeTimelineReport.ToCsv(timeline), cancellationToken).ConfigureAwait(false);

            var source = FaustEmitter.EmitScript(script, new FaustExportOptions(safeCandidateId)).Source;
            var dspPath = Path.Combine(candidateDir, "candidate.dsp");
            await File.WriteAllTextAsync(dspPath, source, cancellationToken).ConfigureAwait(false);

            var referenceFixture = PinkTromboneParityFixtures.ById(mapped.target.ReferenceFixtureId);
            var reference = referenceRenderer.Render(referenceFixture.Controls, mapped.target.Target.DurationSeconds + .18f);
            var candidateRender = await FaustCompiler.RenderAsync(
                source,
                new FaustRenderOptions(reference.SampleRate, reference.Samples.Length / (float)reference.SampleRate),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var referenceWav = Path.Combine(audioDir, "reference.wav");
            var candidateWav = Path.Combine(audioDir, "candidate.wav");
            WriteWav(referenceWav, reference.Samples, reference.SampleRate);
            var metrics = new List<SpeechScoreMetric>
            {
                new("gesture_score", AgentGestureScore(mapped.target.Target, patch, timeline), .25f)
            };
            var timelineFacts = PrimitiveTimelineFactExtractor.Extract(timeline).ToArray();
            string verdict;
            string evaluation;
            var artifacts = new List<SpeechRenderArtifact>
            {
                Artifact("candidate-script", scriptCopy),
                Artifact("candidate-dsp", dspPath),
                Artifact("primitive-timeline", timelinePath),
                Artifact("reference-wav", referenceWav)
            };

            if (candidateRender is null || candidateRender.Samples.Length == 0)
            {
                verdict = "render-failed";
                evaluation = candidateRender is null
                    ? "Faust was not available to render this agent-authored candidate."
                    : $"Faust render produced no samples. stderr: {candidateRender.Stderr}";
                metrics.Add(new SpeechScoreMetric("render_failed", 1, 1));
            }
            else
            {
                WriteWav(candidateWav, candidateRender.Samples, candidateRender.SampleRate);
                artifacts.Add(Artifact("candidate-wav", candidateWav));
                var comparison = analyzer.Compare(reference.Samples, candidateRender.Samples);
                metrics.AddRange(ComparisonMetrics(comparison));
                verdict = Verdict(comparison);
                evaluation = EvaluationSentence(mapped.target.Target.Ipa, mapped.target.ReferenceFixtureId, comparison, verdict);
                var comparisonPath = Path.Combine(audioDir, "comparison.txt");
                await WriteComparisonReportAsync(
                    comparisonPath,
                    mapped.target,
                    new IpaGestureExperimentCandidate(safeCandidateId, candidate.TargetId, "agent-authored", scriptCopy, timelinePath, []),
                    comparison,
                    verdict,
                    cancellationToken).ConfigureAwait(false);
                artifacts.Add(Artifact("comparison-report", comparisonPath));
            }

            var latency = Stopwatch.GetElapsedTime(startedStamp).TotalMilliseconds;
            results.Add(new IpaTrialResult(
                $"{options.BatchId}:{mapped.set.Id}:{safeCandidateId}",
                options.BatchId,
                startedAt.ToString("O", CultureInfo.InvariantCulture),
                mapped.set.Id,
                mapped.set.Targets.Select(item => item.Target.Ipa).Distinct(StringComparer.Ordinal).ToArray(),
                mapped.target.ReferenceFixtureId,
                safeCandidateId,
                candidate.HypothesizerId,
                candidate.Hypothesis,
                scriptCopy,
                referenceWav,
                File.Exists(candidateWav) ? candidateWav : "",
                timelinePath,
                metrics.ToArray(),
                artifacts.ToArray(),
                options.EvaluatorId,
                evaluation,
                verdict,
                KnownLies(options, mapped.target.ReferenceFixtureId),
                [
                    new SpeechTimingReceipt(
                        "ipa-agent-candidate-render-score",
                        startedAt.ToString("O", CultureInfo.InvariantCulture),
                        latency,
                        0,
                        Confidence(metrics),
                        "Local worker rendered an external Codex-authored patch, compared log-mel/articulation evidence, and wrote CultCache trial result data.")
                ],
                timelineFacts));
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

    private static IReadOnlyList<SpeechScoreMetric> ComparisonMetrics(AudioComparison comparison) =>
    [
        new("log_mel_cosine", comparison.LogMelCosineSimilarity, .25f),
        new("log_mel_distance", comparison.LogMelDistance, .15f),
        new("audio_score", comparison.Score, .15f),
        new("articulation_score", comparison.Articulation.ArticulationScore, .10f),
        new("rms_ratio", comparison.RmsRatio, .05f),
        new("speech_band_ratio", comparison.Articulation.SpeechBandRatio, .05f)
    ];

    private static float AgentGestureScore(
        IpaGestureExperimentTarget target,
        SynthPatch patch,
        IReadOnlyList<ProbeTimelineSample> timeline)
    {
        var tokens = DescriptorTokens(target.Descriptor);
        var expected = ExpectedSurfaces(tokens);
        var touched = patch.ControlSplines
            .Where(spline => spline.Enabled)
            .Select(spline => spline.SurfacePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var coverage = expected.Length == 0
            ? 1
            : expected.Count(surface => touched.Contains(surface)) / (float)expected.Length;
        var motion = patch.ControlSplines.Any(spline => spline.Enabled && spline.Points.Count >= 2) ? 1f : 0f;
        var primitive = timeline.Any(sample =>
            (sample.Signal.Equals("output", StringComparison.OrdinalIgnoreCase) ||
             sample.Signal.Equals("flow", StringComparison.OrdinalIgnoreCase) ||
             sample.Signal.Equals("released_flow", StringComparison.OrdinalIgnoreCase)) &&
            float.IsFinite(sample.Value))
            ? 1f
            : 0f;
        return Math.Clamp(.5f * coverage + .25f * motion + .25f * primitive, 0, 1);
    }

    private static string[] ExpectedSurfaces(HashSet<string> tokens)
    {
        var surfaces = new List<string>
        {
            "/vocal/sources/0/pressure",
            "/vocal/sources/0/tension",
            "/vocal/sources/0/opening"
        };

        if (tokens.Contains("vowel"))
        {
            surfaces.AddRange(
            [
                "/vocal/areas/0/area/tongue_index",
                "/vocal/areas/0/area/tongue_diameter",
                "/vocal/areas/0/area/lip_opening",
                "/vocal/radiation/0/aperture"
            ]);
        }
        else
        {
            surfaces.AddRange(
            [
                "/vocal/areas/0/area/constriction_index",
                "/vocal/areas/0/area/constriction_diameter",
                "/vocal/contacts/0/opening"
            ]);
        }

        if (tokens.Contains("nasal"))
        {
            surfaces.Add("/vocal/branches/0/opening");
        }

        if (tokens.Contains("fricative"))
        {
            surfaces.Add("/vocal/sources/0/noise");
            surfaces.Add("/vocal/contacts/0/resistance");
        }

        if (tokens.Contains("plosive") || tokens.Contains("stop"))
        {
            surfaces.Add("/vocal/contacts/0/stored_pressure");
        }

        if (tokens.Contains("bilabial") || tokens.Contains("labial") || tokens.Contains("labiodental") || tokens.Contains("rounded"))
        {
            surfaces.Add("/vocal/areas/0/area/lip_opening");
            surfaces.Add("/vocal/radiation/0/aperture");
        }

        return surfaces.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static HashSet<string> DescriptorTokens(string descriptor) =>
        descriptor
            .ToLowerInvariant()
            .Replace('-', '_')
            .Split(['_', ',', '+', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
        builder.AppendLine("trial_id,target_set_id,candidate_id,reference_id,verdict,gesture_score,log_mel_cosine,log_mel_distance,audio_score,articulation_score,rms_ratio,contact_release_peak,radiation_output_peak,path_passivity_max");
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
            builder.Append(Metric(result, "rms_ratio"));
            builder.Append(',');
            builder.Append(TimelineFact(result, "contact_release_peak"));
            builder.Append(',');
            builder.Append(TimelineFact(result, "radiation_output_peak"));
            builder.Append(',');
            builder.AppendLine(TimelineFact(result, "path_passivity_max"));
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

    private static string TimelineFact(IpaTrialResult result, string name) =>
        (result.TimelineFacts?
            .Where(fact => fact.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(fact => fact.Value)
            .DefaultIfEmpty(0)
            .Max() ?? 0)
        .ToString("0.######", CultureInfo.InvariantCulture);

    private static IReadOnlyList<ProbeTimelineSample> ReadTimeline(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var samples = new List<ProbeTimelineSample>();
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length < 4 ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var block) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            samples.Add(new ProbeTimelineSample(block, parts[1], parts[2], value));
        }

        return samples;
    }

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
