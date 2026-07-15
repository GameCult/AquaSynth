using System.Diagnostics;
using System.Text.Json;
using AquaSynth.Dings;

namespace AquaSynthDingsHost;

public sealed class AquaDaemonClient : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private Process? process;

    public async Task<string> RenderNoteAsync(DingInstrument instrument, float frequency, float gain, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureStarted();
            var id = $"ding-{Guid.NewGuid():N}";
            var envelope = new { command = "instrument.sample", payload = new { commandId = id, patchId = $"dings.{instrument.Id}", faustName = $"dings_{instrument.Id.Replace('-', '_')}", script = instrument.Script, durationSeconds = 1.5f, gain, controls = new Dictionary<string, float> { ["/ding/frequency"] = frequency }, revision = 1 } };
            await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(envelope, json));
            await process.StandardInput.FlushAsync(cancellationToken);
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken) ?? throw new InvalidOperationException("Aqua daemon closed before returning a render receipt.");
            using var receipt = JsonDocument.Parse(line);
            var render = receipt.RootElement.GetProperty("renderReceipt");
            if (!string.Equals(render.GetProperty("status").GetString(), "succeeded", StringComparison.Ordinal))
                throw new InvalidOperationException(render.TryGetProperty("failureMessage", out var failure) ? failure.GetString() : "Aqua render failed.");
            return new Uri(render.GetProperty("wavUri").GetString()!).LocalPath;
        }
        finally { gate.Release(); }
    }

    private void EnsureStarted()
    {
        if (process is { HasExited: false }) return;
        var root = FindRepositoryRoot();
        var daemonDll = Directory.EnumerateFiles(Path.Combine(root, "tools", "AquaSynthDaemon", "bin"), "AquaSynthDaemon.dll", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            ?? throw new InvalidOperationException("AquaSynthDaemon.dll is not built.");
        process = Process.Start(new ProcessStartInfo("dotnet", $"\"{daemonDll}\" daemon --store \"{Path.Combine(root, ".aquasynth", "dings-host")}\"") { WorkingDirectory = root, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })
            ?? throw new InvalidOperationException("Could not start AquaSynth daemon.");
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine($"[aqua] {e.Data}"); };
        process.BeginErrorReadLine();
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AquaSynth.sln"))) return dir.FullName;
        throw new InvalidOperationException("Cannot locate AquaSynth.sln.");
    }

    public ValueTask DisposeAsync()
    {
        if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        process?.Dispose();
        gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
