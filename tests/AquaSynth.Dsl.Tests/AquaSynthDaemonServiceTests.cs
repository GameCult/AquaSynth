using AquaSynth.Faust;

namespace AquaSynth.Dsl.Tests;

public sealed class AquaSynthDaemonServiceTests
{
    [Fact]
    public async Task DaemonSampleCommandWritesFailedReceiptsForInvalidPatch()
    {
        var storeRoot = NewStoreRoot();
        using var service = new AquaSynthDaemonService(new AquaSynthDaemonOptions(storeRoot));

        var result = await service.SampleAsync(new AquaSynthInstrumentSampleCommand(
            "invalid-sample",
            "test.invalid",
            "test_invalid",
            "this is not aqua syntax",
            DurationSeconds: 0.1f));

        Assert.Equal("failed", result.CompileReceipt.Status);
        Assert.Equal("failed", result.RenderReceipt.Status);
        Assert.True(File.Exists(Path.Combine(storeRoot, "compile", "invalid-sample-compile.cc")));
        Assert.True(File.Exists(Path.Combine(storeRoot, "renders", "invalid-sample-render.cc")));
        Assert.True(File.Exists(Path.Combine(storeRoot, "operator", "operator-state.cc")));
    }

    [Fact]
    public async Task DaemonSampleCommandReturnsSamplesWhenNativeFaustIsAvailable()
    {
        var storeRoot = NewStoreRoot();
        using var service = new AquaSynthDaemonService(new AquaSynthDaemonOptions(storeRoot));

        var result = await service.SampleAsync(new AquaSynthInstrumentSampleCommand(
            "sine-sample",
            "test.sine",
            "test_sine",
            "voice wave=sine freq=440 gain=.2 attack=.001 sustain=.05 decay=.05",
            DurationSeconds: 0.1f));

        if (!string.Equals(result.RenderReceipt.Status, "succeeded", StringComparison.Ordinal))
        {
            Assert.Contains("Faust", result.CompileReceipt.FailureMessage, StringComparison.OrdinalIgnoreCase);
            return;
        }

        Assert.Equal("succeeded", result.CompileReceipt.Status);
        Assert.Equal(44100, result.RenderReceipt.SampleRate);
        Assert.Equal(4410, result.RenderReceipt.SampleCount);
        Assert.InRange(result.RenderReceipt.Peak, 0.001f, 1.0f);
        Assert.True(File.Exists(UriToPath(result.RenderReceipt.Float32Uri)));
        Assert.True(File.Exists(UriToPath(result.RenderReceipt.WavUri)));
        Assert.Equal(result.RenderReceipt.SampleCount * sizeof(float), new FileInfo(UriToPath(result.RenderReceipt.Float32Uri)).Length);
        Assert.True(File.Exists(Path.Combine(storeRoot, "sessions", $"{result.CompileReceipt.SessionId}.cc")));
    }

    private static string NewStoreRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "aquasynth-daemon-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string UriToPath(string uri) => new Uri(uri).LocalPath;
}
