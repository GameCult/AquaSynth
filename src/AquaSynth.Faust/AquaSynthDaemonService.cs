using System.Security.Cryptography;
using System.Text;
using AquaSynth.Dsl;
using GameCult.Caching;
using GameCult.Caching.MessagePack;
using GameCult.Mesh;
using MessagePack;

namespace AquaSynth.Faust;

public static class AquaSynthDaemonSchemas
{
    public const string PatchCompileCommand = "aquasynth.command.patch_compile.v1";
    public const string InstrumentSampleCommand = "aquasynth.command.instrument_sample.v1";
    public const string CompiledInstrumentSession = "aquasynth.compiled_instrument_session.v1";
    public const string PatchCompileReceipt = "aquasynth.patch_compile_receipt.v1";
    public const string RenderSampleReceipt = "aquasynth.render_sample_receipt.v1";
    public const string AutomationStreamCommand = "aquasynth.command.automation_stream.v1";
    public const string AutomationStreamReceipt = "aquasynth.automation_stream_receipt.v1";
    public const string InstrumentOpenCommand = "aquasynth.command.instrument_open.v1";
    public const string InstrumentControlCommand = "aquasynth.command.instrument_control.v1";
    public const string InstrumentBlockCommand = "aquasynth.command.instrument_block.v1";
    public const string InstrumentOpenReceipt = "aquasynth.instrument_open_receipt.v1";
    public const string InstrumentControlReceipt = "aquasynth.instrument_control_receipt.v1";
    public const string InstrumentBlockReceipt = "aquasynth.instrument_block_receipt.v1";
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

[CultDocument("aquasynth.command.automation_stream", AquaSynthDaemonSchemas.AutomationStreamCommand)]
[MessagePackObject]
public sealed record AquaSynthAutomationStreamCommand(
    [property: Key(0), CultName] string CommandId,
    [property: Key(1)] string PatchId,
    [property: Key(2)] string FaustName,
    [property: Key(3)] string Script,
    [property: Key(4)] int BlockSize = 128,
    [property: Key(5)] int BlockCount = 8,
    [property: Key(6)] AquaSynthAutomationControlFrame[]? ControlFrames = null,
    [property: Key(7)] float Gain = 1.0f,
    [property: Key(8)] int Revision = 1);

[MessagePackObject]
public sealed record AquaSynthAutomationControlFrame(
    [property: Key(0)] int Block,
    [property: Key(1)] Dictionary<string, float> Controls);

[CultDocument("aquasynth.command.instrument_open", AquaSynthDaemonSchemas.InstrumentOpenCommand)]
[MessagePackObject]
public sealed record AquaSynthInstrumentOpenCommand(
    [property: Key(0), CultName] string CommandId,
    [property: Key(1)] string PatchId,
    [property: Key(2)] string FaustName,
    [property: Key(3)] string Script,
    [property: Key(4)] int BlockSize = 128,
    [property: Key(5)] float Gain = 1.0f,
    [property: Key(6)] Dictionary<string, float>? Controls = null,
    [property: Key(7)] int Revision = 1);

[CultDocument("aquasynth.command.instrument_control", AquaSynthDaemonSchemas.InstrumentControlCommand)]
[MessagePackObject]
public sealed record AquaSynthInstrumentControlCommand(
    [property: Key(0), CultName] string CommandId,
    [property: Key(1), CultIndex("session")] string SessionId,
    [property: Key(2)] Dictionary<string, float> Controls);

[CultDocument("aquasynth.command.instrument_block", AquaSynthDaemonSchemas.InstrumentBlockCommand)]
[MessagePackObject]
public sealed record AquaSynthInstrumentBlockCommand(
    [property: Key(0), CultName] string CommandId,
    [property: Key(1), CultIndex("session")] string SessionId,
    [property: Key(2)] int FrameCount = 128,
    [property: Key(3)] float Gain = 1.0f,
    [property: Key(4)] Dictionary<string, float>? Controls = null);

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

[CultDocument("aquasynth.automation_stream_receipt", AquaSynthDaemonSchemas.AutomationStreamReceipt)]
[MessagePackObject]
public sealed record AquaSynthAutomationStreamReceipt(
    [property: Key(0), CultName] string StreamRunId,
    [property: Key(1), CultIndex("command")] string CommandId,
    [property: Key(2), CultIndex("patch")] string PatchId,
    [property: Key(3)] string SessionId,
    [property: Key(4)] string Status,
    [property: Key(5)] string CompletedAtUtc,
    [property: Key(6)] string VerseId,
    [property: Key(7)] string AudioStreamId,
    [property: Key(8)] string ControlStreamId,
    [property: Key(9)] int SampleRate,
    [property: Key(10)] int BlockSize,
    [property: Key(11)] int BlockCount,
    [property: Key(12)] AquaSynthCultMeshStreamDescriptor[] Streams,
    [property: Key(13)] AquaSynthAutomationPacketReceipt[] Packets,
    [property: Key(14)] string FailureCode = "",
    [property: Key(15)] string FailureMessage = "");

[CultDocument("aquasynth.instrument_open_receipt", AquaSynthDaemonSchemas.InstrumentOpenReceipt)]
[MessagePackObject]
public sealed record AquaSynthInstrumentOpenReceipt(
    [property: Key(0), CultName] string ReceiptId,
    [property: Key(1), CultIndex("command")] string CommandId,
    [property: Key(2), CultIndex("patch")] string PatchId,
    [property: Key(3), CultIndex("session")] string SessionId,
    [property: Key(4)] string Status,
    [property: Key(5)] string OpenedAtUtc,
    [property: Key(6)] int SampleRate,
    [property: Key(7)] int InputCount,
    [property: Key(8)] int OutputCount,
    [property: Key(9)] int BlockSize,
    [property: Key(10)] string[] ControlPaths,
    [property: Key(11)] string[] ProbePaths,
    [property: Key(12)] string FailureCode = "",
    [property: Key(13)] string FailureMessage = "");

[CultDocument("aquasynth.instrument_control_receipt", AquaSynthDaemonSchemas.InstrumentControlReceipt)]
[MessagePackObject]
public sealed record AquaSynthInstrumentControlReceipt(
    [property: Key(0), CultName] string ReceiptId,
    [property: Key(1), CultIndex("command")] string CommandId,
    [property: Key(2), CultIndex("session")] string SessionId,
    [property: Key(3)] string Status,
    [property: Key(4)] string AppliedAtUtc,
    [property: Key(5)] int ControlCount,
    [property: Key(6)] string FailureCode = "",
    [property: Key(7)] string FailureMessage = "");

[CultDocument("aquasynth.instrument_block_receipt", AquaSynthDaemonSchemas.InstrumentBlockReceipt)]
[MessagePackObject]
public sealed record AquaSynthInstrumentBlockReceipt(
    [property: Key(0), CultName] string BlockId,
    [property: Key(1), CultIndex("command")] string CommandId,
    [property: Key(2), CultIndex("session")] string SessionId,
    [property: Key(3)] string Status,
    [property: Key(4)] string CompletedAtUtc,
    [property: Key(5)] ulong Sequence,
    [property: Key(6)] int SampleRate,
    [property: Key(7)] int SampleCount,
    [property: Key(8)] float Peak,
    [property: Key(9)] float Rms,
    [property: Key(10)] string Float32Uri,
    [property: Key(11)] string ContentHash,
    [property: Key(12)] string FailureCode = "",
    [property: Key(13)] string FailureMessage = "");

[MessagePackObject]
public sealed record AquaSynthCultMeshStreamDescriptor(
    [property: Key(0)] string StreamId,
    [property: Key(1)] string VerseId,
    [property: Key(2)] string OwnerPeerId,
    [property: Key(3)] string Kind,
    [property: Key(4)] string SampleFormat,
    [property: Key(5)] int SampleRate,
    [property: Key(6)] int Channels,
    [property: Key(7)] int FramesPerPacket,
    [property: Key(8)] string[] PreferredTransports,
    [property: Key(9)] string? MetadataSchemaId);

[MessagePackObject]
public sealed record AquaSynthAutomationPacketReceipt(
    [property: Key(0)] ulong Sequence,
    [property: Key(1)] long TimestampNs,
    [property: Key(2)] long DurationNs,
    [property: Key(3)] int SampleCount,
    [property: Key(4)] float Peak,
    [property: Key(5)] float Rms,
    [property: Key(6)] string Float32Uri,
    [property: Key(7)] string ContentHash,
    [property: Key(8)] string PageRef,
    [property: Key(9)] int ControlCount);

[CultDocument("aquasynth.operator_state", AquaSynthDaemonSchemas.OperatorState)]
[CultGlobal]
[MessagePackObject]
public sealed record AquaSynthOperatorState(
    [property: Key(0)] string ServiceId,
    [property: Key(1)] string UpdatedAtUtc,
    [property: Key(2)] string StoreRoot,
    [property: Key(3)] int CompileCount,
    [property: Key(4)] int RenderCount,
    [property: Key(5)] int StreamCount,
    [property: Key(6)] string LastCommandId,
    [property: Key(7)] string LastStatus,
    [property: Key(8)] string[] CultMeshKeys);

public sealed record AquaSynthSampleResult(
    AquaSynthPatchCompileReceipt CompileReceipt,
    AquaSynthRenderSampleReceipt RenderReceipt);

public sealed class AquaSynthDaemonService : IDisposable
{
    private readonly AquaSynthDaemonOptions options;
    private readonly AquaSynthPatchCompiler compiler;
    private readonly Dictionary<string, AquaSynthCompiledPatch> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LiveInstrumentSession> liveSessions = new(StringComparer.Ordinal);
    private int compileCount;
    private int renderCount;
    private int streamCount;
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
        if (liveSessions.Remove(sessionId, out var live))
        {
            live.Dispose();
        }

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

