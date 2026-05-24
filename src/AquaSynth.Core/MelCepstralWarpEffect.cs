using System.Numerics;

namespace AquaSynth.Dsl;

public sealed record MelCepstralWarpEffectOptions(
    int SampleRate = 44_100,
    int FftSize = 2048,
    int HopSize = 512,
    int MelBandCount = 48,
    int CepstralCoefficientCount = 20,
    float MinFrequencyHz = 180,
    float MaxFrequencyHz = 15_000,
    float WarpFrames = 0.0f,
    float WarpCoefficients = 0.0f,
    int BlurPasses = 0,
    float Wet = 1.0f);

public sealed class MelCepstralWarpEffect
{
    private readonly MelCepstralWarpEffectOptions options;
    private readonly int fftSize;
    private readonly double[] window;
    private readonly double[][] melFilters;
    private readonly double[] melNormalizer;

    public MelCepstralWarpEffect(MelCepstralWarpEffectOptions? options = null)
    {
        this.options = options ?? new MelCepstralWarpEffectOptions();
        fftSize = NextPowerOfTwo(Math.Max(32, this.options.FftSize));
        window = HannWindow(fftSize);
        melFilters = BuildMelFilterBank(
            Math.Max(1, this.options.MelBandCount),
            fftSize,
            Math.Max(1, this.options.SampleRate),
            Math.Max(1, this.options.MinFrequencyHz),
            Math.Min(this.options.MaxFrequencyHz, this.options.SampleRate * 0.5f));
        melNormalizer = melFilters.Select(filter => Math.Max(1.0e-12, filter.Sum())).ToArray();
    }

    public float[] Process(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return [];
        }

        var hopSize = Math.Max(1, options.HopSize);
        var frameCount = Math.Max(1, 1 + Math.Max(0, samples.Length - fftSize) / hopSize);
        var spectra = new Complex[frameCount][];
        var cepstra = new double[frameCount, options.CepstralCoefficientCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var offset = frame * hopSize;
            var spectrum = new Complex[fftSize];
            for (var index = 0; index < fftSize; index++)
            {
                var sampleIndex = offset + index;
                var sample = sampleIndex < samples.Length ? samples[sampleIndex] : 0.0f;
                spectrum[index] = new Complex(sample * window[index], 0.0);
            }

            FastFourierTransform(spectrum, inverse: false);
            spectra[frame] = spectrum;
            var cepstrum = Dct(SpectrumToLogMel(spectrum), options.CepstralCoefficientCount);
            for (var coefficient = 0; coefficient < options.CepstralCoefficientCount; coefficient++)
            {
                cepstra[frame, coefficient] = cepstrum[coefficient];
            }
        }

        var transformed = WarpCepstrum(cepstra);
        for (var pass = 0; pass < options.BlurPasses; pass++)
        {
            transformed = BlurCepstrum5Tap(transformed);
        }

        var output = Resynthesize(samples.Length, hopSize, spectra, transformed);
        var wet = Math.Clamp(options.Wet, 0.0f, 1.0f);
        if (wet < 1.0f)
        {
            for (var index = 0; index < output.Length; index++)
            {
                output[index] = output[index] * wet + samples[index] * (1.0f - wet);
            }
        }

