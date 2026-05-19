using System.Diagnostics;
using System.Text;
using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

public sealed class EspeakNgReferenceTests
{
    [Fact]
    public async Task EspeakNgReferenceRendererWritesTinyIpaWorkoutWhenInstalled()
    {
        if (!EspeakNgReferenceRenderer.TryFind(out var renderer))
        {
            return;
        }

        var root = RepositoryRoot();
        var artifactDir = Path.Combine(root, "artifacts", "parity", "espeak-ng-ipa-workout", RunFolderName("tiny"));
        Directory.CreateDirectory(artifactDir);

        var fixtures = new[]
        {
            new EspeakNgFixture("open-vowel", "a", "en"),
            new EspeakNgFixture("bilabial-open", "pa", "en"),
            new EspeakNgFixture("alveolar-open", "ta", "en"),
            new EspeakNgFixture("velar-open", "ka", "en"),
            new EspeakNgFixture("sibilant-open", "sa", "en"),
            new EspeakNgFixture("nasal-open", "ma", "en")
        };

        var report = new List<string>
        {
            "eSpeak NG IPA workout",
            $"renderer: {renderer.CommandPath}",
            "role: optional development reference for intelligibility, phoneme timing, and broad IPA/text coverage; not anatomy truth"
        };

        foreach (var fixture in fixtures)
        {
            var wavPath = Path.Combine(artifactDir, $"{fixture.Id}.wav");
            var ipa = await renderer.TranscribeIpaAsync(fixture);
            var render = await renderer.RenderAsync(fixture, wavPath);
            Assert.Equal(0, render.ExitCode);
            Assert.True(File.Exists(wavPath), $"eSpeak NG did not write {wavPath}.");

            var samples = ReadMonoPcm16Wav(wavPath);
            Assert.True(samples.Length > 1024, $"{fixture.Id} rendered too few samples.");
            var features = AudioAnalyzer.AnalyzeAudio(samples).Features;
            Assert.True(features.Peak > 0.001f, $"{fixture.Id} rendered near silence.");

            report.Add($"fixture: {fixture.Id}");
            report.Add($"  text: {fixture.Text}");
            report.Add($"  voice: {fixture.Voice}");
            report.Add($"  ipa: {ipa.Trim()}");
            report.Add($"  samples: {samples.Length}");
            report.Add($"  peak: {features.Peak:0.######}");
            report.Add($"  rms: {features.Rms:0.######}");
            report.Add($"  centroid_hz: {features.SpectralCentroidHz:0.######}");
        }

        await File.WriteAllLinesAsync(Path.Combine(artifactDir, "report.md"), report);
    }

    [Fact]
    public void EspeakNgReferenceRendererReportsMissingTool()
    {
        if (EspeakNgReferenceRenderer.TryFind(out _))
        {
            return;
        }

        var missing = EspeakNgReferenceRenderer.MissingToolMessage;
        Assert.Contains("ESPEAK_NG", missing);
        Assert.Contains("PATH", missing);
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

    private sealed record EspeakNgFixture(string Id, string Text, string Voice);

    private sealed class EspeakNgReferenceRenderer(string commandPath)
    {
        public const string MissingToolMessage =
            "eSpeak NG reference renderer not found. Set ESPEAK_NG to espeak-ng.exe/espeak.exe or put espeak-ng/espeak on PATH.";

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

        public async Task<(int ExitCode, string Stdout, string Stderr)> RenderAsync(EspeakNgFixture fixture, string wavPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(wavPath)!);
            return await RunAsync(["-v", fixture.Voice, "-w", wavPath, fixture.Text]);
        }

        public async Task<string> TranscribeIpaAsync(EspeakNgFixture fixture)
        {
            var result = await RunAsync(["-q", "--ipa=3", "-v", fixture.Voice, fixture.Text]);
            return result.ExitCode == 0 ? result.Stdout : $"unavailable: {result.Stderr.Trim()}";
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
