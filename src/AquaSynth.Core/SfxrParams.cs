namespace AquaSynth.Dsl;

public sealed record SfxrParams
{
    public Waveform WaveType { get; init; } = Waveform.Square;
    public float BaseFreq { get; init; } = 0.3f;
    public float FreqLimit { get; init; }
    public float FreqRamp { get; init; }
    public float FreqDramp { get; init; }
    public float Duty { get; init; }
    public float DutyRamp { get; init; }
    public float VibStrength { get; init; }
    public float VibSpeed { get; init; }
    public float VibDelay { get; init; }
    public float EnvAttack { get; init; } = 0.4f;
    public float EnvSustain { get; init; } = 0.1f;
    public float EnvDecay { get; init; } = 0.5f;
    public float EnvPunch { get; init; }
    public float LpfResonance { get; init; }
    public float LpfFreq { get; init; } = 1;
    public float LpfRamp { get; init; }
    public float HpfFreq { get; init; }
    public float HpfRamp { get; init; }
    public float PhaOffset { get; init; }
    public float PhaRamp { get; init; }
    public float RepeatSpeed { get; init; }
    public float ArpSpeed { get; init; }
    public float ArpMod { get; init; }

    public static SfxrParams? Named(string name) => name.ToLowerInvariant() switch
    {
        "blip" => Blip(),
        "pickup" or "coin" => Pickup(),
        "laser" or "shoot" => Laser(),
        "explosion" => Explosion(),
        "powerup" => Powerup(),
        "hit" or "hurt" => Hit(),
        "jump" => Jump(),
        _ => null
    };

    public static SfxrParams Blip() => new()
    {
        WaveType = Waveform.Sine,
        BaseFreq = 0.42f,
        EnvAttack = 0,
        EnvSustain = 0.13f,
        EnvDecay = 0.08f,
        HpfFreq = 0.1f
    };

    public static SfxrParams Explosion() => new()
    {
        WaveType = Waveform.Noise,
        BaseFreq = 0.18f,
        FreqRamp = -0.15f,
        EnvAttack = 0,
        EnvSustain = 0.29f,
        EnvDecay = 0.36f,
        EnvPunch = 0.52f,
        PhaOffset = -0.22f,
        PhaRamp = -0.1f,
        VibStrength = 0.22f,
        VibSpeed = 0.28f
    };

    public static SfxrParams Powerup() => new()
    {
        WaveType = Waveform.Sine,
        BaseFreq = 0.36f,
        FreqRamp = 0.28f,
        EnvAttack = 0,
        EnvSustain = 0.24f,
        EnvDecay = 0.28f,
        RepeatSpeed = 0.55f,
        VibStrength = 0.18f,
        VibSpeed = 0.33f
    };

    public static SfxrParams Hit() => new()
    {
        WaveType = Waveform.Noise,
        BaseFreq = 0.34f,
        FreqRamp = -0.48f,
        EnvAttack = 0,
        EnvSustain = 0.05f,
        EnvDecay = 0.2f,
        HpfFreq = 0.12f
    };

    public static SfxrParams Jump() => new()
    {
        WaveType = Waveform.Square,
        Duty = 0.24f,
        BaseFreq = 0.42f,
        FreqRamp = 0.22f,
        EnvAttack = 0,
        EnvSustain = 0.22f,
        EnvDecay = 0.18f,
        HpfFreq = 0.05f,
        LpfFreq = 0.72f
    };

    public static SfxrParams Pickup() => new()
    {
        BaseFreq = 0.58f,
        EnvAttack = 0,
        EnvSustain = 0.08f,
        EnvDecay = 0.24f,
        EnvPunch = 0.45f,
        ArpSpeed = 0.58f,
        ArpMod = 0.34f
    };

    public static SfxrParams Laser() => new()
    {
        WaveType = Waveform.Sawtooth,
        BaseFreq = 0.72f,
        FreqLimit = 0.18f,
        FreqRamp = -0.42f,
        Duty = 0.38f,
        DutyRamp = -0.16f,
        EnvAttack = 0,
        EnvSustain = 0.19f,
        EnvDecay = 0.18f,
        PhaOffset = 0.09f,
        PhaRamp = -0.12f,
        HpfFreq = 0.04f
    };