    public async Task<AquaSynthInstrumentOpenReceipt> OpenInstrumentAsync(AquaSynthInstrumentOpenCommand command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateOpen(command);

        var durationSeconds = command.BlockSize / (float)options.SampleRate;
        var compile = await CompileAsync(new AquaSynthPatchCompileCommand(
            command.CommandId,
            command.PatchId,
            command.FaustName,
            command.Script,
            command.Revision,
            durationSeconds)).ConfigureAwait(false);

        if (!string.Equals(compile.Status, "succeeded", StringComparison.Ordinal))
        {
            var failed = new AquaSynthInstrumentOpenReceipt(
                ReceiptId(command.CommandId, "open"),
                command.CommandId,
                command.PatchId,
                compile.SessionId,
                "failed",
                Now(),
                options.SampleRate,
                0,
                0,
                command.BlockSize,
                [],
                [],
                "compile_failed",
                compile.FailureMessage);
            await WriteReceiptAsync("live", failed.ReceiptId, failed).ConfigureAwait(false);
            await WriteOperatorStateAsync(command.CommandId, failed.Status).ConfigureAwait(false);
            return failed;
        }

        var patch = sessions[compile.SessionId];
        var controls = NormalizeControls(command.Controls) ?? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        if (liveSessions.Remove(compile.SessionId, out var oldLive))
        {
            oldLive.Dispose();
        }

        liveSessions[compile.SessionId] = new LiveInstrumentSession(
            compile.SessionId,
            command.PatchId,
            command.BlockSize,
            command.Gain,
            patch,
            patch.CreateStreamingPatch(),
            controls);

        var receipt = new AquaSynthInstrumentOpenReceipt(
            ReceiptId(command.CommandId, "open"),
            command.CommandId,
            command.PatchId,
            compile.SessionId,
            "succeeded",
            Now(),
            patch.Manifest.SampleRate,
            patch.Manifest.InputCount,
            patch.Manifest.OutputCount,
            command.BlockSize,
            [.. patch.ControlPaths],
            [.. patch.ProbePaths]);
        await WriteReceiptAsync("live", receipt.ReceiptId, receipt).ConfigureAwait(false);
        await WriteOperatorStateAsync(command.CommandId, receipt.Status).ConfigureAwait(false);
        return receipt;
    }

