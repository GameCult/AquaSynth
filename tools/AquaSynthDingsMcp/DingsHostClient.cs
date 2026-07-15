using System.Diagnostics;
using System.IO.Pipes;
using AquaSynth.Dings;
using MessagePack;

namespace AquaSynthDingsMcp;

public interface IDingsHostClient
{
    Task<DingsResponse> SendAsync(DingsCommand command, CancellationToken cancellationToken);
}

public sealed class DingsHostClient : IDingsHostClient
{
    public async Task<DingsResponse> SendAsync(DingsCommand command, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { return await SendOnceAsync(command, 350, timeout.Token); }
        catch (TimeoutException) { StartHost(); }
        catch (IOException) { StartHost(); }
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try { return await SendOnceAsync(command, 500, timeout.Token); }
            catch (TimeoutException) when (attempt < 19) { await Task.Delay(100, timeout.Token); }
            catch (IOException) when (attempt < 19) { await Task.Delay(100, timeout.Token); }
        }
        throw new InvalidOperationException("AquaSynth Dings audio host did not become available.");
    }

    private static async Task<DingsResponse> SendOnceAsync(DingsCommand command, int connectTimeoutMs, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", DingsProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(connectTimeoutMs, cancellationToken);
        var payload = MessagePackSerializer.Serialize(command);
        await pipe.WriteAsync(BitConverter.GetBytes(payload.Length), cancellationToken);
        await pipe.WriteAsync(payload, cancellationToken);
        await pipe.FlushAsync(cancellationToken);
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header, cancellationToken);
        var length = BitConverter.ToInt32(header);
        if (length is <= 0 or > 1024 * 1024) throw new InvalidDataException("Invalid Dings response frame.");
        var responsePayload = new byte[length];
        await pipe.ReadExactlyAsync(responsePayload, cancellationToken);
        var response = MessagePackSerializer.Deserialize<DingsResponse>(responsePayload);
        if (response.Version != DingsProtocol.Version) throw new InvalidDataException("Incompatible Dings host protocol.");
        return response;
    }

    private static void StartHost()
    {
        var root = FindRepositoryRoot();
        var host = Directory.EnumerateFiles(Path.Combine(root, "tools", "AquaSynthDingsHost", "bin"), "AquaSynthDingsHost.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            ?? throw new InvalidOperationException("AquaSynthDingsHost.exe is not built.");
        Process.Start(new ProcessStartInfo(host) { WorkingDirectory = root, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AquaSynth.sln"))) return dir.FullName;
        throw new InvalidOperationException("Cannot locate AquaSynth repository root.");
    }
}
