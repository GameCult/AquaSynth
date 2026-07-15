using MessagePack;

namespace AquaSynth.Dings;

public static class DingsProtocol
{
    public const int Version = 1;
    public const string PipeName = "GameCult.AquaSynthDings.Playback.v1";
    public const string MutexName = "Local\\GameCult.AquaSynthDings.Playback.v1";
}

public enum DingsCommandKind { Ping, Play, StopAll, GetVolume, SetVolume, SetMuted }

[MessagePackObject]
public sealed record DingsCommand(
    [property: Key(0)] int Version,
    [property: Key(1)] DingsCommandKind Kind,
    [property: Key(2)] string EventId = "",
    [property: Key(3)] string InstrumentId = "",
    [property: Key(4)] float Value = 0);

[MessagePackObject]
public sealed record DingsResponse(
    [property: Key(0)] int Version,
    [property: Key(1)] bool Succeeded,
    [property: Key(2)] string Status,
    [property: Key(3)] string Message = "",
    [property: Key(4)] string PlaybackId = "",
    [property: Key(5)] float Volume = 1,
    [property: Key(6)] bool Muted = false,
    [property: Key(7)] int HostProcessId = 0,
    [property: Key(8)] string SessionName = "AquaSynth Dings");
