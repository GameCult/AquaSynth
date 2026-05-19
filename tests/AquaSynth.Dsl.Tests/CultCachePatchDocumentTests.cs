using System.Text.Json;
using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

public sealed class CultCachePatchDocumentTests
{
    [Fact]
    public void CultCachePatchDocumentLoadsAsTypedSoundClaimBundle()
    {
        var root = RepositoryRoot();
        var documentPath = Path.Combine(root, "patches", "aquasynth-patch-cultcache.json");
        var document = JsonSerializer.Deserialize<CultCachePatchDocument>(
            File.ReadAllText(documentPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(document);
        Assert.Equal("cultcache.document.aquasynth.sound_claims", document!.Type);
        Assert.Equal("cultcache/aquasynth/sound-claims.v1", document.Schema);
        Assert.Equal(1, document.Version);
        Assert.Equal(LibraryEntryCount(root), document.PatchDatabase.Entries.Count);
        Assert.Contains(document.PatchClaims, claim => claim.Id == "dx7/public-domain-mc-mm-5-3/aquasynth" && claim.Tier == "parity");
        Assert.Contains(document.PatchClaims, claim => claim.Id == "zyn/project-pad-texture/aquasynth" && claim.Reference.SynthOrSource.Contains("ZynAddSubFX", StringComparison.Ordinal));
        Assert.Contains(document.SpeechClaims, claim => claim.Id == "speech/espeak-ng-tiny-workout" && claim.Target.AnatomyOrProfile.Contains("human", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(document.SpeechClaims, claim => claim.Id == "speech/compiled-faust-loss-surface" && claim.Tier == "proof");
    }

    [Fact]
    public void EverySeriousClaimNamesAReferenceAndReceiptSurface()
    {
        var document = JsonSerializer.Deserialize<CultCachePatchDocument>(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "patches", "aquasynth-patch-cultcache.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        foreach (var claim in document.PatchClaims)
        {
            Assert.False(string.IsNullOrWhiteSpace(claim.Reference.SynthOrSource), claim.Id);
            Assert.False(string.IsNullOrWhiteSpace(claim.Reference.FixtureOrArtifact), claim.Id);
            Assert.False(string.IsNullOrWhiteSpace(claim.Intent.PerceptualClaim), claim.Id);
            Assert.False(string.IsNullOrWhiteSpace(claim.Proof.LatestRenderArtifact), claim.Id);
            Assert.False(string.IsNullOrWhiteSpace(claim.Proof.LatestListeningReceipt), claim.Id);
            Assert.NotEmpty(claim.Proof.KnownLies);
        }

        foreach (var claim in document.SpeechClaims)
        {
            Assert.False(string.IsNullOrWhiteSpace(claim.UtteranceSource.PhonemeSource), claim.Id);
            Assert.False(string.IsNullOrWhiteSpace(claim.UtteranceSource.ReferenceRenderer), claim.Id);
            Assert.False(string.IsNullOrWhiteSpace(claim.Target.AnatomyOrProfile), claim.Id);
            Assert.False(string.IsNullOrWhiteSpace(claim.Proof.LatestRenderArtifact), claim.Id);
            Assert.False(string.IsNullOrWhiteSpace(claim.Proof.LatestListeningReceipt), claim.Id);
            Assert.NotEmpty(claim.Proof.KnownLies);
        }
    }

    [Fact]
    public void CultCachePatchClaimsCoverReferenceRebuildCatalog()
    {
        var document = JsonSerializer.Deserialize<CultCachePatchDocument>(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "patches", "aquasynth-patch-cultcache.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var claimIds = document.PatchClaims.Select(claim => claim.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var rebuild in ReferenceRebuildCatalog.All())
        {
            Assert.Contains(rebuild.Id, claimIds);
        }
    }

    private static int LibraryEntryCount(string root) =>
        File.ReadLines(Path.Combine(root, "patches", "library.yaml"))
            .Count(line => line.TrimStart().StartsWith("- path:", StringComparison.Ordinal));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AquaSynth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("could not find repository root");
    }
}
