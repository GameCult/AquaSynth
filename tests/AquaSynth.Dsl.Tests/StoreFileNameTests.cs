using AquaSynth.Faust;

namespace AquaSynth.Dsl.Tests;

[Collection("NativeFaust")]
public sealed class StoreFileNameTests
{
    [Fact]
    public void DistinctIdsGetDistinctNames()
    {
        var ids = new[] { "a/b", "a b", "a:b", "a-b", "a_b", "a\\b", "a?b" };
        var names = ids.Select(StoreFileName.From).ToArray();
        Assert.Equal(ids.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("dings.warm-bell")]
    [InlineData("demo-stream-block")]
    [InlineData("a_b.c-1")]
    [InlineData("conx")]
    [InlineData("com10")]
    public void CleanIdsPassThroughUnchanged(string id) => Assert.Equal(id, StoreFileName.From(id));

    [Fact]
    public void RewrittenIdOutputAsAnIdDoesNotCollideWithTheOriginal()
    {
        var rewritten = StoreFileName.From("a b");
        Assert.NotEqual(rewritten, StoreFileName.From(rewritten));
        Assert.NotEqual(StoreFileName.From("a_b"), rewritten);
    }

    [Fact]
    public void IdsDifferingOnlyByCaseGetDistinctNames()
    {
        var names = new[] { "A", "a", "Ab", "aB", "ab" }.Select(StoreFileName.From).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("Aux.txt")]
    [InlineData("com1")]
    [InlineData("COM9.f32")]
    [InlineData("lpt0")]
    [InlineData("prn.cc")]
    [InlineData("nul.x")]
    [InlineData("LPT1.x")]
    [InlineData("CON.tar.gz")]
    public void WindowsDeviceStemsAreRewritten(string id)
    {
        var name = StoreFileName.From(id);
        Assert.StartsWith("_", name);
        Assert.NotEqual(id.ToLowerInvariant(), name.ToLowerInvariant());
        Assert.Matches("-[0-9a-f]{16}$", name);
        var stem = name.Split('.')[0].TrimEnd(' ');
        Assert.DoesNotMatch("^(?i:con|prn|aux|nul|com[0-9]|lpt[0-9])$", stem);
    }

    [Theory]
    [InlineData("nul ")]
    [InlineData("COM\u00b9")]
    [InlineData("COM1 .x")]
    [InlineData("con\u00e9.x")]
    [InlineData("NUL.")]
    public void DeviceLookalikesNeverLeaveAReservedStem(string id)
    {
        var name = StoreFileName.From(id);
        Assert.Matches("^[A-Za-z0-9._-]+$", name);
        Assert.DoesNotMatch("^(?i:con|prn|aux|nul|com[0-9]|lpt[0-9])$", name.Split('.')[0].TrimEnd(' '));
        Assert.Matches("-[0-9a-f]{16}$", name);
    }

    [Fact]
    public void OverlongIdsAreBoundedAndStayDistinct()
    {
        var a = StoreFileName.From(new string('x', 400));
        var b = StoreFileName.From(new string('x', 399) + "y");
        Assert.True(a.Length + ".cc".Length <= 255);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RewrittenIdsCarryHashOfTheOriginal()
    {
        // sha256("a b") begins c2a5..., checked independently of the implementation.
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("a b"u8.ToArray()))[..16].ToLowerInvariant();
        Assert.Equal($"a_b-{expected}", StoreFileName.From("a b"));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    public void DotsAndEmptyAreRefused(string id) => Assert.Throws<ArgumentException>(() => StoreFileName.From(id));

    [Theory]
    [InlineData("../..")]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\x")]
    [InlineData("/abs/path")]
    [InlineData("C:\\abs")]
    [InlineData("a.")]
    [InlineData("a/..")]
    public void NoIdCanMakePathCombineEscapeTheRoot(string id)
    {
        var root = Path.Combine(Path.GetTempPath(), "store-root-probe", "store");
        var name = StoreFileName.From(id);
        Assert.Equal(name, Path.GetFileName(name));
        Assert.NotEqual(".", name);
        Assert.NotEqual("..", name);
        var full = Path.GetFullPath(Path.Combine(root, name));
        Assert.Equal(Path.GetFullPath(root), Path.GetDirectoryName(full));
    }

    [Fact]
    public async Task DaemonSampleFilesForDotDotCommandIdStayInsideStore()
    {
        var parent = Path.Combine(Path.GetTempPath(), "aquasynth-sfn-" + Guid.NewGuid().ToString("N"));
        var storeRoot = Path.Combine(parent, "store");
        using var service = new AquaSynthDaemonService(new AquaSynthDaemonOptions(storeRoot));

        // A patch that compiles reaches the samples/ write; without native Faust only the compile-failed branch runs.
        var result = await service.SampleAsync(new AquaSynthInstrumentSampleCommand(
            "..", "test.sine", "test_sine",
            "voice wave=sine freq=440 gain=.2 attack=.001 sustain=.05 decay=.05", DurationSeconds: 0.1f));

        Assert.Empty(Directory.GetFiles(parent));
        Assert.Empty(Directory.GetDirectories(parent).Where(d => Path.GetFileName(d) != "store"));
        if (string.Equals(result.RenderReceipt.Status, "succeeded", StringComparison.Ordinal))
        {
            Assert.Equal(storeRoot, Path.GetDirectoryName(Path.GetDirectoryName(UriPath(result.RenderReceipt.Float32Uri))));
            Assert.Equal(storeRoot, Path.GetDirectoryName(Path.GetDirectoryName(UriPath(result.RenderReceipt.WavUri))));
        }
    }

    [Fact]
    public void IpaRoundCandidatesWithCollidingSanitisedIdsKeepDistinctFilesAndStayInRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aquasynth-ipa-sfn-{Guid.NewGuid():N}");
        var result = IpaGestureExperiment.WriteRound(
            root,
            "round-sfn",
            [new IpaGestureExperimentTarget("a/b", "p", "voiceless_bilabial_plosive", .1f), new IpaGestureExperimentTarget("a-b", "m", "voiced_bilabial_nasal", .1f)],
            [new IpaGestureExperimentVariant("base")]);

        Assert.Equal(2, result.Candidates.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(result.Candidates, c => Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(c.ScriptPath)));
        Assert.Throws<ArgumentException>(() => IpaGestureExperiment.WriteRound(root, "..", [new IpaGestureExperimentTarget("a", "p", "voiceless_bilabial_plosive")], [new IpaGestureExperimentVariant("base")]));
    }

    [Fact]
    public void IpaRoundJoinedTargetVariantIdsThatConcatenateEquallyKeepDistinctFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aquasynth-ipa-join-{Guid.NewGuid():N}");
        var result = IpaGestureExperiment.WriteRound(
            root,
            "round-join",
            [new IpaGestureExperimentTarget("a", "p", "voiceless_bilabial_plosive", .1f), new IpaGestureExperimentTarget("a-b", "m", "voiced_bilabial_nasal", .1f)],
            [new IpaGestureExperimentVariant("b-c"), new IpaGestureExperimentVariant("c")]);

        Assert.Equal(4, result.Candidates.Count);
        Assert.Equal(4, result.Candidates.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(4, result.Candidates.Select(c => c.ScriptPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void IpaRoundPairsWithSeparatorCharactersInIdsStayDistinct()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aquasynth-ipa-pair-{Guid.NewGuid():N}");
        string[] targets = ["a", "a/b", "a-b", "a_b", "A", "a b"];
        string[] variants = ["b-c", "c", "b/c", "b_c", "B-C", "b c"];
        var result = IpaGestureExperiment.WriteRound(
            root,
            "round-pairs",
            targets.Select(t => new IpaGestureExperimentTarget(t, "p", "voiceless_bilabial_plosive", .1f)).ToArray(),
            variants.Select(v => new IpaGestureExperimentVariant(v)).ToArray());

        Assert.Equal(36, result.Candidates.Count);
        Assert.Equal(36, result.Candidates.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(36, result.Candidates.Select(c => c.ScriptPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("..\\x")]
    [InlineData("/etc/x")]
    [InlineData("C:\\x")]
    public void BatchIdsFromTheCommandLineCannotLeaveTheArtifactRoot(string batchId)
    {
        var root = Path.Combine(Path.GetTempPath(), "artifact-root-probe", "artifacts");
        var full = Path.GetFullPath(StoreFileName.Combine(root, batchId));
        Assert.Equal(Path.GetFullPath(root), Path.GetDirectoryName(full));
    }

    [Theory]
    [InlineData("\\\\host\\share\\x")]
    [InlineData("C:x")]
    [InlineData("a:b")]
    [InlineData("x.")]
    [InlineData("x ")]
    [InlineData("x. ")]
    public void WindowsAbsoluteAndTrailingDotOrSpaceIdsStayContainedAndPlain(string id)
    {
        var root = Path.Combine(Path.GetTempPath(), "artifact-root-probe", "artifacts");
        var name = StoreFileName.From(id);
        Assert.Matches("^[A-Za-z0-9._-]+$", name);
        Assert.False(name.EndsWith('.') || name.EndsWith(' '));
        Assert.Equal(Path.GetFullPath(root), Path.GetDirectoryName(Path.GetFullPath(StoreFileName.Combine(root, id))));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("name:with:colons")]
    public void LoweredDspFileNameStaysInTheOutputDirectory(string name)
    {
        var outDir = Path.Combine(Path.GetTempPath(), "aqualower-probe", "out");
        var file = StoreFileName.From(name) + ".dsp";
        Assert.Equal(Path.GetFullPath(outDir), Path.GetDirectoryName(Path.GetFullPath(Path.Combine(outDir, file))));
    }

    [Fact]
    public void BareDotDotBatchIdIsRefused() =>
        Assert.Throws<ArgumentException>(() => StoreFileName.Combine(Path.GetTempPath(), ".."));

    private static string UriPath(string uri) => new Uri(uri).LocalPath;
}
