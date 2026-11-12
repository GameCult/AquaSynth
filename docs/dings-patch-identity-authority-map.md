# Dings patch identity: authority map

Serves follow-up `aqua-body:follow_up:aquasynth-dings-authority-map`. Claims are from
source at `c0fe4ee` (origin/main). Nothing was built or run; the one place behaviour is
inferred rather than read is marked. Paths are repo-relative; `Host` =
`tools/AquaSynthDingsHost`, `Mcp` = `tools/AquaSynthDingsMcp`.

Companion to `docs/research/dings-playback-and-ogg-export.md`, which describes the
playback path and an export sketch. This note asks a narrower question: in the Dings
path, what decides which sound "this patch" is, and how many places decide it.

## What I counted

The follow-up says four owners. The source supports four places that *decide* a
voice's identity, plus one place that names the same thing a second time without
deciding, and one generated mirror:

| # | Owner | Decides | Section |
|---|---|---|---|
| 1 | `DingCatalog` | the instrument id and the script that defines its sound | A |
| 2 | Host and daemon derivation | the daemon `patchId`, `faustName`, `revision`, session id and store file names | B |
| 3 | MCP `AgentInstrumentRegistry` | which instrument an agent session gets | C |
| 4 | `tools/dings-voices` | which voice a sender (agent localpart) gets, in a separate synth | D |
| - | Earcon Workshop `IDENTITIES` | reuses catalog instrument ids with its own parameters | E |
| - | `tools/aqua-feel/lowered-dings` | checked-in copy of #1, guarded by a test | E |

The follow-up does not list its four, so this grouping is my reading. Someone who
counted differently (for example splitting the daemon's session id from the store file
name, or counting Earcon Workshop as a fifth) would get a different number from the same
code. The grouping above follows who writes the key.

## A. `DingCatalog` instrument id and script

- Where: `src/AquaSynth.Dings/DingCatalog.cs:22-28` (four instruments), built by the
  private `Instrument(...)` at `:48-50`, which writes the `Script` string (an AquaSynth patch script, `param path=...; voice wave=...`, that `FaustEmitter.EmitScript` lowers to Faust; not Faust source) and
  fixes `RootFrequency = 261.6256f` for all four.
- Keys on: the instrument `Id` string (`warm-bell`, `glass-chime`, `soft-triangle`,
  `muted-pluck`), case-insensitive dictionary (`:28`). Events are a separate keyed
  dictionary (`:9-20`, nine ids) and are not voice identity.
- Lifetime: compiled in. Changing an entry means a new build of every consumer.
- Writer: whoever edits the file. `ValidateCuration` (`:31-46`) is the only gate.
- Readers: `Host/DingsPlaybackHost.cs:66-67`, `Mcp/DingService.cs:9-10`,
  `Mcp/AgentInstrumentRegistry.cs:11`, `Mcp/DingTools.cs:28,31,49`,
  `tools/AquaLower/Program.cs:17`, and the tests `DingCatalogTests`.
- Content hash: none. The id is a label; the script is the content. Nothing here says
  "this script, at this version".

## B. Host and daemon derivation of the patch

The Host does not pass the catalog id through. It builds the daemon's identity fields
itself, and the daemon builds more keys from them.

- `Host/AquaDaemonClient.cs:20` per note: `patchId = "dings." + instrument.Id`,
  `faustName = "dings_" + instrument.Id.Replace('-', '_')`, `revision = 1` (a literal),
  `durationSeconds = 1.5f`, `controls["/ding/frequency"]`.
- `src/AquaSynth.Faust/NativeFaustRuntime.cs:596-599`: the compile key is SHA-256 of
  `"{revision}\n{faustName}\n{script}"`. `patchId` is not in it. So two ids with the same
  name and script share a compile key; one id with an edited script gets a new key even
  though `revision` stays 1.
- `src/AquaSynth.Faust/AquaSynthDaemonService.cs:1136`: session id =
  `StoreFileName.From(patchId) + "-" + compileKey[..12]`. A recompile with the same
  session id disposes the old session (`:366-376`).
- `src/AquaSynth.Core/StoreFileName.cs:26-54`: the mapping from any id to a file name
  (ASCII letters, digits, `-`, `_`, `.` pass unchanged unless the id has an uppercase letter, a device stem, a trailing dot, is over 100 characters or already ends in a hash shape; anything else gets a 16-hex SHA-256 suffix, `StoreFileName.cs:37,59-67`).
  `dings.warm-bell` is a fixed point (pinned by `StoreFileNameTests`).
