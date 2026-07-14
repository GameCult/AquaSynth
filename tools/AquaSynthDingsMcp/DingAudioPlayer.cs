using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AquaSynthDingsMcp;

public sealed class DingAudioPlayer : IDisposable
{
    private readonly object sync = new();
    private readonly MixingSampleProvider mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)) { ReadFully = true };
    private readonly WaveOutEvent output;
    private readonly List<IDisposable> readers = [];

    public DingAudioPlayer()
    {
        output = new WaveOutEvent { DesiredLatency = 80 };
        output.Init(mixer);
        output.Play();
    }

    public void Play(string wavPath, int delayMilliseconds, float gain)
    {
        var reader = new AudioFileReader(wavPath) { Volume = gain };
        var cleanup = new OffsetSampleProvider(reader.ToStereo())
        {
            DelayBy = TimeSpan.FromMilliseconds(delayMilliseconds),
            Take = reader.TotalTime + TimeSpan.FromMilliseconds(delayMilliseconds)
        };
        lock (sync) readers.Add(reader);
        mixer.AddMixerInput(cleanup);
        _ = Task.Delay(cleanup.Take + TimeSpan.FromMilliseconds(100)).ContinueWith(_ =>
        {
            lock (sync) { readers.Remove(reader); reader.Dispose(); }
        }, TaskScheduler.Default);
    }

    public void StopAll() => mixer.RemoveAllMixerInputs();

    public void Dispose()
    {
        output.Stop();
        output.Dispose();
        lock (sync) { foreach (var reader in readers) reader.Dispose(); readers.Clear(); }
    }
}
