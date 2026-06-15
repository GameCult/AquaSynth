using GameCult.Caching;
using GameCult.Networking;
using MessagePack;
using R3;

namespace AquaSynth.Faust;

public sealed record AquaSynthCultNetDaemonOptions(
    string StoreRoot,
    string RuntimeId = "aquasynth.daemon",
    string? CachePath = null)
{
    public string ResolvedCachePath =>
        string.IsNullOrWhiteSpace(CachePath)
            ? Path.Combine(StoreRoot, "cultnet", "aquasynth-daemon.ccmp")
            : CachePath!;
}

[CultDocument("aquasynth.cultnet_provider", "aquasynth.cultnet_provider.v1")]
[CultGlobal]
[MessagePackObject]
public sealed record AquaSynthCultNetProviderState(
    [property: Key(0)] string ProviderId,
    [property: Key(1)] string RuntimeId,
    [property: Key(2)] string UpdatedAtUtc,
    [property: Key(3)] string StoreRoot,
    [property: Key(4)] string CachePath,
    [property: Key(5)] string[] CommandSchemas,
    [property: Key(6)] string[] ReceiptSchemas,
    [property: Key(7)] string[] OperatorKeys);

public sealed class AquaSynthCultNetDaemon : IAsyncDisposable
{
    private readonly AquaSynthCultNetDaemonOptions options;
    private readonly AquaSynthDaemonService service;
    private readonly SemaphoreSlim commandGate = new(1, 1);
    private readonly HashSet<string> handledCommands = new(StringComparer.Ordinal);
    private readonly List<IDisposable> subscriptions = [];
    private CultNetHost? host;

    private AquaSynthCultNetDaemon(
        AquaSynthCultNetDaemonOptions options,
        AquaSynthDaemonService service)
    {
        this.options = options;
        this.service = service;
    }

    public CultNetDatabase Database =>
        host?.Database ?? throw new InvalidOperationException("AquaSynth CultNet daemon has not been started.");

