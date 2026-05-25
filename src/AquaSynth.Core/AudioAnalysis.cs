using System.Numerics;

namespace AquaSynth.Dsl;

public sealed record AudioAnalysisConfig(
    float SampleRate = 44100,
    float GateFloor = 0.0005f,
    float GateRatio = 0.03f,
    int FftSize = 512,
    int HopSize = 128,
    int MelBandCount = 32,
    float MinFrequencyHz = 40,
    float MaxFrequencyHz = 16000);

public sealed record AudioFeatures(
    float AttackSeconds,
    float DurationSeconds,
    float Peak,
    float Rms,
    float ZeroCrossingRate,
    float SpectralCentroidHz,
    float SpectralRolloffHz);

public sealed record Spectrogram(int Frames, int Bands, float[] Values)
{
    public float At(int frame, int band) => Values[frame * Bands + band];
}

public sealed record AudioAnalysis(AudioFeatures Features, Spectrogram LogMelSpectrogram, float[] RmsEnvelope);

public sealed record AudioComparison(
    AudioAnalysis Reference,
    AudioAnalysis Candidate,
    float DurationRatio,
    float RmsRatio,
    float ZeroCrossingRatio,
    float CentroidRatio,
    float EnvelopeDistance,
    float LogMelDistance,
    float LogMelCosineSimilarity,
    float Score);

public sealed class AudioAnalyzer
{
    private readonly AudioAnalysisConfig _config;
    private readonly int _fftSize;
    private readonly Complex[] _fftBuffer;
    private readonly float[] _spectrumBuffer;
    private readonly int[] _melEdges;

    public AudioAnalyzer(AudioAnalysisConfig? config = null)
    {
        _config = config ?? new AudioAnalysisConfig();
        _fftSize = NextPowerOfTwo(Math.Max(32, _config.FftSize));
        _fftBuffer = new Complex[_fftSize];
        _spectrumBuffer = new float[_fftSize / 2 + 1];
        _melEdges = MelBandEdges(Math.Max(1, _config.MelBandCount), _fftSize, _config.SampleRate, _config.MinFrequencyHz, _config.MaxFrequencyHz);
    }

    public AudioAnalysisConfig Config => _config;

    public AudioAnalysis Analyze(ReadOnlySpan<float> samples)
    {
        var features = ExtractFeatures(samples);
        var envelope = RmsEnvelope(samples, _fftSize, _config.HopSize);
        var spectrogram = LogMelSpectrogram(samples);
        return new AudioAnalysis(features, spectrogram, envelope);
    }

    public AudioComparison Compare(ReadOnlySpan<float> referenceSamples, ReadOnlySpan<float> candidateSamples) =>
        FromAnalysis(Analyze(referenceSamples), Analyze(candidateSamples));

    private AudioFeatures ExtractFeatures(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples) peak = Math.Max(peak, Math.Abs(sample));
        var gate = Math.Max(peak * _config.GateRatio, _config.GateFloor);

        var first = 0;
        while (first < samples.Length && Math.Abs(samples[first]) < gate) first++;
        if (first >= samples.Length) first = 0;

        var last = samples.Length - 1;
        while (last > first && Math.Abs(samples[last]) < gate) last--;

        var active = samples.Length == 0 ? ReadOnlySpan<float>.Empty : samples[first..(last + 1)];
        var durationSeconds = (last - first + (samples.Length == 0 ? 0 : 1)) / Math.Max(1, _config.SampleRate);
        var zeroCrossings = 0;
        for (var i = 1; i < active.Length; i++)
        {
            if (MathF.Sign(active[i - 1]) != MathF.Sign(active[i])) zeroCrossings++;
        }

