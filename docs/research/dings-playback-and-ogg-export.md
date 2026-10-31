# Dings playback path and a `dings/<localpart>/<motif>.ogg` export sketch

Serves follow-up `aqua-body:follow_up:chat-s327-335` (Void's hook wants per-agent
audio files). Claims below are from source at `8a65f58`; nothing was run or probed.
Paths are repo-relative; `Host` = `tools/AquaSynthDingsHost`.

## 1. Per-note synthesis in `DingsPlaybackHost`

**Transport.** One named-pipe server per client, `PipeName =
GameCult.AquaSynthDings.Playback.v1` (`src/AquaSynth.Dings/DingsProtocol.cs:8`),
`CurrentUserOnly` (`Host/DingsPlaybackHost.cs:16`). Frame = 4-byte int length
(1 B..1 MiB, `:86-87`) + MessagePack body (`:82-91`, `:93-99`). Request
`DingsCommand(Version, Kind, EventId, InstrumentId, Value)` and response
`DingsResponse` are keyed MessagePack records (`DingsProtocol.cs:13-31`). One
request, one response, pipe closed (`DingsPlaybackHost.cs:28-30`). A named mutex makes
the host a singleton (`Host/Program.cs:3-4`).

**Play path** (`DingsPlaybackHost.cs:64-77`):
1. Look up `EventId` in `DingCatalog.Events` and `InstrumentId` in
   `DingCatalog.Instruments` (`:66-67`); both are case-insensitive dictionaries
   (`src/AquaSynth.Dings/DingCatalog.cs:20,28`). `Value` is gain, must be finite in [0,1] (`:68`).
2. For each `DingNote(Semitones, DelayMilliseconds, Gain)` of the event (`DingCatalog.cs:3`),
   in order and serialized:
   - frequency = `instrument.RootFrequency * 2^(semitones/12)` (`:72`); root is 261.6256 Hz
     for every instrument (`DingCatalog.cs:50`);
   - `daemon.RenderNoteAsync(instrument, frequency, Value * note.Gain)` (`:73`);
   - hand the returned WAV path and the note's delay to the audio player (`:74`).
3. Respond `playing` with a fresh `ding-<guid>` id (`:69,76`). The response returns after
   every note is *rendered and queued*, not after it has sounded.

**Render call** (`Host/AquaDaemonClient.cs:14-32`). A child `dotnet AquaSynthDaemon.dll
daemon --store <repo>/.aquasynth/dings-host` is spawned lazily (`:36-44`) and spoken to
over JSON lines on stdin/stdout. A `SemaphoreSlim(1,1)` serializes all renders (`:11,16`).
Each note sends `instrument.sample` with: `patchId=dings.<instrument>`, the instrument's
Faust-DSL `script`, `durationSeconds = 1.5f` (fixed, `:23`, independent of the
instrument's `DecaySeconds`), `gain`, `controls {"/ding/frequency": f}`, `revision = 1`.
Failure surfaces through `renderReceipt.status != "succeeded"` (`:27-28`). The WAV path is
taken from `renderReceipt.wavUri` (`:29`).

**What the daemon does with it** (`src/AquaSynth.Faust/AquaSynthDaemonService.cs:619-686`):
compiles the script (`:624-631`), renders `patch.Render(controls, gain)` (`:656`), writes
`<store>/samples/<renderId>.f32` and `.wav` (`:662-667`) and a receipt under `renders`
(`:683`). WAV is mono 16-bit PCM at the patch sample rate, floats clamped to int16
(`:1089-1109`; the 44100 default sits in the options record, `:34`). Note: each
note is a fresh compile+render; nothing is cached across notes or events, and the
instrument script is a single `voice` line parameterised by `/ding/frequency`
(`DingCatalog.cs:49-50`).

**Instruments** (`DingCatalog.cs:22-28`): `warm-bell`, `glass-chime`, `soft-triangle`,
`muted-pluck`. **Events** (`:9-20`): nine, 1-3 notes each, onset gaps of 180-320 ms
enforced by `ValidateCuration` (`:41-45`).

**Mixing and output: `DingAudioPlayer`** (`Host/DingAudioPlayer.cs`). A 44.1 kHz stereo
float `MixingSampleProvider` (`:19`) feeds a shared-mode WASAPI output on the default
render endpoint (`:25-27,29`). `Play(wavPath, delayMs)` reads the WAV (`:43`), upmixes to
stereo, wraps it in an `OffsetSampleProvider` with `DelayBy = delay` (`:44`) and adds it
to the mixer (`:45`), then removes it after `TotalTime + delay + 150 ms` (`:46-49`).
Overlap is plain summation; there is no limiter or normalization. Volume and mute are
the WASAPI session's, not the signal's (`:38-39,59-71`). This class is Windows audio
hardware plumbing (NAudio WASAPI, `MMDevice`) and has nothing a file export can reuse
except the *idea* of delay-offset summation.

**Who picks the instrument.** Not the host: it takes `InstrumentId` from the caller. The
MCP side assigns one per agent *session id*, by SHA-256 of the id modulo the sorted
instrument list, or by explicit claim, in an in-process dictionary
(`tools/AquaSynthDingsMcp/AgentInstrumentRegistry.cs:10,13-36`). It is not persisted and
is keyed by session id, not by agent identity.

## 2. Sketch: `dings/<localpart>/<motif>.ogg` for Void's hook

Status: design sketch, not built. "Motif" and "localpart" are not terms in this repo; I
read them as *motif = a `DingEvent`* and *localpart = the agent's address localpart*.
Confirm both with whoever owns Void's hook.

**Which piece renders.** Not `DingsPlaybackHost` (needs a live WASAPI endpoint and a pipe
client) and not `DingAudioPlayer`. The reusable pieces are the catalog
(`DingCatalog`, pure data) and the daemon's `instrument.sample` (headless, already
writes WAVs). A small offline exporter, a new `tools/` project beside the host, would:
1. For `(instrument, event)`, issue the same per-note `instrument.sample` calls as
   `DingsPlaybackHost.cs:70-75` (same frequency formula and gain), via the same
   `AquaDaemonClient` logic. Share it by moving `AquaDaemonClient` into a library rather
   than copying it; it already has no host dependency except `FindRepositoryRoot` (`:35-40`).
