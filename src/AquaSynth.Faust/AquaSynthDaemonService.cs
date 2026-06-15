using System.Security.Cryptography;
using System.Text;
using AquaSynth.Dsl;
using GameCult.Caching;
using GameCult.Caching.MessagePack;
using MessagePack;

namespace AquaSynth.Faust;

public static class AquaSynthDaemonSchemas
{
    public const string PatchCompileCommand = "aquasynth.command.patch_compile.v1";
    public const string InstrumentSampleCommand = "aquasynth.command.instrument_sample.v1";
    public const string CompiledInstrumentSession = "aquasynth.compiled_instrument_session.v1";
    public const string PatchCompileReceipt = "aquasynth.patch_compile_receipt.v1";
    public const string RenderSampleReceipt = "aquasynth.render_sample_receipt.v1";
    public const string OperatorState = "aquasynth.operator_state.v1";
}

public sealed record AquaSynthDaemonOptions(
    string StoreRoot,
    int SampleRate = 44100,
    float MinRenderSeconds = 0.05f,
    float MaxRenderSeconds = 3.0f)
{
    public static AquaSynthDaemonOptions Default =>
        new(Path.Combine(Environment.CurrentDirectory, ".aquasynth"));
}

[CultDocument("aquasynth.command.patch_compile", AquaSynthDaemonSchemas.PatchCompileCommand)]
[MessagePackObject]
public sealed record AquaSynthPatchCompileCommand(
    [property: Key(0), CultName] string CommandId,
    [property: Key(1)] string PatchId,
    [property: Key(2)] string FaustName,
    [property: Key(3)] string Script,
    [property: Key(4)] int Revision = 1,
    [property: Key(5)] float? DurationSeconds = null);

[CultDocument("aquasynth.command.instrument_sample", AquaSynthDaemonSchemas.InstrumentSampleCommand)]
[MessagePackObject]
public sealed record AquaSynthInstrumentSampleCommand(
    [property: Key(0), CultName] string CommandId,
    [property: Key(1)] string PatchId,
    [property: Key(2)] string FaustName,
    [property: Key(3)] string Script,
    [property: Key(4)] float DurationSeconds = 0.25f,
    [property: Key(5)] float Gain = 1.0f,
    [property: Key(6)] Dictionary<string, float>? Controls = null,
    [property: Key(7)] int Revision = 1);

[CultDocument("aquasynth.compiled_instrument_session", AquaSynthDaemonSchemas.CompiledInstrumentSession)]
[MessagePackObject]
public sealed record AquaSynthCompiledInstrumentSession(
    [property: Key(0), CultName] string SessionId,
    [property: Key(1), CultIndex("patch")] string PatchId,
    [property: Key(2)] string CompileKey,
    [property: Key(3)] string FaustName,
    [property: Key(4)] int Revision,
    [property: Key(5)] int SampleRate,
    [property: Key(6)] int InputCount,
    [property: Key(7)] int OutputCount,
    [property: Key(8)] int FrameCount,
    [property: Key(9)] float DurationSeconds,
    [property: Key(10)] string CreatedAtUtc,
    [property: Key(11)] string[] ControlPaths,
    [property: Key(12)] string[] ProbePaths);

[CultDocument("aquasynth.patch_compile_receipt", AquaSynthDaemonSchemas.PatchCompileReceipt)]
[MessagePackObject]
public sealed record AquaSynthPatchCompileReceipt(
    [property: Key(0), CultName] string ReceiptId,
    [property: Key(1), CultIndex("command")] string CommandId,
    [property: Key(2), CultIndex("patch")] string PatchId,
    [property: Key(3)] string SessionId,
    [property: Key(4)] string Status,
    [property: Key(5)] string DecidedAtUtc,
    [property: Key(6)] string CompileKey,
    [property: Key(7)] double CompileMilliseconds,
    [property: Key(8)] string[] ControlPaths,
    [property: Key(9)] string[] ProbePaths,
    [property: Key(10)] string FailureCode = "",
    [property: Key(11)] string FailureMessage = "");

