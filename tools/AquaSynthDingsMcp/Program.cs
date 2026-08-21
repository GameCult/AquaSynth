using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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

if (args is ["--http"] or ["--http", _])
{
    var listenUrl = args.Length == 2 ? args[1] : "http://127.0.0.1:17878";
    using var singleton = new Mutex(initiallyOwned: true, "Local\\GameCult.AquaSynthDings.McpHttp.v1", out var ownsMutex);
    if (!ownsMutex) return;

    var webBuilder = WebApplication.CreateBuilder([]);
    webBuilder.Logging.ClearProviders();
    webBuilder.WebHost.UseUrls(listenUrl);
    webBuilder.Services.AddSingleton<IDingsHostClient, DingsHostClient>();
    webBuilder.Services.AddSingleton<DingService>();
    webBuilder.Services.AddSingleton<AgentInstrumentRegistry>();
    webBuilder.Services.AddMcpServer()
        .WithHttpTransport(options => options.Stateless = false)
        .WithTools<DingTools>();

    var app = webBuilder.Build();
    app.MapMcp("/mcp");
    await app.RunAsync();
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddSingleton<IDingsHostClient, DingsHostClient>();
builder.Services.AddSingleton<DingService>();
builder.Services.AddSingleton<AgentInstrumentRegistry>();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DingTools>();

await builder.Build().RunAsync();
