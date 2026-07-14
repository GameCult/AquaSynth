# AquaSynth Dings MCP

Stdio MCP service for synthesized agent notifications. The event motif says
what happened; the curated instrument timbre says which agent spoke. The
service starts/calls `AquaSynthDaemon`, then owns speaker playback through one
persistent NAudio mixer.

```powershell
dotnet run --project tools/AquaSynthDingsMcp
```

For a direct synthesis/playback smoke outside an MCP host:

```powershell
dotnet run --project tools/AquaSynthDingsMcp -- --play task.complete warm-bell
```

The project builds and launches `AquaSynthDaemon.dll` as its synthesis process.
Set `AQUASYNTH_DAEMON_DLL` only to select a different built daemon. MCP tools are `play_ding`,
`list_ding_events`, `list_instruments`, and `stop_dings`.

Instrument availability is catalog-only. Additions must satisfy mechanical
curation checks and then survive listening/identification tests; see
`docs/research/auditory-notifications/README.md`.
