using System.IO.Pipes;
using AquaSynth.Dings;
using MessagePack;

namespace AquaSynthDingsHost;

public sealed class DingsPlaybackHost : IAsyncDisposable
{
    private readonly AquaDaemonClient daemon = new();
    private readonly DingAudioPlayer audio = new();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(DingsProtocol.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(cancellationToken);
            _ = HandleClientAsync(pipe, cancellationToken);
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            try
            {
                var command = await ReadAsync<DingsCommand>(pipe, cancellationToken);
                var response = await ExecuteAsync(command, cancellationToken);
                await WriteAsync(pipe, response, cancellationToken);
            }
            catch (Exception ex)
            {
                if (pipe.IsConnected) await WriteAsync(pipe, Response(false, "failed", ex.Message), CancellationToken.None);
            }
        }
    }

    private async Task<DingsResponse> ExecuteAsync(DingsCommand command, CancellationToken cancellationToken)
    {
        if (command.Version != DingsProtocol.Version) return Response(false, "incompatible", $"Protocol {command.Version} is not supported.");
        switch (command.Kind)
        {
            case DingsCommandKind.Ping:
            case DingsCommandKind.GetVolume:
                return Response(true, "ready");
            case DingsCommandKind.SetVolume:
                if (!float.IsFinite(command.Value) || command.Value is < 0 or > 1) return Response(false, "rejected", "Volume must be between 0 and 1.");
                audio.Volume = command.Value;
                return Response(true, "volume-set");
            case DingsCommandKind.SetMuted:
                audio.Muted = command.Value >= .5f;
                return Response(true, audio.Muted ? "muted" : "unmuted");
            case DingsCommandKind.StopAll:
                audio.StopAll();
                return Response(true, "stopped");
            case DingsCommandKind.Play:
                return await PlayAsync(command, cancellationToken);
            default:
                return Response(false, "rejected", "Unknown command.");
        }
    }

    private async Task<DingsResponse> PlayAsync(DingsCommand command, CancellationToken cancellationToken)
    {
        if (!DingCatalog.Events.TryGetValue(command.EventId, out var dingEvent)) return Response(false, "rejected", $"Unknown event '{command.EventId}'.");
        if (!DingCatalog.Instruments.TryGetValue(command.InstrumentId, out var instrument)) return Response(false, "rejected", $"Unknown instrument '{command.InstrumentId}'.");
        if (!float.IsFinite(command.Value) || command.Value is < 0 or > 1) return Response(false, "rejected", "Gain must be between 0 and 1.");
        var playbackId = $"ding-{Guid.NewGuid():N}";
        foreach (var note in dingEvent.Notes)
        {
            var frequency = instrument.RootFrequency * MathF.Pow(2, (float)note.Semitones / 12f);
            var wav = await daemon.RenderNoteAsync(instrument, frequency, command.Value * note.Gain, cancellationToken);
            audio.Play(wav, note.DelayMilliseconds);
        }
        return Response(true, "playing", playbackId: playbackId);
    }

    private DingsResponse Response(bool succeeded, string status, string message = "", string playbackId = "") =>
        new(DingsProtocol.Version, succeeded, status, message, playbackId, audio.Volume, audio.Muted, Environment.ProcessId);

    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BitConverter.ToInt32(header);
        if (length is <= 0 or > 1024 * 1024) throw new InvalidDataException("Invalid Dings frame length.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return MessagePackSerializer.Deserialize<T>(payload);
    }

    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var payload = MessagePackSerializer.Serialize(value);
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length), cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        audio.Dispose();
        await daemon.DisposeAsync();
    }
}
