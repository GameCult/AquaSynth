using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AquaSynth.Dsl;
using AquaSynth.Faust;
using NAudio.Wave;
using NVorbis;

var root = RepositoryRoot();
var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture);
var runRoot = Path.Combine(root, "artifacts", "bird-syrinx-golf", timestamp);
var sourceRoot = Path.Combine(runRoot, "sources");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
BirdSource[] sources =
[
    new(
        "common-iora-xc125847",
        "Common Iora",
        "Aegithina tiphia",
        "song",
        BirdKind.HarmonicSong,
        "Sudipto Roy",
        "CC BY-SA 3.0",
        "https://creativecommons.org/licenses/by-sa/3.0",
        "https://commons.wikimedia.org/wiki/File:Aegithina_tiphia_-_Common_Iora_XC125847.ogg",
        "https://upload.wikimedia.org/wikipedia/commons/transcoded/7/7c/Aegithina_tiphia_-_Common_Iora_XC125847.ogg/Aegithina_tiphia_-_Common_Iora_XC125847.ogg.mp3",
        "https://upload.wikimedia.org/wikipedia/commons/7/7c/Aegithina_tiphia_-_Common_Iora_XC125847.ogg"),
    new(
        "bohemian-waxwing-xc132884",
        "Bohemian Waxwing",
        "Bombycilla garrulus",
        "flight call",
        BirdKind.HighCall,
        "Bushman",
        "CC BY-SA 3.0",
        "https://creativecommons.org/licenses/by-sa/3.0",
        "https://commons.wikimedia.org/wiki/File:Bombycilla_garrulus_-_Bohemian_Waxwing_XC132884.ogg",
        "https://upload.wikimedia.org/wikipedia/commons/transcoded/4/46/Bombycilla_garrulus_-_Bohemian_Waxwing_XC132884.ogg/Bombycilla_garrulus_-_Bohemian_Waxwing_XC132884.ogg.mp3",
        "https://upload.wikimedia.org/wikipedia/commons/4/46/Bombycilla_garrulus_-_Bohemian_Waxwing_XC132884.ogg"),
    new(
        "california-quail-xc109825",
        "California Quail",
        "Callipepla californica",
        "natural calls",
        BirdKind.LowCall,
        "Jonathon Jongsma",
        "CC BY-SA 3.0",
        "https://creativecommons.org/licenses/by-sa/3.0",
        "https://commons.wikimedia.org/wiki/File:Callipepla_californica_-_California_Quail_-_XC109825.ogg",
        "https://upload.wikimedia.org/wikipedia/commons/transcoded/3/3c/Callipepla_californica_-_California_Quail_-_XC109825.ogg/Callipepla_californica_-_California_Quail_-_XC109825.ogg.mp3",
        "https://upload.wikimedia.org/wikipedia/commons/3/3c/Callipepla_californica_-_California_Quail_-_XC109825.ogg"),
    new(
        "american-crow-xc115429",
        "American Crow",
        "Corvus brachyrhynchos",
        "soft rattling calls",
        BirdKind.LowCall,
        "Jonathon Jongsma",
        "CC BY-SA 3.0",
        "https://creativecommons.org/licenses/by-sa/3.0",
        "https://commons.wikimedia.org/wiki/File:Corvus_brachyrhynchos_-_American_Crow_-_XC115429.ogg",
        "https://upload.wikimedia.org/wikipedia/commons/transcoded/9/90/Corvus_brachyrhynchos_-_American_Crow_-_XC115429.ogg/Corvus_brachyrhynchos_-_American_Crow_-_XC115429.ogg.mp3",
        "https://upload.wikimedia.org/wikipedia/commons/9/90/Corvus_brachyrhynchos_-_American_Crow_-_XC115429.ogg")
];
Directory.CreateDirectory(sourceRoot);

if (FaustCompiler.FindFaust() is null)
{
    Console.Error.WriteLine("Faust was not found on PATH or FAUST_HOME; cannot render Aqua syrinx candidates.");
    return 2;
}

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AquaSynthBirdSyrinxGolf", "0.1"));
http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(https://github.com/GameCult/AquaSynth)"));
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/ogg"));
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));