    public static async Task<AquaSynthCultNetDaemon> StartAsync(
        AquaSynthCultNetDaemonOptions options,
        AquaSynthDaemonService? service = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.ResolvedCachePath)!);
        var daemon = new AquaSynthCultNetDaemon(
            options,
            service ?? new AquaSynthDaemonService(new AquaSynthDaemonOptions(options.StoreRoot)));
        daemon.host = await CultNetLocal.StartHostAsync(
            options.ResolvedCachePath,
            new CultNetHostOptions
            {
                DatabaseOptions = new CultNetDatabaseOptions
                {
                    RuntimeId = options.RuntimeId,
                    DocumentRegistry = AquaSynthCultNetDocuments.CreateRegistry()
                }
            }).ConfigureAwait(false);
        daemon.Subscribe();
        await daemon.PublishProviderStateAsync().ConfigureAwait(false);
        return daemon;
    }

    public Task<AquaSynthPatchCompileReceipt> SubmitCompileAsync(
        AquaSynthPatchCompileCommand command,
        TimeSpan? timeout = null)
    {
        var key = CompileReceiptKey(command.CommandId);
        return SubmitAndWaitAsync<AquaSynthPatchCompileCommand, AquaSynthPatchCompileReceipt>(
            command,
            CommandKey(command.CommandId),
            key,
            timeout);
    }

    public Task<AquaSynthRenderSampleReceipt> SubmitSampleAsync(
        AquaSynthInstrumentSampleCommand command,
        TimeSpan? timeout = null)
    {
        var key = RenderReceiptKey(command.CommandId);
        return SubmitAndWaitAsync<AquaSynthInstrumentSampleCommand, AquaSynthRenderSampleReceipt>(
            command,
            CommandKey(command.CommandId),
            key,
            timeout);
    }

    public Task<AquaSynthAutomationStreamReceipt> SubmitStreamAsync(
        AquaSynthAutomationStreamCommand command,
        TimeSpan? timeout = null)
    {
        var key = StreamReceiptKey(command.CommandId);
        return SubmitAndWaitAsync<AquaSynthAutomationStreamCommand, AquaSynthAutomationStreamReceipt>(
            command,
            CommandKey(command.CommandId),
            key,
            timeout);
    }

    public Task<AquaSynthInstrumentOpenReceipt> SubmitOpenAsync(
        AquaSynthInstrumentOpenCommand command,
        TimeSpan? timeout = null)
    {
        var key = OpenReceiptKey(command.CommandId);
        return SubmitAndWaitAsync<AquaSynthInstrumentOpenCommand, AquaSynthInstrumentOpenReceipt>(
            command,
            CommandKey(command.CommandId),
            key,
            timeout);
    }

    public Task<AquaSynthInstrumentControlReceipt> SubmitControlAsync(
        AquaSynthInstrumentControlCommand command,
        TimeSpan? timeout = null)
    {
        var key = ControlReceiptKey(command.CommandId);
        return SubmitAndWaitAsync<AquaSynthInstrumentControlCommand, AquaSynthInstrumentControlReceipt>(
            command,
            CommandKey(command.CommandId),
            key,
            timeout);
    }

    public Task<AquaSynthInstrumentBlockReceipt> SubmitBlockAsync(
        AquaSynthInstrumentBlockCommand command,
        TimeSpan? timeout = null)
    {
        var key = BlockReceiptKey(command.CommandId);
        return SubmitAndWaitAsync<AquaSynthInstrumentBlockCommand, AquaSynthInstrumentBlockReceipt>(
            command,
            CommandKey(command.CommandId),
            key,
            timeout);
    }

    public Task<AquaSynthInstrumentCloseReceipt> SubmitCloseAsync(
        AquaSynthInstrumentCloseCommand command,
        TimeSpan? timeout = null)
    {
        var key = CloseReceiptKey(command.CommandId);
        return SubmitAndWaitAsync<AquaSynthInstrumentCloseCommand, AquaSynthInstrumentCloseReceipt>(
            command,
            CommandKey(command.CommandId),
            key,
            timeout);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }

        subscriptions.Clear();
        commandGate.Dispose();
        host?.Dispose();
        service.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task<TReceipt> SubmitAndWaitAsync<TCommand, TReceipt>(
        TCommand command,
        CultRecordKey commandKey,
        CultRecordKey receiptKey,
        TimeSpan? timeout)
        where TCommand : class
        where TReceipt : class
    {
        var database = Database;
        if (await database.GetAsync<TReceipt>(receiptKey).ConfigureAwait(false) is { } existing)
        {
            return existing;
        }

        var completion = new TaskCompletionSource<TReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = database
            .WatchRecord<TReceipt>(receiptKey)
            .Subscribe(change =>
            {
                if (change.Document is { } document)
                {
                    completion.TrySetResult(document);
                }
            });

        await database.PutAsync(commandKey, command).ConfigureAwait(false);

        var wait = timeout ?? TimeSpan.FromSeconds(30);
        var completed = await Task.WhenAny(completion.Task, Task.Delay(wait)).ConfigureAwait(false);
        if (!ReferenceEquals(completed, completion.Task))
        {
            throw new TimeoutException($"AquaSynth CultNet command '{commandKey.Value}' did not publish receipt '{receiptKey.Value}' within {wait}.");
        }

        return await completion.Task.ConfigureAwait(false);
    }

    private void Subscribe()
    {
        var database = Database;
        subscriptions.Add(database.Watch<AquaSynthPatchCompileCommand>().Subscribe(change =>
        {
            if (change.Document is { } command && ShouldHandle(nameof(AquaSynthPatchCompileCommand), command.CommandId))
            {
                _ = Task.Run(() => HandleCompileAsync(command));
            }
        }));
        subscriptions.Add(database.Watch<AquaSynthInstrumentSampleCommand>().Subscribe(change =>
        {
            if (change.Document is { } command && ShouldHandle(nameof(AquaSynthInstrumentSampleCommand), command.CommandId))
            {
                _ = Task.Run(() => HandleSampleAsync(command));
            }
        }));
        subscriptions.Add(database.Watch<AquaSynthAutomationStreamCommand>().Subscribe(change =>
        {
            if (change.Document is { } command && ShouldHandle(nameof(AquaSynthAutomationStreamCommand), command.CommandId))
            {
                _ = Task.Run(() => HandleStreamAsync(command));
            }
        }));
        subscriptions.Add(database.Watch<AquaSynthInstrumentOpenCommand>().Subscribe(change =>
        {
            if (change.Document is { } command && ShouldHandle(nameof(AquaSynthInstrumentOpenCommand), command.CommandId))
            {
                _ = Task.Run(() => HandleOpenAsync(command));
            }
        }));
        subscriptions.Add(database.Watch<AquaSynthInstrumentControlCommand>().Subscribe(change =>
        {
            if (change.Document is { } command && ShouldHandle(nameof(AquaSynthInstrumentControlCommand), command.CommandId))
            {
                _ = Task.Run(() => HandleControlAsync(command));
            }
        }));
        subscriptions.Add(database.Watch<AquaSynthInstrumentBlockCommand>().Subscribe(change =>
        {
            if (change.Document is { } command && ShouldHandle(nameof(AquaSynthInstrumentBlockCommand), command.CommandId))
            {
                _ = Task.Run(() => HandleBlockAsync(command));
            }
        }));
        subscriptions.Add(database.Watch<AquaSynthInstrumentCloseCommand>().Subscribe(change =>
        {
            if (change.Document is { } command && ShouldHandle(nameof(AquaSynthInstrumentCloseCommand), command.CommandId))
            {
                _ = Task.Run(() => HandleCloseAsync(command));
            }
        }));
    }

    private bool ShouldHandle(string commandType, string commandId)
    {
        lock (handledCommands)
        {
            return handledCommands.Add($"{commandType}:{commandId}");
        }
    }

    private async Task HandleCompileAsync(AquaSynthPatchCompileCommand command)
    {
        await commandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var receipt = await service.CompileAsync(command).ConfigureAwait(false);
            await Database.PutAsync(CompileReceiptKey(command.CommandId), receipt).ConfigureAwait(false);
            await PublishProviderStateAsync().ConfigureAwait(false);
        }
        finally
        {
            commandGate.Release();
        }
    }

    private async Task HandleSampleAsync(AquaSynthInstrumentSampleCommand command)
    {
        await commandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var result = await service.SampleAsync(command).ConfigureAwait(false);
            await Database.PutAsync(CompileReceiptKey(command.CommandId), result.CompileReceipt).ConfigureAwait(false);
            await Database.PutAsync(RenderReceiptKey(command.CommandId), result.RenderReceipt).ConfigureAwait(false);
            await PublishProviderStateAsync().ConfigureAwait(false);
        }
        finally
        {
            commandGate.Release();
        }
    }

    private async Task HandleStreamAsync(AquaSynthAutomationStreamCommand command)
    {
        await commandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var receipt = await service.StreamAutomationAsync(command).ConfigureAwait(false);
            await Database.PutAsync(StreamReceiptKey(command.CommandId), receipt).ConfigureAwait(false);
            await PublishProviderStateAsync().ConfigureAwait(false);
        }
        finally
        {
            commandGate.Release();
        }
    }

    private async Task HandleOpenAsync(AquaSynthInstrumentOpenCommand command)
    {
        await commandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var receipt = await service.OpenInstrumentAsync(command).ConfigureAwait(false);
            await Database.PutAsync(OpenReceiptKey(command.CommandId), receipt).ConfigureAwait(false);
            await PublishProviderStateAsync().ConfigureAwait(false);
        }
        finally
        {
            commandGate.Release();
        }
    }

    private async Task HandleControlAsync(AquaSynthInstrumentControlCommand command)
    {
        await commandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var receipt = await service.ControlInstrumentAsync(command).ConfigureAwait(false);
            await Database.PutAsync(ControlReceiptKey(command.CommandId), receipt).ConfigureAwait(false);
            await PublishProviderStateAsync().ConfigureAwait(false);
        }
        finally
        {
            commandGate.Release();
        }
    }

    private async Task HandleBlockAsync(AquaSynthInstrumentBlockCommand command)
    {
        await commandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var receipt = await service.ProcessInstrumentBlockAsync(command).ConfigureAwait(false);
            await Database.PutAsync(BlockReceiptKey(command.CommandId), receipt).ConfigureAwait(false);
            await PublishProviderStateAsync().ConfigureAwait(false);
        }
        finally
        {
            commandGate.Release();
        }
    }

    private async Task HandleCloseAsync(AquaSynthInstrumentCloseCommand command)
    {
        await commandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var receipt = await service.CloseInstrumentAsync(command).ConfigureAwait(false);
            await Database.PutAsync(CloseReceiptKey(command.CommandId), receipt).ConfigureAwait(false);
            await PublishProviderStateAsync().ConfigureAwait(false);
        }
        finally
        {
            commandGate.Release();
        }
    }

    private Task PublishProviderStateAsync()
    {
        var state = new AquaSynthCultNetProviderState(
            "aquasynth.service",
            options.RuntimeId,
            DateTimeOffset.UtcNow.ToString("O"),
            options.StoreRoot,
            options.ResolvedCachePath,
            [
                AquaSynthDaemonSchemas.PatchCompileCommand,
                AquaSynthDaemonSchemas.InstrumentSampleCommand,
                AquaSynthDaemonSchemas.AutomationStreamCommand,
                AquaSynthDaemonSchemas.InstrumentOpenCommand,
                AquaSynthDaemonSchemas.InstrumentControlCommand,
                AquaSynthDaemonSchemas.InstrumentBlockCommand,
                AquaSynthDaemonSchemas.InstrumentCloseCommand
            ],
            [
                AquaSynthDaemonSchemas.PatchCompileReceipt,
                AquaSynthDaemonSchemas.RenderSampleReceipt,
                AquaSynthDaemonSchemas.AutomationStreamReceipt,
                AquaSynthDaemonSchemas.InstrumentOpenReceipt,
                AquaSynthDaemonSchemas.InstrumentControlReceipt,
                AquaSynthDaemonSchemas.InstrumentBlockReceipt,
                AquaSynthDaemonSchemas.InstrumentCloseReceipt
            ],
            [
                "global:aquasynth.cultnet_provider",
                "global:aquasynth.operator_state"
            ]);
        return Database.PutAsync(new CultRecordKey("global:aquasynth.cultnet_provider"), state);
    }

    public static CultRecordKey CommandKey(string commandId) => new($"commands/{commandId}");

    public static CultRecordKey CompileReceiptKey(string commandId) => new($"{commandId}-compile");

    public static CultRecordKey RenderReceiptKey(string commandId) => new($"{commandId}-render");

    public static CultRecordKey StreamReceiptKey(string commandId) => new($"{commandId}-stream");

    public static CultRecordKey OpenReceiptKey(string commandId) => new($"{commandId}-open");

    public static CultRecordKey ControlReceiptKey(string commandId) => new($"{commandId}-control");

    public static CultRecordKey BlockReceiptKey(string commandId) => new($"{commandId}-block");

    public static CultRecordKey CloseReceiptKey(string commandId) => new($"{commandId}-close");
}

public static class AquaSynthCultNetDocuments
{
    public static CultNetDocumentRegistry CreateRegistry() =>
        new CultNetDocumentRegistry(CultDocumentRegistry.Shared)
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthPatchCompileCommand>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthInstrumentSampleCommand>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthAutomationStreamCommand>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthInstrumentOpenCommand>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthInstrumentControlCommand>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthInstrumentBlockCommand>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthInstrumentCloseCommand>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthCompiledInstrumentSession>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthPatchCompileReceipt>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthRenderSampleReceipt>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthAutomationStreamReceipt>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthInstrumentOpenReceipt>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthInstrumentControlReceipt>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthInstrumentBlockReceipt>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthInstrumentCloseReceipt>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthOperatorState>())
            .Register(CultNetDocumentBinding.ForDocument<AquaSynthCultNetProviderState>());
}
