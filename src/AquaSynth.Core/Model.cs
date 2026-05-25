namespace AquaSynth.Dsl;

public enum Waveform
{
    Sine,
    Square,
    Sawtooth,
    Triangle,
    Noise
}

public enum ModWaveform
{
    Sine,
    Triangle,
    Square,
    SampleHold
}

public enum ModTarget
{
    Gain,
    Pitch,
    Duty,
    LowPass,
    HighPass,
    Noise,
    Drive,
    Fold,
    FormantMix,
    FmIndex
}

public sealed record Envelope(
    float AttackSeconds = 0,
    float DecaySeconds = 0.01f,
    float SustainLevel = 1,
    float ReleaseSeconds = 0.1f)
{
    public float DurationSeconds => AttackSeconds + DecaySeconds + ReleaseSeconds;
}

public enum RateLevelCurve
{
    Linear,
    Exponential
}

public sealed record RateLevelEnvelope(
    float Rate1Seconds,
    float Level1,
    float Rate2Seconds,
    float Level2,
    float Rate3Seconds,
    float Level3,
    float Rate4Seconds,
    float Level4,
    RateLevelCurve Curve1 = RateLevelCurve.Linear,
    RateLevelCurve Curve2 = RateLevelCurve.Linear,
    RateLevelCurve Curve3 = RateLevelCurve.Linear,
    RateLevelCurve Curve4 = RateLevelCurve.Linear,
    float StartLevel = 0);

public enum NoteSource
{
    OneShot,
    Host
}

public enum PlaybackMode
{
    OneShot,
    Mono,
    Poly
}

public sealed record Note(
    float FrequencyHz = 440,
    float GateSeconds = 0.1f,
    NoteSource Source = NoteSource.OneShot);

public sealed record Playback(
    PlaybackMode Mode = PlaybackMode.OneShot,
    int Voices = 1,
    bool Midi = false,
    float FrequencyHz = 440,
    float Gain = 1);

public sealed record Oscillator(
    Waveform Waveform = Waveform.Sine,
    float FrequencyHz = 440,
    float Duty = 0.5f,
    float Phase = 0);

public sealed record PitchMotion(
    float MinFrequencyHz = 20,
    float RampPerSecond = 0,
    float DeltaRampPerSecond = 0,
    float VibratoDepth = 0,
    float VibratoHz = 0,
    float VibratoDelaySeconds = 0);

public sealed record DutyMotion(float RampPerSecond = 0);

public sealed record Filter(
    float LowPass = 1,
    float LowPassRamp = 0,
    float LowPassResonance = 0,
    float LowPassQ = 0,
    int LowPassOrder = 1,
    float HighPass = 0,
    float HighPassRamp = 0,
    int HighPassOrder = 1,
    float BandPass = 0,
    float BandPassQ = 1,
    int BandPassOrder = 1,
    float Notch = 0,
    float NotchQ = 1,
    int NotchOrder = 1,
    RateLevelEnvelope? LowPassEnvelope = null,
    RateLevelEnvelope? HighPassEnvelope = null);

public sealed record Phaser(float OffsetSeconds = 0, float RampSecondsPerSecond = 0);

public sealed record Arpeggio(float DelaySeconds, float Multiplier);

public sealed record FrequencyModulation(float Ratio = 1, float Index = 0, float IndexDecaySeconds = 0);

public sealed record VoiceColor(
    float NoiseMix = 0,
    float Drive = 0,
    float Fold = 0,
    float TremoloDepth = 0,
    float TremoloHz = 0,
    float FormantMix = 0);

public sealed record Formant(float FrequencyHz, float BandwidthHz, float Gain);

public sealed record FormantFrame(IReadOnlyList<Formant> Formants);

public sealed record TractAreaFunction(IReadOnlyList<float> Diameters)
{
    public int Sections => Diameters.Count;

    public IReadOnlyList<float> Areas => Diameters.Select(diameter => MathF.Max(0, diameter) * MathF.Max(0, diameter)).ToArray();

    public IReadOnlyList<float> ReflectionCoefficients
    {
        get
        {
            if (Diameters.Count < 2) return Array.Empty<float>();
            var reflections = new float[Diameters.Count - 1];
            for (var i = 1; i < Diameters.Count; i++)
            {
                var previousArea = MathF.Max(0.000001f, Diameters[i - 1] * Diameters[i - 1]);
                var area = MathF.Max(0.000001f, Diameters[i] * Diameters[i]);
                reflections[i - 1] = (previousArea - area) / (previousArea + area);
            }

            return reflections;
        }
    }

    public float AverageDiameter(float startFraction, float endFraction)
    {
        if (Diameters.Count == 0) return 0;
        var start = Math.Clamp((int)MathF.Floor(startFraction * Diameters.Count), 0, Diameters.Count - 1);
        var end = Math.Clamp((int)MathF.Ceiling(endFraction * Diameters.Count), start + 1, Diameters.Count);
        var sum = 0f;
        for (var i = start; i < end; i++)
        {
            sum += Diameters[i];
        }

        return sum / Math.Max(1, end - start);
    }

