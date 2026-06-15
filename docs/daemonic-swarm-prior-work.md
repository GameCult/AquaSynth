# Daemonic Swarm Prior Work

This note collects the local prior work behind the GameCult daemonic swarm
architecture. It is a reference ledger for
`docs/gamecult-daemonic-swarm.md`, not a second doctrine surface.

## AquaSynth

- `docs/verse-service-contract.md`: AquaSynth is already named as a synth
  daemon. It accepts `.aqua` patch/control documents, owns Faust compilation,
  native instrument lifetime, live control sessions, sampled outputs, and
  typed receipts. It now exposes `cultnet-host`, `cultnet-once`,
  `cultnet-stream`, and `cultnet-daemon` as local CultNet command surfaces.
- `src/AquaSynth.Faust/AquaSynthDaemonService.cs`: the service owns
  `patch.compile`, `instrument.sample`, and bounded `instrument.stream`
  execution. It writes compile/session/render/stream/operator witnesses and
  emits CultMesh-compatible stream descriptors and packet receipts.
- `src/AquaSynth.Faust/AquaSynthCultNetDaemon.cs`: local CultNet database host
  and command watcher. Typed command documents enter the database; typed
  receipts and provider state come back out.
- `state/spine.yaml`: the current invariant says `AquaSynthDaemonService` owns
  execution while `AquaSynthCultNetDaemon` owns local CultNet command
  observation. JSON-lines modes are only operator/xeno edges.

## Eve

- `Eve:README.md`: Eve is the display/control/sensor edge for GameCult apps.
  Participants publish structured surfaces through CultMesh; clients render
  those surfaces locally, return operator intent, and publish local sensor
  observations. Durable service state belongs in CultCache `.cc`; local Verse
  visibility belongs in CultMesh; discovery belongs in Odin.
- `Eve:docs/surface-contract-v1.md`: `gamecult.eve.surface.v1` is a retained
  CultMesh UI document. Providers own truth, accepted state, command effects,
  and style token values. Eve owns the surface contract, command envelope,
  renderer parity, and local input/sensor publication. Renderers own native
  projection only.
- `gamecult-site:docs/gamecult_void_dossier.md`: the blunt public sentence:
  Eve should not share applications; Eve should share inspectable control
  surfaces. The same semantic surface gets different local bodies: web,
  desktop, mobile, Unity, overlays, rooms.

## CultLib And CultMesh

- `CultLib:src/GameCult.Mesh/docs/public-api.md`: `CultMeshNode` wraps the
  local runtime pieces: `CultCache`, `CultNet` server, distributed realtime
  database facade, and schema-v0 bridge. CultMesh is the package surface;
  CultNet remains lower-level transport plumbing where appropriate.
- `CultLib-main-work:packages/cultmesh-kotlin/README.md`: Kotlin/Android
  clients carry typed MessagePack document codecs, a small CultNet lane, and
  Eve dashboard/sensor document contracts. Device streams are observation
  transport, not synchronization authority.
- `CultLib-main-work:packages/cultcache-py/src/cultmesh_py/facade.py`: Python
  has the same conceptual entrypoint shape: create/start nodes, local servers,
  Verse discovery, peer exchange, game sessions, simulation fact commitment,
  and stream catalogs.

## Odin And Idunn

- `Odin:src/Idunn/README.md`: Idunn is the keepalive daemon for the Odin swarm.
  It knows which GameCult daemons should be alive, restarts or alarms when
  appropriate, records deployment/restart decisions as typed state, and does not
  steal work ownership from the daemons it supervises.
- `Odin:crates/idunn-daemon/src/main.rs`: the Starfire/local swarm profile
  includes concrete daemon targets such as Odin, VoidBot, Weksa, Muninn, Vili,
  Yggdrasil services, Nightwing Gjallar, and other local machine helpers.
- `gamecult-site:GameCult/Projects/index.md`: CultLib is described as the
  nervous system family: CultCache persists typed state, CultNet carries typed
  communication, and CultMesh networks daemons, service surfaces, schema
  catalogs, and interface projections. Odin is the all-seer for Verse
  discovery, schema awareness, translation routes, provider surfaces, and
  interface aggregation. Idunn keeps the swarm alive.

## Operating Pattern

The repeated shape across the prior work is:

```text
script or supervisor starts daemons
  -> each daemon publishes typed state, command boundaries, health, and surfaces
  -> Odin discovers and aggregates provider state
  -> Eve lowers provider-owned surfaces into the local native runtime
  -> users issue command intent through the surface
  -> providers accept or reject commands
  -> providers publish updated typed state and receipts
```

The window is usually not the application. The window is the local Eve body. The
application is the swarm behind it.
