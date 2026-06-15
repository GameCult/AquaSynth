using AquaSynth.Faust;
using GameCult.Caching;

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

    [Fact]
    public async Task DaemonAutomationStreamPublishesCultMeshFrameReceiptsWhenNativeFaustIsAvailable()
    {
        var storeRoot = NewStoreRoot();
        using var service = new AquaSynthDaemonService(new AquaSynthDaemonOptions(storeRoot));

        var receipt = await service.StreamAutomationAsync(new AquaSynthAutomationStreamCommand(
            "stream-sine",
            "test.stream.sine",
            "test_stream_sine",
            "param path=/macro/gain default=.2 min=0 max=1 step=.001; voice wave=sine freq=440 gain=@/macro/gain attack=.001 sustain=.1 decay=.05",
            BlockSize: 128,
            BlockCount: 3,
            ControlFrames:
            [
                new AquaSynthAutomationControlFrame(1, new Dictionary<string, float> { ["/macro/gain"] = .05f }),
                new AquaSynthAutomationControlFrame(2, new Dictionary<string, float> { ["/macro/gain"] = .3f })
            ]));

        if (!string.Equals(receipt.Status, "succeeded", StringComparison.Ordinal))
        {
            Assert.Contains("Faust", receipt.FailureMessage, StringComparison.OrdinalIgnoreCase);
            return;
        }

        Assert.Equal("aquasynth.instrument", receipt.VerseId);
        Assert.Equal(2, receipt.Streams.Length);
        Assert.Contains(receipt.Streams, stream => stream.StreamId == receipt.AudioStreamId && stream.Kind == "Audio");
        Assert.Contains(receipt.Streams, stream => stream.StreamId == receipt.ControlStreamId && stream.Kind == "Tensor");
        Assert.Equal(3, receipt.Packets.Length);
        Assert.All(receipt.Packets, packet =>
        {
            Assert.Equal(128, packet.SampleCount);
            Assert.True(File.Exists(UriToPath(packet.Float32Uri)));
            Assert.Equal(128 * sizeof(float), new FileInfo(UriToPath(packet.Float32Uri)).Length);
            Assert.StartsWith("file:///", packet.PageRef, StringComparison.Ordinal);
        });
        Assert.Equal(0, receipt.Packets[0].ControlCount);
        Assert.Equal(1, receipt.Packets[1].ControlCount);
        Assert.Equal(1, receipt.Packets[2].ControlCount);
        Assert.True(File.Exists(Path.Combine(storeRoot, "streams", "stream-sine-stream.cc")));
    }

    [Fact]
    public async Task CultNetDaemonConsumesCommandDocumentsAndPublishesReceiptDocuments()
    {
        var storeRoot = NewStoreRoot();
        await using var daemon = await AquaSynthCultNetDaemon.StartAsync(new AquaSynthCultNetDaemonOptions(storeRoot));

        var receipt = await daemon.SubmitSampleAsync(new AquaSynthInstrumentSampleCommand(
            "cultnet-invalid-sample",
            "test.cultnet.invalid",
            "test_cultnet_invalid",
            "this is not aqua syntax",
            DurationSeconds: 0.1f));

        Assert.Equal("failed", receipt.Status);
        Assert.Equal("cultnet-invalid-sample", receipt.CommandId);
        Assert.NotNull(await daemon.Database.GetAsync<AquaSynthInstrumentSampleCommand>(
            AquaSynthCultNetDaemon.CommandKey("cultnet-invalid-sample")));
        Assert.NotNull(await daemon.Database.GetAsync<AquaSynthPatchCompileReceipt>(
            AquaSynthCultNetDaemon.CompileReceiptKey("cultnet-invalid-sample")));
        Assert.NotNull(await daemon.Database.GetAsync<AquaSynthRenderSampleReceipt>(
            AquaSynthCultNetDaemon.RenderReceiptKey("cultnet-invalid-sample")));

        var provider = await daemon.Database.GetAsync<AquaSynthCultNetProviderState>(
            new CultRecordKey("global:aquasynth.cultnet_provider"));
        Assert.NotNull(provider);
        Assert.Contains(AquaSynthDaemonSchemas.InstrumentSampleCommand, provider.CommandSchemas);
        Assert.StartsWith(storeRoot, provider.CachePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.GetDirectoryName(provider.CachePath)));
    }

    private static string NewStoreRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "aquasynth-daemon-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string UriToPath(string uri) => new Uri(uri).LocalPath;
}