- Keyed by patch: the session document. `WriteReceiptAsync("sessions", sessionId, session)`
  (`:407`) runs on every compile, and `SampleAsync` compiles on every note
  (`:417-425`), so the store holds `sessions/<StoreFileName(sessionId)>.cc` for each
  distinct `(patchId, compile key)`, rewritten on each note. This is the one store
  artifact addressable by instrument. `NativeFaustRuntime.cs:516,628-636` would also write
  `<StoreFileName(patchId)>.dsp`, but only when `options.DspSourceDirectory` is set; the
  daemon builds its native options from sample rate and render limits only
  (`AquaSynthDaemonService.cs:330-334`) and nothing else in `src` or `tools` sets it, so
  for the Dings host it is off. (Read from source, not observed.)
- Not keyed by patch: render output and the render and compile receipts. Samples are
  `<store>/samples/<StoreFileName(commandId + "-render")>.wav` (`:664-667`), where
  `commandId` is a fresh `ding-<guid>` per note (`AquaDaemonClient.cs:19`). Receipts go
  through `WriteReceiptAsync` (`:922-937`) under command-derived ids, with the patch id
  only in the body. The store therefore keeps one WAV per note played, none addressable
  by instrument. Whether the store ever gets cleaned I did not find.
- Faust name, three forms: the Host sends `dings_<id with - as _>`; the daemon applies
  `SafeFaustName` (`NativeFaustRuntime.cs:602-607`, called at
  `AquaSynthDaemonService.cs:911` and `NativeFaustRuntime.cs:513`) so the manifest and
  emitted source carry the rewritten name, while the compile key (`:596-599`) hashes the
  raw `identity.FaustName`. Two raw names that sanitise alike compile alike but key
  differently.
- The `dings_<id>` rule is written three times: `AquaDaemonClient.cs:20` and
  `tools/AquaLower/Program.cs:19`, and
  `DingCatalogTests.cs:45` (`LoweredDingsFedToTheFeelMeterMatchTheCatalog` builds the
  name with the same expression). Nothing ties the three together except the test.
- Writer: the Host (fields) and the daemon (session id, file names). Readers: the
  daemon's compile/render path and receipts; no Dings code reads the patch id back.
- `Host/AquaDaemonClient.cs:23-28` returns the WAV path from the receipt. The Host
  never asks the daemon which patch rendered it.

## C. MCP `AgentInstrumentRegistry`

- Where: `Mcp/AgentInstrumentRegistry.cs:10` (dictionary), `:13-17` `GetOrAssign`,
  `:19-29` `Claim`, `:31-36` `AssignDeterministically`.
- Keys on: `agentSessionId`, which is `server.SessionId ?? "stdio:" + ProcessId`
  (`Mcp/DingTools.cs:45`; that `SessionId` is null under stdio is inferred from the `??` fallback, not read from the SDK). That is the MCP transport session, not the agent. In HTTP
  mode (`Mcp/Program.cs:34-54`, `Stateless = false`) it lives as long as that MCP
  session; in stdio mode it is the bridge process id.
- Value: an instrument id string. Unclaimed sessions get
  `SHA256(sessionId)[0..4] % sortedInstrumentIds.Length`. `Claim` overwrites silently,
  and there is no uniqueness check, so two sessions can hold the same instrument.
- Lifetime: in-process, a `ConcurrentDictionary`. Lost on bridge restart. A restarted
  or reconnected session gets a different session id, hence generally a different
  hashed instrument unless it claims again. (Inferred from the keying; I did not
  observe a reconnect.)
- Writer: the agent, through `claim_instrument`, or the hash. Reader: `play_ding`
  (`DingTools.cs:16`) and `get_agent_instrument` (`:19`).
- Also decides: `DingService.PlayAsync` (`Mcp/DingService.cs:9-11`) re-validates event,
  instrument and gain, and the Host validates the same three again
  (`DingsPlaybackHost.cs:66-68`). Two gates on one decision; they agree today because
  both read `DingCatalog`.

## D. `tools/dings-voices` sender voices

- Where: `tools/dings-voices/src/voices.mjs:8` (`SENDERS`, eight localparts), `:51-73`
  (`VOICES`, one `hit` function each), `:13-18` (`RHYTHM`, four motifs).
- Keys on: sender localpart x motif (`mention`, `dm`, `message`, `invite`), emitted as
  `senders/<sender>/<motif>.ogg` and `manifest.json`
  (`finish.py:62`, shape `{neutral, senders:{localpart:{motif:path}}}`).
- It does not use `DingCatalog`, `AquaSynthDaemon` or Faust. It is a JS synthesizer that
  imports only the sample rate from `earcon-workshop/web/earcon.js` (`voices.mjs:5`).
  Its vocabulary (4 motifs, 8 senders) shares no ids with Aqua's (9 events, 4
  instruments).