[CultDocument("aquasynth.render_sample_receipt", AquaSynthDaemonSchemas.RenderSampleReceipt)]
[MessagePackObject]
public sealed record AquaSynthRenderSampleReceipt(
    [property: Key(0), CultName] string RenderId,
    [property: Key(1), CultIndex("command")] string CommandId,
    [property: Key(2), CultIndex("patch")] string PatchId,
    [property: Key(3)] string SessionId,
    [property: Key(4)] string Status,
    [property: Key(5)] string CompletedAtUtc,
    [property: Key(6)] int SampleRate,
    [property: Key(7)] int SampleCount,
    [property: Key(8)] float DurationSeconds,
    [property: Key(9)] float Peak,
    [property: Key(10)] float Rms,
    [property: Key(11)] string Float32Uri,
    [property: Key(12)] string WavUri,
    [property: Key(13)] string ContentHash,
    [property: Key(14)] string FailureCode = "",
    [property: Key(15)] string FailureMessage = "");

[CultDocument("aquasynth.operator_state", AquaSynthDaemonSchemas.OperatorState)]
[CultGlobal]
[MessagePackObject]
public sealed record AquaSynthOperatorState(
    [property: Key(0)] string ServiceId,
    [property: Key(1)] string UpdatedAtUtc,
    [property: Key(2)] string StoreRoot,
    [property: Key(3)] int CompileCount,
    [property: Key(4)] int RenderCount,
    [property: Key(5)] string LastCommandId,
    [property: Key(6)] string LastStatus,
    [property: Key(7)] string[] CultMeshKeys);

public sealed record AquaSynthSampleResult(
    AquaSynthPatchCompileReceipt CompileReceipt,
    AquaSynthRenderSampleReceipt RenderReceipt);

public sealed class AquaSynthDaemonService : IDisposable
{
    private readonly AquaSynthDaemonOptions options;
    private readonly AquaSynthPatchCompiler compiler;
    private readonly Dictionary<string, AquaSynthCompiledPatch> sessions = new(StringComparer.Ordinal);
    private int compileCount;
    private int renderCount;
    private bool disposed;

