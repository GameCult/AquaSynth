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
        "common-blackbird-1059970",
        "Common Blackbird",
        "Turdus merula",
        "song",
        BirdKind.HarmonicSong,
        "Diana Tudor",
        "CC BY 4.0",
        "https://creativecommons.org/licenses/by/4.0",
        "https://commons.wikimedia.org/wiki/File:Common_Blackbird_song_(Turdus_merula).ogg",
        "https://upload.wikimedia.org/wikipedia/commons/transcoded/3/30/Common_Blackbird_song_%28Turdus_merula%29.ogg/Common_Blackbird_song_%28Turdus_merula%29.ogg.mp3",
        "https://upload.wikimedia.org/wikipedia/commons/3/30/Common_Blackbird_song_%28Turdus_merula%29.ogg"),
    new(
        "red-footed-falcon",
        "Red-footed Falcon",
        "Falco vespertinus",
        "typical calls",
        BirdKind.HighCall,
        "Bubulcus",
        "CC BY 3.0",
        "https://creativecommons.org/licenses/by/3.0",
        "https://commons.wikimedia.org/wiki/File:Falco_vespertinus.ogg",
        "https://upload.wikimedia.org/wikipedia/commons/transcoded/5/59/Falco_vespertinus.ogg/Falco_vespertinus.ogg.mp3",
        "https://upload.wikimedia.org/wikipedia/commons/5/59/Falco_vespertinus.ogg"),
    new(
        "warbling-white-eye-ko",
        "Warbling White-eye",
        "Zosterops japonicus",
        "call",
        BirdKind.HighCall,
        "National Institute of Biological Resources",
        "KOGL Type 1",
        "http://www.kogl.or.kr/info/licenseType1.do",
        "https://commons.wikimedia.org/wiki/File:%EB%8F%99%EB%B0%95%EC%83%88.ogg",
        "https://upload.wikimedia.org/wikipedia/commons/transcoded/e/e7/%EB%8F%99%EB%B0%95%EC%83%88.ogg/%EB%8F%99%EB%B0%95%EC%83%88.ogg.mp3",
        "https://upload.wikimedia.org/wikipedia/commons/e/e7/%EB%8F%99%EB%B0%95%EC%83%88.ogg"),
    new(
        "eurasian-wren-ko",
        "Eurasian Wren",
        "Troglodytes troglodytes",
        "song",
        BirdKind.HarmonicSong,
        "National Institute of Biological Resources",
        "KOGL Type 1",
        "http://www.kogl.or.kr/info/licenseType1.do",
        "https://commons.wikimedia.org/wiki/File:%EA%B5%B4%EB%9A%9D%EC%83%88.ogg",
        "https://upload.wikimedia.org/wikipedia/commons/transcoded/2/2b/%EA%B5%B4%EB%9A%9D%EC%83%88.ogg/%EA%B5%B4%EB%9A%9D%EC%83%88.ogg.mp3",
        "https://upload.wikimedia.org/wikipedia/commons/2/2b/%EA%B5%B4%EB%9A%9D%EC%83%88.ogg")
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
    var referenceFeatures = ExtractReferenceFeatures(target44, 44100);
    WriteWav(Path.Combine(sourceDir, "reference-clip.wav"), target44, 44100);
    File.WriteAllText(Path.Combine(sourceDir, "source.json"), JsonSerializer.Serialize(new SourceSnapshot(source, downloadedFrom), jsonOptions));
    File.WriteAllText(Path.Combine(sourceDir, "reference-features.json"), JsonSerializer.Serialize(referenceFeatures, jsonOptions));

    var best = default(CandidateResult?);
    var candidateIndex = 0;
    foreach (var candidate in CandidateGrid(source, referenceFeatures))
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
        rows.Add(new ResultRow(source.Id, source.CommonName, "no-render", referenceFeatures.DominantHz, referenceFeatures.ActiveDuty, 0, 0, 0, 0, ""));
        continue;
    }

    WriteWav(Path.Combine(sourceDir, "candidate-syrinx.wav"), best.Samples, 44100);
    File.WriteAllText(Path.Combine(sourceDir, "candidate-syrinx.aqua"), best.Script);
    File.WriteAllText(Path.Combine(sourceDir, "report.txt"), Report(source, downloadedFrom, referenceFeatures, best));
    rows.Add(new ResultRow(
        source.Id,
        source.CommonName,
        source.CallType,
        referenceFeatures.DominantHz,
        referenceFeatures.ActiveDuty,
        best.Comparison.Score,
        best.Comparison.LogMelCosineSimilarity,
        best.Comparison.LogMelDistance,
        best.Comparison.CentroidRatio,
        best.Candidate.ToString()));
}

