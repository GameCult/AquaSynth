using AquaSynth.Faust;

namespace AquaSynth.Dsl.Tests;

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
    public void WindowsDeviceStemsAreRewritten(string id)
    {
        var name = StoreFileName.From(id);
        Assert.StartsWith("_", name);
        Assert.NotEqual(id.ToLowerInvariant(), name.ToLowerInvariant());
        Assert.Matches("-[0-9a-f]{16}$", name);
        var stem = name.Split('.')[0].TrimEnd(' ');
        Assert.DoesNotMatch("^(?i:con|prn|aux|nul|com[0-9]|lpt[0-9])$", stem);
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

    [Fact]
    public void BareDotDotBatchIdIsRefused() =>
        Assert.Throws<ArgumentException>(() => StoreFileName.Combine(Path.GetTempPath(), ".."));

    private static string UriPath(string uri) => new Uri(uri).LocalPath;
}
