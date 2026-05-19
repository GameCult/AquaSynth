using System.Text.Json.Serialization;

namespace AquaSynth.Dsl;

public sealed record CultCachePatchDocument(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("generated_from")] IReadOnlyList<string> GeneratedFrom,
    [property: JsonPropertyName("patch_database")] CultCachePatchDatabase PatchDatabase,
    [property: JsonPropertyName("patch_claims")] IReadOnlyList<CultCachePatchClaimCard> PatchClaims,
    [property: JsonPropertyName("speech_claims")] IReadOnlyList<CultCacheSpeechClaimCard> SpeechClaims);

public sealed record CultCachePatchDatabase(
    [property: JsonPropertyName("source_path")] string SourcePath,
    [property: JsonPropertyName("entries")] IReadOnlyList<CultCachePatchDatabaseEntry> Entries);

public sealed record CultCachePatchDatabaseEntry(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("family")] string Family,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("notes")] string Notes);

public sealed record CultCachePatchClaimCard(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("patch_path")] string? PatchPath,
    [property: JsonPropertyName("reference")] CultCacheReferenceTruth Reference,
    [property: JsonPropertyName("intent")] CultCacheIntentTruth Intent,
    [property: JsonPropertyName("tier")] string Tier,
    [property: JsonPropertyName("proof")] CultCacheProofTruth Proof);

public sealed record CultCacheSpeechClaimCard(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("utterance_source")] CultCacheUtteranceSourceTruth UtteranceSource,
    [property: JsonPropertyName("target")] CultCacheSpeechTargetTruth Target,
    [property: JsonPropertyName("intent")] CultCacheIntentTruth Intent,
    [property: JsonPropertyName("tier")] string Tier,
    [property: JsonPropertyName("proof")] CultCacheProofTruth Proof);

public sealed record CultCacheReferenceTruth(
    [property: JsonPropertyName("synth_or_source")] string SynthOrSource,
    [property: JsonPropertyName("fixture_or_artifact")] string FixtureOrArtifact,
    [property: JsonPropertyName("license_scope")] string LicenseScope);

public sealed record CultCacheUtteranceSourceTruth(
    [property: JsonPropertyName("text_or_ipa")] string TextOrIpa,
    [property: JsonPropertyName("phoneme_source")] string PhonemeSource,
    [property: JsonPropertyName("reference_renderer")] string ReferenceRenderer);

public sealed record CultCacheSpeechTargetTruth(
    [property: JsonPropertyName("anatomy_or_profile")] string AnatomyOrProfile,
    [property: JsonPropertyName("phonetic_constraints")] IReadOnlyList<string> PhoneticConstraints);

public sealed record CultCacheIntentTruth(
    [property: JsonPropertyName("perceptual_claim")] string PerceptualClaim,
    [property: JsonPropertyName("use_context")] string UseContext);

public sealed record CultCacheProofTruth(
    [property: JsonPropertyName("latest_render_artifact")] string LatestRenderArtifact,
    [property: JsonPropertyName("latest_listening_receipt")] string LatestListeningReceipt,
    [property: JsonPropertyName("metric_summary")] string MetricSummary,
    [property: JsonPropertyName("known_lies")] IReadOnlyList<string> KnownLies);