- One link to the catalog family: the neutral fallback is rendered through the Earcon
  Workshop `warm-bell` identity (`build.mjs:10-14`), with an event mapping of
  `mention=warning, dm=waiting, message=message, invite=done` (`:12`) that the file
  itself calls provisional.
- Lifetime: a build artifact. The files are not committed (README, "Not here"); the
  consumer, Ratatoskr's `dings.ts`, is outside this repo and I did not read it.
- Writer: a person editing `voices.mjs`; `NIBU_PROPOSALS` (`:81-92`) shows the selection
  of the default voice is a one-line assignment (`VOICES.nibu = ...`, `:92`).
- This is the only place a *sender* is identity. Everything else in the Dings path
  keys on an MCP session.

## E. Places that echo the identity without deciding it

- Earcon Workshop `IDENTITIES`, `tools/earcon-workshop/web/earcon.js:22-27`. Same four
  ids; its comment (`:21`) says values "start from the Dings catalog instruments and
  were not tuned by ear". The values have drifted from the catalog:

  | id | catalog root / decay (`DingCatalog.cs:24-27`) | workshop `rootHz` / `decay` |
  |---|---|---|
  | warm-bell | 261.6256 / 1.15 | 261.6256 / 0.55 |
  | glass-chime | 261.6256 / 0.85 | 329.6276 / 0.45 |
  | muted-pluck | 261.6256 / 0.34 | 293.6648 / 0.25 |

  Decay is read with different units in the two voice models, so only the root column
  is a clean disagreement. A "warm-bell" in the Ratatoskr neutral fallback is therefore
  not the "warm-bell" an MCP agent hears.
- `tools/aqua-feel/lowered-dings/` (checked in): `.dsp` per instrument and `dings.json`,
  produced by `AquaLower --dings`. `DingCatalogTests` fails when it falls out of date
  (the test comment at the top of `LoweredDingsFedToTheFeelMeterMatchTheCatalog` names
  the regeneration command). A mirror guarded by a test; not an independent owner.
  `tools/aqua-feel/shelf.mjs:64` and `catalog.js:22-24` read it, and
  `shelf.mjs:64` defines `warm-bell` as the shelf default (`DEF_INS`, used at
  `:71,:75,:76`), a fifth voice default beyond the registry's hash.

## Where owners can disagree

1. **Name versus content.** A is a name plus a script. B keys sessions on the content
   (compile key) and labels receipts with the name. Editing a script in place keeps
   `patchId = dings.warm-bell` and `revision = 1`, so receipts from before and after the
   edit carry the same patch id and different session ids. Nothing in a receipt says
   which catalog version produced it.
2. **Session versus agent.** C assigns by MCP session; the stated intent in
   `Mcp/README.md:3-4` is "which agent spoke". After a reconnect the same agent can
   change timbre. D, meanwhile, keys on the agent address localpart. No code maps a
   session id to a localpart, so the two cannot be joined today.
3. **Four timbres versus eight senders.** C picks among 4 catalog instruments by hash;
   D has 8 hand-made voices. An agent can have one voice in `play_ding` and a different
   one in its Ratatoskr notification, and nothing records either choice.
4. **Same id, different sound.** `warm-bell` is a catalog instrument (A), a workshop
   identity (E) and the neutral fallback voice (D). Three definitions, parameter sets
   that have drifted (table above).
5. **Derivation written three times.** `dings_<id>` in `AquaDaemonClient.cs:20`,
   `AquaLower/Program.cs:19`, and the test; and the `dings.<id>` patch id appears in
   `StoreFileNameTests` as a literal. A rename rule changed in one place breaks
   store-file naming in another without a compile error.
6. **Double validation.** `DingService.cs:9-11` and `DingsPlaybackHost.cs:66-68` each
   decide whether an id is valid. Safe while both read one dictionary.

## The Host pipe protocol, as read from source

This is the "private pipe": the only channel into the audio host. MCP is a client of it;
it is not itself the pipe.

**Listener.** `AquaSynthDingsHost.exe`. `Host/Program.cs:3-4` takes the named mutex
`Local\GameCult.AquaSynthDings.Playback.v1` and exits if another instance holds it, then
runs `DingsPlaybackHost.RunAsync` (`:7`). The accept loop is
`DingsPlaybackHost.cs:12-20`: one `NamedPipeServerStream` per accepted client, name
`GameCult.AquaSynthDings.Playback.v1` (`src/AquaSynth.Dings/DingsProtocol.cs:8`),
`PipeDirection.InOut`, byte mode, `MaxAllowedServerInstances`, `Asynchronous |
CurrentUserOnly` (`:16`). Each connection is handled fire-and-forget (`:18`). The host
uses NAudio WASAPI (`Host/DingAudioPlayer.cs`) and is Windows-only in practice.

