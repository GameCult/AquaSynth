using System.ComponentModel;
using ModelContextProtocol.Server;
using AquaSynth.Dings;

namespace AquaSynthDingsMcp;

[McpServerToolType]
public sealed class DingTools(DingService service, AgentInstrumentRegistry instruments)
{
    [McpServerTool, Description("Synthesizes and audibly plays a semantic notification motif using the stable instrument owned by this agent session.")]
    public Task<object> PlayDing(
        McpServer server,
        [Description("Semantic event such as task.complete or input.required.")] string eventId,
        [Description("Playback gain from 0 to 1.")] float gain = .7f,
        CancellationToken cancellationToken = default) =>
        service.PlayAsync(eventId, instruments.GetOrAssign(AgentSessionId(server)), gain, cancellationToken);

    [McpServerTool, Description("Returns the stable instrument assigned to this agent session, assigning one automatically if needed.")]
    public object GetAgentInstrument(McpServer server) => Describe(instruments.GetOrAssign(AgentSessionId(server)));

    [McpServerTool, Description("Lets this agent session claim a preferred registered instrument. Future play_ding calls use it automatically.")]
    public object ClaimInstrument(
        McpServer server,
        [Description("Registered timbre such as warm-bell or glass-chime.")] string preferredInstrumentId) =>
        Describe(instruments.Claim(AgentSessionId(server), preferredInstrumentId));

    [McpServerTool, Description("Lists the semantic notification events and their meanings.")]
    public object ListDingEvents() => DingCatalog.Events.Values.Select(x => new { x.Id, x.Meaning, noteCount = x.Notes.Count }).ToArray();

    [McpServerTool, Description("Lists registered agent timbres available to play_ding.")]
    public object ListInstruments() => DingCatalog.Instruments.Values.Select(x => new { x.Id, x.Description, x.TimbreFamily, x.Brightness, x.DecaySeconds }).ToArray();

    [McpServerTool, Description("Stops all notification sounds currently being mixed by this MCP service.")]
    public Task<DingsResponse> StopDings(CancellationToken cancellationToken = default) => service.StopAllAsync(cancellationToken);

    [McpServerTool, Description("Gets the authoritative AquaSynth Dings master volume and mute state shared with Windows Volume Mixer.")]
    public Task<DingsResponse> GetVolume(CancellationToken cancellationToken = default) => service.GetVolumeAsync(cancellationToken);

    [McpServerTool, Description("Sets the global AquaSynth Dings master volume shared by every Codex task and Windows Volume Mixer.")]
    public Task<DingsResponse> SetVolume([Description("Master volume from 0 to 1.")] float volume, CancellationToken cancellationToken = default) => service.SetVolumeAsync(volume, cancellationToken);

    [McpServerTool, Description("Mutes or unmutes the global AquaSynth Dings Windows audio session.")]
    public Task<DingsResponse> MuteDings(bool muted = true, CancellationToken cancellationToken = default) => service.SetMutedAsync(muted, cancellationToken);

    private static string AgentSessionId(McpServer server) => server.SessionId ?? $"stdio:{Environment.ProcessId}";

    private static object Describe(string instrumentId)
    {
        var instrument = DingCatalog.Instruments[instrumentId];
        return new { instrument.Id, instrument.Description, instrument.TimbreFamily };
    }
}
