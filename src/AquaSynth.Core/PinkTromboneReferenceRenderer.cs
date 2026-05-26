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

public sealed record PinkTromboneControlPoint(
    float TimeSeconds,
    PinkTromboneFixtureControls Controls,
    string Label = "");

public sealed class PinkTromboneReferenceRenderer(int sampleRate = 44100)
{
    private const int Sections = 44;
    private const int NoseSections = 28;
    private const float Loss = 0.999f;
    private readonly int sampleRate = Math.Max(1, sampleRate);

    public PinkTromboneReferenceRender Render(PinkTromboneFixtureControls controls, float durationSeconds = 0.57f)
    {
        var frames = Math.Max(1, (int)MathF.Round(sampleRate * Math.Max(durationSeconds, 1f / sampleRate)));
        var synth = new State(sampleRate);
        var samples = new float[frames];
        synth.Render(samples, controls);
        return new PinkTromboneReferenceRender(samples, sampleRate, controls);
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

        var ordered = controlPoints
            .OrderBy(point => point.TimeSeconds)
            .ToArray();
        var frames = Math.Max(1, (int)MathF.Round(sampleRate * Math.Max(durationSeconds, 1f / sampleRate)));
        var synth = new State(sampleRate);
        var samples = new float[frames];
        synth.Render(samples, frame => InterpolateControls(ordered, frame / (float)sampleRate));
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

    private sealed class State
    {
        private readonly float[] right = new float[Sections];
        private readonly float[] left = new float[Sections];
        private readonly float[] junctionRight = new float[Sections];
        private readonly float[] junctionLeft = new float[Sections + 1];
        private readonly float[] reflection = new float[Sections];
        private readonly float[] diameter = new float[Sections];
        private readonly float[] targetDiameter = new float[Sections];
        private readonly float[] noseRight = new float[NoseSections];
        private readonly float[] noseLeft = new float[NoseSections];
        private readonly float[] noseJunctionRight = new float[NoseSections];
        private readonly float[] noseJunctionLeft = new float[NoseSections + 1];
        private readonly float[] noseReflection = new float[NoseSections];
        private readonly float[] noseDiameter = new float[NoseSections];
        private readonly int sampleRate;
        private float glottalPhase;
        private float noise = 0.1234567f;
        private float lastConstrictionDiameter = 1;
        private float transient;
        private float dcBlockX;
        private float dcBlockY;

        public State(int sampleRate)
        {
            this.sampleRate = sampleRate;
            InitRest();
            UpdateNose(0.01f);
        }

        public void Render(float[] output, PinkTromboneFixtureControls controls)
        {
            Render(output, _ => controls);
        }

        public void Render(float[] output, Func<int, PinkTromboneFixtureControls> controlsAtFrame)
        {
            for (var i = 0; i < output.Length; i++)
            {
                var controls = controlsAtFrame(i);
                UpdateTargets(controls);
                UpdateNose(controls.Velum);
                UpdateReflection();
                UpdateTransient(controls);
                SlewDiameters();
                transient *= 0.995f;
                var glottal = Glottis(controls) + Rand() * transient;
                var first = Step(glottal, controls);
                var second = Step(glottal, controls);
                output[i] = Condition((first + second) * 0.72f) * controls.Gain;
            }
        }

        private float Rand()
        {
            noise = (noise * 16807) % 2147483647;
            return noise / 1073741823.5f - 1;
        }

        private void InitRest()
        {
            for (var i = 0; i < Sections; i++)
            {
                var value = RestDiameter(i);
                diameter[i] = value;
                targetDiameter[i] = value;
            }
        }

        private static float RestDiameter(int index) =>
            index < 7 ? 0.6f : index < 10 ? 1.1f : 1.5f;

        private void UpdateTargets(PinkTromboneFixtureControls controls)
        {
            for (var i = 0; i < Sections; i++)
            {
                var value = RestDiameter(i);
                if (i > 10 && i < 39)
                {
                    var angle = 1.1f * MathF.PI * (controls.TongueIndex - i) / 22;
                    var fixedTongueDiameter = 2 + (controls.TongueDiameter - 2) / 1.5f;
                    var curve = (1.5f - fixedTongueDiameter + 1.7f) * MathF.Cos(angle);
                    if (i is 8 or 38) curve *= 0.8f;
                    if (i is 10 or 37) curve *= 0.94f;
                    value = 1.5f - curve;
                }

                targetDiameter[i] = Math.Max(0, value);
            }

            ApplyConstriction(controls.ConstrictionIndex, controls.ConstrictionDiameter);
            targetDiameter[Sections - 1] = Math.Max(0.05f, controls.LipOpening);
        }

        private void ApplyConstriction(float position, float constrictionDiameter)
        {
            var newDiameter = Math.Max(0, constrictionDiameter - 0.3f);
            var range = position < 25 ? 10 : 5;
            var lower = Math.Max(0, (int)MathF.Floor(position - range - 1));
            var upper = Math.Min(Sections - 1, (int)MathF.Ceiling(position + range + 1));
            for (var i = lower; i <= upper; i++)
            {
                var offset = MathF.Abs(i - position) - 0.5f;
                float scale;
                if (offset <= 0) scale = 0;
                else if (offset > range) scale = 1;
                else scale = 0.5f * (1 - MathF.Cos(MathF.PI * offset / range));

                var difference = targetDiameter[i] - newDiameter;
                if (difference > 0) targetDiameter[i] = newDiameter + difference * scale;
            }
        }

        private void SlewDiameters()
        {
            for (var i = 0; i < Sections; i++)
            {
                var speed = i < 17 ? 0.00035f : i < 32 ? 0.00045f : 0.0007f;
                diameter[i] += Math.Clamp(targetDiameter[i] - diameter[i], -speed, speed * 2);
            }
        }

        private void UpdateNose(float velum)
        {
            for (var i = 0; i < NoseSections; i++)
            {
                var d = 2f * i / NoseSections;
                var value = i == 0 ? velum : d < 1 ? 0.4f + 1.6f * d : 0.5f + 1.5f * (2 - d);
                noseDiameter[i] = Math.Min(value, 1.9f);
            }

            for (var i = 1; i < NoseSections; i++)
            {
                noseReflection[i] = Reflection(noseDiameter[i - 1], noseDiameter[i]);
            }
        }

        private void UpdateReflection()
        {
            for (var i = 1; i < Sections; i++)
            {
                reflection[i] = Reflection(diameter[i - 1], diameter[i]);
            }
        }

        private static float Reflection(float previousDiameter, float nextDiameter)
        {
            var previousArea = Math.Max(1e-6f, previousDiameter * previousDiameter);
            var nextArea = Math.Max(1e-6f, nextDiameter * nextDiameter);
            return (previousArea - nextArea) / (previousArea + nextArea);
        }

        private float Glottis(PinkTromboneFixtureControls controls)
        {
            glottalPhase += controls.Frequency / sampleRate;
            glottalPhase -= MathF.Floor(glottalPhase);
            var phase = glottalPhase;
            var tenseness = Math.Clamp(controls.Tenseness, 0, 1);
            var openPhase = 0.55f + 0.32f * (1 - tenseness);
            var pulse = phase < openPhase
                ? MathF.Sin(MathF.PI * phase / openPhase)
                : -0.28f * MathF.Sin(MathF.PI * (phase - openPhase) / (1 - openPhase));
            var harmonicBite = (0.12f + tenseness * 0.62f) * MathF.Sin(4 * MathF.PI * phase);
            var aspiration = Rand() * controls.Intensity * (1 - MathF.Sqrt(tenseness)) * 0.18f;
            return (pulse - harmonicBite + aspiration) * controls.Intensity * (0.45f + 0.75f * MathF.Pow(tenseness, 0.35f));
        }

        private void InjectTurbulence(PinkTromboneFixtureControls controls)
        {
            var thinness = Math.Clamp(8 * (0.7f - controls.ConstrictionDiameter), 0, 1);
            var openness = Math.Clamp(30 * (controls.ConstrictionDiameter - 0.3f), 0, 1);
            var frontLift = 0.35f + 0.65f * Math.Clamp(controls.ConstrictionIndex / Sections, 0, 1);
            var amount = Rand() * controls.Turbulence * thinness * openness * controls.Intensity * frontLift * 1.8f;
            var index = (int)MathF.Floor(controls.ConstrictionIndex);
            var delta = controls.ConstrictionIndex - index;
            if (index + 1 < Sections)
            {
                right[index + 1] += amount * (1 - delta) * 0.5f;
                left[index + 1] += amount * (1 - delta) * 0.5f;
            }

            if (index + 2 < Sections)
            {
                right[index + 2] += amount * delta * 0.5f;
                left[index + 2] += amount * delta * 0.5f;
            }
        }

        private float Step(float input, PinkTromboneFixtureControls controls)
        {
            InjectTurbulence(controls);
            junctionRight[0] = left[0] * controls.GlottalReflection + input;
            junctionLeft[Sections] = right[Sections - 1] * controls.LipReflection;

            for (var i = 1; i < Sections; i++)
            {
                var wave = reflection[i] * (right[i - 1] + left[i]);
                junctionRight[i] = right[i - 1] - wave;
                junctionLeft[i] = left[i] + wave;
            }

            var noseStart = Sections - NoseSections + 1;
            var velumArea = Math.Max(1e-6f, controls.Velum * controls.Velum);
            var leftArea = Math.Max(1e-6f, diameter[noseStart] * diameter[noseStart]);
            var rightArea = Math.Max(1e-6f, diameter[noseStart + 1] * diameter[noseStart + 1]);
            var sum = leftArea + rightArea + velumArea;
            var reflectLeft = (2 * leftArea - sum) / sum;
            var reflectRight = (2 * rightArea - sum) / sum;
            var reflectNose = (2 * velumArea - sum) / sum;
            junctionLeft[noseStart] = reflectLeft * right[noseStart - 1] + (1 + reflectLeft) * (noseLeft[0] + left[noseStart]);
            junctionRight[noseStart] = reflectRight * left[noseStart] + (1 + reflectRight) * (right[noseStart - 1] + noseLeft[0]);
            noseJunctionRight[0] = reflectNose * noseLeft[0] + (1 + reflectNose) * (left[noseStart] + right[noseStart - 1]);

            for (var i = 0; i < Sections; i++)
            {
                right[i] = junctionRight[i] * Loss;
                left[i] = junctionLeft[i + 1] * Loss;
            }

            var lipOutput = right[Sections - 1];
            noseJunctionLeft[NoseSections] = noseRight[NoseSections - 1] * controls.LipReflection;
            for (var i = 1; i < NoseSections; i++)
            {
                var wave = noseReflection[i] * (noseRight[i - 1] + noseLeft[i]);
                noseJunctionRight[i] = noseRight[i - 1] - wave;
                noseJunctionLeft[i] = noseLeft[i] + wave;
            }

            for (var i = 0; i < NoseSections; i++)
            {
                noseRight[i] = noseJunctionRight[i] * Loss;
                noseLeft[i] = noseJunctionLeft[i + 1] * Loss;
            }

            return lipOutput * (0.85f + controls.LipOpening * 0.28f) +
                   noseRight[NoseSections - 1] * Math.Clamp(controls.Velum / 0.4f, 0, 1);
        }

        private void UpdateTransient(PinkTromboneFixtureControls controls)
        {
            var opening = controls.ConstrictionDiameter - lastConstrictionDiameter;
            if (opening > 0.18f && lastConstrictionDiameter < 0.28f)
            {
                transient += opening * controls.Intensity * (0.18f + controls.Turbulence * 0.42f) * controls.Burst;
            }

            lastConstrictionDiameter = controls.ConstrictionDiameter;
        }

        private float Condition(float output)
        {
            var blocked = output - dcBlockX + 0.995f * dcBlockY;
            dcBlockX = output;
            dcBlockY = blocked;
            return MathF.Tanh(blocked * 1.9f);
        }
    }
}