    public float MinimumDiameter => Diameters.Count == 0 ? 0 : Diameters.Min();

    public static TractAreaFunction FromAreas(IReadOnlyList<float> areas) =>
        new(areas.Select(area => MathF.Sqrt(MathF.Max(0, area))).ToArray());
}

public sealed record TractShape(string Name, TractAreaFunction AreaFunction);

public sealed record GlottalSource(
    string Name = "",
    float Intensity = 0.72f,
    float Tenseness = 0.6f,
    float Aspiration = 0.08f,
    float Reflection = 0.75f,
    float Skew = 0.42f);

public sealed record TractInjection(
    string Name = "",
    float Position = 32,
    float Diameter = 1,
    float Turbulence = 0,
    float Burst = 0,
    float Width = 1);

public enum TractPropagationMode
{
    Resonator,
    Waveguide
}

public sealed record NasalBranch(
    string Name = "",
    TractAreaFunction? AreaFunction = null,
    int JunctionIndex = 17,
    float Velum = 0.01f,
    float Reflection = -0.85f,
    float Loss = 0.999f);

public sealed record TractMotion(
    string Name = "",
    float DiameterSlewPerSecond = 18,
    float ShapeReturnPerSecond = 8,
    float ConstrictionSlewPerSecond = 24,
    float VelumSlewPerSecond = 16,
    float ObstructionThreshold = 0.05f);

public sealed record Modulator(
    ModTarget Target,
    ModWaveform Waveform = ModWaveform.Sine,
    float FrequencyHz = 1,
    float Depth = 0,
    float Phase = 0,
    float Bias = 0);

public sealed record ControlLane(string Name, Modulator Modulator);

public sealed record OperatorNode(
    int Id,
    float Ratio = 1,
    float Level = 1,
    float Feedback = 0,
    Note Note = null!,
    Envelope Envelope = null!,
    RateLevelEnvelope? RateLevelEnvelope = null)
{
    public Note Note { get; init; } = Note ?? new();
    public Envelope Envelope { get; init; } = Envelope ?? new();
}

public sealed record OperatorEdge(int SourceId, int TargetId, float Index = 1);

public sealed record OperatorGraph(
    string Name,
    float FrequencyHz,
    IReadOnlyList<OperatorNode> Operators,
    IReadOnlyList<OperatorEdge> Edges,
    IReadOnlyList<int> Carriers,
    Note Note = null!,
    float Gain = 0.2f,
    float VibratoDepth = 0,
    float VibratoHz = 0,
    float VibratoDelaySeconds = 0)
{
    public Note Note { get; init; } = Note ?? new();
}

public sealed record VocalTract(
    int Sections = 44,
    int NoseSections = 28,
    float Intensity = 0.72f,
    float Tenseness = 0.6f,
    float TongueIndex = 12.9f,
    float TongueDiameter = 2.43f,
    float Velum = 0.01f,
    float ConstrictionIndex = 32,
    float ConstrictionDiameter = 1,
    float Turbulence = 0,
    float LipOpening = 1.5f,
    float GlottalReflection = 0.75f,
    float LipReflection = -0.85f,
    TractAreaFunction? AreaFunction = null,
    GlottalSource? Glottis = null,
    TractInjection? Injection = null,
    NasalBranch? Nasal = null,
    TractMotion? Motion = null,
    TractPropagationMode Propagation = TractPropagationMode.Resonator,
    float WaveguideLoss = 0.999f,
    int Substeps = 1);

public sealed record PatchParameter(
    string Path,
    string Label,
    float Default,
    float Min,
    float Max,
    float Step,
    string Unit = "",
    string AutomationRate = "control",
    string Notes = "");

public sealed record ParameterBinding(string FieldPath, string ParameterPath);

public sealed record ReferenceSource(
    string Kind,
    string Uri,
    string License,
    string Hash,
    string Notes = "");

public sealed record ReferenceFeature(string Name, string Value, string Notes = "");

public sealed record PatchLayer(
    string Name,
    string Engine = "",
    int? MinKey = null,
    int? MaxKey = null,
    float Gain = 1,
    string EffectSend = "");

public sealed record HarmonicPartial(float Ratio, float Gain);

public sealed record HarmonicBank(
    string LayerName,
    float RootFrequencyHz,
    IReadOnlyList<HarmonicPartial> Partials);

public enum PadSpectrumMode
{
    Generic,
    Bandwidth,
    Discrete,
    Continuous
}

public enum PadProfileBaseType
{
    Gaussian = 0,
    Square = 1,
    DoubleExponential = 2
}

public enum PadProfileAmplitudeType
{
    Off = 0,
    Gaussian = 1,
    Sine = 2,
    Flat = 3
}

public enum PadProfileAmplitudeMode
{
    Sum = 0,
    Mult = 1,
    Div1 = 2,
    Div2 = 3
}

public enum PadProfileHalf
{
    Full = 0,
    Upper = 1,
    Lower = 2
}

public enum PadHarmonicPositionType
{
    Harmonic = 0,
    ShiftUp = 1,
    ShiftDown = 2,
    PowerUp = 3,
    PowerDown = 4,
    Sine = 5,
    Power = 6,
    Shift = 7
}

