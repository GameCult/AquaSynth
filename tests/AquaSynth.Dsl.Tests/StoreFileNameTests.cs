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
    [InlineData("A_b.c-1")]
    public void CleanIdsPassThroughUnchanged(string id) => Assert.Equal(id, StoreFileName.From(id));

    [Fact]
    public void RewrittenIdsCarryHashOfTheOriginal()
    {
        // sha256("a b") begins c2a5..., checked independently of the implementation.
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("a b"u8.ToArray()))[..8].ToLowerInvariant();
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
    public async Task DaemonReceiptForTraversalCommandIdStaysInsideStore()
    {
        var parent = Path.Combine(Path.GetTempPath(), "aquasynth-sfn-" + Guid.NewGuid().ToString("N"));
        var storeRoot = Path.Combine(parent, "store");
        using var service = new AquaSynthDaemonService(new AquaSynthDaemonOptions(storeRoot));

        await service.SampleAsync(new AquaSynthInstrumentSampleCommand(
            "../../escape", "test.invalid", "test_invalid", "this is not aqua syntax", DurationSeconds: 0.1f));

        Assert.Empty(Directory.GetFiles(parent));
        Assert.Empty(Directory.GetDirectories(parent).Where(d => Path.GetFileName(d) != "store"));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(storeRoot, "compile")));
    }
}
