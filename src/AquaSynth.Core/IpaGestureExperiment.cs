using System.Globalization;
using System.Text;

namespace AquaSynth.Dsl;

public sealed record IpaGestureExperimentTarget(
    string Id,
    string Ipa,
    string Descriptor,
    float DurationSeconds = 0.18f,
    string Notes = "");

public sealed record IpaGestureExperimentVariant(
    string Id,
    float IntensityScale = 1,
    float DurationScale = 1,
    IReadOnlyList<string>? Tags = null,
    string Notes = "")
{
    public IReadOnlyList<string> Tags { get; init; } = Tags ?? Array.Empty<string>();
}

public sealed record IpaGestureExperimentCandidate(
    string Id,
    string TargetId,
    string VariantId,
    string ScriptPath,
    string TimelinePath,
    IReadOnlyList<string> Tags)
{
    public IReadOnlyList<string> Tags { get; init; } = Tags ?? Array.Empty<string>();
}

public sealed record IpaGestureExperimentMetric(
    string CandidateId,
    string TargetId,
    string Layer,
    string Metric,
    float Value);

public sealed record IpaGestureExperimentResult(
    string RoundId,
    string RoundDirectory,
    string ManifestPath,
    string MetricsPath,
    string EvidencePath,
    IReadOnlyList<IpaGestureExperimentCandidate> Candidates,
    IReadOnlyList<IpaGestureExperimentMetric> Metrics)
{
    public IReadOnlyList<IpaGestureExperimentCandidate> Candidates { get; init; } = Candidates ?? Array.Empty<IpaGestureExperimentCandidate>();
    public IReadOnlyList<IpaGestureExperimentMetric> Metrics { get; init; } = Metrics ?? Array.Empty<IpaGestureExperimentMetric>();
}

public sealed record IpaGestureMetricSummary(
    string TargetId,
    string Metric,
    float Mean,
    float Minimum,
    float Maximum,
    float Spread,
    string BestCandidateId,
    string WorstCandidateId);

public sealed record IpaGestureCandidateCluster(
    string TargetId,
    string Cluster,
    IReadOnlyList<string> CandidateIds,
    float MeanGestureScore)
{
    public IReadOnlyList<string> CandidateIds { get; init; } = CandidateIds ?? Array.Empty<string>();
}

public sealed record IpaGestureExperimentAnalysisResult(
    string AnalysisDirectory,
    string ScienceBriefPath,
    string MetricSummaryPath,
    string CandidateClustersPath,
    IReadOnlyList<IpaGestureMetricSummary> MetricSummaries,
    IReadOnlyList<IpaGestureCandidateCluster> CandidateClusters)
{
    public IReadOnlyList<IpaGestureMetricSummary> MetricSummaries { get; init; } = MetricSummaries ?? Array.Empty<IpaGestureMetricSummary>();
    public IReadOnlyList<IpaGestureCandidateCluster> CandidateClusters { get; init; } = CandidateClusters ?? Array.Empty<IpaGestureCandidateCluster>();
}

public static class IpaGestureExperiment
{
    private const string NetworkName = "voice";

    public static IpaGestureExperimentResult WriteRound(
        string rootDirectory,
        string roundId,
        IEnumerable<IpaGestureExperimentTarget> targets,
        IEnumerable<IpaGestureExperimentVariant> variants,
        int timelineBlocks = 12)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(roundId);

        var targetList = targets.ToArray();
        var variantList = variants.ToArray();
        if (targetList.Length == 0)
        {
            throw new ArgumentException("At least one IPA target is required.", nameof(targets));
        }

        if (variantList.Length == 0)
        {
            throw new ArgumentException("At least one experiment variant is required.", nameof(variants));
        }

        var roundDirectory = Path.Combine(rootDirectory, SafeFileName(roundId));
        var candidateDirectory = Path.Combine(roundDirectory, "candidates");
        var timelineDirectory = Path.Combine(roundDirectory, "timelines");
        Directory.CreateDirectory(candidateDirectory);
        Directory.CreateDirectory(timelineDirectory);

        var candidates = new List<IpaGestureExperimentCandidate>();
        var metrics = new List<IpaGestureExperimentMetric>();
        var evidence = new List<string>();