2. Mix offline: sample-accurate sum of each note's mono WAV at `delay * sampleRate`,
   length `max(delay) + 1.5 s` (the fixed render length, `AquaDaemonClient.cs:23`), peak-
   normalize or limit to a fixed headroom (the live path has none, so the export decides).
3. Encode to Ogg (Vorbis or Opus). No encoder exists in this repo (the only `.ogg`
   mentions are bird-recording citations in `research/bird-syrinx-golf/README.md`). Options:
   shell out to `ffmpeg`/`oggenc` (availability on the render host unchecked), or a
   managed encoder package. Opus mandates 48 kHz, so renders at 44.1 kHz would be
   resampled; Vorbis at 44.1 kHz avoids that. Recommendation: Vorbis via `oggenc`/ffmpeg,
   confidence moderate, since I have not checked what Void's consumer decodes.

**Manifest: agent to instrument.** The live registry cannot serve (hash of a session id,
in memory, `AgentInstrumentRegistry.cs:10,31-36`), because a file path keyed by localpart
needs a stable per-agent choice. Proposed typed document, CultCache-held per doctrine
(not load-bearing JSON), one per agent:

| field | meaning |
|---|---|
| `localpart` | agent address localpart; the key |
| `instrument_id` | member of `DingCatalog.Instruments` |
| `gain` | export gain, 0..1 (default 0.6, matching the 0.5-0.7 policy) |
| `motifs` | list of `DingEvent` ids to export; default all nine |
| `catalog_revision` | hash of catalog entries + instrument script, so a changed instrument re-renders |

Assignment rule when no entry exists: reuse `AssignDeterministically` with `localpart` as
the key, so the registry's existing behavior becomes stable per agent; collisions (four
instruments, many agents) are accepted by the current design and so are not new here.
Distinctness beyond four instruments needs a catalog change (more instruments, or a
per-agent transpose of `RootFrequency`); that is a product fork, not decided here.

**Landing path.** `dings/<localpart>/<motif>.ogg`, `motif` = event id (`task.complete.ogg`
and so on; dots are legal in filenames). Root of `dings/`: undecided, I do not know where
Void's hook reads. Candidates: a directory in Void's repo, or a published artifact tree.
The exporter should take the root as an argument and write only inside it, idempotently
(skip when `catalog_revision` matches a sidecar stamp), so the landing decision does not
touch the renderer. Receipts: the daemon already keeps render receipts with a SHA-256 of
the f32 output (`AquaSynthDaemonService.cs:683-686`); the export should record the Ogg's
hash beside the manifest entry.

**Open questions for the hook owner:** (a) the meaning of "motif"; (b) the landing root and
who commits it; (c) decoder constraints (Vorbis vs Opus, mono vs stereo, max duration);
(d) whether agents outside the four-instrument pool need distinct voices.
