namespace AquaSynth.Dsl;

public sealed record PinkTromboneReferenceRender(
    float[] Samples,
    int SampleRate,
    PinkTromboneFixtureControls Controls);

public sealed record PinkTromboneUtteranceRender(
    float[] Samples,
    int SampleRate,
    string FixtureId,
    IReadOnlyList<PinkTromboneControlPoint> ControlPoints);

public sealed record PinkTromboneReferenceTimelineSample(
    int Block,
    string Primitive,
    string Signal,
    float Value);

public sealed record PinkTromboneControlPoint(
    float TimeSeconds,
    PinkTromboneFixtureControls Controls,
    string Label = "");

public sealed class PinkTromboneReferenceRenderer(int sampleRate = 44100)
{
    private readonly int sampleRate = Math.Max(1, sampleRate);

    public PinkTromboneReferenceRender Render(PinkTromboneFixtureControls controls, float durationSeconds = 0.57f)
    {
        var frames = Math.Max(1, (int)MathF.Round(sampleRate * Math.Max(durationSeconds, 1f / sampleRate)));
        var synth = new Synthesizer(sampleRate);
        var samples = new float[frames];
        synth.Synthesize(samples, _ => controls);
        return new PinkTromboneReferenceRender(samples, sampleRate, controls);
    }

    public IReadOnlyList<PinkTromboneReferenceTimelineSample> RenderTimeline(
        PinkTromboneFixtureControls controls,
        float durationSeconds = 0.12f,
        int blockSize = 512)
    {
        var frames = Math.Max(1, (int)MathF.Round(sampleRate * Math.Max(durationSeconds, 1f / sampleRate)));
        var synth = new Synthesizer(sampleRate);
        var samples = new float[frames];
        return synth.SynthesizeTimeline(samples, _ => controls, Math.Clamp(blockSize, 1, 4096));
    }

    public PinkTromboneUtteranceRender RenderUtterance(
        string fixtureId,
        IReadOnlyList<PinkTromboneControlPoint> controlPoints,
        float durationSeconds)
    {
        if (controlPoints.Count == 0)
        {
            throw new ArgumentException("Utterance render needs at least one control point.", nameof(controlPoints));
        }

        var ordered = controlPoints.OrderBy(point => point.TimeSeconds).ToArray();
        var frames = Math.Max(1, (int)MathF.Round(sampleRate * Math.Max(durationSeconds, 1f / sampleRate)));
        var synth = new Synthesizer(sampleRate);
        var samples = new float[frames];
        synth.Synthesize(samples, frame => InterpolateControls(ordered, frame / (float)sampleRate));
        return new PinkTromboneUtteranceRender(samples, sampleRate, fixtureId, ordered);
    }

    private static PinkTromboneFixtureControls InterpolateControls(IReadOnlyList<PinkTromboneControlPoint> points, float timeSeconds)
    {
        if (timeSeconds <= points[0].TimeSeconds) return points[0].Controls;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var current = points[i];
            var next = points[i + 1];
            if (timeSeconds > next.TimeSeconds) continue;
            var span = Math.Max(0.0001f, next.TimeSeconds - current.TimeSeconds);
            var t = Smooth01((timeSeconds - current.TimeSeconds) / span);
            return Lerp(current.Controls, next.Controls, t);
        }

