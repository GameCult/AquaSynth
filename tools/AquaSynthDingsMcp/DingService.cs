namespace AquaSynthDingsMcp;

public sealed class DingService(AquaDaemonClient daemon, DingAudioPlayer player)
{
    public async Task<object> PlayAsync(string eventId, string instrumentId, float gain, CancellationToken cancellationToken)
    {
        if (!DingCatalog.Events.TryGetValue(eventId, out var dingEvent)) throw new ArgumentException($"Unknown event '{eventId}'.");
        if (!DingCatalog.Instruments.TryGetValue(instrumentId, out var instrument)) throw new ArgumentException($"Unknown instrument '{instrumentId}'.");
        if (gain is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(gain), "Gain must be between 0 and 1.");
        var playbackId = $"ding-{Guid.NewGuid():N}";
        foreach (var note in dingEvent.Notes)
        {
            var frequency = instrument.RootFrequency * MathF.Pow(2, (float)note.Semitones / 12f);
            var wav = await daemon.RenderNoteAsync(instrument, frequency, gain * note.Gain, cancellationToken);
            player.Play(wav, note.DelayMilliseconds, 1f);
        }
        return new { playbackId, eventId = dingEvent.Id, instrumentId = instrument.Id, status = "playing" };
    }

    public void StopAll() => player.StopAll();
}
