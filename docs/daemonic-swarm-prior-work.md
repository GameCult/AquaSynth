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

## Brokkr

- `Brokkr:README.md`: Brokkr is the GameCult creative-tool broker daemon. It
  lets Unity, Blender, and future editor runtimes expose live authoring
  surfaces to the Verse without pretending those runtimes are the same machine.
  Brokkr owns the broker contract, provider advertisement, routing policy, and
  typed command receipts. Unity and Blender keep editor truth.
- `Brokkr:docs/architecture.md`: the Unity package opens a durable CultMesh node
  backed by `.brokkr/unity-editor.ccmp`, writes editor snapshots and command
  receipts, and watches typed command-intent documents. The Blender add-on uses
  CultLib's Python CultMesh runtime as its local mirror node and can serve that
  node for external clients.
- `Brokkr:docs/provider-contract.md`: Brokkr exposes capability families for
  mirror publishing, intent watching, host status, scene tree, component state,
  selection, asset catalog, object creation, component/property writes, command
  receipts, and Eve GUI/TUI publication. It defines Unity and Blender host
  snapshots, command intents, receipts, sync sessions, object bindings, sync
  vars, and timeline bindings.

## Weksa, Vili, And Ghostlight

- `E:\Projects\weksa\README.md`: Weksa is a procedural language engine grounded
  in concept, culture, grammar, phonology, morphology, and diachronic history
  rather than English word substitution. Its daemon contract owns typed
  conversational intent, pronunciation plans, utterance handoffs, and the
  Eve/CultMesh surfaces that make those documents inspectable.
- `E:\Projects\weksa\docs\verse-service-contract.md`: Weksa is the
  language-intent daemon. It turns writing, dialogue pressure, and agent state
  into typed intent, pronunciation, and utterance documents for text, speech,
  and synthesis consumers. AquaSynth owns learned speech synthesis and audio;
  external speech providers own rendered audio; Weksa owns the accepted
  utterance/request projection and trace.
- `Vili:README.md`: Vili is the GameCult Persona animation daemon. It bridges
  Persona performance intent to body-motion generation through Kimodo, while
  Vili owns job intake, Kimodo runtime checks, resident worker lifecycle,
  generated motion artifact references, and provider/operator surfaces.
- `Vili:docs/verse-service-contract.md`: Vili is an animation service, not a
  per-request CLI wrapper. It accepts gesture/action prompts, keeps a resident
  Kimodo worker warm, publishes job records and motion artifacts, and leaves
  retargeting/playback/facial performance/camera/lip sync to future renderers.
- `Ghostlight:notes/fresh-workspace-handoff.md`: Ghostlight's live
  product-facing goal is procedural branching scene generation for games:
  speech and non-speech actions, NPC responses, consequences, future hooks, and
  state/memory/social-perception mutation.
- `Ghostlight:prompts/sandboxed-coordinator-turn.md`: the Ghostlight
  coordinator worker moves one scene beat through structured state. It consumes
  scene/world state, event records, participant summaries, unresolved hooks,
  available characters, clocks, resources, gates, and allowed machinery calls,
  then emits the next playable state.

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

## Composition Lesson

The cinematic pipeline makes the scale law visible: daemon capabilities compose
into greater daemons through typed command and receipt surfaces. A larger daemon
may own workflow state, scheduling, review, aggregate receipts, and escalation,
but it does not absorb the smaller daemons' truth. Brokkr coordination does not
own Unity or Blender state. Ghostlight coordination does not own Weksa language
truth, Vili motion synthesis, or AquaSynth audio rendering. The aggregate
surface must preserve lower-daemon provenance, rejections, and failures instead
of flattening them into one victorious-looking dashboard.