    public SfxrParams Mutate(ulong seed, float amount)
    {
        var rng = new SeededRng(seed);
        amount = Math.Max(0, amount);
        float Nudge(float value, float min, float max) => Math.Clamp(value + rng.Range(-amount, amount), min, max);

        return this with
        {
            BaseFreq = Nudge(BaseFreq, 0, 1),
            FreqRamp = Nudge(FreqRamp, -1, 1),
            FreqDramp = Nudge(FreqDramp, -1, 1),
            Duty = Nudge(Duty, 0, 1),
            DutyRamp = Nudge(DutyRamp, -1, 1),
            VibStrength = Nudge(VibStrength, 0, 1),
            VibSpeed = Nudge(VibSpeed, 0, 1),
            VibDelay = Nudge(VibDelay, 0, 1),
            EnvAttack = Nudge(EnvAttack, 0, 1),
            EnvSustain = Nudge(EnvSustain, 0, 1),
            EnvDecay = Nudge(EnvDecay, 0, 1),
            EnvPunch = Nudge(EnvPunch, -1, 1),
            LpfResonance = Nudge(LpfResonance, 0, 1),
            LpfFreq = Nudge(LpfFreq, 0, 1),
            LpfRamp = Nudge(LpfRamp, -1, 1),
            HpfFreq = Nudge(HpfFreq, 0, 1),
            HpfRamp = Nudge(HpfRamp, -1, 1),
            PhaOffset = Nudge(PhaOffset, -1, 1),
            PhaRamp = Nudge(PhaRamp, -1, 1),
            RepeatSpeed = Nudge(RepeatSpeed, 0, 1),
            ArpSpeed = Nudge(ArpSpeed, 0, 1),
            ArpMod = Nudge(ArpMod, -1, 1)
        };
    }

    public SynthPatch ToPatch()
    {
        var baseFrequency = SfxrFrequencyHz(BaseFreq);
        var punchGain = PunchGain(EnvPunch);
        var voice = new Voice
        {
            Oscillator = new Oscillator(WaveType, baseFrequency, 0.5f - Math.Clamp(Duty, 0, 1) * 0.5f),
            Note = new Note(baseFrequency, NormalizedEnvSeconds(EnvSustain)),
            Envelope = new Envelope(
                NormalizedEnvSeconds(EnvAttack),
                0,
                1 / punchGain,
                NormalizedEnvSeconds(EnvDecay)),
            Pitch = new PitchMotion(
                Math.Min(SfxrFrequencyHz(FreqLimit), baseFrequency),
                -Cube(FreqRamp) * 9.5f,
                -Cube(FreqDramp) * 0.65f,
                Math.Clamp(VibStrength, 0, 1) * 0.5f,
                2 + Square(Math.Clamp(VibSpeed, 0, 1)) * 18,
                Square(Math.Clamp(VibDelay, 0, 1)) * 0.8f),
            Duty = new DutyMotion(-DutyRamp * 0.35f),
            Filter = new Filter(
                Math.Clamp(LpfFreq, 0, 1),
                Math.Clamp(LpfRamp, -1, 1),
                Math.Clamp(LpfResonance, 0, 1),
                0,
                1,
                Math.Clamp(HpfFreq, 0, 1),
                Math.Clamp(HpfRamp, -1, 1)),
            Phaser = new Phaser(
                MathF.Sign(PhaOffset) * MathF.Abs(Square(PhaOffset)) * 0.018f,
                MathF.Sign(PhaRamp) * MathF.Abs(Square(PhaRamp)) * 0.035f),
            Arpeggio = ArpSpeed > 0
                ? new Arpeggio(
                    Square(1 - ArpSpeed) * 0.46f + 0.0007f,
                    ArpMod >= 0
                        ? 1 / Math.Max(0.1f, 1 - Square(ArpMod) * 0.9f)
                        : Math.Max(0.1f, 1 - Square(ArpMod) * 0.75f))
                : null,
            Color = new VoiceColor(
                WaveType == Waveform.Noise ? 0.35f : 0,
                0.12f + Math.Max(0, EnvPunch) * 0.18f,
                0,
                Math.Clamp(VibStrength, 0, 1) * 0.12f,
                8 + Math.Clamp(VibSpeed, 0, 1) * 18),
            Gain = 0.22f * punchGain
        };

        return new SynthPatch
        {
            Voices = [voice],
            Repeat = RepeatSpeed > 0 ? new Repeat(Square(1 - RepeatSpeed) * 0.46f + 0.02f) : null,
            Gain = 1,
            SoftClip = true
        };
    }

    private static float SfxrFrequencyHz(float value)
    {
        var period = 100 / (Square(Math.Clamp(value, 0, 1)) + 0.001f);
        return Math.Clamp(44100 / period, 20, 20000);
    }

    private static float NormalizedEnvSeconds(float value) => Square(Math.Clamp(value, 0, 1)) * 100000 / 44100;
    private static float PunchGain(float value) => 1 + Math.Max(0, Math.Clamp(value, -1, 1));
    private static float Square(float value) => value * value;
    private static float Cube(float value) => value * value * value;
}

file struct SeededRng(ulong seed)
{
    private ulong _state = seed ^ 0x517c_c1b7_2722_0a95UL;

    public float NextFloat()
    {
        _state = (_state + 0x9e37_79b9_7f4a_7c15UL).RotateLeft(17);
        var value = _state;
        value ^= value >> 30;
        value *= 0xbf58_476d_1ce4_e5b9UL;
        value ^= value >> 27;
        value *= 0x94d0_49bb_1331_11ebUL;
        value ^= value >> 31;
        return (value >> 40) / 16777216f;
    }

    public float Range(float min, float max) => min + (max - min) * NextFloat();
}

file static class UlongExtensions
{
    public static ulong RotateLeft(this ulong value, int offset) => (value << offset) | (value >> (64 - offset));
}
