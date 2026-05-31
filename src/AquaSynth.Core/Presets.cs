namespace AquaSynth.Dsl;

public static class Presets
{
    public static SynthPatch AquaSynthPluck()
    {
        var envelope = new Envelope(0.002f, 0, 0.7246377f, 0.43f);
        return new SynthPatch
        {
            Voices =
            [
                Simple(new Oscillator(Waveform.Sine, 440), envelope, 0.09f, 0.2484f),
                Simple(new Oscillator(Waveform.Triangle, 880), envelope, 0.09f, 0.069f),
                Simple(new Oscillator(Waveform.Sine, 1760), envelope, 0.09f, 0.02484f)
            ],
            Gain = 0.95f,
            SoftClip = true
        };
    }

    public static SynthPatch AquaSynthHeartbeat()
    {
        var envelope = new Envelope(0.004f, 0, 0.617284f, 0.2f);
        return new SynthPatch
        {
            Voices =
            [
                Simple(new Oscillator(Waveform.Sine, 72), envelope, 0.08f, 0.3564f),
                Simple(new Oscillator(Waveform.Sine, 116), envelope, 0.08f, 0.1458f)
            ],
            Gain = 0.9f,
            SoftClip = true
        };
    }

    public static SynthPatch AquaSynthVoice()
    {
        return PatchScript.Parse(BuiltInScripts.SyrinxVoiceInstrument);
    }

    public static SynthPatch Sfxr(string name) =>
        SfxrParams.Named(name)?.ToPatch() ?? throw new ArgumentException($"Unknown SFXR preset `{name}`.", nameof(name));

    private static Voice Simple(Oscillator oscillator, Envelope envelope, float gateSeconds, float gain) => new()
    {
        Oscillator = oscillator,
        Note = new Note(oscillator.FrequencyHz, gateSeconds),
        Envelope = envelope,
        Gain = gain
    };
}
