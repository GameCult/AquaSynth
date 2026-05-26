using System.Globalization;
using System.Text.Json;
using AquaSynth.Dsl;
using AquaSynth.Faust;

var input = (await Console.In.ReadToEndAsync()).TrimStart('\uFEFF', '\u200B', ' ', '\t', '\r', '\n');
var request = JsonSerializer.Deserialize<RenderRequest>(
    string.IsNullOrWhiteSpace(input) ? "{}" : input,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new RenderRequest();

var outputPath = string.IsNullOrWhiteSpace(request.OutputPath)
    ? Path.Combine(Path.GetTempPath(), $"aquasynth-tract-{Guid.NewGuid():N}.wav")
    : Path.GetFullPath(request.OutputPath);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

if (FaustCompiler.FindFaust() is null)
{
    Console.Error.WriteLine("Faust was not found on PATH or FAUST_HOME; cannot render actual Aqua graph audio.");
    return 2;
}

var script = Script(request);
var export = FaustEmitter.EmitScript(script, new FaustExportOptions("aqua_tract_graph_audition"));
var render = await FaustCompiler.RenderAsync(
    export.Source,
    new FaustRenderOptions(request.SampleRate, request.DurationSeconds));

if (render is null || render.Samples.Length == 0)
{
    Console.Error.WriteLine(render?.Stderr.Length > 0 ? render.Stderr : "Faust render produced no samples.");
    return 3;
}

WriteWav(outputPath, render.Samples, render.SampleRate, 0.92f);
var peak = render.Samples.Select(MathF.Abs).DefaultIfEmpty(0).Max();
var rms = MathF.Sqrt(render.Samples.Select(sample => sample * sample).DefaultIfEmpty(0).Average());
Console.WriteLine(JsonSerializer.Serialize(new
{
    outputPath,
    render.SampleRate,
    samples = render.Samples.Length,
    peak,
    rms,
    warnings = export.Warnings
}));

return 0;

static string Script(RenderRequest controls) =>
    $$"""
    patch gain=0.48 soft_clip=true

    tract_shape
        name=human
        diameters=0.6,0.6,0.6,0.6,0.6,0.7,0.8,1.0,1.1,1.1,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.5,1.4,1.3,1.2,1.15,1.5

    glottis name=modal intensity={{F(controls.Intensity)}} tenseness={{F(controls.Tenseness)}} aspiration=.12 reflection={{F(controls.GlottalReflection)}} skew=.42
    tract_injection name=inj position=32 diameter=1 turbulence=.1 burst=.25 width=1
    nasal_branch name=nose junction=17 velum=.01 reflection=-.85 loss=.999 diameters=0.01,0.35,0.5,0.65,0.8,0.95,1.1,1.25,1.4,1.55,1.7,1.8,1.9,1.9,1.85,1.75,1.65,1.55,1.45,1.35,1.25,1.15,1.05,0.95,0.85,0.75,0.65,0.55
    tract_motion name=motion diameter_slew=18 shape_return=8 constriction_slew=24 velum_slew=16 obstruction_threshold=.05

    tract shape=human glottis=modal injection=inj nasal_branch=nose motion=motion propagation=graph waveguide_loss=.999 freq={{F(controls.Frequency)}} gain={{F(controls.Gain)}} intensity={{F(controls.Intensity)}} tenseness={{F(controls.Tenseness)}} sustain={{F(Math.Max(0.05f, controls.DurationSeconds - 0.12f))}} decay=.12 tongue_index={{F(controls.TongueIndex)}} tongue_diameter={{F(controls.TongueDiameter)}} constriction_index={{F(controls.ConstrictionIndex)}} constriction_diameter={{F(controls.ConstrictionDiameter)}} turbulence={{F(controls.Turbulence)}} velum={{F(controls.Velum)}} lip={{F(controls.LipOpening)}} burst={{F(controls.Burst)}} glottal_reflection={{F(controls.GlottalReflection)}} lip_reflection={{F(controls.LipReflection)}}
    """;

static string F(float value) =>
    value.ToString("0.######", CultureInfo.InvariantCulture);

static void WriteWav(string path, IReadOnlyList<float> samples, int sampleRate, float normalizePeak)
{
    var peak = samples.Select(MathF.Abs).DefaultIfEmpty(0).Max();
    var gain = peak > 0 ? Math.Min(1, normalizePeak / peak) : 1;
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    var dataLength = samples.Count * sizeof(short);
    writer.Write("RIFF"u8);
    writer.Write(36 + dataLength);
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
    writer.Write(dataLength);
    foreach (var sample in samples)
    {
        writer.Write((short)Math.Clamp(sample * gain * short.MaxValue, short.MinValue, short.MaxValue));
    }
}

public sealed record RenderRequest(
    string OutputPath = "",
    int SampleRate = 44100,
    float DurationSeconds = 0.57f,
    float Frequency = 140,
    float Intensity = 0.72f,
    float Tenseness = 0.6f,
    float TongueIndex = 13,
    float TongueDiameter = 2.7f,
    float ConstrictionIndex = 32,
    float ConstrictionDiameter = 1,
    float Turbulence = 0.1f,
    float Velum = 0.01f,
    float LipOpening = 1.5f,
    float GlottalReflection = 0.75f,
    float LipReflection = -0.85f,
    float Gain = 0.7f,
    float Burst = 0.25f);
