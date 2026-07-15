using AquaSynth.Dings;
using AquaSynthDingsMcp;

namespace AquaSynth.Dsl.Tests;

public sealed class DingsBridgeTests
{
    [Fact]
    public async Task SeparateBridgesObserveOneHostVolumeAuthority()
    {
        var host = new FakeHostClient();
        var first = new DingService(host);
        var second = new DingService(host);

        await first.SetVolumeAsync(.27f, CancellationToken.None);
        var observed = await second.GetVolumeAsync(CancellationToken.None);

        Assert.Equal(.27f, observed.Volume);
        Assert.All(host.Commands, command => Assert.Equal(DingsProtocol.Version, command.Version));
    }

    [Fact]
    public async Task PerPlayGainDoesNotMutateMasterVolume()
    {
        var host = new FakeHostClient { Volume = .4f };
        var service = new DingService(host);

        await service.PlayAsync("task.complete", "warm-bell", .7f, CancellationToken.None);

        Assert.Equal(.4f, host.Volume);
        Assert.Equal(.7f, host.Commands.Single().Value);
    }

    [Fact]
    public void BridgeProjectContainsNoAudioOutputOwner()
    {
        var root = RepositoryRoot();
        var bridge = Path.Combine(root, "tools", "AquaSynthDingsMcp");
        var source = string.Join('\n', Directory.EnumerateFiles(bridge, "*.cs").Select(File.ReadAllText));
        Assert.DoesNotContain("WaveOutEvent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WasapiOut", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MixingSampleProvider", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AquaSynth.sln"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FakeHostClient : IDingsHostClient
    {
        public List<DingsCommand> Commands { get; } = [];
        public float Volume { get; set; } = 1;
        public bool Muted { get; set; }

        public Task<DingsResponse> SendAsync(DingsCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (command.Kind == DingsCommandKind.SetVolume) Volume = command.Value;
            if (command.Kind == DingsCommandKind.SetMuted) Muted = command.Value >= .5f;
            return Task.FromResult(new DingsResponse(DingsProtocol.Version, true, "ok", Volume: Volume, Muted: Muted, HostProcessId: 42));
        }
    }
}
