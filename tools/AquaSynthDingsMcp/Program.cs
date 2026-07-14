using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AquaSynthDingsMcp;

if (args is ["--play", var eventId, var instrumentId])
{
    await using var daemon = new AquaDaemonClient();
    using var audio = new DingAudioPlayer();
    var service = new DingService(daemon, audio);
    await service.PlayAsync(eventId, instrumentId, .7f, CancellationToken.None);
    await Task.Delay(TimeSpan.FromSeconds(2.5));
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddSingleton<AquaDaemonClient>();
builder.Services.AddSingleton<DingAudioPlayer>();
builder.Services.AddSingleton<DingService>();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DingTools>();

await builder.Build().RunAsync();
