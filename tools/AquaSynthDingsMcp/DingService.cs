using AquaSynth.Dings;

namespace AquaSynthDingsMcp;

public sealed class DingService(IDingsHostClient host)
{
    public async Task<object> PlayAsync(string eventId, string instrumentId, float gain, CancellationToken cancellationToken)
    {
        if (!DingCatalog.Events.TryGetValue(eventId, out var dingEvent)) throw new ArgumentException($"Unknown event '{eventId}'.");
        if (!DingCatalog.Instruments.TryGetValue(instrumentId, out var instrument)) throw new ArgumentException($"Unknown instrument '{instrumentId}'.");
        if (gain is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(gain), "Gain must be between 0 and 1.");
        var response = await host.SendAsync(new(DingsProtocol.Version, DingsCommandKind.Play, dingEvent.Id, instrument.Id, gain), cancellationToken);
        EnsureSuccess(response);
        return new { playbackId = response.PlaybackId, eventId = dingEvent.Id, instrumentId = instrument.Id, response.Status, response.HostProcessId };
    }

    public async Task<DingsResponse> StopAllAsync(CancellationToken cancellationToken) => EnsureSuccess(await host.SendAsync(new(DingsProtocol.Version, DingsCommandKind.StopAll), cancellationToken));
    public async Task<DingsResponse> GetVolumeAsync(CancellationToken cancellationToken) => EnsureSuccess(await host.SendAsync(new(DingsProtocol.Version, DingsCommandKind.GetVolume), cancellationToken));
    public async Task<DingsResponse> SetVolumeAsync(float volume, CancellationToken cancellationToken) => EnsureSuccess(await host.SendAsync(new(DingsProtocol.Version, DingsCommandKind.SetVolume, Value: volume), cancellationToken));
    public async Task<DingsResponse> SetMutedAsync(bool muted, CancellationToken cancellationToken) => EnsureSuccess(await host.SendAsync(new(DingsProtocol.Version, DingsCommandKind.SetMuted, Value: muted ? 1 : 0), cancellationToken));

    private static DingsResponse EnsureSuccess(DingsResponse response)
    {
        if (!response.Succeeded) throw new InvalidOperationException(response.Message);
        return response;
    }
}