    public async Task<AquaSynthInstrumentControlReceipt> ControlInstrumentAsync(AquaSynthInstrumentControlCommand command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateControl(command);

        if (!liveSessions.TryGetValue(command.SessionId, out var session))
        {
            var missing = new AquaSynthInstrumentControlReceipt(
                ReceiptId(command.CommandId, "control"),
                command.CommandId,
                command.SessionId,
                "failed",
                Now(),
                0,
                "session_not_found",
                $"Live AquaSynth session '{command.SessionId}' is not open.");
            await WriteReceiptAsync("live", missing.ReceiptId, missing).ConfigureAwait(false);
            await WriteOperatorStateAsync(command.CommandId, missing.Status).ConfigureAwait(false);
            return missing;
        }

        var controls = NormalizeControls(command.Controls) ?? [];
        session.ApplyControls(controls);
        var receipt = new AquaSynthInstrumentControlReceipt(
            ReceiptId(command.CommandId, "control"),
            command.CommandId,
            command.SessionId,
            "succeeded",
            Now(),
            controls.Count);
        await WriteReceiptAsync("live", receipt.ReceiptId, receipt).ConfigureAwait(false);
        await WriteOperatorStateAsync(command.CommandId, receipt.Status).ConfigureAwait(false);
        return receipt;
    }

