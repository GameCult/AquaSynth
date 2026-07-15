using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AquaSynthDingsHost;

public sealed class DingAudioPlayer : IDisposable
{
    private static readonly Guid SessionGroup = new("8414a5ca-a680-4b7c-9f17-cdb954177f9d");
    private readonly object sync = new();
    private readonly MixingSampleProvider mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)) { ReadFully = true };
    private readonly MMDevice device;
    private readonly WasapiOut output;
    private readonly List<AudioFileReader> readers = [];
    private AudioSessionControl? session;

    public DingAudioPlayer()
    {
        using var enumerator = new MMDeviceEnumerator();
        device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        output = new WasapiOut(device, AudioClientShareMode.Shared, true, 80);
        output.Init(mixer);
        output.Play();
        session = FindSession();
        if (session is not null)
        {
            session.DisplayName = "AquaSynth Dings";
            session.SetGroupingParam(SessionGroup, Guid.Empty);
        }
    }

    public float Volume { get => RequireSession().SimpleAudioVolume.Volume; set => RequireSession().SimpleAudioVolume.Volume = value; }
    public bool Muted { get => RequireSession().SimpleAudioVolume.Mute; set => RequireSession().SimpleAudioVolume.Mute = value; }

    public void Play(string wavPath, int delayMilliseconds)
    {
        var reader = new AudioFileReader(wavPath);
        var input = new OffsetSampleProvider(reader.ToStereo()) { DelayBy = TimeSpan.FromMilliseconds(delayMilliseconds), Take = reader.TotalTime + TimeSpan.FromMilliseconds(delayMilliseconds) };
        lock (sync) { readers.Add(reader); mixer.AddMixerInput(input); }
        _ = Task.Delay(input.Take + TimeSpan.FromMilliseconds(150)).ContinueWith(_ =>
        {
            lock (sync) { mixer.RemoveMixerInput(input); readers.Remove(reader); reader.Dispose(); }
        }, TaskScheduler.Default);
    }

    public void StopAll()
    {
        lock (sync) { mixer.RemoveAllMixerInputs(); foreach (var reader in readers) reader.Dispose(); readers.Clear(); }
    }

    private AudioSessionControl RequireSession() => session ??= FindSession() ?? throw new InvalidOperationException("AquaSynth Dings WASAPI session is unavailable.");

    private AudioSessionControl? FindSession()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            device.AudioSessionManager.RefreshSessions();
            var sessions = device.AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                var candidate = sessions[i];
                if (candidate.GetProcessID == (uint)Environment.ProcessId) return candidate;
            }
            Thread.Sleep(25);
        }
        return null;
    }

    public void Dispose()
    {
        StopAll();
        session?.Dispose();
        output.Stop();
        output.Dispose();
        device.Dispose();
    }
}
