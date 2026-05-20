using System.Diagnostics;
using System.Text;
using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

public sealed class EspeakNgGradientDescentTests
{
    [Fact]
    public async Task EspeakNgGroundTruthTrainsUtteranceEmbeddingAndSynthDriverWhenInstalled()
    {
        if (!EspeakNgReferenceRenderer.TryFind(out var renderer))
        {
            return;
        }

        var root = RepositoryRoot();
        var artifactDir = Path.Combine(root, "artifacts", "parity", "espeak-ng-gradient-descent", RunFolderName("tiny"));
        Directory.CreateDirectory(artifactDir);

        var fixtures = Fixtures();
        var groundTruth = new List<GroundTruthFixture>();
        foreach (var fixture in fixtures)
        {
            var wavPath = Path.Combine(artifactDir, $"{fixture.Id}.wav");
            var render = await renderer.RenderAsync(fixture.Text, fixture.Voice, wavPath);
            Assert.Equal(0, render.ExitCode);
            Assert.True(File.Exists(wavPath), $"eSpeak NG did not write {wavPath}.");

            var samples = ReadMonoPcm16Wav(wavPath);
            var analysis = AudioAnalyzer.AnalyzeAudio(samples);
            var logMelMean = MeanBands(analysis.LogMelSpectrogram);
            groundTruth.Add(new GroundTruthFixture(fixture, logMelMean));
        }

        var utteranceEncoder = UtteranceEmbeddingNeuralEncoder.Create(
            inputSize: groundTruth[0].Fixture.UtteranceInput.ToFeatureVector().Values.Count,
            embeddingSize: 16,
            hiddenLayerSizes: [64, 48, 32],
            seed: 101);
        var vocalMapper = VocalTractNeuralMapper.Create(semanticEmbeddingSize: 16, melBandCount: 32, hiddenLayerSizes: [80, 56, 40], seed: 103);
        var pipeline = new SpeechBackpropagationPipeline(utteranceEncoder, vocalMapper);
        var examples = groundTruth
            .Select(item => new SpeechBackpropagationTrainingExample(
                item.Fixture.UtteranceInput,
                item.Fixture.Event,
                TargetFromGroundTruth(item.Fixture.Event, item.LogMelMean)))
            .ToArray();

        var pipelineBefore = AveragePipelineLoss(pipeline, examples);
        var pipelineResult = pipeline.Train(examples, new SpeechBackpropagationTrainingOptions(
            Epochs: 640,
            UtteranceLearningRate: 0.04f,
            SynthDriverLearningRate: 0.04f,
            Seed: 107));
        var pipelineAfter = AveragePipelineLoss(pipeline, examples);

        Assert.True(pipelineAfter < pipelineBefore * 0.55f, $"end-to-end speech pipeline loss did not drop enough; before={pipelineBefore}, after={pipelineAfter}");
        Assert.True(pipelineResult.Steps[^1].Loss < pipelineResult.Steps[0].Loss, "end-to-end pipeline loss should decrease across epochs");

        var report = new List<string>
        {
            "eSpeak NG gradient descent fixture",
            $"renderer: {renderer.CommandPath}",
            "role: generated supervised ground truth for end-to-end AquaSynth utterance embedding -> synth-driver backpropagation",
            $"pipeline_loss_before: {pipelineBefore:0.######}",
            $"pipeline_loss_after: {pipelineAfter:0.######}",
            $"pipeline_epochs: {pipelineResult.Steps.Count}",
            "fixtures:"
        };
        foreach (var item in groundTruth)
        {
            report.Add($"  {item.Fixture.Id}: text={item.Fixture.Text}, mel={FormatBands(item.LogMelMean)}");
        }

        await File.WriteAllLinesAsync(Path.Combine(artifactDir, "training-report.md"), report);
    }