File.WriteAllText(Path.Combine(runRoot, "summary.md"), Summary(rows));
Console.WriteLine(runRoot);
return 0;

static IEnumerable<SyrinxCandidate> CandidateGrid(BirdSource source, ReferenceFeatures features)
{
    var fallbackBases = source.Kind switch
    {
        BirdKind.HarmonicSong => new[] { 750f, 1050f, 1400f },
        BirdKind.HighCall => new[] { 1400f, 2200f, 3000f },
        BirdKind.LowCall => new[] { 260f, 480f, 760f },
        _ => new[] { 800f, 1300f, 2100f }
    };
    var featureBase = features.DominantHz is > 120 and < 6000
        ? features.DominantHz
        : fallbackBases[1];
    var bases = new[]
    {
        Math.Clamp(featureBase * 0.78f, 120f, 5000f),
        Math.Clamp(featureBase, 120f, 5000f),
        Math.Clamp(featureBase * 1.24f, 120f, 5000f)
    };
    var active = Math.Clamp(features.ActiveDuty, 0.05f, 1f);
    var flux = Math.Clamp(features.SpectralFlux, 0f, 1f);
    var basePressure = Math.Clamp(0.58f + active * 0.28f + flux * 0.10f, 0.45f, 0.95f);
    var baseOpening = source.Kind == BirdKind.HighCall
        ? Math.Clamp(0.10f + features.Rms * 1.5f, 0.08f, 0.28f)
        : Math.Clamp(0.16f + features.Rms * 1.7f, 0.12f, 0.42f);
    var loads = new[] { 0.65f, 0.9f };
    var leftGates = new[] { 1.0f, 0.72f };
    foreach (var frequency in bases)
    foreach (var load in loads)
    foreach (var leftGate in leftGates)
    {
        var tension = Math.Clamp(0.18f + frequency / 5200f + flux * 0.12f, 0.18f, 0.88f);
        var stiffness = Math.Clamp(MathF.Pow(frequency / 3200f, 2) * 0.09f, 0.004f, 0.16f);
        var mass = Math.Clamp(0.48f - frequency / 9000f, 0.10f, 0.46f);
        var damping = Math.Clamp(0.08f + (1f - active) * 0.16f + flux * 0.08f, 0.06f, 0.38f);
        yield return new SyrinxCandidate(
            frequency,
            Pressure: basePressure * leftGate,
            RightPressure: Math.Clamp(basePressure * (0.82f + flux * 0.18f), 0.35f, 0.95f),
            Tension: tension,
            RightTension: Math.Clamp(tension + 0.07f, 0, 1),
            Opening: baseOpening,
            RightOpening: Math.Max(0.04f, baseOpening * (0.82f + flux * 0.18f)),
            Load: load,
            Mass: mass,
            RightMass: Math.Clamp(mass * 1.06f, 0.08f, 0.6f),
            Damping: damping,
            RightDamping: Math.Clamp(damping * 1.08f, 0.04f, 0.5f),
            Stiffness: stiffness,
            RightStiffness: Math.Clamp(stiffness * 1.12f, 0.002f, 0.2f),
            Drive: Math.Clamp(0.9f + features.Peak * 0.45f, 0.7f, 1.5f),
            RightDrive: Math.Clamp(0.82f + features.Peak * 0.40f, 0.65f, 1.4f),
            LoadCoupling: Math.Clamp(0.28f + load * 0.18f, 0.2f, 0.7f),
            RestOpening: Math.Clamp(baseOpening * 0.10f, 0.01f, 0.08f),
            BeakOpening: source.Kind == BirdKind.LowCall ? 0.75f : 0.95f,
            Gain: source.Kind == BirdKind.LowCall ? 0.42f : 0.32f);
    }
}