**Clients.** `Mcp/DingsHostClient.cs`, used by the MCP bridge (both stdio and
`--http`) and by the bridge's direct flags `--play`, `--get-volume`, `--set-volume`,
`--mute` (`Mcp/Program.cs:9-32`). Nothing else in the repo opens the pipe.

**Framing** (identical in both directions):
`[int32 length][MessagePack body]`. The length is `BitConverter.GetBytes(int)`, so
native byte order (little-endian on every platform this builds for, but the code does not
fix it). Valid length is 1 to 1,048,576 bytes; outside that the reader throws
(`DingsPlaybackHost.cs:86-87`, `DingsHostClient.cs:42`). Body is
`MessagePackSerializer` of the keyed records below. One request, one response, then the
server disposes the pipe (`DingsPlaybackHost.cs:24-36`). No streaming, no pipelining, no
request id.

**Request** `DingsCommand` (`DingsProtocol.cs:13-19`), keyed:

| key | field | note |
|---|---|---|
| 0 | `Version` | must equal `DingsProtocol.Version` = 1, else status `incompatible` (`DingsPlaybackHost.cs:41`) |
| 1 | `Kind` | `Ping, Play, StopAll, GetVolume, SetVolume, SetMuted` (`:11`) |
| 2 | `EventId` | catalog event id, `Play` only |
| 3 | `InstrumentId` | catalog instrument id, `Play` only |
| 4 | `Value` | `Play`: gain 0..1; `SetVolume`: volume 0..1; `SetMuted`: `>= .5` means muted |

**Response** `DingsResponse` (`DingsProtocol.cs:21-31`): `Version`, `Succeeded`,
`Status`, `Message`, `PlaybackId`, `Volume`, `Muted`, `HostProcessId`, `SessionName`
(default `"AquaSynth Dings"`). Every response carries current volume and mute, built at
`DingsPlaybackHost.cs:79-80`.

**Per kind** (`DingsPlaybackHost.cs:39-62`): `Ping` and `GetVolume` are the same branch
(status `ready`). `SetVolume` rejects non-finite or out-of-range values
(`rejected`) and otherwise sets `audio.Volume` (`volume-set`). `SetMuted` -> `muted` /
`unmuted`. `StopAll` -> `stopped`. `Play` (`:64-77`) validates event, instrument and
gain, renders each note through the daemon, queues each WAV with its delay, and answers
`playing` with a fresh `ding-<guid>` `PlaybackId`. The answer comes after rendering and
queueing, not after sound. `PlaybackId` is not stored and nothing takes it as input.
Any exception becomes `Succeeded = false, Status = "failed", Message = ex.Message`
(`:32-35`), provided the pipe is still connected.

**Client behaviour** (`Mcp/DingsHostClient.cs:15-48`): the client validates the
response `Version` (`:46`). Connect timeout 350 ms; on `TimeoutException` or
`IOException` it launches the host (`StartHost`, `:50-56`: newest
`AquaSynthDingsHost.exe` under `tools/AquaSynthDingsHost/bin`, hidden window, working
directory the repo root found by walking up to `AquaSynth.sln`) and retries up to 20
times at 500 ms connect with 100 ms gaps, all inside a 15 s overall timeout. A
`Succeeded = false` response is turned into `InvalidOperationException(response.Message)`
by `DingService.EnsureSuccess` (`DingService.cs:22-26`).

**MCP side, for completeness.** Tools (`Mcp/DingTools.cs`): `play_ding`,
`get_agent_instrument`, `claim_instrument`, `list_ding_events`, `list_instruments`,
`stop_dings`, `get_volume`, `set_volume`, `mute_dings`. Transports: stdio by default
(`Program.cs:56-65`); `--http [url]` serves Streamable HTTP at `/mcp`, default
`http://127.0.0.1:17878`, singleton by mutex `Local\GameCult.AquaSynthDings.McpHttp.v1`
(`Program.cs:34-54`). Only `play_ding`, `get_agent_instrument`, `claim_instrument` touch
agent identity; the volume and stop tools address the whole host and are
machine-global.

**Not authenticated beyond the OS.** `CurrentUserOnly` limits it to the same Windows
user. Any process of that user can send `Play`. There is no per-caller identity on the
pipe: `DingsCommand` carries an instrument, not a caller, so the Host cannot know who
asked.

