# AquaSynth Dings MCP

Stdio MCP bridge for synthesized agent notifications. Event motif says what
happened; curated instrument timbre says which agent spoke.

All MCP instances are command bridges. One persistent
`AquaSynthDingsHost.exe` owns the WASAPI session named **AquaSynth Dings**, the
Aqua synthesis child, mixer, master volume, and mute state. Windows Volume
Mixer and MCP volume tools address the same session.

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

MCP tools are `play_ding`, `list_ding_events`, `list_instruments`,
`stop_dings`, `get_volume`, `set_volume`, and `mute_dings`.

Instrument availability is catalog-only. Additions must satisfy mechanical
curation checks and then survive listening/identification tests; see
`docs/research/auditory-notifications/README.md`.