static string SyrinxScript(SyrinxCandidate candidate, float durationSeconds) =>
    $$"""
    patch gain=0.42 soft_clip=true

    param path=/bird/left/pressure default={{F(candidate.Pressure)}} min=0 max=1 step=.001
    param path=/bird/right/pressure default={{F(candidate.RightPressure)}} min=0 max=1 step=.001
    param path=/bird/left/opening default={{F(candidate.Opening)}} min=0 max=1 step=.001
    param path=/bird/right/opening default={{F(candidate.RightOpening)}} min=0 max=1 step=.001
    param path=/bird/beak/opening default={{F(candidate.BeakOpening)}} min=0 max=1.5 step=.001
    curve name=left_pressure path=/bird/left/pressure points={{PressureCurve(candidate.Pressure, durationSeconds)}} depth=1
    curve name=right_pressure path=/bird/right/pressure points={{PressureCurve(candidate.RightPressure, durationSeconds)}} depth=1
    curve name=left_opening path=/bird/left/opening points={{OpeningCurve(candidate.Opening, durationSeconds)}} depth=.9
    curve name=right_opening path=/bird/right/opening points={{OpeningCurve(candidate.RightOpening, durationSeconds)}} depth=.9
    curve name=beak_opening path=/bird/beak/opening points={{BeakCurve(candidate.BeakOpening, durationSeconds)}} depth=.65

    path name=left_bronchus length_cm=3.8 diameters=.22,.30,.36,.42
    path name=right_bronchus length_cm=3.6 diameters=.20,.28,.34,.40
    path name=trachea length_cm=8.4 diameters=.38,.48,.56,.46

    source_port name=left_labium path=left_bronchus kind=syrinx model=tissue_valve position=0 pressure=@/bird/left/pressure tension={{F(candidate.Tension)}} opening=@/bird/left/opening noise=.025 impedance={{F(candidate.Load)}} mass={{F(candidate.Mass)}} damping={{F(candidate.Damping)}} stiffness={{F(candidate.Stiffness)}} saturation=.9 drive={{F(candidate.Drive)}} load_coupling={{F(candidate.LoadCoupling)}} rest_opening={{F(candidate.RestOpening)}}
    source_port name=right_labium path=right_bronchus kind=syrinx model=tissue_valve position=0 pressure=@/bird/right/pressure tension={{F(candidate.RightTension)}} opening=@/bird/right/opening noise=.02 balance=.96 impedance={{F(candidate.Load)}} mass={{F(candidate.RightMass)}} damping={{F(candidate.RightDamping)}} stiffness={{F(candidate.RightStiffness)}} saturation=.9 drive={{F(candidate.RightDrive)}} load_coupling={{F(candidate.LoadCoupling)}} rest_opening={{F(candidate.RestOpening)}}

    terminal name=left_merge path=left_bronchus position=1 kind=junction area_scale=1
    terminal name=right_merge path=right_bronchus position=1 kind=junction area_scale=1
    terminal name=trachea_base path=trachea position=0 kind=junction area_scale=1
    connect name=syrinx_merge terminals=left_merge,right_merge,trachea_base law=area_scatter coupling=1

    radiation_port name=beak path=trachea kind=beak position=1 opening=@/bird/beak/opening reflection=-.72
    wave_clock name=bird_clock strategy=linear max_delay=1024 smoothing_ms=2
    acoustic_network name=bird_syrinx path=trachea wave_clock=bird_clock sources=left_labium,right_labium radiation=beak terminals=left_merge,right_merge,trachea_base connections=syrinx_merge
    acoustic network=bird_syrinx freq={{F(candidate.Frequency)}} gain={{F(candidate.Gain)}} sustain={{F(Math.Max(0.05f, durationSeconds - 0.12f))}} decay=.08
    """;

