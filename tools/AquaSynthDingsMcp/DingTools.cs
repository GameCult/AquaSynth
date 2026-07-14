using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AquaSynthDingsMcp;

[McpServerToolType]
public sealed class DingTools(DingService service)
{
    [McpServerTool, Description("Synthesizes and audibly plays a semantic notification motif. Event says what happened; instrument identifies the agent by timbre.")]
    public Task<object> PlayDing(
        [Description("Semantic event such as task.complete or input.required.")] string eventId,
        [Description("Registered timbre such as warm-bell or glass-chime.")] string instrumentId,
        [Description("Playback gain from 0 to 1.")] float gain = .7f,
        CancellationToken cancellationToken = default) => service.PlayAsync(eventId, instrumentId, gain, cancellationToken);

    [McpServerTool, Description("Lists the semantic notification events and their meanings.")]
    public object ListDingEvents() => DingCatalog.Events.Values.Select(x => new { x.Id, x.Meaning, noteCount = x.Notes.Count }).ToArray();

    [McpServerTool, Description("Lists registered agent timbres available to play_ding.")]
    public object ListInstruments() => DingCatalog.Instruments.Values.Select(x => new { x.Id, x.Description, x.TimbreFamily, x.Brightness, x.DecaySeconds }).ToArray();

    [McpServerTool, Description("Stops all notification sounds currently being mixed by this MCP service.")]
    public object StopDings() { service.StopAll(); return new { status = "stopped" }; }
}