    public async Task<AquaSynthInstrumentBlockReceipt> ProcessInstrumentBlockAsync(AquaSynthInstrumentBlockCommand command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateBlock(command);

        if (!liveSessions.TryGetValue(command.SessionId, out var session))
        {
            var missing = new AquaSynthInstrumentBlockReceipt(
                BlockId(command.CommandId),
                command.CommandId,
                command.SessionId,
                "failed",
                Now(),
                0,
                options.SampleRate,
                0,
                0,
                0,
                "",
                "",
                "session_not_found",
                $"Live AquaSynth session '{command.SessionId}' is not open.");
            await WriteReceiptAsync("live", missing.BlockId, missing).ConfigureAwait(false);
            await WriteOperatorStateAsync(command.CommandId, missing.Status).ConfigureAwait(false);
            return missing;
        }

        if (NormalizeControls(command.Controls) is { } controls)
        {
            session.ApplyControls(controls);
        }

        var outputs = new float[Math.Max(1, session.Stream.OutputCount)][];
        for (var channel = 0; channel < outputs.Length; channel++)
        {
            outputs[channel] = new float[command.FrameCount];
        }

        session.Stream.ProcessBlock([], outputs, command.FrameCount, session.Controls);
        var mono = MixMono(outputs, command.FrameCount, command.Gain * session.Gain);
        var blockId = BlockId(command.CommandId);
        var blockDir = Path.Combine(options.StoreRoot, "live", SafeFileName(command.SessionId));
        Directory.CreateDirectory(blockDir);
        var floatPath = Path.Combine(blockDir, $"{SafeFileName(blockId)}.f32");
        await WriteFloat32Async(floatPath, mono).ConfigureAwait(false);

        var receipt = new AquaSynthInstrumentBlockReceipt(
            blockId,
            command.CommandId,
            command.SessionId,
            "succeeded",
            Now(),
            session.NextSequence(),
            session.Patch.Manifest.SampleRate,
            mono.Length,
            Peak(mono),
            Rms(mono),
            ToArtifactUri(floatPath),
            await Sha256Async(floatPath).ConfigureAwait(false));
        await WriteReceiptAsync("live", receipt.BlockId, receipt).ConfigureAwait(false);
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

    public async Task<AquaSynthAutomationStreamReceipt> StreamAutomationAsync(AquaSynthAutomationStreamCommand command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateStream(command);

        var durationSeconds = command.BlockSize * command.BlockCount / (float)options.SampleRate;
        var compile = await CompileAsync(new AquaSynthPatchCompileCommand(
            command.CommandId,
            command.PatchId,
            command.FaustName,
            command.Script,
            command.Revision,
            durationSeconds)).ConfigureAwait(false);

        var runId = $"{command.CommandId}-stream";
        var verseId = "aquasynth.instrument";
        var audioStreamId = $"aquasynth.audio.{SafeFileName(command.CommandId)}";
        var controlStreamId = $"aquasynth.controls.{SafeFileName(command.CommandId)}";

        if (!string.Equals(compile.Status, "succeeded", StringComparison.Ordinal))
        {
            var failed = new AquaSynthAutomationStreamReceipt(
                runId,
                command.CommandId,
                command.PatchId,
                compile.SessionId,
                "failed",
                Now(),
                verseId,
                audioStreamId,
                controlStreamId,
                options.SampleRate,
                command.BlockSize,
                command.BlockCount,
                [],
                [],
                "compile_failed",
                compile.FailureMessage);
            await WriteReceiptAsync("streams", failed.StreamRunId, failed).ConfigureAwait(false);
            await WriteOperatorStateAsync(command.CommandId, failed.Status).ConfigureAwait(false);
            return failed;
        }

        var patch = sessions[compile.SessionId];
        var catalog = new CultMeshStreamCatalog();
        var audioDescriptor = CreateAudioStreamDescriptor(audioStreamId, verseId, command.BlockSize, patch.Manifest.SampleRate);
        var controlDescriptor = CreateControlStreamDescriptor(controlStreamId, verseId, command.BlockSize, patch.Manifest.SampleRate);
        catalog.Declare(audioDescriptor);
        catalog.Declare(controlDescriptor);

        var controlsByBlock = (command.ControlFrames ?? [])
            .GroupBy(frame => frame.Block)
            .ToDictionary(group => group.Key, group => NormalizeControls(group.Last().Controls), EqualityComparer<int>.Default);

        var streamDir = Path.Combine(options.StoreRoot, "streams", SafeFileName(runId));
        Directory.CreateDirectory(streamDir);
        var packets = new List<AquaSynthAutomationPacketReceipt>();
        using (var stream = patch.CreateStreamingPatch())
        {
            for (var block = 0; block < command.BlockCount; block++)
            {
                controlsByBlock.TryGetValue(block, out var controls);
                var outputs = new float[Math.Max(1, stream.OutputCount)][];
                for (var channel = 0; channel < outputs.Length; channel++)
                {
                    outputs[channel] = new float[command.BlockSize];
                }

                stream.ProcessBlock([], outputs, command.BlockSize, controls);
                var mono = MixMono(outputs, command.BlockSize, command.Gain);
                var floatPath = Path.Combine(streamDir, $"audio-{block:D6}.f32");
                await WriteFloat32Async(floatPath, mono).ConfigureAwait(false);

                var timestampNs = block * command.BlockSize * 1_000_000_000L / patch.Manifest.SampleRate;
                var durationNs = command.BlockSize * 1_000_000_000L / patch.Manifest.SampleRate;
                var pageRef = ToArtifactUri(floatPath);
                var audioHandle = new CultMeshStreamFrameHandle(
                    audioStreamId,
                    (ulong)block,
                    timestampNs,
                    CultMeshStreamBodyTransport.CultCachePage,
                    durationNs,
                    mono.Length * sizeof(float),
                    pageRef: pageRef,
                    metadata: new Dictionary<string, string>
                    {
                        ["schema"] = AquaSynthDaemonSchemas.AutomationStreamReceipt,
                        ["patchId"] = command.PatchId,
                        ["sessionId"] = compile.SessionId
                    });
                catalog.PublishFrame(audioHandle);
                catalog.PublishFrame(new CultMeshStreamFrameHandle(
                    controlStreamId,
                    (ulong)block,
                    timestampNs,
                    CultMeshStreamBodyTransport.InlineBytes,
                    durationNs,
                    controls?.Count ?? 0,
                    metadata: (controls ?? new Dictionary<string, float>())
                        .ToDictionary(pair => pair.Key, pair => pair.Value.ToString("R"), StringComparer.OrdinalIgnoreCase)));

                packets.Add(new AquaSynthAutomationPacketReceipt(
                    (ulong)block,
                    timestampNs,
                    durationNs,
                    mono.Length,
                    Peak(mono),
                    Rms(mono),
                    pageRef,
                    await Sha256Async(floatPath).ConfigureAwait(false),
                    audioHandle.PageRef ?? pageRef,
                    controls?.Count ?? 0));
            }
        }

        streamCount++;
        var receipt = new AquaSynthAutomationStreamReceipt(
            runId,
            command.CommandId,
            command.PatchId,
            compile.SessionId,
            "succeeded",
            Now(),
            verseId,
            audioStreamId,
            controlStreamId,
            patch.Manifest.SampleRate,
            command.BlockSize,
            command.BlockCount,
            [ToReceiptDescriptor(audioDescriptor), ToReceiptDescriptor(controlDescriptor)],
            [.. packets]);
        await WriteReceiptAsync("streams", receipt.StreamRunId, receipt).ConfigureAwait(false);
        await WriteOperatorStateAsync(command.CommandId, receipt.Status).ConfigureAwait(false);
        return receipt;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        foreach (var session in liveSessions.Values)
        {
            session.Dispose();
        }

        foreach (var patch in sessions.Values)
        {
            patch.Dispose();
        }

        liveSessions.Clear();
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
            streamCount,
            commandId,
            status,
            [
                "aquasynth.service/provider-advertisement",
                "aquasynth.compile/jobs",
                "aquasynth.instrument/sessions",
                "aquasynth.render/samples",
                "aquasynth.instrument/automation-streams",
                "aquasynth.instrument/live-sessions",
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

    private static void ValidateStream(AquaSynthAutomationStreamCommand command)
    {
        Require(command.CommandId, nameof(command.CommandId));
        Require(command.PatchId, nameof(command.PatchId));
        Require(command.FaustName, nameof(command.FaustName));
        Require(command.Script, nameof(command.Script));
        if (command.BlockSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.BlockSize), "Stream block size must be positive.");
        }

        if (command.BlockCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.BlockCount), "Stream block count must be positive.");
        }
    }