var rows = new List<ResultRow>();
foreach (var source in sources)
{
    Console.WriteLine($"Fetching {source.Id} {source.CommonName}");
    var sourceDir = Path.Combine(sourceRoot, source.Id);
    Directory.CreateDirectory(sourceDir);
    var referencePath = Path.Combine(sourceDir, "reference.mp3");
    var downloadedFrom = source.FileUrls[0];
    if (!File.Exists(referencePath))
    {
        downloadedFrom = await DownloadFirstAvailableAsync(http, source, referencePath);
    }

    var decoded = DecodeMono(referencePath, downloadedFrom);
    var target = LoudestWindow(decoded.Samples, decoded.SampleRate, 1.2f);
    var target44 = Resample(target, decoded.SampleRate, 44100);
    NormalizePeak(target44, 0.9f);
    WriteWav(Path.Combine(sourceDir, "reference-clip.wav"), target44, 44100);
    File.WriteAllText(Path.Combine(sourceDir, "source.json"), JsonSerializer.Serialize(new SourceSnapshot(source, downloadedFrom), jsonOptions));

    var best = default(CandidateResult?);
    var candidateIndex = 0;
    foreach (var candidate in CandidateGrid(source))
    {
        candidateIndex++;
        var script = SyrinxScript(candidate, target44.Length / 44100f);
        var export = FaustEmitter.EmitScript(script, new FaustExportOptions("bird_syrinx_candidate"));
        var render = await FaustCompiler.RenderAsync(export.Source, new FaustRenderOptions(44100, target44.Length / 44100f));
        if (render is null || render.Samples.Length == 0)
        {
            Console.WriteLine($"  candidate {candidateIndex:00}: render failed {render?.Stderr}");
            continue;
        }

        var candidateSamples = MatchLength(render.Samples, target44.Length);
        NormalizePeak(candidateSamples, 0.9f);
        var comparison = AudioAnalyzer.CompareAudio(target44, candidateSamples);
        var result = new CandidateResult(candidate, candidateSamples, comparison, script);
        if (best is null || result.Comparison.Score > best.Comparison.Score)
        {
            best = result;
        }

        Console.WriteLine(FormattableString.Invariant(
            $"  candidate {candidateIndex:00}: score={comparison.Score:0.0000} logMel={comparison.LogMelCosineSimilarity:0.0000} {candidate}"));
    }

    if (best is null)
    {
        rows.Add(new ResultRow(source.Id, source.CommonName, "no-render", 0, 0, 0, 0, ""));
        continue;
    }

    WriteWav(Path.Combine(sourceDir, "candidate-syrinx.wav"), best.Samples, 44100);
    File.WriteAllText(Path.Combine(sourceDir, "candidate-syrinx.aqua"), best.Script);
    File.WriteAllText(Path.Combine(sourceDir, "report.txt"), Report(source, downloadedFrom, best));
    rows.Add(new ResultRow(
        source.Id,
        source.CommonName,
        source.CallType,
        best.Comparison.Score,
        best.Comparison.LogMelCosineSimilarity,
        best.Comparison.LogMelDistance,
        best.Comparison.CentroidRatio,
        best.Candidate.ToString()));
}

File.WriteAllText(Path.Combine(runRoot, "summary.md"), Summary(rows));
Console.WriteLine(runRoot);
return 0;

static IEnumerable<SyrinxCandidate> CandidateGrid(BirdSource source)
{
    var bases = source.Kind switch
    {
        BirdKind.HarmonicSong => new[] { 750f, 1050f, 1400f },
        BirdKind.HighCall => new[] { 1400f, 2200f, 3000f },
        BirdKind.LowCall => new[] { 260f, 480f, 760f },
        _ => new[] { 800f, 1300f, 2100f }
    };
    var tensions = source.Kind == BirdKind.LowCall ? new[] { 0.28f, 0.5f } : new[] { 0.38f, 0.62f };
    var openings = source.Kind == BirdKind.HighCall ? new[] { 0.14f, 0.28f } : new[] { 0.20f, 0.38f };
    var loads = new[] { 0.85f };

    foreach (var frequency in bases)
    foreach (var tension in tensions)
    foreach (var opening in openings)
    foreach (var load in loads)
    {
        yield return new SyrinxCandidate(
            frequency,
            Pressure: source.Kind == BirdKind.LowCall ? 0.74f : 0.82f,
            RightPressure: source.Kind == BirdKind.LowCall ? 0.62f : 0.70f,
            Tension: tension,
            RightTension: Math.Clamp(tension + 0.07f, 0, 1),
            Opening: opening,
            RightOpening: Math.Max(0.05f, opening * 0.88f),
            Load: load,
            BeakOpening: source.Kind == BirdKind.LowCall ? 0.75f : 0.95f,
            Gain: source.Kind == BirdKind.LowCall ? 0.42f : 0.32f);
    }
}

