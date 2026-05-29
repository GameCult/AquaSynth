using System.Globalization;
using System.Text;

using AquaSynth.Dsl;
using AquaSynth.Faust;

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
        case "dump":
            await DumpAsync(options);
            return 0;
        case "search":
            await SearchAsync(options);
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

static async Task DumpAsync(Dictionary<string, string> options)
{
    var store = Required(options, "store");
    var output = Required(options, "output");
    var results = await IpaTrialResultCultCacheStore.ReadResultsAsync(store);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, StoreReport(store, results), Encoding.UTF8);
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
    await File.WriteAllTextAsync(output, SearchReport(store, query, Rank(results, query, limit)), Encoding.UTF8);
    Console.WriteLine(output);
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

static IReadOnlyList<(IpaTrialResult Result, float Score, string[] MatchedTerms)> Rank(
    IReadOnlyList<IpaTrialResult> results,
    string query,
    int limit)
{
    var terms = ExpandTerms(Tokenize(query)).ToArray();
    return results
        .Select(result =>
        {
            var document = Tokenize(DocumentText(result)).ToArray();
            var termCounts = document.GroupBy(term => term, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var matched = terms.Where(term => termCounts.ContainsKey(term)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var lexical = matched.Sum(term => 1f + MathF.Log(1 + termCounts[term]));
            var metricBias = MetricBias(result, terms);
            var score = lexical + metricBias;
            return (result, score, matched);
        })
        .Where(item => item.score > 0)
        .OrderByDescending(item => item.score)
        .ThenByDescending(item => ScoreSort(item.result))
        .ThenBy(item => item.result.TrialId, StringComparer.Ordinal)
        .Take(limit)
        .Select(item => (item.result, item.score, item.matched))
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

static string DocumentText(IpaTrialResult result)
{
    var builder = new StringBuilder();
    builder.Append(result.TrialId).Append(' ');
    builder.Append(result.BatchId).Append(' ');
    builder.Append(result.TargetSetId).Append(' ');
    builder.AppendJoin(' ', result.Phonemes).Append(' ');
    builder.Append(result.ReferenceId).Append(' ');
    builder.Append(result.CandidateId).Append(' ');
    builder.Append(result.HypothesizerId).Append(' ');
    builder.Append(result.Hypothesis).Append(' ');
    builder.Append(result.EvaluatorId).Append(' ');
    builder.Append(result.EvaluationSummary).Append(' ');
    builder.Append(result.Verdict).Append(' ');
    builder.AppendJoin(' ', result.KnownLies).Append(' ');
    foreach (var metric in result.Metrics)
    {
        builder.Append(metric.Name).Append(' ');
    }

    foreach (var artifact in result.Artifacts)
    {
        builder.Append(artifact.Kind).Append(' ').Append(artifact.Uri).Append(' ');
    }

    return builder.ToString();
}

static string[] Tokenize(string text) =>
    text.ToLowerInvariant()
        .Split([' ', '\t', '\r', '\n', ',', '.', ':', ';', '/', '\\', '`', '"', '\'', '[', ']', '(', ')', '{', '}', '<', '>', '|', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(term => term.Length > 1 || "aeioubdklmnprstvzθŋ".Contains(term, StringComparison.Ordinal))
        .ToArray();

static string SearchReport(
    string store,
    string query,
    IReadOnlyList<(IpaTrialResult Result, float Score, string[] MatchedTerms)> ranked)
{
    var builder = new StringBuilder();
    builder.AppendLine("# IPA Trial Semantic Search");
    builder.AppendLine();
    builder.AppendLine($"store: `{store}`");
    builder.AppendLine($"query: `{query}`");
    builder.AppendLine($"matches: {ranked.Count}");
    builder.AppendLine();
    foreach (var (result, score, matched) in ranked)
    {
        builder.Append("- ");
        builder.Append(result.TrialId);
        builder.Append(" / ");
        builder.Append(result.CandidateId);
        builder.Append(" / ");
        builder.Append(result.Verdict);
        builder.Append(" / score=");
        builder.Append(score.ToString("0.###", CultureInfo.InvariantCulture));
        builder.Append(" / matched=");
        builder.AppendLine(string.Join('|', matched.Take(12)));
        builder.Append("  metrics: gesture=");
        builder.Append(Metric(result, "gesture_score"));
        builder.Append(", logMelCosine=");
        builder.Append(Metric(result, "log_mel_cosine"));
        builder.Append(", articulation=");
        builder.Append(Metric(result, "articulation_score"));
        builder.Append(", rmsRatio=");
        builder.AppendLine(Metric(result, "rms_ratio"));
        builder.Append("  hypothesis: ");
        builder.AppendLine(result.Hypothesis);
        builder.Append("  evaluation: ");
        builder.AppendLine(result.EvaluationSummary);
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

static float ScoreSort(IpaTrialResult result) =>
    result.Metrics.FirstOrDefault(metric => metric.Name == "log_mel_cosine")?.Value ?? float.NegativeInfinity;

static string Metric(IpaTrialResult result, string name) =>
    (result.Metrics.FirstOrDefault(metric => metric.Name == name)?.Value ?? 0)
    .ToString("0.######", CultureInfo.InvariantCulture);

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

static void PrintHelp()
{
    Console.WriteLine("""
        IPA trial worker

        Commands:
          seed  --artifact-root <dir> [--batch-id five-seed-trials] [--store <trial-results.cc>]
          score --patch-root <dir> --artifact-root <dir> [--batch-id round-001] [--store <trial-results.cc>] [--hypothesizer id]
          dump  --store <trial-results.cc> --output <report.md>
          search --store <trial-results.cc> --query <text> --output <report.md> [--limit 12]
          show  --store <trial-results.cc> --trial-id <id-or-candidate> --output <detail.md>

        Agent-authored patch files must be named <targetId>__candidate-name.aqua.
        Known seed target ids: a, i, u, e, o, m, n, ng, l, r, s, z, f, v, th, p, b, t, d, k, mix-a, mix-m, mix-s, mix-p, mix-u.
        """);
}