        MatchRms(samples, output);
        return output;
    }

    private float[] Resynthesize(int sampleCount, int hopSize, IReadOnlyList<Complex[]> spectra, double[,] cepstra)
    {
        var output = new double[Math.Max(sampleCount, (spectra.Count - 1) * hopSize + fftSize)];
        var weights = new double[output.Length];
        for (var frame = 0; frame < spectra.Count; frame++)
        {
            var logMel = InverseDct(Row(cepstra, frame), options.MelBandCount);
            var magnitudes = LogMelToMagnitude(logMel);
            var spectrum = new Complex[fftSize];
            for (var bin = 0; bin <= fftSize / 2; bin++)
            {
                var original = spectra[frame][bin];
                var phase = original.Magnitude <= 1.0e-12 ? Complex.One : original / original.Magnitude;
                spectrum[bin] = phase * magnitudes[bin];
                if (bin > 0 && bin < fftSize / 2)
                {
                    spectrum[fftSize - bin] = Complex.Conjugate(spectrum[bin]);
                }
            }

            FastFourierTransform(spectrum, inverse: true);
            var offset = frame * hopSize;
            for (var index = 0; index < fftSize && offset + index < output.Length; index++)
            {
                output[offset + index] += spectrum[index].Real * window[index];
                weights[offset + index] += window[index] * window[index];
            }
        }

        var samples = new float[sampleCount];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = weights[index] <= 1.0e-12
                ? 0.0f
                : (float)(output[index] / weights[index]);
        }

        return samples;
    }

    private double[] SpectrumToLogMel(Complex[] spectrum)
    {
        var magnitudes = new double[spectrum.Length / 2 + 1];
        for (var bin = 0; bin < magnitudes.Length; bin++)
        {
            magnitudes[bin] = spectrum[bin].Magnitude;
        }

        var logMel = new double[melFilters.Length];
        for (var mel = 0; mel < melFilters.Length; mel++)
        {
            var energy = 0.0;
            for (var bin = 0; bin < magnitudes.Length; bin++)
            {
                energy += magnitudes[bin] * melFilters[mel][bin];
            }

            logMel[mel] = Math.Log(1.0e-7 + energy / melNormalizer[mel]);
        }

        return logMel;
    }

    private double[] LogMelToMagnitude(IReadOnlyList<double> logMel)
    {
        var magnitudes = new double[fftSize / 2 + 1];
        var weights = new double[magnitudes.Length];
        for (var mel = 0; mel < melFilters.Length; mel++)
        {
            var value = Math.Exp(Math.Clamp(logMel[mel], -24.0, 6.0));
            for (var bin = 0; bin < magnitudes.Length; bin++)
            {
                var weight = melFilters[mel][bin];
                magnitudes[bin] += value * weight;
                weights[bin] += weight;
            }
        }

        for (var bin = 0; bin < magnitudes.Length; bin++)
        {
            magnitudes[bin] = weights[bin] <= 1.0e-12 ? 0.0 : magnitudes[bin] / weights[bin];
        }

        return magnitudes;
    }

    private double[,] WarpCepstrum(double[,] input)
    {
        var frames = input.GetLength(0);
        var coefficients = input.GetLength(1);
        var output = new double[frames, coefficients];
        for (var frame = 0; frame < frames; frame++)
        {
            for (var coefficient = 0; coefficient < coefficients; coefficient++)
            {
                var warpT = options.WarpFrames * SimplexNoise2D(frame * 0.071, coefficient * 0.137 + 19.0);
                var warpC = options.WarpCoefficients * SimplexNoise2D(frame * 0.053 + 41.0, coefficient * 0.113);
                output[frame, coefficient] = SampleCepstrumBilinear(input, frame + warpT, coefficient + warpC);
            }
        }

        return output;
    }

    private static double[,] BlurCepstrum5Tap(double[,] input)
    {
        var kernel = new[] { 1.0, 4.0, 6.0, 4.0, 1.0 };
        var frames = input.GetLength(0);
        var coefficients = input.GetLength(1);
        var temp = new double[frames, coefficients];
        var output = new double[frames, coefficients];
        for (var frame = 0; frame < frames; frame++)
        {
            for (var coefficient = 0; coefficient < coefficients; coefficient++)
            {
                var sum = 0.0;
                for (var tap = -2; tap <= 2; tap++)
                {
                    sum += input[frame, Math.Clamp(coefficient + tap, 0, coefficients - 1)] * kernel[tap + 2];
                }

                temp[frame, coefficient] = sum / 16.0;
            }
        }

        for (var frame = 0; frame < frames; frame++)
        {
            for (var coefficient = 0; coefficient < coefficients; coefficient++)
            {
                var sum = 0.0;
                for (var tap = -2; tap <= 2; tap++)
                {
                    sum += temp[Math.Clamp(frame + tap, 0, frames - 1), coefficient] * kernel[tap + 2];
                }

                output[frame, coefficient] = sum / 16.0;
            }
        }

        return output;
    }

    private static double SampleCepstrumBilinear(double[,] input, double frame, double coefficient)
    {
        var frames = input.GetLength(0);
        var coefficients = input.GetLength(1);
        var f0 = Math.Clamp((int)Math.Floor(frame), 0, frames - 1);
        var c0 = Math.Clamp((int)Math.Floor(coefficient), 0, coefficients - 1);
        var f1 = Math.Clamp(f0 + 1, 0, frames - 1);
        var c1 = Math.Clamp(c0 + 1, 0, coefficients - 1);
        var ft = Math.Clamp(frame - Math.Floor(frame), 0.0, 1.0);
        var ct = Math.Clamp(coefficient - Math.Floor(coefficient), 0.0, 1.0);
        var a = input[f0, c0] * (1.0 - ct) + input[f0, c1] * ct;
        var b = input[f1, c0] * (1.0 - ct) + input[f1, c1] * ct;
        return a * (1.0 - ft) + b * ft;
    }

    private static double[][] BuildMelFilterBank(int melBins, int fftSize, int sampleRate, double minHz, double maxHz)
    {
        var minMel = HzToMel(minHz);
        var maxMel = HzToMel(maxHz);
        var points = Enumerable.Range(0, melBins + 2)
            .Select(index => MelToHz(minMel + (maxMel - minMel) * index / (melBins + 1)))
            .Select(hz => Math.Clamp((int)Math.Round(hz / sampleRate * fftSize), 0, fftSize / 2))
            .ToArray();
        var filters = new double[melBins][];
        for (var mel = 0; mel < melBins; mel++)
        {
            filters[mel] = new double[fftSize / 2 + 1];
            var left = points[mel];
            var center = Math.Max(points[mel + 1], left + 1);
            var right = Math.Max(points[mel + 2], center + 1);
            for (var bin = left; bin <= right && bin < filters[mel].Length; bin++)
            {
                filters[mel][bin] = bin <= center
                    ? (bin - left) / (double)Math.Max(1, center - left)
                    : (right - bin) / (double)Math.Max(1, right - center);
            }
        }

        return filters;
    }

    private static double[] Dct(IReadOnlyList<double> values, int coefficientCount)
    {
        var output = new double[coefficientCount];
        var scale = Math.PI / values.Count;
        for (var coefficient = 0; coefficient < coefficientCount; coefficient++)
        {
            for (var index = 0; index < values.Count; index++)
            {
                output[coefficient] += values[index] * Math.Cos(scale * (index + 0.5) * coefficient);
            }
        }

        return output;
    }

    private static double[] InverseDct(IReadOnlyList<double> coefficients, int valueCount)
    {
        var output = new double[valueCount];
        var scale = Math.PI / valueCount;
        for (var index = 0; index < valueCount; index++)
        {
            var sum = coefficients[0] / valueCount;
            for (var coefficient = 1; coefficient < coefficients.Count; coefficient++)
            {
                sum += 2.0 * coefficients[coefficient] * Math.Cos(scale * (index + 0.5) * coefficient) / valueCount;
            }

            output[index] = sum;
        }

        return output;
    }

    private static double[] Row(double[,] matrix, int row)
    {
        var values = new double[matrix.GetLength(1)];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = matrix[row, index];
        }

        return values;
    }

    private static void MatchRms(ReadOnlySpan<float> source, float[] output)
    {
        var sourceRms = Rms(source);
        var outputRms = Rms(output);
        if (sourceRms <= 1.0e-9 || outputRms <= 1.0e-9)
        {
            return;
        }

        var gain = sourceRms / outputRms;
        for (var index = 0; index < output.Length; index++)
        {
            output[index] = (float)Math.Clamp(output[index] * gain, -1.0, 1.0);
        }
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0.0;
        }

        var sum = 0.0;
        foreach (var sample in samples)
        {
            sum += sample * sample;
        }

        return Math.Sqrt(sum / samples.Length);
    }

    private static double[] HannWindow(int length)
    {
        var window = new double[length];
        for (var index = 0; index < length; index++)
        {
            window[index] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * index / Math.Max(1, length - 1));
        }

        return window;
    }

    private static double HzToMel(double hz) => 2595.0 * Math.Log10(1.0 + hz / 700.0);

    private static double MelToHz(double mel) => 700.0 * (Math.Pow(10.0, mel / 2595.0) - 1.0);

    private static double SimplexNoise2D(double x, double y)
    {
        const double f2 = 0.3660254037844386;
        const double g2 = 0.21132486540518713;
        var s = (x + y) * f2;
        var i = FastFloor(x + s);
        var j = FastFloor(y + s);
        var t = (i + j) * g2;
        var x0 = x - (i - t);
        var y0 = y - (j - t);
        var i1 = x0 > y0 ? 1 : 0;
        var j1 = x0 > y0 ? 0 : 1;
        return 70.0 * (
            SimplexCorner(i, j, x0, y0) +
            SimplexCorner(i + i1, j + j1, x0 - i1 + g2, y0 - j1 + g2) +
            SimplexCorner(i + 1, j + 1, x0 - 1.0 + 2.0 * g2, y0 - 1.0 + 2.0 * g2));
    }

    private static double SimplexCorner(int i, int j, double x, double y)
    {
        var t = 0.5 - x * x - y * y;
        if (t < 0.0)
        {
            return 0.0;
        }

        var hash = Hash2D(i, j) & 7;
        var gx = hash < 4 ? 1.0 : 2.0;
        var gy = hash < 4 ? 2.0 : 1.0;
        if ((hash & 1) != 0) gx = -gx;
        if ((hash & 2) != 0) gy = -gy;
        t *= t;
        return t * t * (gx * x + gy * y);
    }

    private static int Hash2D(int x, int y)
    {
        unchecked
        {
            var hash = x * 0x1f1f1f1f ^ y * 0x5f356495;
            hash ^= hash >> 16;
            hash *= 0x45d9f3b;
            hash ^= hash >> 16;
            return hash;
        }
    }

    private static int FastFloor(double value) => value >= 0.0 ? (int)value : (int)value - 1;

    private static int NextPowerOfTwo(int value)
    {
        var power = 1;
        while (power < value) power <<= 1;
        return power;
    }

    private static void FastFourierTransform(Complex[] values, bool inverse)
    {
        var j = 0;
        for (var i = 1; i < values.Length; i++)
        {
            var bit = values.Length >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (values[i], values[j]) = (values[j], values[i]);
        }

        for (var length = 2; length <= values.Length; length <<= 1)
        {
            var angle = 2.0 * Math.PI / length * (inverse ? 1.0 : -1.0);
            var wLength = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var i = 0; i < values.Length; i += length)
            {
                var w = Complex.One;
                for (var k = 0; k < length / 2; k++)
                {
                    var even = values[i + k];
                    var odd = values[i + k + length / 2] * w;
                    values[i + k] = even + odd;
                    values[i + k + length / 2] = even - odd;
                    w *= wLength;
                }
            }
        }

        if (!inverse) return;
        for (var i = 0; i < values.Length; i++) values[i] /= values.Length;
    }
}