        foreach (var target in targetList)
        {
            foreach (var variant in variantList)
            {
                var candidateId = SafeFileName($"{target.Id}-{variant.Id}");
                var script = BuildCandidateScript(target, variant);
                var scriptPath = Path.Combine(candidateDirectory, $"{candidateId}.aqua");
                File.WriteAllText(scriptPath, script, Encoding.UTF8);

                var patch = PatchScript.Parse(script);
                var timeline = ProbeTimelineReport.Build(patch, NetworkName, Math.Max(1, timelineBlocks));
                var timelinePath = Path.Combine(timelineDirectory, $"{candidateId}.csv");
                File.WriteAllText(timelinePath, ProbeTimelineReport.ToCsv(timeline), Encoding.UTF8);

                var tags = variant.Tags.Concat(DescriptorTokens(target.Descriptor).Select(token => $"descriptor:{token}")).ToArray();
                var candidate = new IpaGestureExperimentCandidate(candidateId, target.Id, variant.Id, scriptPath, timelinePath, tags);
                candidates.Add(candidate);

                var scored = ScoreGesture(candidate, target, patch, timeline);
                metrics.AddRange(scored);
                evidence.Add(EvidenceLine(roundId, candidate, target, variant, scored));
            }
        }

        var manifestPath = Path.Combine(roundDirectory, "manifest.yaml");
        File.WriteAllText(manifestPath, Manifest(roundId, targetList, variantList, candidates), Encoding.UTF8);

        var metricsPath = Path.Combine(roundDirectory, "metrics.csv");
        File.WriteAllText(metricsPath, MetricsCsv(metrics), Encoding.UTF8);

        var evidencePath = Path.Combine(roundDirectory, "evidence.jsonl");
        File.WriteAllText(evidencePath, string.Join(Environment.NewLine, evidence) + Environment.NewLine, Encoding.UTF8);