public sealed record PadHarmonicProfile(
    PadProfileBaseType BaseType = PadProfileBaseType.Gaussian,
    int BaseParameter = 80,
    int FrequencyMultiplier = 0,
    int ModulatorParameter = 0,
    int ModulatorFrequency = 30,
    int Width = 127,
    PadProfileAmplitudeType AmplitudeType = PadProfileAmplitudeType.Off,
    PadProfileAmplitudeMode AmplitudeMode = PadProfileAmplitudeMode.Sum,
    int AmplitudeParameter1 = 80,
    int AmplitudeParameter2 = 64,
    bool AutoScale = true,
    PadProfileHalf Half = PadProfileHalf.Full);

public sealed record PadHarmonicPosition(
    PadHarmonicPositionType Type = PadHarmonicPositionType.Harmonic,
    int Parameter1 = 0,
    int Parameter2 = 0,
    int Parameter3 = 0);

public sealed record PadSpectrumProfile(
    PadSpectrumMode Mode = PadSpectrumMode.Generic,
    int Bandwidth = 500,
    int BandwidthScale = 0,
    PadHarmonicProfile HarmonicProfile = null!,
    PadHarmonicPosition HarmonicPosition = null!)
{
    public static PadSpectrumProfile Generic { get; } = new();

    public PadHarmonicProfile HarmonicProfile { get; init; } = HarmonicProfile ?? new();

    public PadHarmonicPosition HarmonicPosition { get; init; } = HarmonicPosition ?? new();
}

public sealed record SpectralBank(
    string LayerName,
    float RootFrequencyHz,
    float SpreadRatio,
    IReadOnlyList<HarmonicPartial> Partials,
    Voice Treatment,
    PadSpectrumProfile Profile = null!)
{
    public PadSpectrumProfile Profile { get; init; } = Profile ?? PadSpectrumProfile.Generic;
}

public sealed record ReferencePatch(
    string Id,
    string Family,
    string Name,
    ReferenceSource Source,
    IReadOnlyList<ReferenceFeature> Features,
    IReadOnlyList<PatchParameter> Parameters,
    string? AquaSynthScript = null);

public sealed record Repeat(float IntervalSeconds);

public sealed record Voice
{
    public PatchLayer? Layer { get; init; }
    public Oscillator Oscillator { get; init; } = new();
    public Note Note { get; init; } = new();
    public Envelope Envelope { get; init; } = new();
    public RateLevelEnvelope? RateLevelEnvelope { get; init; }
    public PitchMotion Pitch { get; init; } = new();
    public DutyMotion Duty { get; init; } = new();
    public Filter Filter { get; init; } = new();
    public Phaser Phaser { get; init; } = new();
    public Arpeggio? Arpeggio { get; init; }
    public FrequencyModulation Fm { get; init; } = new();
    public VocalTract? Tract { get; init; }
    public VoiceColor Color { get; init; } = new();
    public IReadOnlyList<Formant> Formants { get; init; } = Array.Empty<Formant>();
    public IReadOnlyList<FormantFrame> FormantFrames { get; init; } = Array.Empty<FormantFrame>();
    public float FormantFrameRateHz { get; init; } = 0.5f;
    public IReadOnlyList<Modulator> Modulators { get; init; } = Array.Empty<Modulator>();
    public float Gain { get; init; } = 0.2f;
}

public sealed record SynthPatch
{
    public IReadOnlyList<Voice> Voices { get; init; } = Array.Empty<Voice>();
    public IReadOnlyList<PatchLayer> Layers { get; init; } = Array.Empty<PatchLayer>();
    public IReadOnlyList<HarmonicBank> HarmonicBanks { get; init; } = Array.Empty<HarmonicBank>();
    public IReadOnlyList<SpectralBank> SpectralBanks { get; init; } = Array.Empty<SpectralBank>();
    public IReadOnlyList<TractShape> TractShapes { get; init; } = Array.Empty<TractShape>();
    public IReadOnlyList<GlottalSource> GlottalSources { get; init; } = Array.Empty<GlottalSource>();
    public IReadOnlyList<TractInjection> TractInjections { get; init; } = Array.Empty<TractInjection>();
    public IReadOnlyList<NasalBranch> NasalBranches { get; init; } = Array.Empty<NasalBranch>();
    public IReadOnlyList<TractMotion> TractMotions { get; init; } = Array.Empty<TractMotion>();
    public IReadOnlyList<OperatorGraph> OperatorGraphs { get; init; } = Array.Empty<OperatorGraph>();
    public IReadOnlyList<ControlLane> Controls { get; init; } = Array.Empty<ControlLane>();
    public IReadOnlyList<PatchParameter> Parameters { get; init; } = Array.Empty<PatchParameter>();
    public IReadOnlyList<ParameterBinding> ParameterBindings { get; init; } = Array.Empty<ParameterBinding>();
    public Playback Playback { get; init; } = new();
    public Repeat? Repeat { get; init; }
    public float Gain { get; init; } = 1;
    public bool SoftClip { get; init; } = true;
}
