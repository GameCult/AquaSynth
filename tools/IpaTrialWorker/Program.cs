using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    }
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

static float ScoreSort(IpaTrialResult result) =>
    result.Metrics.FirstOrDefault(metric => metric.Name == "log_mel_cosine")?.Value ?? float.NegativeInfinity;

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

static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web)
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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

static void PrintHelp()
{
    Console.WriteLine("""
        IPA trial worker

        Commands:
          seed  --artifact-root <dir> [--batch-id five-seed-trials] [--store <trial-results.cc>]
          score --patch-root <dir> --artifact-root <dir> [--batch-id round-001] [--store <trial-results.cc>] [--hypothesizer id]
          dump  --store <trial-results.cc> --output <report.md>
          index --store <trial-results.cc> [--output <report.md>] [--force true]
          search --store <trial-results.cc> --query <text> --output <report.md> [--limit 12] [--no-vector true] [--require-vector true] [--skip-index true]
          show  --store <trial-results.cc> --trial-id <id-or-candidate> --output <detail.md>

        Vector search defaults:
          --qdrant-url http://127.0.0.1:6333
          --ollama-url http://127.0.0.1:11434
          --embed-model qwen3-embedding:0.6b
          --collection aquasynth_ipa_trial_results

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