        return new IpaGestureExperimentResult(
            roundId,
            roundDirectory,
            manifestPath,
            metricsPath,
            evidencePath,
            candidates,
            metrics);
    }

    public static IpaGestureExperimentAnalysisResult AnalyzeRound(IpaGestureExperimentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var analysisDirectory = Path.Combine(result.RoundDirectory, "analysis");
        Directory.CreateDirectory(analysisDirectory);

        var summaries = SummarizeMetrics(result.Metrics);
        var clusters = ClusterCandidates(result.Metrics);

        var summaryPath = Path.Combine(analysisDirectory, "metric-summary.csv");
        File.WriteAllText(summaryPath, MetricSummaryCsv(summaries), Encoding.UTF8);

        var clusterPath = Path.Combine(analysisDirectory, "candidate-clusters.csv");
        File.WriteAllText(clusterPath, CandidateClusterCsv(clusters), Encoding.UTF8);

        var briefPath = Path.Combine(analysisDirectory, "science-brief.md");
        File.WriteAllText(briefPath, ScienceBrief(result, summaries, clusters), Encoding.UTF8);

        return new IpaGestureExperimentAnalysisResult(
            analysisDirectory,
            briefPath,
            summaryPath,
            clusterPath,
            summaries,
            clusters);
    }

    public static string BuildCandidateScript(IpaGestureExperimentTarget target, IpaGestureExperimentVariant variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Ipa);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant.Id);

        var duration = Math.Clamp(target.DurationSeconds * variant.DurationScale, 0.03f, 1.5f);
        var intensity = Math.Clamp(variant.IntensityScale, 0.05f, 2f);
        var sustain = Math.Clamp(duration + 0.12f, 0.15f, 2f);
        var descriptor = target.Descriptor.Replace(' ', '_');
        var frequency = DescriptorTokens(target.Descriptor).Contains("vowel") ? 150 : 135;

        return $$"""
            patch gain=.45
            morphology name=oral length_cm=17 diameters=.55,.75,1.05,1.45,1.25,.8 tongue_index=3 tongue_diameter=1.4 constriction_index=4 constriction_diameter=1 lip_opening=1.2
            morphology name=nasal length_cm=12 diameters=.04,.28,.52,.72
            waveguide_path name=oral_path morphology=oral strategy=thiran order=1 max_delay=4096 loss=.998
            waveguide_path name=nasal_path morphology=nasal strategy=thiran order=1 max_delay=4096 loss=.997
            source_port name=folds path=oral_path kind=glottal position=0 pressure=.66 tension=.54 opening=.42 noise=.04 impedance=.32 flow_scale=.12
            branch_port name=velopharynx from=oral_path from_position=.45 to=nasal_path opening=.01 coupling=1
            constriction_contact name=contact path=oral_path position=.92 opening=.5 resistance=.45 stored_pressure=.12
            radiation_load name=mouth path=oral_path kind=lip position=1 aperture=.8 reflection=-.82 impedance=.28
            probe_timeline name=flow network=voice blocks=12 block_size=64
            vocal_network name=voice paths=oral_path,nasal_path sources=folds contacts=contact branches=velopharynx radiation=mouth probe=flow
            phoneme_gesture name={{SafeDslName(target.Id)}} ipa={{target.Ipa}} descriptor={{descriptor}} start=0 dur={{Format(duration)}} intensity={{Format(intensity)}}
            vocal network=voice freq={{Format(frequency)}} gain=.7 sustain={{Format(sustain)}}
            """;
    }

    private static IReadOnlyList<IpaGestureMetricSummary> SummarizeMetrics(IReadOnlyList<IpaGestureExperimentMetric> metrics) =>
        metrics
            .Where(metric => metric.Layer.Equals("gesture", StringComparison.OrdinalIgnoreCase))
            .GroupBy(metric => (metric.TargetId, metric.Metric))
            .Select(group =>
            {
                var ordered = group.OrderBy(metric => metric.Value).ToArray();
                var minimum = ordered[0];
                var maximum = ordered[^1];
                return new IpaGestureMetricSummary(
                    group.Key.TargetId,
                    group.Key.Metric,
                    group.Average(metric => metric.Value),
                    minimum.Value,
                    maximum.Value,
                    maximum.Value - minimum.Value,
                    maximum.CandidateId,
                    minimum.CandidateId);
            })
            .OrderBy(summary => summary.TargetId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.Metric, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<IpaGestureCandidateCluster> ClusterCandidates(IReadOnlyList<IpaGestureExperimentMetric> metrics) =>
        metrics
            .Where(metric => metric.Metric.Equals("gesture_score", StringComparison.OrdinalIgnoreCase))
            .GroupBy(metric => (metric.TargetId, ClusterName(metric.Value)))
            .Select(group => new IpaGestureCandidateCluster(
                group.Key.TargetId,
                group.Key.Item2,
                group.Select(metric => metric.CandidateId).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                group.Average(metric => metric.Value)))
            .OrderBy(cluster => cluster.TargetId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(cluster => ClusterRank(cluster.Cluster))
            .ToArray();

    private static IReadOnlyList<IpaGestureExperimentMetric> ScoreGesture(
        IpaGestureExperimentCandidate candidate,
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
        var direction = DirectionScore(tokens, patch.ControlSplines);
        var contour = ContourTimingScore(patch.ControlSplines);
        var primitive = PrimitiveTimelineScore(tokens, timeline);
        var score = 0.25f * coverage + 0.25f * direction + 0.20f * contour + 0.20f * primitive;

        return
        [
            Metric("surface_coverage", coverage),
            Metric("motion_direction", direction),
            Metric("contour_timing", contour),
            Metric("primitive_timeline", primitive),
            Metric("gesture_score", score)
        ];

        IpaGestureExperimentMetric Metric(string name, float value) =>
            new(candidate.Id, target.Id, "gesture", name, Math.Clamp(value, 0, 1));
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

    private static float DirectionScore(HashSet<string> tokens, IReadOnlyList<ControlSpline> splines)
    {
        var checks = new List<float>();
        if (tokens.Contains("nasal"))
        {
            checks.Add(Rises(splines, "/vocal/branches/0/opening"));
            checks.Add(FallsOrReleases(splines, "/vocal/contacts/0/opening"));
        }

        if (tokens.Contains("fricative"))
        {
            checks.Add(Rises(splines, "/vocal/sources/0/noise"));
            checks.Add(HoldsNarrow(splines, "/vocal/areas/0/area/constriction_diameter"));
        }

        if (tokens.Contains("plosive") || tokens.Contains("stop"))
        {
            checks.Add(Rises(splines, "/vocal/contacts/0/opening"));
            checks.Add(Falls(splines, "/vocal/contacts/0/stored_pressure"));
        }

        if (tokens.Contains("vowel"))
        {
            checks.Add(Exists(splines, "/vocal/areas/0/area/tongue_index"));
            checks.Add(Exists(splines, "/vocal/areas/0/area/tongue_diameter"));
            checks.Add(Exists(splines, "/vocal/radiation/0/aperture"));
        }

        if (tokens.Contains("voiceless"))
        {
            checks.Add(Falls(splines, "/vocal/sources/0/tension"));
        }
        else
        {
            checks.Add(RisesOrHolds(splines, "/vocal/sources/0/tension"));
        }

        return checks.Count == 0 ? 1 : checks.Average();
    }

    private static float ContourTimingScore(IReadOnlyList<ControlSpline> splines)
    {
        var enabled = splines.Where(spline => spline.Enabled).ToArray();
        if (enabled.Length == 0)
        {
            return 0;
        }

        var scored = 0;
        foreach (var spline in enabled)
        {
            var ordered = spline.Points.OrderBy(point => point.TimeSeconds).ToArray();
            if (ordered.Length < 2)
            {
                continue;
            }

            var monotonic = ordered.Zip(ordered.Skip(1), (a, b) => b.TimeSeconds >= a.TimeSeconds).All(value => value);
            var hasSpan = ordered[^1].TimeSeconds - ordered[0].TimeSeconds > 0.005f;
            var valuesMove = ordered.Max(point => point.Value) - ordered.Min(point => point.Value) >= 0.0001f;
            if (monotonic && hasSpan && valuesMove)
            {
                scored++;
            }
        }

        return scored / (float)enabled.Length;
    }

    private static float PrimitiveTimelineScore(HashSet<string> tokens, IReadOnlyList<ProbeTimelineSample> timeline)
    {
        if (timeline.Count == 0)
        {
            return 0;
        }

        var checks = new List<float>
        {
            Finite(timeline, "path:oral_path", "passivity_ratio"),
            Finite(timeline, "source:folds", "flow"),
            Finite(timeline, "radiation:mouth", "output")
        };

        if (tokens.Contains("nasal"))
        {
            checks.Add(Positive(timeline, "branch:velopharynx", "admittance"));
        }

        if (tokens.Contains("fricative") || tokens.Contains("plosive") || tokens.Contains("stop") || tokens.Contains("nasal"))
        {
            checks.Add(Finite(timeline, "contact:contact", "opening"));
            checks.Add(Finite(timeline, "contact:contact", "released_flow"));
        }

        if (tokens.Contains("plosive") || tokens.Contains("stop"))
        {
            checks.Add(Positive(timeline, "contact:contact", "reservoir"));
        }

        return checks.Average();
    }

    private static float Finite(IReadOnlyList<ProbeTimelineSample> timeline, string primitive, string signal) =>
        timeline.Any(sample =>
            sample.Primitive.Equals(primitive, StringComparison.OrdinalIgnoreCase) &&
            sample.Signal.Equals(signal, StringComparison.OrdinalIgnoreCase) &&
            float.IsFinite(sample.Value))
            ? 1
            : 0;

    private static float Positive(IReadOnlyList<ProbeTimelineSample> timeline, string primitive, string signal) =>
        timeline.Any(sample =>
            sample.Primitive.Equals(primitive, StringComparison.OrdinalIgnoreCase) &&
            sample.Signal.Equals(signal, StringComparison.OrdinalIgnoreCase) &&
            sample.Value > 0 &&
            float.IsFinite(sample.Value))
            ? 1
            : 0;

    private static float Exists(IReadOnlyList<ControlSpline> splines, string surface) =>
        splines.Any(spline => spline.Enabled && spline.SurfacePath.Equals(surface, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

    private static float Rises(IReadOnlyList<ControlSpline> splines, string surface) =>
        SignedMotion(splines, surface, (first, last) => last > first);

    private static float Falls(IReadOnlyList<ControlSpline> splines, string surface) =>
        SignedMotion(splines, surface, (first, last) => last < first);

    private static float RisesOrHolds(IReadOnlyList<ControlSpline> splines, string surface) =>
        SignedMotion(splines, surface, (first, last) => last >= first);

    private static float HoldsNarrow(IReadOnlyList<ControlSpline> splines, string surface) =>
        SignedMotion(splines, surface, (first, last) => Math.Max(first, last) <= 0.35f);

    private static float FallsOrReleases(IReadOnlyList<ControlSpline> splines, string surface) =>
        SignedMotion(splines, surface, (first, last) => last >= first || first <= 0.08f);

    private static float SignedMotion(IReadOnlyList<ControlSpline> splines, string surface, Func<float, float, bool> predicate)
    {
        var spline = splines.FirstOrDefault(item => item.Enabled && item.SurfacePath.Equals(surface, StringComparison.OrdinalIgnoreCase));
        if (spline is null || spline.Points.Count == 0)
        {
            return 0;
        }

        var ordered = spline.Points.OrderBy(point => point.TimeSeconds).ToArray();
        return predicate(ordered[0].Value, ordered[^1].Value) ? 1 : 0;
    }

    private static string Manifest(
        string roundId,
        IReadOnlyList<IpaGestureExperimentTarget> targets,
        IReadOnlyList<IpaGestureExperimentVariant> variants,
        IReadOnlyList<IpaGestureExperimentCandidate> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"round_id: {Yaml(roundId)}");
        builder.AppendLine($"created_utc: {Yaml(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))}");
        builder.AppendLine("authority:");
        builder.AppendLine("  owns: frozen candidate scripts, primitive timelines, first-layer gesture metrics");
        builder.AppendLine("  does_not_own: clean vocal audio identity, full spectrogram parity, optimizer checkpoints, worker orchestration");
        builder.AppendLine("targets:");
        foreach (var target in targets)
        {
            builder.AppendLine($"  - id: {Yaml(target.Id)}");
            builder.AppendLine($"    ipa: {Yaml(target.Ipa)}");
            builder.AppendLine($"    descriptor: {Yaml(target.Descriptor)}");
            builder.AppendLine($"    duration_seconds: {Format(target.DurationSeconds)}");
        }

        builder.AppendLine("variants:");
        foreach (var variant in variants)
        {
            builder.AppendLine($"  - id: {Yaml(variant.Id)}");
            builder.AppendLine($"    intensity_scale: {Format(variant.IntensityScale)}");
            builder.AppendLine($"    duration_scale: {Format(variant.DurationScale)}");
            builder.AppendLine($"    tags: [{string.Join(", ", variant.Tags.Select(Yaml))}]");
        }

        builder.AppendLine("candidates:");
        foreach (var candidate in candidates)
        {
            builder.AppendLine($"  - id: {Yaml(candidate.Id)}");
            builder.AppendLine($"    target_id: {Yaml(candidate.TargetId)}");
            builder.AppendLine($"    variant_id: {Yaml(candidate.VariantId)}");
            builder.AppendLine($"    script: {Yaml(candidate.ScriptPath)}");
            builder.AppendLine($"    primitive_timeline: {Yaml(candidate.TimelinePath)}");
        }

        return builder.ToString();
    }

    private static string MetricsCsv(IEnumerable<IpaGestureExperimentMetric> metrics)
    {
        var builder = new StringBuilder();
        builder.AppendLine("candidate_id,target_id,layer,metric,value");
        foreach (var metric in metrics)
        {
            builder.Append(EscapeCsv(metric.CandidateId));
            builder.Append(',');
            builder.Append(EscapeCsv(metric.TargetId));
            builder.Append(',');
            builder.Append(EscapeCsv(metric.Layer));
            builder.Append(',');
            builder.Append(EscapeCsv(metric.Metric));
            builder.Append(',');
            builder.AppendLine(Format(metric.Value));
        }

        return builder.ToString();
    }

    private static string EvidenceLine(
        string roundId,
        IpaGestureExperimentCandidate candidate,
        IpaGestureExperimentTarget target,
        IpaGestureExperimentVariant variant,
        IReadOnlyList<IpaGestureExperimentMetric> metrics)
    {
        var score = metrics.First(metric => metric.Metric == "gesture_score").Value;
        return "{" +
            $"\"round_id\":\"{Json(roundId)}\"," +
            $"\"candidate_id\":\"{Json(candidate.Id)}\"," +
            $"\"target_id\":\"{Json(target.Id)}\"," +
            $"\"ipa\":\"{Json(target.Ipa)}\"," +
            $"\"descriptor\":\"{Json(target.Descriptor)}\"," +
            $"\"variant_id\":\"{Json(variant.Id)}\"," +
            $"\"gesture_score\":{Format(score)}," +
            $"\"script\":\"{Json(candidate.ScriptPath)}\"," +
            $"\"primitive_timeline\":\"{Json(candidate.TimelinePath)}\"" +
            "}";
    }

    private static string MetricSummaryCsv(IEnumerable<IpaGestureMetricSummary> summaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("target_id,metric,mean,min,max,spread,best_candidate_id,worst_candidate_id");
        foreach (var summary in summaries)
        {
            builder.Append(EscapeCsv(summary.TargetId));
            builder.Append(',');
            builder.Append(EscapeCsv(summary.Metric));
            builder.Append(',');
            builder.Append(Format(summary.Mean));
            builder.Append(',');
            builder.Append(Format(summary.Minimum));
            builder.Append(',');
            builder.Append(Format(summary.Maximum));
            builder.Append(',');
            builder.Append(Format(summary.Spread));
            builder.Append(',');
            builder.Append(EscapeCsv(summary.BestCandidateId));
            builder.Append(',');
            builder.AppendLine(EscapeCsv(summary.WorstCandidateId));
        }

        return builder.ToString();
    }

    private static string CandidateClusterCsv(IEnumerable<IpaGestureCandidateCluster> clusters)
    {
        var builder = new StringBuilder();
        builder.AppendLine("target_id,cluster,mean_gesture_score,candidate_count,candidate_ids");
        foreach (var cluster in clusters)
        {
            builder.Append(EscapeCsv(cluster.TargetId));
            builder.Append(',');
            builder.Append(EscapeCsv(cluster.Cluster));
            builder.Append(',');
            builder.Append(Format(cluster.MeanGestureScore));
            builder.Append(',');
            builder.Append(cluster.CandidateIds.Count);
            builder.Append(',');
            builder.AppendLine(EscapeCsv(string.Join('|', cluster.CandidateIds)));
        }

        return builder.ToString();
    }

    private static string ScienceBrief(
        IpaGestureExperimentResult result,
        IReadOnlyList<IpaGestureMetricSummary> summaries,
        IReadOnlyList<IpaGestureCandidateCluster> clusters)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# IPA Gesture Experiment Science Brief: {result.RoundId}");
        builder.AppendLine();
        builder.AppendLine("Authority: this brief summarizes frozen gesture-layer evidence only. It does not accept or reject clean vocal audio, full spectrogram parity, optimizer checkpoints, or distributed worker behavior.");
        builder.AppendLine();
        builder.AppendLine("## Score Surface");
        foreach (var summary in summaries.Where(summary => summary.Metric == "gesture_score"))
        {
            builder.Append("- ");
            builder.Append(summary.TargetId);
            builder.Append(": mean ");
            builder.Append(Format(summary.Mean));
            builder.Append(", spread ");
            builder.Append(Format(summary.Spread));
            builder.Append(", best ");
            builder.Append(summary.BestCandidateId);
            builder.Append(", worst ");
            builder.AppendLine(summary.WorstCandidateId);
        }

        builder.AppendLine();
        builder.AppendLine("## Candidate Clusters");
        foreach (var cluster in clusters)
        {
            builder.Append("- ");
            builder.Append(cluster.TargetId);
            builder.Append(' ');
            builder.Append(cluster.Cluster);
            builder.Append(": ");
            builder.Append(cluster.CandidateIds.Count);
            builder.Append(" candidates, mean ");
            builder.Append(Format(cluster.MeanGestureScore));
            builder.Append(" [");
            builder.Append(string.Join(", ", cluster.CandidateIds));
            builder.AppendLine("]");
        }

        builder.AppendLine();
        builder.AppendLine("## Next Hypothesis Pressure");
        foreach (var weak in summaries
            .Where(summary => summary.Metric != "gesture_score" && summary.Mean < 0.85f)
            .OrderBy(summary => summary.Mean)
            .ThenBy(summary => summary.TargetId, StringComparer.OrdinalIgnoreCase)
            .Take(6))
        {
            builder.Append("- ");
            builder.Append(weak.TargetId);
            builder.Append(' ');
            builder.Append(weak.Metric);
            builder.Append(" mean ");
            builder.Append(Format(weak.Mean));
            builder.Append(": inspect ");
            builder.Append(weak.WorstCandidateId);
            builder.Append(" before tuning ");
            builder.AppendLine(weak.BestCandidateId);
        }

        return builder.ToString();
    }

    private static string ClusterName(float score) =>
        score >= 0.85f ? "strong" : score >= 0.60f ? "workable" : "weak";

    private static int ClusterRank(string cluster) => cluster switch
    {
        "strong" => 0,
        "workable" => 1,
        _ => 2
    };

    private static HashSet<string> DescriptorTokens(string descriptor) =>
        descriptor
            .ToLowerInvariant()
            .Replace('-', '_')
            .Split(['_', ',', '+', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string SafeDslName(string value) =>
        new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) || char.IsWhiteSpace(character) ? '-' : character).ToArray());
        return safe.Length == 0 ? "candidate" : safe;
    }

    private static string Format(float value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Yaml(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string Json(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string EscapeCsv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
}