static string PressureCurve(float value, float durationSeconds) =>
    Curve((0, MathF.Max(0.01f, value * 0.08f)), (0.035f, value), (durationSeconds * 0.55f, value * 0.94f), (durationSeconds, value * 0.35f));

static string OpeningCurve(float value, float durationSeconds) =>
    Curve((0, MathF.Max(0.01f, value * 0.15f)), (0.03f, value), (durationSeconds * 0.65f, value * 0.86f), (durationSeconds, MathF.Max(0.01f, value * 0.20f)));

static string BeakCurve(float value, float durationSeconds) =>
    Curve((0, value * 0.45f), (0.05f, value), (durationSeconds * 0.72f, Math.Min(1.5f, value * 1.08f)), (durationSeconds, value * 0.55f));

static string Curve(params (float Time, float Value)[] points) =>
    string.Join(",", points.Select(point => $"{F(Math.Max(0, point.Time))}:{F(Math.Max(0, point.Value))}"));

static string Report(BirdSource source, string downloadedFrom, ReferenceFeatures features, CandidateResult best) =>
    $"""
    # {source.CommonName} ({source.ScientificName})

    Source: {source.DescriptionUrl}
    Downloaded file: {downloadedFrom}
    License: {source.LicenseName} ({source.LicenseUrl})
    Author: {source.Author}
    Call type: {source.CallType}

    Reference features:
    rms={features.Rms:0.0000}
    peak={features.Peak:0.0000}
    activeDuty={features.ActiveDuty:0.0000}
    onsetSeconds={features.OnsetSeconds:0.0000}
    offsetSeconds={features.OffsetSeconds:0.0000}
    dominantHz={features.DominantHz:0.0}
    spectralFlux={features.SpectralFlux:0.0000}

    Best candidate:
    {best.Candidate}

    Metrics:
    score={best.Comparison.Score:0.0000}
    logMelCosine={best.Comparison.LogMelCosineSimilarity:0.0000}
    logMelDistance={best.Comparison.LogMelDistance:0.0000}
    rmsRatio={best.Comparison.RmsRatio:0.0000}
    centroidRatio={best.Comparison.CentroidRatio:0.0000}
    articulation={best.Comparison.Articulation.ArticulationScore:0.0000}

    This is a feature-informed graph-native syrinx golf pass, not an accepted species model. Gesture curves are currently compressed into source parameters because Aqua does not yet have general arbitrary-path control automation.
    """;

static string Summary(IEnumerable<ResultRow> rows)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Bird Syrinx Golf");
    builder.AppendLine();
    builder.AppendLine("All references are open-licensed bird recordings from Wikimedia Commons. Candidates are graph-native Aqua syrinx patches using paired tissue-valve source ports.");
    builder.AppendLine();
    builder.AppendLine("| id | bird | call | dominantHz | activeDuty | score | logMelCosine | logMelDistance | centroidRatio | candidate |");
    builder.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
    foreach (var row in rows)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"| {row.Id} | {row.Bird} | {row.Call} | {row.DominantHz:0.0} | {row.ActiveDuty:0.0000} | {row.Score:0.0000} | {row.LogMelCosine:0.0000} | {row.LogMelDistance:0.0000} | {row.CentroidRatio:0.0000} | `{row.Candidate}` |");
    }
    return builder.ToString();
}