    private static IReadOnlyList<TrainingFixture> Fixtures() =>
    [
        Fixture("open-vowel", "a", new PhoneticFeatures(PhoneticManner.Vowel, Height: VowelHeight.Open, Backness: VowelBackness.Front), [0.90f, 0.15f, 0.20f, 0.70f, 0.40f, 0.10f, 0.25f, 0.55f]),
        Fixture("bilabial-open", "pa", new PhoneticFeatures(PhoneticManner.Stop, PhoneticPlace.Bilabial, Phonation.Voiceless), [0.80f, 0.70f, 0.22f, 0.65f, 0.35f, 0.12f, 0.40f, 0.52f]),
        Fixture("alveolar-open", "ta", new PhoneticFeatures(PhoneticManner.Stop, PhoneticPlace.Alveolar, Phonation.Voiceless), [0.74f, 0.62f, 0.35f, 0.58f, 0.42f, 0.16f, 0.45f, 0.49f]),
        Fixture("velar-open", "ka", new PhoneticFeatures(PhoneticManner.Stop, PhoneticPlace.Velar, Phonation.Voiceless), [0.68f, 0.55f, 0.48f, 0.52f, 0.46f, 0.18f, 0.52f, 0.45f]),
        Fixture("sibilant-open", "sa", new PhoneticFeatures(PhoneticManner.Fricative, PhoneticPlace.Alveolar, Phonation.Voiceless), [0.55f, 0.38f, 0.86f, 0.40f, 0.60f, 0.25f, 0.72f, 0.36f]),
        Fixture("nasal-open", "ma", new PhoneticFeatures(PhoneticManner.Nasal, PhoneticPlace.Bilabial, Phonation.Voiced, Nasalized: true), [0.82f, 0.48f, 0.28f, 0.74f, 0.38f, 0.58f, 0.30f, 0.62f])
    ];

    private static TrainingFixture Fixture(string id, string text, PhoneticFeatures features, IReadOnlyList<float> textEmbedding)
    {
        var prosody = new[] { 0.75f, text.Length / 4f, features.Manner == PhoneticManner.Fricative ? 0.95f : 0.35f, features.Manner == PhoneticManner.Nasal ? 0.80f : 0.25f };
        var phoneticRealization = new[]
        {
            features.Manner == PhoneticManner.Vowel ? 1f : 0f,
            features.Manner == PhoneticManner.Stop ? 1f : 0f,
            features.Manner == PhoneticManner.Fricative ? 1f : 0f,
            features.Manner == PhoneticManner.Nasal ? 1f : 0f,
            features.Place == PhoneticPlace.Bilabial ? 1f : 0f,
            features.Place == PhoneticPlace.Alveolar ? 1f : 0f,
            features.Place == PhoneticPlace.Velar ? 1f : 0f,
            features.Phonation == Phonation.Voiced ? 1f : 0f
        };
        var characterState = new[] { 0.65f, 0.35f, 0.55f, features.Place == PhoneticPlace.Bilabial ? 0.72f : 0.28f };
        var input = new UtteranceEmbeddingInput(textEmbedding, phoneticRealization, prosody, characterState);
        var phoneticEvent = new PhoneticEvent(id, text, features, DurationSeconds: 0.20, Prosody: new PhoneticProsody(Stress: 0.75f, Intensity: 0.8f));
        return new TrainingFixture(id, text, "en", phoneticEvent, input);
    }

    private static VocalTractControlTarget TargetFromGroundTruth(PhoneticEvent phoneticEvent, IReadOnlyList<float> logMelMean)
    {
        var isStop = phoneticEvent.Features.Manner == PhoneticManner.Stop;
        var isFricative = phoneticEvent.Features.Manner == PhoneticManner.Fricative;
        var isNasal = phoneticEvent.Features.Manner == PhoneticManner.Nasal;
        var lipAperture = phoneticEvent.Features.Place == PhoneticPlace.Bilabial ? 0.22f : 0.58f;
        return new VocalTractControlTarget(
            TongueBody: phoneticEvent.Features.Place == PhoneticPlace.Velar ? 0.72f : 0.44f,
            TongueTip: phoneticEvent.Features.Place == PhoneticPlace.Alveolar ? 0.86f : 0.34f,
            LipAperture: phoneticEvent.Features.Manner == PhoneticManner.Vowel ? 0.88f : lipAperture,
            LipRounding: 0.05f,
            Velum: isNasal ? 0.85f : 0.0f,
            GlottalTenseness: phoneticEvent.Features.Phonation == Phonation.Voiceless ? 0.20f : 0.52f,
            Turbulence: isFricative ? 0.94f : isStop ? 0.55f : 0.05f,
            Pressure: isStop ? 0.86f : isFricative ? 0.72f : 0.38f,
            AmDepth: isNasal ? 0.30f : 0.12f,
            FmDepth: isFricative ? 0.42f : 0.16f,
            LfoRate: 0.20f,
            LfoDepth: isNasal ? 0.24f : 0.10f,
            FilterCutoff: isFricative ? 0.82f : 0.55f,
            FilterResonance: isFricative ? 0.62f : 0.22f,
            MelSpectralEnvelope: logMelMean);
    }

