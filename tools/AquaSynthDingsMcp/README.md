# AquaSynth Dings MCP

Stdio MCP bridge for synthesized agent notifications. Event motif says what
happened; curated instrument timbre says which agent spoke.

All MCP instances are command bridges. One persistent
`AquaSynthDingsHost.exe` owns the WASAPI session named **AquaSynth Dings**, the
Aqua synthesis child, mixer, master volume, and mute state. Windows Volume
Mixer and MCP volume tools address the same session.

Do not register the STDIO bridge globally in an agent harness that creates a
separate MCP client for every root agent, subagent, or critic. STDIO ownership
means one resident .NET bridge per client even though audio authority is already
shared by `AquaSynthDingsHost.exe`. Run one supervised Streamable HTTP MCP server
instead and point every agent at its loopback endpoint:

```powershell
AquaSynthDingsMcp.exe --http http://127.0.0.1:17878
```

The HTTP server is machine-singleton and keeps only MCP session-to-instrument
assignments. Each agent session is assigned a stable timbre automatically and
may claim another with `claim_instrument`; `play_ding` then uses that assignment
without an instrument argument. `AquaSynthDingsHost.exe` remains the sole
audio-session and playback owner.

Install the per-user singleton on Windows:

```powershell
.\scripts\install-dings-mcp-task.ps1
```

```powershell
dotnet run --project tools/AquaSynthDingsMcp
```

Direct smokes and controls:

```powershell
dotnet run --project tools/AquaSynthDingsMcp -- --play task.complete warm-bell
dotnet run --project tools/AquaSynthDingsMcp -- --get-volume
dotnet run --project tools/AquaSynthDingsMcp -- --set-volume 0.5
dotnet run --project tools/AquaSynthDingsMcp -- --mute true
```

MCP tools are `play_ding`, `get_agent_instrument`, `claim_instrument`,
`list_ding_events`, `list_instruments`, `stop_dings`, `get_volume`,
`set_volume`, and `mute_dings`.

Instrument availability is catalog-only. Additions must satisfy mechanical
curation checks and then survive listening/identification tests; see
`docs/research/auditory-notifications/README.md`.
