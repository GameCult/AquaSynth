using GameCult.Caching;
using GameCult.Caching.MessagePack;
using MessagePack;

namespace AquaSynth.Dsl;

[CultDocument("aquasynth.ipa_trial_result", "aquasynth.ipa_trial_result.v1")]
[MessagePackObject]
public sealed record IpaTrialResult(
    [property: Key(0)]
    [property: CultName]
    string TrialId,
    [property: Key(1)] string BatchId,
    [property: Key(2)] string CreatedAtUtc,
    [property: Key(3)] string TargetSetId,
    [property: Key(4)] string[] Phonemes,
    [property: Key(5)] string ReferenceId,
    [property: Key(6)] string CandidateId,
    [property: Key(7)] string HypothesizerId,
    [property: Key(8)] string Hypothesis,
    [property: Key(9)] string CandidatePatchUri,
    [property: Key(10)] string ReferenceArtifactUri,
    [property: Key(11)] string CandidateArtifactUri,
    [property: Key(12)] string PrimitiveTimelineUri,
    [property: Key(13)] SpeechScoreMetric[] Metrics,
    [property: Key(14)] SpeechRenderArtifact[] Artifacts,
    [property: Key(15)] string EvaluatorId,
    [property: Key(16)] string EvaluationSummary,
    [property: Key(17)] string Verdict,
    [property: Key(18)] string[] KnownLies,
    [property: Key(19)] SpeechTimingReceipt[] TimingReceipts,
    [property: Key(20)] PrimitiveTimelineFact[] TimelineFacts);

[MessagePackObject]
public sealed record PrimitiveTimelineFact(
    [property: Key(0)] string Name,
    [property: Key(1)] string Primitive,
    [property: Key(2)] string Signal,
    [property: Key(3)] float Value,
    [property: Key(4)] string Unit,
    [property: Key(5)] int BlockStart,
    [property: Key(6)] int BlockEnd,
    [property: Key(7)] string Summary);

[CultDocument("aquasynth.song_challenge_evidence", "aquasynth.song_challenge_evidence.v1")]
[MessagePackObject]
public sealed record SongChallengeEvidenceDocument(
    [property: Key(0)]
    [property: CultName]
    string EvidenceId,
    [property: Key(1)] string ChallengeId,
    [property: Key(2)] string Kind,
    [property: Key(3)] string ContentType,
    [property: Key(4)] string Content,
    [property: Key(5)] string ContentHash,
    [property: Key(6)] string SourcePath,
    [property: Key(7)] string CreatedAtUtc);

[CultDocument("aquasynth.song_trial_distillation", "aquasynth.song_trial_distillation.v1")]
[MessagePackObject]
public sealed record SongTrialDistillationDocument(
    [property: Key(0)]
    [property: CultName]
    string DistillationId,
    [property: Key(1)] string SourceStore,
    [property: Key(2)] string CreatedAtUtc,
    [property: Key(3)] int InputTrialCount,
    [property: Key(4)] int KeptTrialCount,
    [property: Key(5)] string Summary,
    [property: Key(6)] string[] ReusableSceneRoles,
    [property: Key(7)] string[] TransferRules,
    [property: Key(8)] string[] FailurePatterns,
    [property: Key(9)] string[] KeptTrialIds,
    [property: Key(10)] string[] DroppedTrialIds,
    [property: Key(11)] SpeechScoreMetric[] AggregateMetrics);

public static class IpaTrialResultCultCacheStore
{
    public static Task UpsertResultsAsync(string filePath, IEnumerable<IpaTrialResult> results) =>
        UpsertAsync(filePath, results, result => $"ipa-trial-result:{result.TrialId}");

    public static Task UpsertSongChallengeEvidenceAsync(string filePath, IEnumerable<SongChallengeEvidenceDocument> documents) =>
        UpsertAsync(filePath, documents, document => $"song-challenge-evidence:{document.EvidenceId}");

    public static Task UpsertSongTrialDistillationsAsync(string filePath, IEnumerable<SongTrialDistillationDocument> documents) =>
        UpsertAsync(filePath, documents, document => $"song-trial-distillation:{document.DistillationId}");

    public static async Task<IReadOnlyList<IpaTrialResult>> ReadResultsAsync(string filePath)
    {
        if (!StoreExists(filePath))
        {
            return [];
        }

        using var cache = await OpenTrialStoreAsync(filePath, pullOnOpen: true).ConfigureAwait(false);
        return cache.GetAll<IpaTrialResult>()
            .OrderBy(result => result.TrialId, StringComparer.Ordinal)
            .ToArray();
    }

    public static async Task<IReadOnlyList<SongChallengeEvidenceDocument>> ReadSongChallengeEvidenceAsync(string filePath)
    {
        if (!StoreExists(filePath))
        {
            return [];
        }

        using var cache = await OpenTrialStoreAsync(filePath, pullOnOpen: true).ConfigureAwait(false);
        return cache.GetAll<SongChallengeEvidenceDocument>()
            .OrderBy(document => document.EvidenceId, StringComparer.Ordinal)
            .ToArray();
    }

    public static async Task<IReadOnlyList<SongTrialDistillationDocument>> ReadSongTrialDistillationsAsync(string filePath)
    {
        if (!StoreExists(filePath))
        {
            return [];
        }

        using var cache = await OpenTrialStoreAsync(filePath, pullOnOpen: true).ConfigureAwait(false);
        return cache.GetAll<SongTrialDistillationDocument>()
            .OrderBy(document => document.DistillationId, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task UpsertAsync<T>(
        string filePath,
        IEnumerable<T> documents,
        Func<T, string> key)
        where T : class
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var cache = await OpenTrialStoreAsync(filePath, pullOnOpen: StoreExists(filePath)).ConfigureAwait(false);
        foreach (var document in documents)
        {
            await cache.UpsertAsync(document, new CultRecordHandle<T>(new CultRecordKey(key(document)))).ConfigureAwait(false);
        }

        await cache.FlushAsync().ConfigureAwait(false);
    }

    private static Task<CultCache> OpenTrialStoreAsync(string filePath, bool pullOnOpen) =>
        CultCacheMessagePack.OpenAsync(
            filePath,
            new CultCacheOpenOptions
            {
                PullOnOpen = pullOnOpen,
                UseDirectoryStore = true
            });

    private static bool StoreExists(string filePath) =>
        File.Exists(filePath) ||
        Directory.Exists(DirectoryMessagePackBackingStore.DefaultRecordDirectoryPath(filePath));
}
