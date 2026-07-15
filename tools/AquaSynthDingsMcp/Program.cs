using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AquaSynthDingsMcp;
using AquaSynth.Dings;

if (args is ["--play", var eventId, var instrumentId])
{
    var service = new DingService(new DingsHostClient());
    await service.PlayAsync(eventId, instrumentId, .7f, CancellationToken.None);
    return;
}
if (args is ["--get-volume"])
{
    var state = await new DingService(new DingsHostClient()).GetVolumeAsync(CancellationToken.None);
    Console.WriteLine($"volume={state.Volume:0.###} muted={state.Muted} hostPid={state.HostProcessId} session=\"{state.SessionName}\"");
    return;
}
if (args is ["--set-volume", var volumeText] && float.TryParse(volumeText, System.Globalization.CultureInfo.InvariantCulture, out var volume))
{
    var state = await new DingService(new DingsHostClient()).SetVolumeAsync(volume, CancellationToken.None);
    Console.WriteLine($"volume={state.Volume:0.###} muted={state.Muted} hostPid={state.HostProcessId}");
    return;
}
if (args is ["--mute", var muteText] && bool.TryParse(muteText, out var muted))
{
    var state = await new DingService(new DingsHostClient()).SetMutedAsync(muted, CancellationToken.None);
    Console.WriteLine($"volume={state.Volume:0.###} muted={state.Muted} hostPid={state.HostProcessId}");
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddSingleton<IDingsHostClient, DingsHostClient>();
builder.Services.AddSingleton<DingService>();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DingTools>();

await builder.Build().RunAsync();