static ReferenceFeatures ExtractReferenceFeatures(IReadOnlyList<float> samples, int sampleRate)
{
    var peak = samples.Select(MathF.Abs).DefaultIfEmpty(0).Max();
    var rms = MathF.Sqrt(samples.Select(sample => sample * sample).DefaultIfEmpty(0).Average());
    var frameSize = Math.Max(64, sampleRate / 100);
    var threshold = Math.Max(0.02f, rms * 0.45f);
    var frameRms = new List<float>();
    for (var start = 0; start < samples.Count; start += frameSize)
    {
        var end = Math.Min(samples.Count, start + frameSize);
        var sum = 0f;
        for (var i = start; i < end; i++) sum += samples[i] * samples[i];
        frameRms.Add(MathF.Sqrt(sum / Math.Max(1, end - start)));
    }

    var activeFrames = frameRms
        .Select((value, index) => (value, index))
        .Where(frame => frame.value >= threshold)
        .Select(frame => frame.index)
        .ToArray();
    var activeDuty = frameRms.Count == 0 ? 0 : activeFrames.Length / (float)frameRms.Count;
    var onset = activeFrames.Length == 0 ? 0 : activeFrames[0] * frameSize / (float)sampleRate;
    var offset = activeFrames.Length == 0 ? samples.Count / (float)sampleRate : Math.Min(samples.Count, (activeFrames[^1] + 1) * frameSize) / (float)sampleRate;
    var flux = 0f;
    for (var i = 1; i < frameRms.Count; i++) flux += Math.Max(0, frameRms[i] - frameRms[i - 1]);
    flux = frameRms.Count <= 1 || peak <= 0 ? 0 : Math.Clamp(flux / (frameRms.Count * peak), 0, 1);

    return new ReferenceFeatures(rms, peak, activeDuty, onset, offset, DominantFrequency(samples, sampleRate, activeFrames, frameSize), flux);
}

static float DominantFrequency(IReadOnlyList<float> samples, int sampleRate, IReadOnlyList<int> activeFrames, int frameSize)
{
    if (samples.Count < 256) return 0;
    var activeCenter = activeFrames.Count == 0
        ? samples.Count / 2
        : Math.Clamp((int)((activeFrames[activeFrames.Count / 2] + 0.5f) * frameSize), 0, samples.Count - 1);
    var size = Math.Min(4096, samples.Count);
    var start = Math.Clamp(activeCenter - size / 2, 0, Math.Max(0, samples.Count - size));
    var minLag = Math.Max(1, sampleRate / 5000);
    var maxLag = Math.Min(size / 2, sampleRate / 120);
    var bestLag = 0;
    var best = 0f;
    for (var lag = minLag; lag <= maxLag; lag++)
    {
        var sum = 0f;
        for (var i = 0; i < size - lag; i++)
        {
            sum += samples[start + i] * samples[start + i + lag];
        }
        if (sum > best)
        {
            best = sum;
            bestLag = lag;
        }
    }
    return bestLag == 0 ? 0 : sampleRate / (float)bestLag;
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
    float Mass,
    float RightMass,
    float Damping,
    float RightDamping,
    float Stiffness,
    float RightStiffness,
    float Drive,
    float RightDrive,
    float LoadCoupling,
    float RestOpening,
    float BeakOpening,
    float Gain)
{
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"freqHint={Frequency:0.#} pressure={Pressure:0.###}/{RightPressure:0.###} tension={Tension:0.###}/{RightTension:0.###} opening={Opening:0.###}/{RightOpening:0.###} mass={Mass:0.###}/{RightMass:0.###} damp={Damping:0.###}/{RightDamping:0.###} stiff={Stiffness:0.###}/{RightStiffness:0.###} drive={Drive:0.###}/{RightDrive:0.###} load={Load:0.###} loadCoupling={LoadCoupling:0.###} rest={RestOpening:0.###} beak={BeakOpening:0.###} gain={Gain:0.###}");
}

public sealed record CandidateResult(SyrinxCandidate Candidate, float[] Samples, AudioComparison Comparison, string Script);

public sealed record ResultRow(
    string Id,
    string Bird,
    string Call,
    float DominantHz,
    float ActiveDuty,
    float Score,
    float LogMelCosine,
    float LogMelDistance,
    float CentroidRatio,
    string Candidate);

public sealed record DecodedAudio(int SampleRate, float[] Samples);

public sealed record ReferenceFeatures(
    float Rms,
    float Peak,
    float ActiveDuty,
    float OnsetSeconds,
    float OffsetSeconds,
    float DominantHz,
    float SpectralFlux);