        var spectrum = AveragePowerSpectrum(active);
        var shape = SpectralShape(spectrum, _config.SampleRate, 0.85f);
        return new AudioFeatures(
            first / Math.Max(1, _config.SampleRate),
            durationSeconds,
            peak,
            MathF.Sqrt(MeanSquare(active)),
            zeroCrossings / Math.Max(durationSeconds, 1 / _config.SampleRate),
            shape.CentroidHz,
            shape.RolloffHz);
    }

    private Spectrogram LogMelSpectrogram(ReadOnlySpan<float> samples)
    {
        var bands = Math.Max(1, _config.MelBandCount);
        if (samples.IsEmpty) return new Spectrogram(1, bands, new float[bands]);

        var values = new List<float>();
        var frames = 0;
        for (var start = 0; start < samples.Length; start += Math.Max(1, _config.HopSize))
        {
            WriteFramePowerSpectrum(samples, start);
            for (var band = 0; band < bands; band++)
            {
                var left = _melEdges[band];
                var center = Math.Max(_melEdges[band + 1], left + 1);
                var right = Math.Max(_melEdges[band + 2], center + 1);
                var energy = 0f;
                var weightSum = 0f;
                for (var bin = left; bin < Math.Min(right, _spectrumBuffer.Length); bin++)
                {
                    var weight = Math.Max(0, bin <= center
                        ? (bin - left) / (float)Math.Max(1, center - left)
                        : (right - bin) / (float)Math.Max(1, right - center));
                    energy += _spectrumBuffer[bin] * weight;
                    weightSum += weight;
                }
                values.Add(MathF.Log(energy / Math.Max(1, weightSum) + 1e-9f));
            }
            frames++;
        }

        var array = values.ToArray();
        NormalizeInPlace(array);
        return new Spectrogram(frames, bands, array);
    }

    private float[] AveragePowerSpectrum(ReadOnlySpan<float> samples)
    {
        var sum = new float[_fftSize / 2 + 1];
        if (samples.IsEmpty) return sum;

        var frames = 0f;
        for (var start = 0; start < samples.Length; start += Math.Max(1, _fftSize / 2))
        {
            WriteFramePowerSpectrum(samples, start);
            for (var i = 0; i < sum.Length; i++) sum[i] += _spectrumBuffer[i];
            frames++;
        }

        for (var i = 0; i < sum.Length; i++) sum[i] /= frames;
        return sum;
    }

    private void WriteFramePowerSpectrum(ReadOnlySpan<float> samples, int start)
    {
        for (var i = 0; i < _fftSize; i++)
        {
            var sample = start + i < samples.Length ? samples[start + i] : 0;
            _fftBuffer[i] = new Complex(sample * Hann(i, _fftSize), 0);
        }

        Fft(_fftBuffer);
        for (var bin = 0; bin < _spectrumBuffer.Length; bin++)
        {
            _spectrumBuffer[bin] = (float)(_fftBuffer[bin].Real * _fftBuffer[bin].Real + _fftBuffer[bin].Imaginary * _fftBuffer[bin].Imaginary);
        }
    }

    public static AudioAnalysis AnalyzeAudio(ReadOnlySpan<float> samples, AudioAnalysisConfig? config = null) =>
        new AudioAnalyzer(config).Analyze(samples);

    public static AudioComparison CompareAudio(ReadOnlySpan<float> referenceSamples, ReadOnlySpan<float> candidateSamples, AudioAnalysisConfig? config = null) =>
        new AudioAnalyzer(config).Compare(referenceSamples, candidateSamples);

    private static AudioComparison FromAnalysis(AudioAnalysis reference, AudioAnalysis candidate)
    {
        var durationRatio = SafeRatio(candidate.Features.DurationSeconds, reference.Features.DurationSeconds);
        var rmsRatio = SafeRatio(candidate.Features.Rms, reference.Features.Rms);
        var zeroCrossingRatio = SafeRatio(candidate.Features.ZeroCrossingRate, reference.Features.ZeroCrossingRate);
        var centroidRatio = SafeRatio(candidate.Features.SpectralCentroidHz, reference.Features.SpectralCentroidHz);
        var envelopeDistance = NormalizedDistance(reference.RmsEnvelope, candidate.RmsEnvelope);
        var logMelDistance = NormalizedDistance(reference.LogMelSpectrogram.Values, candidate.LogMelSpectrogram.Values);
        var logMelCosine = CosineSimilarity(reference.LogMelSpectrogram.Values, candidate.LogMelSpectrogram.Values);
        var ratioPenalty = RatioDistance(durationRatio) + RatioDistance(rmsRatio) + RatioDistance(zeroCrossingRatio) * 0.5f + RatioDistance(centroidRatio) * 0.5f;
        var score = 1 / (1 + envelopeDistance * 0.7f + logMelDistance + ratioPenalty);
        return new AudioComparison(reference, candidate, durationRatio, rmsRatio, zeroCrossingRatio, centroidRatio, envelopeDistance, logMelDistance, logMelCosine, score);
    }

    private static void Fft(Complex[] buffer)
    {
        var n = buffer.Length;
        for (var i = 1; i < n; i++)
        {
            var bit = n >> 1;
            var j = i;
            while ((j & bit) != 0) j ^= bit;
            j ^= bit;
            if (i < j) (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            var wLen = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (var j = 0; j < len / 2; j++)
                {
                    var u = buffer[i + j];
                    var v = buffer[i + j + len / 2] * w;
                    buffer[i + j] = u + v;
                    buffer[i + j + len / 2] = u - v;
                    w *= wLen;
                }
            }
        }
    }

    private static float[] RmsEnvelope(ReadOnlySpan<float> samples, int windowSize, int hopSize)
    {
        windowSize = Math.Max(16, windowSize);
        hopSize = Math.Max(1, hopSize);
        if (samples.IsEmpty) return [0];

        var envelope = new List<float>();
        for (var start = 0; start < samples.Length; start += hopSize)
        {
            var end = Math.Min(start + windowSize, samples.Length);
            envelope.Add(MathF.Sqrt(MeanSquare(samples[start..end])));
        }
        return envelope.ToArray();
    }

    private static int[] MelBandEdges(int bandCount, int fftSize, float sampleRate, float minFrequencyHz, float maxFrequencyHz)
    {
        var minMel = HzToMel(Math.Max(0, minFrequencyHz));
        var maxMel = HzToMel(Math.Max(minFrequencyHz + 1, Math.Min(maxFrequencyHz, sampleRate * 0.5f)));
        return Enumerable.Range(0, bandCount + 2)
            .Select(index =>
            {
                var t = index / (float)(bandCount + 1);
                var hz = MelToHz(minMel + (maxMel - minMel) * t);
                return (int)MathF.Round(hz / Math.Max(1, sampleRate) * fftSize);
            })
            .ToArray();
    }

    private static (float CentroidHz, float RolloffHz) SpectralShape(IReadOnlyList<float> spectrum, float sampleRate, float rolloffPortion)
    {
        var total = spectrum.Sum();
        if (total <= float.Epsilon) return (0, 0);

        var weighted = 0f;
        var cumulative = 0f;
        var rolloff = 0f;
        for (var bin = 0; bin < spectrum.Count; bin++)
        {
            var frequency = bin * sampleRate * 0.5f / Math.Max(1, spectrum.Count - 1);
            weighted += frequency * spectrum[bin];
            cumulative += spectrum[bin];
            if (rolloff == 0 && cumulative >= total * rolloffPortion) rolloff = frequency;
        }
        return (weighted / total, rolloff);
    }

    private static float NormalizedDistance(IReadOnlyList<float> reference, IReadOnlyList<float> candidate)
    {
        var length = Math.Max(1, Math.Max(reference.Count, candidate.Count));
        var error = 0f;
        var scale = 0f;
        for (var i = 0; i < length; i++)
        {
            var a = ResampledAt(reference, i, length);
            var b = ResampledAt(candidate, i, length);
            var delta = a - b;
            error += delta * delta;
            scale += a * a + b * b;
        }
        return MathF.Sqrt(error / Math.Max(float.Epsilon, scale));
    }

    public static float CosineSimilarity(IReadOnlyList<float> reference, IReadOnlyList<float> candidate)
    {
        var length = Math.Max(1, Math.Max(reference.Count, candidate.Count));
        var dot = 0f;
        var left = 0f;
        var right = 0f;
        for (var i = 0; i < length; i++)
        {
            var a = ResampledAt(reference, i, length);
            var b = ResampledAt(candidate, i, length);
            dot += a * b;
            left += a * a;
            right += b * b;
        }

        return dot / MathF.Max(0.000001f, MathF.Sqrt(left) * MathF.Sqrt(right));
    }

    private static float ResampledAt(IReadOnlyList<float> values, int index, int targetLength)
    {
        if (values.Count == 0) return 0;
        if (values.Count == 1 || targetLength <= 1) return values[0];
        var position = index * (values.Count - 1f) / (targetLength - 1);
        var left = (int)MathF.Floor(position);
        var right = Math.Min(left + 1, values.Count - 1);
        var t = position - left;
        return values[left] * (1 - t) + values[right] * t;
    }

    private static void NormalizeInPlace(float[] values)
    {
        if (values.Length == 0) return;
        var mean = values.Sum() / values.Length;
        var variance = values.Select(value => (value - mean) * (value - mean)).Sum() / values.Length;
        var scale = MathF.Sqrt(variance).ClampMin(1e-6f);
        for (var i = 0; i < values.Length; i++) values[i] = (values[i] - mean) / scale;
    }

    private static float MeanSquare(ReadOnlySpan<float> samples)
    {
        var sum = 0f;
        foreach (var sample in samples) sum += sample * sample;
        return sum / Math.Max(1, samples.Length);
    }

    private static float Hann(int index, int size) => size <= 1 ? 1 : 0.5f - 0.5f * MathF.Cos(MathF.Tau * index / (size - 1));
    private static float HzToMel(float hz) => 2595 * MathF.Log10(1 + hz / 700);
    private static float MelToHz(float mel) => 700 * (MathF.Pow(10, mel / 2595) - 1);
    private static float SafeRatio(float candidate, float reference) => candidate / Math.Max(float.Epsilon, reference);
    private static float RatioDistance(float ratio) => MathF.Abs(MathF.Log(Math.Max(float.Epsilon, ratio)));
    private static int NextPowerOfTwo(int value) => 1 << (int)MathF.Ceiling(MathF.Log2(value));
}

file static class FloatExtensions
{
    public static float ClampMin(this float value, float min) => Math.Max(value, min);
}