        return points[^1].Controls;
    }

    private static float Smooth01(float value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static PinkTromboneFixtureControls Lerp(PinkTromboneFixtureControls a, PinkTromboneFixtureControls b, float t) =>
        new(
            Frequency: Mix(a.Frequency, b.Frequency, t),
            Intensity: Mix(a.Intensity, b.Intensity, t),
            Tenseness: Mix(a.Tenseness, b.Tenseness, t),
            TongueIndex: Mix(a.TongueIndex, b.TongueIndex, t),
            TongueDiameter: Mix(a.TongueDiameter, b.TongueDiameter, t),
            ConstrictionIndex: Mix(a.ConstrictionIndex, b.ConstrictionIndex, t),
            ConstrictionDiameter: Mix(a.ConstrictionDiameter, b.ConstrictionDiameter, t),
            Turbulence: Mix(a.Turbulence, b.Turbulence, t),
            Velum: Mix(a.Velum, b.Velum, t),
            LipOpening: Mix(a.LipOpening, b.LipOpening, t),
            GlottalReflection: Mix(a.GlottalReflection, b.GlottalReflection, t),
            LipReflection: Mix(a.LipReflection, b.LipReflection, t),
            Gain: Mix(a.Gain, b.Gain, t),
            Burst: Mix(a.Burst, b.Burst, t));

    private static float Mix(float a, float b, float t) => a + (b - a) * t;

    private sealed class Synthesizer
    {
        private const int MaxBlockLength = 512;
        private readonly int sampleRate;
        private readonly Glottis glottis;
        private readonly Tract tract;
        private readonly TractShaper tractShaper;
        private float dcBlockX;
        private float dcBlockY;

        public Synthesizer(int sampleRate)
        {
            this.sampleRate = sampleRate;
            glottis = new Glottis(sampleRate);
            tract = new Tract(glottis, sampleRate * 2);
            tractShaper = new TractShaper(tract);
            CalculateNewBlockParameters(0, new PinkTromboneFixtureControls());
        }

        public void Synthesize(float[] buffer, Func<int, PinkTromboneFixtureControls> controlsAtFrame)
        {
            var p = 0;
            while (p < buffer.Length)
            {
                var blockLength = Math.Min(MaxBlockLength, buffer.Length - p);
                SynthesizeBlock(buffer, p, blockLength, controlsAtFrame(p));
                p += blockLength;
            }
        }

        public IReadOnlyList<PinkTromboneReferenceTimelineSample> SynthesizeTimeline(
            float[] buffer,
            Func<int, PinkTromboneFixtureControls> controlsAtFrame,
            int blockSize)
        {
            var p = 0;
            var block = 0;
            var timeline = new List<PinkTromboneReferenceTimelineSample>();
            while (p < buffer.Length)
            {
                var blockLength = Math.Min(Math.Min(MaxBlockLength, blockSize), buffer.Length - p);
                SynthesizeBlock(buffer, p, blockLength, controlsAtFrame(p));
                tract.Snapshot(block, timeline);
                p += blockLength;
                block++;
            }

            return timeline;
        }

        private void SynthesizeBlock(float[] buffer, int offset, int count, PinkTromboneFixtureControls controls)
        {
            var deltaTime = count / (float)sampleRate;
            CalculateNewBlockParameters(deltaTime, controls);
            for (var i = 0; i < count; i++)
            {
                var lambda1 = i / (float)count;
                var lambda2 = (i + 0.5f) / count;
                var glottalOutput = glottis.Step(lambda1);
                var vocalOutput1 = tract.Step(glottalOutput, lambda1, controls);
                var vocalOutput2 = tract.Step(glottalOutput, lambda2, controls);
                buffer[offset + i] = Condition((vocalOutput1 + vocalOutput2) * 0.125f * controls.Gain);
            }
        }

        private void CalculateNewBlockParameters(float deltaTime, PinkTromboneFixtureControls controls)
        {
            glottis.TargetFrequency = controls.Frequency;
            glottis.TargetTenseness = controls.Tenseness;
            glottis.TargetIntensity = controls.Intensity;
            glottis.AdjustParameters(deltaTime);
            tractShaper.SetTargets(controls);
            tractShaper.AdjustTractShape(deltaTime);
            tract.CalculateNewBlockParameters();
        }

        private float Condition(float output)
        {
            var blocked = output - dcBlockX + 0.995f * dcBlockY;
            dcBlockX = output;
            dcBlockY = blocked;
            return MathF.Tanh(blocked * 1.25f);
        }
    }

    private sealed class Glottis
    {
        private readonly int sampleRate;
        private readonly FilteredNoiseSource aspirationNoiseSource;
        private int sampleCount;
        private float intensity;
        private float loudness = 1;
        private float smoothFrequency = 140;
        private float timeInWaveform;
        private float newTenseness = 0.6f;
        private float oldTenseness = 0.6f;
        private float newFrequency = 140;
        private float oldFrequency = 140;
        private float waveformLength;
        private float alpha;
        private float e0;
        private float epsilon;
        private float shift;
        private float delta;
        private float te;
        private float omega;

        public float TargetTenseness { get; set; } = 0.6f;
        public float TargetFrequency { get; set; } = 140;
        public float TargetIntensity { get; set; } = 0.72f;

        public Glottis(int sampleRate)
        {
            this.sampleRate = sampleRate;
            aspirationNoiseSource = new FilteredNoiseSource(500, 0.5f, sampleRate, 0x8000, 0x5EED1234u);
            SetupWaveform(0);
        }

        public float Step(float lambda)
        {
            var time = sampleCount / (float)sampleRate;
            if (timeInWaveform > waveformLength)
            {
                timeInWaveform -= waveformLength;
                SetupWaveform(lambda);
            }

            var out1 = NormalizedLfWaveform(timeInWaveform / waveformLength);
            var aspirationNoise = aspirationNoiseSource.Next();
            var aspiration1 = intensity * (1 - MathF.Sqrt(Math.Clamp(TargetTenseness, 0, 1))) * GetNoiseModulator() * aspirationNoise;
            var aspiration2 = aspiration1 * (0.2f + 0.02f * SimplexNoise.Simplex1(time * 1.99f));
            sampleCount++;
            timeInWaveform += 1f / sampleRate;
            return out1 + aspiration2;
        }

        public float GetNoiseModulator()
        {
            var voiced = 0.1f + 0.2f * Math.Max(0, MathF.Sin(MathF.PI * 2 * timeInWaveform / waveformLength));
            var amount = Math.Clamp(TargetTenseness, 0, 1) * intensity;
            return amount * voiced + (1 - amount) * 0.3f;
        }

        public void AdjustParameters(float deltaTime)
        {
            var delta = deltaTime * sampleRate / 512f;
            var oldTime = sampleCount / (float)sampleRate;
            var newTime = oldTime + deltaTime;
            AdjustIntensity(delta);
            CalculateNewFrequency(newTime, delta);
            CalculateNewTenseness(newTime);
        }

        private void CalculateNewFrequency(float time, float delta)
        {
            if (intensity == 0)
            {
                smoothFrequency = TargetFrequency;
            }
            else if (TargetFrequency > smoothFrequency)
            {
                smoothFrequency = Math.Min(smoothFrequency * (1 + 0.1f * delta), TargetFrequency);
            }
            else if (TargetFrequency < smoothFrequency)
            {
                smoothFrequency = Math.Max(smoothFrequency / (1 + 0.1f * delta), TargetFrequency);
            }

            oldFrequency = newFrequency;
            newFrequency = Math.Max(10, smoothFrequency * (1 + CalculateVibrato(time)));
        }

        private void CalculateNewTenseness(float time)
        {
            oldTenseness = newTenseness;
            newTenseness = Math.Max(
                0,
                TargetTenseness + 0.1f * SimplexNoise.Simplex1(time * 0.46f) + 0.05f * SimplexNoise.Simplex1(time * 0.36f));
            if (TargetIntensity > 0)
            {
                newTenseness += (3 - TargetTenseness) * Math.Max(0, 1 - intensity);
            }
        }

        private void AdjustIntensity(float delta)
        {
            var target = Math.Clamp(TargetIntensity, 0, 1);
            if (intensity < target)
            {
                intensity = Math.Min(intensity + 0.13f * delta, target);
            }
            else
            {
                intensity = Math.Max(intensity - 0.05f * delta, target);
            }
        }

        private static float CalculateVibrato(float time)
        {
            var vibrato = 0.005f * MathF.Sin(2 * MathF.PI * time * 6);
            vibrato += 0.02f * SimplexNoise.Simplex1(time * 4.07f);
            vibrato += 0.04f * SimplexNoise.Simplex1(time * 2.15f);
            vibrato += 0.2f * SimplexNoise.Simplex1(time * 0.98f);
            vibrato += 0.4f * SimplexNoise.Simplex1(time * 0.5f);
            return vibrato;
        }

        private void SetupWaveform(float lambda)
        {
            var frequency = Interpolate(oldFrequency, newFrequency, lambda);
            var tenseness = Interpolate(oldTenseness, newTenseness, lambda);
            waveformLength = 1 / frequency;
            loudness = MathF.Pow(Math.Max(0, tenseness), 0.25f);

            var rd = Math.Clamp(3 * (1 - tenseness), 0.5f, 2.7f);
            var ra = -0.01f + 0.048f * rd;
            var rk = 0.224f + 0.118f * rd;
            var rg = (rk / 4) * (0.5f + 1.2f * rk) / (0.11f * rd - ra * (0.5f + 1.2f * rk));
            var ta = ra;
            var tp = 1 / (2 * rg);
            te = tp + tp * rk;
            epsilon = 1 / ta;
            shift = MathF.Exp(-epsilon * (1 - te));
            delta = 1 - shift;

            var rhsIntegral = ((1 / epsilon) * (shift - 1) + (1 - te) * shift) / delta;
            var totalLowerIntegral = rhsIntegral - (te - tp) / 2;
            var totalUpperIntegral = -totalLowerIntegral;
            omega = MathF.PI / tp;
            var s = MathF.Sin(omega * te);
            var y = -MathF.PI * s * totalUpperIntegral / (tp * 2);
            var z = MathF.Log(y);
            alpha = z / (tp / 2 - te);
            e0 = -1 / (s * MathF.Exp(alpha * te));
        }

        private float NormalizedLfWaveform(float t)
        {
            float output;
            if (t > te)
            {
                output = (-MathF.Exp(-epsilon * (t - te)) + shift) / delta;
            }
            else
            {
                output = e0 * MathF.Exp(alpha * t) * MathF.Sin(omega * t);
            }

            return output * intensity * loudness;
        }
    }

    private sealed class Tract
    {
        public const int N = 44;
        public const int BladeStart = 10;
        public const int TipStart = 32;
        public const int LipStart = 39;
        public const int NoseLength = 28;
        public const int NoseStart = N - NoseLength + 1;

        private const float Loss = 0.999f;
        private const float GlottalReflection = 0.75f;
        private const float LipReflection = -0.85f;
        private readonly Glottis glottis;
        private readonly int tractSampleRate;
        private readonly FilteredNoiseSource fricationNoiseSource;
        private int sampleCount;
        private float reflectionLeft;
        private float newReflectionLeft;
        private float reflectionRight;
        private float newReflectionRight;
        private float reflectionNose;
        private float newReflectionNose;

        public readonly float[] Diameter = new float[N];
        public readonly float[] NoseDiameter = new float[NoseLength];
        private readonly float[] right = new float[N];
        private readonly float[] left = new float[N];
        private readonly float[] reflection = new float[N];
        private readonly float[] newReflection = new float[N];
        private readonly float[] junctionOutputRight = new float[N];
        private readonly float[] junctionOutputLeft = new float[N + 1];
        private readonly float[] noseRight = new float[NoseLength];
        private readonly float[] noseLeft = new float[NoseLength];
        private readonly float[] noseJunctionOutputRight = new float[NoseLength];
        private readonly float[] noseJunctionOutputLeft = new float[NoseLength + 1];
        private readonly float[] noseReflection = new float[NoseLength];
        private readonly List<Transient> transients = [];

        public float Time { get; private set; }
        public TurbulencePoint TurbulencePoint { get; set; }

        public Tract(Glottis glottis, int tractSampleRate)
        {
            this.glottis = glottis;
            this.tractSampleRate = tractSampleRate;
            fricationNoiseSource = new FilteredNoiseSource(1000, 0.5f, tractSampleRate, 0x8000, 0xA11CEu);
        }

        public void CalculateNoseReflections()
        {
            Span<float> area = stackalloc float[NoseLength];
            for (var i = 0; i < NoseLength; i++)
            {
                area[i] = Math.Max(1e-6f, NoseDiameter[i] * NoseDiameter[i]);
            }

            for (var i = 1; i < NoseLength; i++)
            {
                noseReflection[i] = (area[i - 1] - area[i]) / (area[i - 1] + area[i]);
            }
        }

        public void CalculateNewBlockParameters()
        {
            CalculateMainTractReflections();
            CalculateNoseJunctionReflections();
        }

        public float Step(float glottalOutput, float lambda, PinkTromboneFixtureControls controls)
        {
            ProcessTransients();
            AddTurbulenceNoise(controls);

            junctionOutputRight[0] = left[0] * GlottalReflection + glottalOutput;
            junctionOutputLeft[N] = right[N - 1] * LipReflection;

            for (var i = 1; i < N; i++)
            {
                var r = Interpolate(reflection[i], newReflection[i], lambda);
                var w = r * (right[i - 1] + left[i]);
                junctionOutputRight[i] = right[i - 1] - w;
                junctionOutputLeft[i] = left[i] + w;
            }

            {
                const int i = NoseStart;
                var r = Interpolate(reflectionLeft, newReflectionLeft, lambda);
                junctionOutputLeft[i] = r * right[i - 1] + (1 + r) * (noseLeft[0] + left[i]);
                r = Interpolate(reflectionRight, newReflectionRight, lambda);
                junctionOutputRight[i] = r * left[i] + (1 + r) * (right[i - 1] + noseLeft[0]);
                r = Interpolate(reflectionNose, newReflectionNose, lambda);
                noseJunctionOutputRight[0] = r * noseLeft[0] + (1 + r) * (left[i] + right[i - 1]);
            }

            for (var i = 0; i < N; i++)
            {
                right[i] = junctionOutputRight[i] * Loss;
                left[i] = junctionOutputLeft[i + 1] * Loss;
            }

            var lipOutput = right[N - 1];
            noseJunctionOutputLeft[NoseLength] = noseRight[NoseLength - 1] * LipReflection;

            for (var i = 1; i < NoseLength; i++)
            {
                var w = noseReflection[i] * (noseRight[i - 1] + noseLeft[i]);
                noseJunctionOutputRight[i] = noseRight[i - 1] - w;
                noseJunctionOutputLeft[i] = noseLeft[i] + w;
            }

            for (var i = 0; i < NoseLength; i++)
            {
                noseRight[i] = noseJunctionOutputRight[i];
                noseLeft[i] = noseJunctionOutputLeft[i + 1];
            }

            var noseOutput = noseRight[NoseLength - 1];
            sampleCount++;
            Time = sampleCount / (float)tractSampleRate;
            return lipOutput + noseOutput;
        }

        public void Snapshot(int block, List<PinkTromboneReferenceTimelineSample> timeline)
        {
            var oralArea = Diameter.Select(diameter => diameter * diameter).DefaultIfEmpty(0).Average();
            var nasalArea = NoseDiameter.Select(diameter => diameter * diameter).DefaultIfEmpty(0).Average();
            var oralIncoming = right.Select(MathF.Abs).DefaultIfEmpty(0).Average() + left.Select(MathF.Abs).DefaultIfEmpty(0).Average();
            var oralOutgoing = junctionOutputRight.Select(MathF.Abs).DefaultIfEmpty(0).Average() + junctionOutputLeft.Take(N).Select(MathF.Abs).DefaultIfEmpty(0).Average();
            var oralEnergyIn = WaveEnergy(Diameter, right, left);
            var oralEnergyOut = WaveEnergy(Diameter, junctionOutputRight, junctionOutputLeft);
            var nasalAdmittance = Math.Clamp(NoseDiameter[0] * NoseDiameter[0], 0, 1);
            var obstruction = Diameter.Select((diameter, index) => (diameter, index)).MinBy(item => item.diameter);
            var lipFlow = right[N - 1];
            var noseFlow = noseRight[NoseLength - 1];

            Add(timeline, block, "path:pt_oral", "area", oralArea);
            Add(timeline, block, "path:pt_oral", "incoming_wave", oralIncoming);
            Add(timeline, block, "path:pt_oral", "outgoing_wave", oralOutgoing);
            Add(timeline, block, "path:pt_oral", "energy_in", oralEnergyIn);
            Add(timeline, block, "path:pt_oral", "energy_out", oralEnergyOut);
            Add(timeline, block, "path:pt_oral", "passivity_ratio", oralEnergyIn <= 0.000001f ? 1 : oralEnergyOut / oralEnergyIn);
            Add(timeline, block, "path:pt_nasal", "area", nasalArea);
            Add(timeline, block, "branch:pt_velopharynx", "admittance", nasalAdmittance);
            Add(timeline, block, "branch:pt_velopharynx", "reflection_left", newReflectionLeft);
            Add(timeline, block, "branch:pt_velopharynx", "reflection_right", newReflectionRight);
            Add(timeline, block, "branch:pt_velopharynx", "reflection_nose", newReflectionNose);
            Add(timeline, block, "contact:pt_obstruction", "position", obstruction.index / (float)Math.Max(1, N - 1));
            Add(timeline, block, "contact:pt_obstruction", "opening", Math.Clamp(obstruction.diameter / 2.5f, 0, 1));
            Add(timeline, block, "contact:pt_obstruction", "reservoir", transients.Count);
            Add(timeline, block, "contact:pt_obstruction", "released_flow", transients.Sum(transient => transient.Strength));
            Add(timeline, block, "radiation:pt_lip", "reflection", LipReflection);
            Add(timeline, block, "radiation:pt_lip", "boundary_flow", lipFlow);
            Add(timeline, block, "radiation:pt_lip", "flow", lipFlow);
            Add(timeline, block, "radiation:pt_lip", "output", lipFlow + noseFlow);
        }

        public void AddTransient(int position, float strength)
        {
            transients.Add(new Transient(position, Time, 0.2f, 0.3f * strength, 200));
        }

        private static float WaveEnergy(float[] diameters, float[] rightWave, float[] leftWave)
        {
            var count = Math.Min(diameters.Length, Math.Min(rightWave.Length, leftWave.Length));
            var energy = 0f;
            for (var i = 0; i < count; i++)
            {
                var area = Math.Max(0.000001f, diameters[i] * diameters[i]);
                energy += area * (rightWave[i] * rightWave[i] + leftWave[i] * leftWave[i]);
            }

            return energy;
        }

        private static void Add(List<PinkTromboneReferenceTimelineSample> timeline, int block, string primitive, string signal, float value) =>
            timeline.Add(new PinkTromboneReferenceTimelineSample(block, primitive, signal, value));

        private void CalculateMainTractReflections()
        {
            Span<float> area = stackalloc float[N];
            for (var i = 0; i < N; i++)
            {
                area[i] = Diameter[i] * Diameter[i];
            }

            for (var i = 1; i < N; i++)
            {
                reflection[i] = newReflection[i];
                var sum = area[i - 1] + area[i];
                newReflection[i] = Math.Abs(sum) > 1e-6f ? (area[i - 1] - area[i]) / sum : 1;
            }
        }

        private void CalculateNoseJunctionReflections()
        {
            reflectionLeft = newReflectionLeft;
            reflectionRight = newReflectionRight;
            reflectionNose = newReflectionNose;
            var velumA = NoseDiameter[0] * NoseDiameter[0];
            var an0 = Diameter[NoseStart] * Diameter[NoseStart];
            var an1 = Diameter[NoseStart + 1] * Diameter[NoseStart + 1];
            var sum = an0 + an1 + velumA;
            newReflectionLeft = Math.Abs(sum) > 1e-6f ? (2 * an0 - sum) / sum : 1;
            newReflectionRight = Math.Abs(sum) > 1e-6f ? (2 * an1 - sum) / sum : 1;
            newReflectionNose = Math.Abs(sum) > 1e-6f ? (2 * velumA - sum) / sum : 1;
        }

        private void ProcessTransients()
        {
            for (var i = transients.Count - 1; i >= 0; i--)
            {
                var transient = transients[i];
                var timeAlive = Time - transient.StartTime;
                if (timeAlive > transient.LifeTime)
                {
                    transients.RemoveAt(i);
                    continue;
                }

                var amplitude = transient.Strength * MathF.Pow(2, -transient.Exponent * timeAlive);
                right[transient.Position] += amplitude / 2;
                left[transient.Position] += amplitude / 2;
            }
        }

        private void AddTurbulenceNoise(PinkTromboneFixtureControls controls)
        {
            var point = TurbulencePoint;
            if (point.Strength <= 0 || point.Position < 2 || point.Position > N || point.Diameter <= 0)
            {
                return;
            }

            var intensity = Math.Clamp((Time - point.StartTime) / 0.1f, 0, 1);
            if (intensity <= 0)
            {
                return;
            }

            var turbulenceNoise = 0.66f * fricationNoiseSource.Next() * intensity * point.Strength * glottis.GetNoiseModulator();
            AddTurbulenceNoiseAtPosition(turbulenceNoise, point.Position, point.Diameter, controls);
        }

        private void AddTurbulenceNoiseAtPosition(
            float turbulenceNoise,
            float position,
            float diameter,
            PinkTromboneFixtureControls controls)
        {
            var i = (int)MathF.Floor(position);
            var delta = position - i;
            var thinness0 = Math.Clamp(8 * (0.7f - diameter), 0, 1);
            var openness = Math.Clamp(30 * (diameter - 0.3f), 0, 1);
            var noise0 = turbulenceNoise * (1 - delta) * thinness0 * openness * controls.Turbulence;
            var noise1 = turbulenceNoise * delta * thinness0 * openness * controls.Turbulence;
            if (i + 1 < N)
            {
                right[i + 1] += noise0 / 2;
                left[i + 1] += noise0 / 2;
            }

            if (i + 2 < N)
            {
                right[i + 2] += noise1 / 2;
                left[i + 2] += noise1 / 2;
            }
        }
    }

    private sealed class TractShaper
    {
        private const float GridOffset = 1.7f;
        private const float MovementSpeed = 15;
        private const float VelumOpenTarget = 0.4f;
        private const float VelumClosedTarget = 0.01f;
        private readonly Tract tract;
        private readonly float[] targetDiameter = new float[Tract.N];
        private int lastObstruction = -1;
        private float velumTarget;
        private float lastTurbulencePosition = float.NaN;
        private float transientStrength = 1;

        public TractShaper(Tract tract)
        {
            this.tract = tract;
            velumTarget = VelumOpenTarget;
            ShapeNose();
            tract.CalculateNoseReflections();
            velumTarget = VelumClosedTarget;
            ShapeNose();
            ShapeMainTract();
        }

        public void SetTargets(PinkTromboneFixtureControls controls)
        {
            for (var i = 0; i < Tract.N; i++)
            {
                targetDiameter[i] = GetRestDiameter(i, controls.TongueIndex, controls.TongueDiameter);
            }

            ReduceTargetDiametersByTouch(controls.ConstrictionIndex, Math.Max(0, controls.ConstrictionDiameter - 0.3f));
            targetDiameter[Tract.N - 1] = Math.Min(targetDiameter[Tract.N - 1], Math.Max(0, controls.LipOpening));
            velumTarget = controls.Velum > 0.08f ? Math.Clamp(controls.Velum, VelumClosedTarget, VelumOpenTarget) : VelumClosedTarget;
            transientStrength = Math.Clamp(controls.Burst, 0, 2);
            if (controls.Turbulence > 0.001f)
            {
                if (float.IsNaN(lastTurbulencePosition) || MathF.Abs(lastTurbulencePosition - controls.ConstrictionIndex) > 0.5f)
                {
                    lastTurbulencePosition = controls.ConstrictionIndex;
                    tract.TurbulencePoint = new TurbulencePoint(controls.ConstrictionIndex, controls.ConstrictionDiameter, tract.Time, controls.Turbulence);
                }
                else
                {
                    var current = tract.TurbulencePoint;
                    tract.TurbulencePoint = current with
                    {
                        Position = controls.ConstrictionIndex,
                        Diameter = controls.ConstrictionDiameter,
                        Strength = controls.Turbulence
                    };
                }
            }
            else
            {
                tract.TurbulencePoint = default;
                lastTurbulencePosition = float.NaN;
            }
        }

        public void AdjustTractShape(float deltaTime)
        {
            var amount = deltaTime * MovementSpeed;
            var newLastObstruction = -1;
            for (var i = 0; i < Tract.N; i++)
            {
                var diameter = tract.Diameter[i];
                if (diameter <= 0)
                {
                    newLastObstruction = i;
                }

                float slowReturn;
                if (i < Tract.NoseStart)
                {
                    slowReturn = 0.6f;
                }
                else if (i >= Tract.TipStart)
                {
                    slowReturn = 1;
                }
                else
                {
                    slowReturn = 0.6f + 0.4f * (i - Tract.NoseStart) / (Tract.TipStart - Tract.NoseStart);
                }

                tract.Diameter[i] = MoveTowards(diameter, targetDiameter[i], slowReturn * amount, 2 * amount);
            }

            if (lastObstruction > -1 && newLastObstruction == -1 && tract.NoseDiameter[0] < 0.223f)
            {
                tract.AddTransient(lastObstruction, transientStrength);
            }

            lastObstruction = newLastObstruction;
            tract.NoseDiameter[0] = MoveTowards(tract.NoseDiameter[0], velumTarget, amount * 0.25f, amount * 0.1f);
        }

        private void ShapeMainTract()
        {
            for (var i = 0; i < Tract.N; i++)
            {
                var d = GetRestDiameter(i, 12.9f, 2.43f);
                tract.Diameter[i] = d;
                targetDiameter[i] = d;
            }
        }

        private static float GetRestDiameter(int i, float tongueIndex, float tongueDiameter)
        {
            if (i < 7) return 0.6f;
            if (i < Tract.BladeStart) return 1.1f;
            if (i >= Tract.LipStart) return 1.5f;
            var t = 1.1f * MathF.PI * (tongueIndex - i) / (Tract.TipStart - Tract.BladeStart);
            var fixedTongueDiameter = 2 + (tongueDiameter - 2) / 1.5f;
            var curve = (1.5f - fixedTongueDiameter + GridOffset) * MathF.Cos(t);
            if (i == Tract.BladeStart - 2 || i == Tract.LipStart - 1) curve *= 0.8f;
            if (i == Tract.BladeStart || i == Tract.LipStart - 2) curve *= 0.94f;
            return 1.5f - curve;
        }

        private void ReduceTargetDiametersByTouch(float index, float diameter)
        {
            if (index < 2 || index >= Tract.N || diameter >= 3)
            {
                return;
            }

            float width;
            if (index < 25)
            {
                width = 10;
            }
            else if (index >= Tract.TipStart)
            {
                width = 5;
            }
            else
            {
                width = 10 - 5 * (index - 25) / (Tract.TipStart - 25);
            }

            for (var offset = -(int)MathF.Ceiling(width) - 1; offset < width + 1; offset++)
            {
                var p = (int)MathF.Round(index) + offset;
                if (p < 0 || p >= Tract.N) continue;
                var relpos = MathF.Abs(p - index) - 0.5f;
                float shrink;
                if (relpos <= 0) shrink = 0;
                else if (relpos > width) shrink = 1;
                else shrink = 0.5f * (1 - MathF.Cos(MathF.PI * relpos / width));
                if (diameter < targetDiameter[p])
                {
                    targetDiameter[p] = diameter + (targetDiameter[p] - diameter) * shrink;
                }
            }
        }

        private void ShapeNose()
        {
            for (var i = 0; i < Tract.NoseLength; i++)
            {
                float diameter;
                var d = 2f * i / Tract.NoseLength;
                if (i == 0) diameter = velumTarget;
                else if (d < 1) diameter = 0.4f + 1.6f * d;
                else diameter = 0.5f + 1.5f * (2 - d);
                tract.NoseDiameter[i] = Math.Min(diameter, 1.9f);
            }
        }
    }

    private sealed class FilteredNoiseSource
    {
        private readonly float[] buffer;
        private readonly Biquad filter;
        private int index;

        public FilteredNoiseSource(float f0, float q, int sampleRate, int bufferSize, uint seed)
        {
            buffer = new float[bufferSize];
            var rng = new Lcg(seed);
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 2 * rng.NextFloat() - 1;
            }

            filter = Biquad.BandPass(f0, q, sampleRate);
        }

        public float Next()
        {
            if (index >= buffer.Length)
            {
                index = 0;
            }

            return filter.Process(buffer[index++]);
        }
    }

    private sealed class Biquad
    {
        private readonly float nb0;
        private readonly float nb1;
        private readonly float nb2;
        private readonly float na1;
        private readonly float na2;
        private float x1;
        private float x2;
        private float y1;
        private float y2;

        private Biquad(float b0, float b1, float b2, float a0, float a1, float a2)
        {
            nb0 = b0 / a0;
            nb1 = b1 / a0;
            nb2 = b2 / a0;
            na1 = a1 / a0;
            na2 = a2 / a0;
        }

        public static Biquad BandPass(float f0, float q, int sampleRate)
        {
            var w0 = 2 * MathF.PI * f0 / sampleRate;
            var alpha = MathF.Sin(w0) / (2 * q);
            return new Biquad(alpha, 0, -alpha, 1 + alpha, -2 * MathF.Cos(w0), 1 - alpha);
        }

        public float Process(float x)
        {
            var y = nb0 * x + nb1 * x1 + nb2 * x2 - na1 * y1 - na2 * y2;
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            return y;
        }
    }

    private struct Lcg(uint state)
    {
        private uint state = state == 0 ? 1u : state;

        public float NextFloat()
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return (state >> 8) / 16777216f;
        }
    }

    private readonly record struct Transient(int Position, float StartTime, float LifeTime, float Strength, float Exponent);

    private readonly record struct TurbulencePoint(float Position, float Diameter, float StartTime, float Strength);

    private static float Interpolate(float v1, float v2, float position) => v1 * (1 - position) + v2 * position;

    private static float MoveTowards(float current, float target, float amountUp, float amountDown) =>
        current < target ? Math.Min(current + amountUp, target) : Math.Max(current - amountDown, target);

    private static class SimplexNoise
    {
        private sealed record Grad(float X, float Y, float Z)
        {
            public float Dot2(float x, float y) => X * x + Y * y;
        }

        private static readonly Grad[] Grad3 =
        [
            new(1, 1, 0), new(-1, 1, 0), new(1, -1, 0), new(-1, -1, 0),
            new(1, 0, 1), new(-1, 0, 1), new(1, 0, -1), new(-1, 0, -1),
            new(0, 1, 1), new(0, -1, 1), new(0, 1, -1), new(0, -1, -1)
        ];

        private static readonly int[] P =
        [
            151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,
            8,99,37,240,21,10,23,190,6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,
            35,11,32,57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,74,165,71,
            134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,
            55,46,245,40,244,102,143,54,65,25,63,161,1,216,80,73,209,76,132,187,208,89,
            18,169,200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,52,217,226,
            250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,
            189,28,42,223,183,170,213,119,248,152,2,44,154,163,70,221,153,101,155,167,43,
            172,9,129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,218,246,97,
            228,251,34,242,193,238,210,144,12,191,179,162,241,81,51,145,235,249,14,239,
            107,49,192,214,31,181,199,106,157,184,84,204,176,115,121,50,45,127,4,150,254,
            138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180
        ];

        private static readonly int[] Perm = new int[512];
        private static readonly Grad[] GradP = new Grad[512];

        static SimplexNoise()
        {
            SetSeed(0);
        }

        public static float Simplex1(float x) => Simplex2(x * 1.2f, -x * 0.7f);

        private static void SetSeed(int seed0)
        {
            var seed = seed0;
            if (seed < 256) seed |= seed << 8;
            for (var i = 0; i < 256; i++)
            {
                var v = (i & 1) != 0 ? P[i] ^ (seed & 255) : P[i] ^ ((seed >> 8) & 255);
                Perm[i] = Perm[i + 256] = v;
                GradP[i] = GradP[i + 256] = Grad3[v % 12];
            }
        }

        private static float Simplex2(float xin, float yin)
        {
            const float f2 = 0.3660254037844386f;
            const float g2 = 0.21132486540518713f;
            var s = (xin + yin) * f2;
            var i = (int)MathF.Floor(xin + s);
            var j = (int)MathF.Floor(yin + s);
            var t = (i + j) * g2;
            var x0 = xin - i + t;
            var y0 = yin - j + t;
            int i1;
            int j1;
            if (x0 > y0)
            {
                i1 = 1;
                j1 = 0;
            }
            else
            {
                i1 = 0;
                j1 = 1;
            }

            var x1 = x0 - i1 + g2;
            var y1 = y0 - j1 + g2;
            var x2 = x0 - 1 + 2 * g2;
            var y2 = y0 - 1 + 2 * g2;
            i &= 255;
            j &= 255;
            var gi0 = GradP[i + Perm[j]];
            var gi1 = GradP[i + i1 + Perm[j + j1]];
            var gi2 = GradP[i + 1 + Perm[j + 1]];

            float n0;
            float n1;
            float n2;
            var t0 = 0.5f - x0 * x0 - y0 * y0;
            if (t0 < 0) n0 = 0;
            else
            {
                t0 *= t0;
                n0 = t0 * t0 * gi0.Dot2(x0, y0);
            }

            var t1 = 0.5f - x1 * x1 - y1 * y1;
            if (t1 < 0) n1 = 0;
            else
            {
                t1 *= t1;
                n1 = t1 * t1 * gi1.Dot2(x1, y1);
            }

            var t2 = 0.5f - x2 * x2 - y2 * y2;
            if (t2 < 0) n2 = 0;
            else
            {
                t2 *= t2;
                n2 = t2 * t2 * gi2.Dot2(x2, y2);
            }

            return 70 * (n0 + n1 + n2);
        }
    }
}