    public AquaSynthDaemonService(AquaSynthDaemonOptions? options = null)
    {
        this.options = options ?? AquaSynthDaemonOptions.Default;
        Directory.CreateDirectory(this.options.StoreRoot);
        compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(
            SampleRate: this.options.SampleRate,
            MinRenderSeconds: this.options.MinRenderSeconds,
            MaxRenderSeconds: this.options.MaxRenderSeconds));
    }

    public async Task<AquaSynthPatchCompileReceipt> CompileAsync(AquaSynthPatchCompileCommand command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateCompile(command);

        var identity = new AquaSynthCompileIdentity(command.PatchId, command.FaustName, command.Script, command.Revision);
        var compiled = command.DurationSeconds is { } requestedDuration
            ? TryCompileScriptForDuration(identity, requestedDuration, out var patch, out var error)
            : compiler.TryCompileScript(identity, out patch, out error);

        if (!compiled)
        {
            var failed = new AquaSynthPatchCompileReceipt(
                ReceiptId(command.CommandId, "compile"),
                command.CommandId,
                command.PatchId,
                "",
                "failed",
                Now(),
                "",
                0,
                [],
                [],
                "compile_failed",
                error ?? "AquaSynth patch compile failed.");
            await WriteReceiptAsync("compile", failed.ReceiptId, failed).ConfigureAwait(false);
            await WriteOperatorStateAsync(command.CommandId, failed.Status).ConfigureAwait(false);
            return failed;
        }

        var sessionId = SessionId(command.PatchId, patch!.Manifest.CompileKey);
        if (sessions.Remove(sessionId, out var old))
        {
            old.Dispose();
        }

        sessions[sessionId] = patch;
        compileCount++;

        var session = new AquaSynthCompiledInstrumentSession(
            sessionId,
            command.PatchId,
            patch.Manifest.CompileKey,
            patch.Manifest.FaustName,
            patch.Manifest.Revision,
            patch.Manifest.SampleRate,
            patch.Manifest.InputCount,
            patch.Manifest.OutputCount,
            patch.Manifest.FrameCount,
            patch.Manifest.DurationSeconds,
            Now(),
            [.. patch.ControlPaths],
            [.. patch.ProbePaths]);

        var receipt = new AquaSynthPatchCompileReceipt(
            ReceiptId(command.CommandId, "compile"),
            command.CommandId,
            command.PatchId,
            sessionId,
            "succeeded",
            Now(),
            patch.Manifest.CompileKey,
            patch.Manifest.CompileMilliseconds,
            [.. patch.ControlPaths],
            [.. patch.ProbePaths]);

        await WriteReceiptAsync("sessions", sessionId, session).ConfigureAwait(false);
        await WriteReceiptAsync("compile", receipt.ReceiptId, receipt).ConfigureAwait(false);
        await WriteOperatorStateAsync(command.CommandId, receipt.Status).ConfigureAwait(false);
        return receipt;
    }

    public async Task<AquaSynthSampleResult> SampleAsync(AquaSynthInstrumentSampleCommand command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateSample(command);

        var compile = await CompileAsync(new AquaSynthPatchCompileCommand(
            command.CommandId,
            command.PatchId,
            command.FaustName,
            command.Script,
            command.Revision,
            command.DurationSeconds)).ConfigureAwait(false);

        if (!string.Equals(compile.Status, "succeeded", StringComparison.Ordinal))
        {
            var failed = new AquaSynthRenderSampleReceipt(
                RenderId(command.CommandId),
                command.CommandId,
                command.PatchId,
                compile.SessionId,
                "failed",
                Now(),
                options.SampleRate,
                0,
                command.DurationSeconds,
                0,
                0,
                "",
                "",
                "",
                "compile_failed",
                compile.FailureMessage);
            await WriteReceiptAsync("renders", failed.RenderId, failed).ConfigureAwait(false);
            await WriteOperatorStateAsync(command.CommandId, failed.Status).ConfigureAwait(false);
            return new AquaSynthSampleResult(compile, failed);
        }

        var patch = sessions[compile.SessionId];
        var controls = NormalizeControls(command.Controls);
        var samples = patch.Render(controls, command.Gain);
        renderCount++;

        var renderId = RenderId(command.CommandId);
        var sampleDir = Path.Combine(options.StoreRoot, "samples");
        Directory.CreateDirectory(sampleDir);
        var floatPath = Path.Combine(sampleDir, $"{renderId}.f32");
        var wavPath = Path.Combine(sampleDir, $"{renderId}.wav");
        await WriteFloat32Async(floatPath, samples).ConfigureAwait(false);
        await WriteWavAsync(wavPath, samples, patch.Manifest.SampleRate).ConfigureAwait(false);

        var receipt = new AquaSynthRenderSampleReceipt(
            renderId,
            command.CommandId,
            command.PatchId,
            compile.SessionId,
            "succeeded",
            Now(),
            patch.Manifest.SampleRate,
            samples.Length,
            samples.Length / (float)patch.Manifest.SampleRate,
            Peak(samples),
            Rms(samples),
            ToArtifactUri(floatPath),
            ToArtifactUri(wavPath),
            await Sha256Async(floatPath).ConfigureAwait(false));

        await WriteReceiptAsync("renders", receipt.RenderId, receipt).ConfigureAwait(false);
        await WriteOperatorStateAsync(command.CommandId, receipt.Status).ConfigureAwait(false);
        return new AquaSynthSampleResult(compile, receipt);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        foreach (var patch in sessions.Values)
        {
            patch.Dispose();
        }

        sessions.Clear();
        compiler.Dispose();
        disposed = true;
    }

    private async Task WriteOperatorStateAsync(string commandId, string status)
    {
        var state = new AquaSynthOperatorState(
            "aquasynth.service",
            Now(),
            options.StoreRoot,
            compileCount,
            renderCount,
            commandId,
            status,
            [
                "aquasynth.service/provider-advertisement",
                "aquasynth.compile/jobs",
                "aquasynth.instrument/sessions",
                "aquasynth.render/samples",
                "aquasynth.operator/status"
            ]);
        await WriteReceiptAsync("operator", "operator-state", state, global: true).ConfigureAwait(false);
    }

    private bool TryCompileScriptForDuration(
        AquaSynthCompileIdentity identity,
        float durationSeconds,
        out AquaSynthCompiledPatch? patch,
        out string? error)
    {
        patch = null;
        try
        {
            var faustName = AquaSynthNativeCompiler.SafeFaustName(identity.FaustName);
            var source = FaustEmitter.EmitScript(identity.Script, new FaustExportOptions(faustName)).Source;
            return compiler.TryCompileSource(identity, source, durationSeconds, out patch, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private async Task WriteReceiptAsync<T>(string family, string id, T document, bool global = false)
        where T : class
    {
        var filePath = Path.Combine(options.StoreRoot, family, $"{SafeFileName(id)}.cc");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        using var cache = CultCacheMessagePack.Create(filePath, new CultCacheOpenOptions { PullOnOpen = false });
        var key = global ? new CultRecordKey("global") : new CultRecordKey(id);
        await cache.UpsertAsync(document, new CultRecordHandle<T>(key)).ConfigureAwait(false);
        await cache.FlushAsync().ConfigureAwait(false);
    }

    private static Dictionary<string, float>? NormalizeControls(Dictionary<string, float>? controls) =>
        controls is null || controls.Count == 0
            ? null
            : controls.ToDictionary(pair => pair.Key.TrimStart('/'), pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static void ValidateCompile(AquaSynthPatchCompileCommand command)
    {
        Require(command.CommandId, nameof(command.CommandId));
        Require(command.PatchId, nameof(command.PatchId));
        Require(command.FaustName, nameof(command.FaustName));
        Require(command.Script, nameof(command.Script));
    }

    private static void ValidateSample(AquaSynthInstrumentSampleCommand command)
    {
        Require(command.CommandId, nameof(command.CommandId));
        Require(command.PatchId, nameof(command.PatchId));
        Require(command.FaustName, nameof(command.FaustName));
        Require(command.Script, nameof(command.Script));
        if (command.DurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.DurationSeconds), "Sample duration must be positive.");
        }
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} must not be empty.", name);
        }
    }

    private static async Task WriteFloat32Async(string path, float[] samples)
    {
        await using var stream = File.Create(path);
        var buffer = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, buffer, 0, buffer.Length);
        await stream.WriteAsync(buffer).ConfigureAwait(false);
    }

    private static async Task WriteWavAsync(string path, float[] samples, int sampleRate)
    {
        await using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        var dataBytes = samples.Length * sizeof(short);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
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
        writer.Write(dataBytes);
        foreach (var sample in samples)
        {
            writer.Write((short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue));
        }
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static float Peak(IReadOnlyList<float> samples) =>
        samples.Count == 0 ? 0 : samples.Max(sample => MathF.Abs(sample));

    private static float Rms(IReadOnlyList<float> samples) =>
        samples.Count == 0 ? 0 : MathF.Sqrt(samples.Sum(sample => sample * sample) / samples.Count);

    private static string ReceiptId(string commandId, string kind) => $"{commandId}-{kind}";

    private static string RenderId(string commandId) => $"{commandId}-render";

    private static string SessionId(string patchId, string compileKey) => $"{SafeFileName(patchId)}-{compileKey[..Math.Min(12, compileKey.Length)]}";

    private static string SafeFileName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-');
        }

        return builder.ToString().Trim('-');
    }

    private static string ToArtifactUri(string path) => $"file:///{Path.GetFullPath(path).Replace('\\', '/')}";

    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
}