    private static float AveragePipelineLoss(SpeechBackpropagationPipeline pipeline, IReadOnlyList<SpeechBackpropagationTrainingExample> examples)
    {
        var loss = 0f;
        foreach (var example in examples)
        {
            var prediction = pipeline.Predict(example);
            loss += MeanSquaredError(prediction.ToVector(pipeline.SynthDriver.MelBandCount), example.Target.ToVector(pipeline.SynthDriver.MelBandCount));
        }

        return loss / examples.Count;
    }

    private static float MeanSquaredError(IReadOnlyList<float> actual, IReadOnlyList<float> target)
    {
        var count = Math.Min(actual.Count, target.Count);
        var loss = 0f;
        for (var index = 0; index < count; index++)
        {
            var difference = actual[index] - target[index];
            loss += difference * difference;
        }

        return loss / Math.Max(1, count);
    }

    private static float[] MeanBands(Spectrogram spectrogram)
    {
        var means = new float[spectrogram.Bands];
        for (var frame = 0; frame < spectrogram.Frames; frame++)
        {
            for (var band = 0; band < spectrogram.Bands; band++)
            {
                means[band] += spectrogram.At(frame, band);
            }
        }

        for (var band = 0; band < means.Length; band++)
        {
            means[band] /= Math.Max(1, spectrogram.Frames);
        }

        return means;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AquaSynth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("could not find repository root");
    }

    private static string RunFolderName(string label) =>
        $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{label}";

    private static string FormatBands(IReadOnlyList<float> bands) =>
        string.Join(',', bands.Select(value => value.ToString("0.###")));

    private static float[] ReadMonoPcm16Wav(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 ||
            Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
        {
            throw new InvalidOperationException($"{path} is not a RIFF/WAVE file.");
        }

        var offset = 12;
        ushort channels = 0;
        ushort bitsPerSample = 0;
        var dataOffset = -1;
        var dataSize = 0;
        while (offset + 8 <= bytes.Length)
        {
            var chunk = Encoding.ASCII.GetString(bytes, offset, 4);
            var size = BitConverter.ToInt32(bytes, offset + 4);
            offset += 8;
            if (chunk == "fmt ")
            {
                var format = BitConverter.ToUInt16(bytes, offset);
                channels = BitConverter.ToUInt16(bytes, offset + 2);
                bitsPerSample = BitConverter.ToUInt16(bytes, offset + 14);
                if (format != 1 || channels == 0 || bitsPerSample != 16)
                {
                    throw new InvalidOperationException($"{path} must be PCM16 WAV.");
                }
            }
            else if (chunk == "data")
            {
                dataOffset = offset;
                dataSize = size;
                break;
            }

            offset += size + (size & 1);
        }

        if (dataOffset < 0 || channels == 0)
        {
            throw new InvalidOperationException($"{path} has no readable data chunk.");
        }

        var frames = dataSize / (channels * 2);
        var samples = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                var index = dataOffset + (frame * channels + channel) * 2;
                sum += BitConverter.ToInt16(bytes, index) / 32768f;
            }

            samples[frame] = sum / channels;
        }

        return samples;
    }

    private sealed record TrainingFixture(
        string Id,
        string Text,
        string Voice,
        PhoneticEvent Event,
        UtteranceEmbeddingInput UtteranceInput);

    private sealed record GroundTruthFixture(
        TrainingFixture Fixture,
        float[] LogMelMean);

    private sealed class EspeakNgReferenceRenderer(string commandPath)
    {
        public string CommandPath { get; } = commandPath;

        public static bool TryFind(out EspeakNgReferenceRenderer renderer)
        {
            var configured = Environment.GetEnvironmentVariable("ESPEAK_NG");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                renderer = new EspeakNgReferenceRenderer(configured);
                return true;
            }

            foreach (var name in new[] { "espeak-ng", "espeak" })
            {
                if (TryFindOnPath(name, out var path))
                {
                    renderer = new EspeakNgReferenceRenderer(path);
                    return true;
                }
            }

            renderer = null!;
            return false;
        }

        public async Task<(int ExitCode, string Stdout, string Stderr)> RenderAsync(string text, string voice, string wavPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(wavPath)!);
            return await RunAsync(["-v", voice, "-w", wavPath, text]);
        }

        private static bool TryFindOnPath(string name, out string path)
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var extensions = OperatingSystem.IsWindows()
                ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [""];
            foreach (var directory in paths)
            {
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory, name + extension);
                    if (File.Exists(candidate))
                    {
                        path = candidate;
                        return true;
                    }
                }
            }

            path = "";
            return false;
        }

        private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(IReadOnlyList<string> arguments)
        {
            var start = new ProcessStartInfo(CommandPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException($"failed to start `{CommandPath}`");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await stdout, await stderr);
        }
    }
}