    private static void ValidateOpen(AquaSynthInstrumentOpenCommand command)
    {
        Require(command.CommandId, nameof(command.CommandId));
        Require(command.PatchId, nameof(command.PatchId));
        Require(command.FaustName, nameof(command.FaustName));
        Require(command.Script, nameof(command.Script));
        if (command.BlockSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.BlockSize), "Live instrument block size must be positive.");
        }
    }

    private static void ValidateControl(AquaSynthInstrumentControlCommand command)
    {
        Require(command.CommandId, nameof(command.CommandId));
        Require(command.SessionId, nameof(command.SessionId));
        if (command.Controls.Count == 0)
        {
            throw new ArgumentException("At least one control value is required.", nameof(command.Controls));
        }
    }

    private static void ValidateBlock(AquaSynthInstrumentBlockCommand command)
    {
        Require(command.CommandId, nameof(command.CommandId));
        Require(command.SessionId, nameof(command.SessionId));
        if (command.FrameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.FrameCount), "Live instrument frame count must be positive.");
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

    private static float[] MixMono(IReadOnlyList<float[]> outputs, int frameCount, float gain)
    {
        var mono = new float[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = 0.0f;
            for (var channel = 0; channel < outputs.Count; channel++)
            {
                sample += outputs[channel][frame];
            }

            mono[frame] = Math.Clamp(sample / Math.Max(1, outputs.Count) * gain, -1.0f, 1.0f);
        }

        return mono;
    }

    private static CultMeshStreamDescriptor CreateAudioStreamDescriptor(string streamId, string verseId, int blockSize, int sampleRate) =>
        new(
            streamId,
            verseId,
            "aquasynth.service",
            CultMeshStreamKind.Audio,
            new CultMeshStreamClock("aquasynth.audio.clock", "native-faust", sampleRate, confidence: 1.0, evidenceKind: "native-faust-block"),
            [CultMeshStreamBodyTransport.CultCachePage, CultMeshStreamBodyTransport.InlineBytes],
            label: "AquaSynth rendered audio blocks",
            audio: new CultMeshAudioStreamFormat(sampleRate, 1, "f32", blockSize),
            requiredAccess: CultMeshStreamAccess.Read,
            maxInFlightFrames: 4,
            metadataSchemaId: AquaSynthDaemonSchemas.AutomationStreamReceipt);

    private static CultMeshStreamDescriptor CreateControlStreamDescriptor(string streamId, string verseId, int blockSize, int sampleRate) =>
        new(
            streamId,
            verseId,
            "aquasynth.service",
            CultMeshStreamKind.Tensor,
            new CultMeshStreamClock("aquasynth.control.clock", "daemon-automation", sampleRate, confidence: 1.0, evidenceKind: "block-control-frame"),
            [CultMeshStreamBodyTransport.InlineBytes, CultMeshStreamBodyTransport.CultCachePage],
            label: "AquaSynth automation control blocks",
            audio: null,
            requiredAccess: CultMeshStreamAccess.Read,
            maxInFlightFrames: 4,
            metadataSchemaId: AquaSynthDaemonSchemas.AutomationStreamCommand);

    private static AquaSynthCultMeshStreamDescriptor ToReceiptDescriptor(CultMeshStreamDescriptor descriptor) =>
        new(
            descriptor.StreamId,
            descriptor.VerseId,
            descriptor.OwnerPeerId,
            descriptor.Kind.ToString(),
            descriptor.Audio?.SampleFormat ?? "controls",
            descriptor.Audio?.SampleRate ?? descriptor.Clock.SampleRate,
            descriptor.Audio?.Channels ?? 1,
            descriptor.Audio?.FramesPerPacket ?? 0,
            descriptor.PreferredTransports.Select(transport => transport.ToString()).ToArray(),
            descriptor.MetadataSchemaId);

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

    private static string BlockId(string commandId) => $"{commandId}-block";

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

    private sealed class LiveInstrumentSession : IDisposable
    {
        private ulong sequence;
        private bool disposed;

        public LiveInstrumentSession(
            string sessionId,
            string patchId,
            int blockSize,
            float gain,
            AquaSynthCompiledPatch patch,
            AquaSynthStreamingPatch stream,
            Dictionary<string, float> controls)
        {
            SessionId = sessionId;
            PatchId = patchId;
            BlockSize = blockSize;
            Gain = gain;
            Patch = patch;
            Stream = stream;
            Controls = controls;
        }

        public string SessionId { get; }

        public string PatchId { get; }

        public int BlockSize { get; }

        public float Gain { get; }

        public AquaSynthCompiledPatch Patch { get; }

        public AquaSynthStreamingPatch Stream { get; }

        public Dictionary<string, float> Controls { get; }

        public void ApplyControls(IReadOnlyDictionary<string, float> controls)
        {
            foreach (var pair in controls)
            {
                Controls[pair.Key] = pair.Value;
            }
        }

        public ulong NextSequence() => sequence++;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Stream.Dispose();
            disposed = true;
        }
    }
}