## PROPOSED: one owner (a recommendation for the stewarding decision, not decided)

**Proposal.** `DingCatalog` (`src/AquaSynth.Dings`) becomes the only place that decides
what a voice is, and also the only place that derives the daemon keys from it. Its
instrument gains a content revision (a hash of `Script` plus the parameters that matter),
and one function in that assembly produces `PatchId`, `FaustName` and the revision for
the daemon (`AquaLower` and the test need only the faust-name part of it, not
`patchId` or revision). Everything else keys on the catalog id and takes the rest from the catalog.

**What the others become.**
- B (Host/daemon): the Host stops composing `patchId`/`faustName`/`revision`; it calls
  the catalog function. `AquaLower/Program.cs:19` and `DingCatalogTests.cs:45` call only the faust-name slice of it. The daemon
  keeps its session and file naming; those are daemon-internal and derive from what it
  is given.
- C (registry): stays in the bridge as a cache of choices, but its assignment function
  moves beside the catalog (so a future export can call it) and its key becomes an
  agent identity rather than an MCP session id. This depends on the open question
  below.
- D (`dings-voices`): stays a separate synthesizer for sender notifications, since its
  voices are JS functions with no Faust form. Its link to the owner is by reference:
  the neutral fallback names a catalog instrument id instead of a second `warm-bell`.
  Whether sender voices should ever become catalog instruments is a product fork, not
  part of this change.
- E: Earcon Workshop `IDENTITIES` either read their shared values from a generated
  file (as `lowered-dings` already is) or drop the shared ids; the drifted values in
  the table above are resolved by whoever owns the workshop's tuning.

**Why this owner.** A already has the most readers (the list in section A), is the only
definition of the script, and is the only one the host cannot run without. C, D and E
are all consumers or approximations of it by name.

**Migration order** (each step independent and reversible):
1. Add the catalog function; switch `AquaDaemonClient.cs:20` and `AquaLower` onto it;
   delete the test's private copy (see the oracle risk below). Pure refactor while
   `revision` stays the literal 1: the compile key, and so the session id, stay equal
   for unchanged scripts, which is checkable by comparing receipts before and after.
2. Decide the agent-identity question (below); change the registry key. Wire-visible:
   existing sessions lose their hashed assignment.
3. Content revision: DEFERRED, not scheduled here. Replacing the literal `revision = 1`
   with a catalog hash changes every compile key and session id (and the
   `sessions/<...>.cc` file names), so it breaks step 1's equality check by design. It
   needs its own decision on what feeds the hash and whether old session files are
   left or removed.
4. Remove the duplicate gates in `DingService.cs:9-11` or have the Host's gate be the
   single one (the bridge then only forwards). Keep whichever side the tests
   (`DingsBridgeTests`) need for fail-fast, but one of them must be the owner.
5. Point the neutral fallback and Earcon Workshop at the catalog.

**Risks.**
- Step 2 changes what agents hear on first use after deploy; nobody has listened to a
  before/after.
- The deferred content revision changes session ids and session file names, visible in
  receipts but not read by anything I found.
- Oracle loss in step 1: deleting the test's private `"dings_" + id` copy makes
  `LoweredDingsFedToTheFeelMeterMatchTheCatalog` compare the shared function against
  itself. The test should keep an independent expected value (a literal per instrument
  or the checked-in `.dsp` header) so a wrong rule still fails.
- Collapsing 8 senders onto 4 instruments would lose distinctness; the proposal does not
  do that, and I am recommending against it without having listened to either set.
- Moving derivation to the catalog assembly makes `AquaSynth.Dings` the home of
  daemon-protocol knowledge; if the daemon is expected to run without that assembly,
  this is the wrong direction. I did not check whether anything else wants that.

**Confidence.** Moderate on the owner choice; it follows reader counts and the write
path, not any prior art on identity registries. Lower on step 2, because it depends on
what the MCP bridge can learn about its caller.

## Could not verify

- That "four owners" in the follow-up means these four. The count here is mine.
- Whether an MCP client can tell the bridge which agent it is. The only identity read in
  source is `server.SessionId` (`DingTools.cs:45`); I did not inspect the MCP SDK for
  client-info fields, or any harness config.
- What Ratatoskr's `dings.ts` actually reads and whether it shares any naming with
  `DingCatalog`; it is not in this repo.
- Behaviour at runtime: reconnect session ids, store growth under `.aquasynth/dings-host`,
  and whether the Host's `Ping`/`GetVolume` have different callers in practice. The
  pipe protocol above is from reading, not from a captured exchange. Nothing was built,
  run, or listened to for this note.