static string SyrinxScript(SyrinxCandidate candidate, float durationSeconds) =>
    $$"""
    patch gain=0.42 soft_clip=true

    path name=left_bronchus length_cm=3.8 diameters=.22,.30,.36,.42
    path name=right_bronchus length_cm=3.6 diameters=.20,.28,.34,.40
    path name=trachea length_cm=8.4 diameters=.38,.48,.56,.46

    source_port name=left_labium path=left_bronchus kind=syrinx position=0 pressure={{F(candidate.Pressure)}} tension={{F(candidate.Tension)}} opening={{F(candidate.Opening)}} noise=.025 impedance={{F(candidate.Load)}}
    source_port name=right_labium path=right_bronchus kind=syrinx position=0 pressure={{F(candidate.RightPressure)}} tension={{F(candidate.RightTension)}} opening={{F(candidate.RightOpening)}} noise=.02 balance=.96 impedance={{F(candidate.Load)}}

    terminal name=left_merge path=left_bronchus position=1 kind=junction area_scale=1
    terminal name=right_merge path=right_bronchus position=1 kind=junction area_scale=1
    terminal name=trachea_base path=trachea position=0 kind=junction area_scale=1
    connect name=syrinx_merge terminals=left_merge,right_merge,trachea_base law=area_scatter coupling=1

    radiation_port name=beak path=trachea kind=beak position=1 opening={{F(candidate.BeakOpening)}} reflection=-.72
    wave_clock name=bird_clock strategy=linear max_delay=1024 smoothing_ms=2
    acoustic_network name=bird_syrinx path=trachea wave_clock=bird_clock sources=left_labium,right_labium radiation=beak terminals=left_merge,right_merge,trachea_base connections=syrinx_merge
    acoustic network=bird_syrinx freq={{F(candidate.Frequency)}} gain={{F(candidate.Gain)}} sustain={{F(Math.Max(0.05f, durationSeconds - 0.12f))}} decay=.08
    """;

static string Report(BirdSource source, string downloadedFrom, CandidateResult best) =>
    $"""
    # {source.CommonName} ({source.ScientificName})

    Source: {source.DescriptionUrl}
    Downloaded file: {downloadedFrom}
    License: {source.LicenseName} ({source.LicenseUrl})
    Author: {source.Author}
    Call type: {source.CallType}

    Best candidate:
    {best.Candidate}

    Metrics:
    score={best.Comparison.Score:0.0000}
    logMelCosine={best.Comparison.LogMelCosineSimilarity:0.0000}
    logMelDistance={best.Comparison.LogMelDistance:0.0000}
    rmsRatio={best.Comparison.RmsRatio:0.0000}
    centroidRatio={best.Comparison.CentroidRatio:0.0000}
    articulation={best.Comparison.Articulation.ArticulationScore:0.0000}

    This is a first graph-native syrinx golf pass, not an accepted species model.
    """;

static string Summary(IEnumerable<ResultRow> rows)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Bird Syrinx Golf");
    builder.AppendLine();
    builder.AppendLine("All references are Creative Commons files mirrored on Wikimedia Commons from xeno-canto. Candidates are graph-native Aqua syrinx patches using paired labial source ports.");
    builder.AppendLine();
    builder.AppendLine("| id | bird | call | score | logMelCosine | logMelDistance | centroidRatio | candidate |");
    builder.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: | --- |");
    foreach (var row in rows)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"| {row.Id} | {row.Bird} | {row.Call} | {row.Score:0.0000} | {row.LogMelCosine:0.0000} | {row.LogMelDistance:0.0000} | {row.CentroidRatio:0.0000} | `{row.Candidate}` |");
    }
    return builder.ToString();
}

static DecodedAudio DecodeMono(string path, string sourceUrl)
{
    if (sourceUrl.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
    {
        return DecodeMp3Mono(path);
    }

    return DecodeOggMono(path);
}

static DecodedAudio DecodeMp3Mono(string path)
{
    using var reader = new AudioFileReader(path);
    var interleaved = new float[4096 * reader.WaveFormat.Channels];
    var mono = new List<float>();
    int read;
    while ((read = reader.Read(interleaved, 0, interleaved.Length)) > 0)
    {
        for (var i = 0; i < read; i += reader.WaveFormat.Channels)
        {
            var sum = 0f;
            for (var channel = 0; channel < reader.WaveFormat.Channels && i + channel < read; channel++)
            {
                sum += interleaved[i + channel];
            }
            mono.Add(sum / reader.WaveFormat.Channels);
        }
    }

    return new DecodedAudio(reader.WaveFormat.SampleRate, mono.ToArray());
}

static DecodedAudio DecodeOggMono(string path)
{
    using var reader = new VorbisReader(path);
    var interleaved = new float[4096 * reader.Channels];
    var mono = new List<float>();
    int read;
    while ((read = reader.ReadSamples(interleaved, 0, interleaved.Length)) > 0)
    {
        for (var i = 0; i < read; i += reader.Channels)
        {
            var sum = 0f;
            for (var channel = 0; channel < reader.Channels && i + channel < read; channel++)
            {
                sum += interleaved[i + channel];
            }
            mono.Add(sum / reader.Channels);
        }
    }
    return new DecodedAudio(reader.SampleRate, mono.ToArray());
}

static async Task<string> DownloadFirstAvailableAsync(HttpClient http, BirdSource source, string targetPath)
{
    Exception? lastError = null;
    foreach (var url in source.FileUrls)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if ((int)response.StatusCode == 429 && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
                    continue;
                }

                response.EnsureSuccessStatusCode();
                await using var remote = await response.Content.ReadAsStreamAsync();
                await using var local = File.Create(targetPath);
                await remote.CopyToAsync(local);
                return url;
            }
            catch (Exception ex) when (attempt < 3)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }
    }

    throw new InvalidOperationException($"Could not download {source.Id} from any configured source URL.", lastError);
}

static float[] LoudestWindow(IReadOnlyList<float> samples, int sampleRate, float seconds)
{
    var length = Math.Min(samples.Count, Math.Max(1, (int)MathF.Round(sampleRate * seconds)));
    if (samples.Count <= length) return samples.ToArray();

    var hop = Math.Max(1, length / 8);
    var bestStart = 0;
    var bestEnergy = -1f;
    for (var start = 0; start + length <= samples.Count; start += hop)
    {
        var energy = 0f;
        for (var i = start; i < start + length; i++) energy += samples[i] * samples[i];
        if (energy > bestEnergy)
        {
            bestEnergy = energy;
            bestStart = start;
        }
    }

    var window = new float[length];
    for (var i = 0; i < length; i++) window[i] = samples[bestStart + i];
    return window;
}

static float[] Resample(IReadOnlyList<float> samples, int sourceRate, int targetRate)
{
    if (sourceRate == targetRate) return samples.ToArray();
    var result = new float[Math.Max(1, (int)Math.Round(samples.Count * (double)targetRate / sourceRate))];
    for (var i = 0; i < result.Length; i++)
    {
        var position = i * (sourceRate / (double)targetRate);
        var left = Math.Clamp((int)Math.Floor(position), 0, samples.Count - 1);
        var right = Math.Clamp(left + 1, 0, samples.Count - 1);
        var t = (float)(position - left);
        result[i] = samples[left] * (1 - t) + samples[right] * t;
    }
    return result;
}

static float[] MatchLength(IReadOnlyList<float> samples, int length)
{
    var result = new float[length];
    for (var i = 0; i < result.Length && i < samples.Count; i++) result[i] = samples[i];
    return result;
}

static void NormalizePeak(IList<float> samples, float peak)
{
    var current = samples.Select(Math.Abs).DefaultIfEmpty(0).Max();
    if (current <= 0) return;
    var gain = peak / current;
    for (var i = 0; i < samples.Count; i++) samples[i] *= gain;
}

static void WriteWav(string path, IReadOnlyList<float> samples, int sampleRate)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream, Encoding.ASCII);
    var dataSize = samples.Count * sizeof(short);
    writer.Write("RIFF"u8);
    writer.Write(36 + dataSize);
    writer.Write("WAVE"u8);
    writer.Write("fmt "u8);
    writer.Write(16);
    writer.Write((short)1);
    writer.Write((short)1);
    writer.Write(sampleRate);
    writer.Write(sampleRate * sizeof(short));
    writer.Write((short)sizeof(short));
    writer.Write((short)16);
    writer.Write("data"u8);
    writer.Write(dataSize);
    foreach (var sample in samples)
    {
        writer.Write((short)Math.Clamp(MathF.Round(sample * short.MaxValue), short.MinValue, short.MaxValue));
    }
}

static string RepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AquaSynth.sln")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName ?? AppContext.BaseDirectory;
}

static string F(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

public enum BirdKind { HarmonicSong, HighCall, LowCall }

public sealed record BirdSource(
    string Id,
    string CommonName,
    string ScientificName,
    string CallType,
    BirdKind Kind,
    string Author,
    string LicenseName,
    string LicenseUrl,
    string DescriptionUrl,
    params string[] FileUrls);

public sealed record SourceSnapshot(BirdSource Source, string DownloadedFrom);

public sealed record SyrinxCandidate(
    float Frequency,
    float Pressure,
    float RightPressure,
    float Tension,
    float RightTension,
    float Opening,
    float RightOpening,
    float Load,
    float BeakOpening,
    float Gain)
{
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"freq={Frequency:0.#} pressure={Pressure:0.###}/{RightPressure:0.###} tension={Tension:0.###}/{RightTension:0.###} opening={Opening:0.###}/{RightOpening:0.###} load={Load:0.###} beak={BeakOpening:0.###} gain={Gain:0.###}");
}

public sealed record CandidateResult(SyrinxCandidate Candidate, float[] Samples, AudioComparison Comparison, string Script);

public sealed record ResultRow(
    string Id,
    string Bird,
    string Call,
    float Score,
    float LogMelCosine,
    float LogMelDistance,
    float CentroidRatio,
    string Candidate);

public sealed record DecodedAudio(int SampleRate, float[] Samples);
