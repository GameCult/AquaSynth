# The GameCult Daemonic Swarm

Most GameCult apps are not one process wearing one window.

The window is usually a thin Eve terminal in whatever runtime is native to the
machine in front of you: browser, UIKit, Android, Direct2D, Unity, overlay,
framebuffer, TUI, or some future room-sized disgrace with good posture. The
actual application intelligence lives in the swarm of daemons that the script,
launcher, service manager, or Idunn profile brings alive behind it.

That is not ornament. It is the build pattern.

## The Shape

A GameCult app is a visible local body attached to a distributed service mind.

```text
native Eve terminal
  -> CultMesh surface and command documents
  -> provider daemon command boundary
  -> provider-owned state transition
  -> CultCache/CultMesh receipts
  -> Eve projection update
```

The user sees one app. The system is usually several organs:

- a provider daemon that owns domain truth and side effects;
- a CultCache store that preserves typed state and receipts;
- a CultNet/CultMesh node that publishes state and accepts command documents;
- an Eve surface document that describes the controls, views, health, and
  command routes;
- a local renderer that lowers that surface into native controls;
- Odin, when present, discovering providers and assembling Verse sight;
- Idunn, when present, keeping the expected daemons alive and witnessed.

The native window is important. It owns the user's local body: pixels, input,
latency, accessibility, media decode, sensors, device permissions, platform
integration. It does not own the truth just because it has glass.

## Why We Build This Way

Most software quietly fuses three things that should stay separate:

- the thing that knows the domain;
- the thing that persists and publishes state;
- the thing the user touches.

That works until it does not. Then every platform port becomes a second
application, every dashboard becomes a private little kingdom, every renderer
starts deciding state because it was nearby, and every service grows its own
watchdog, schema story, and status panel. The machine fills with plausible
duplicate authorities. It starts passing tests while forgetting what it is.

GameCult cuts the body differently.

Providers own consequences. Eve owns presentation and local input. CultMesh owns
typed visibility, subscriptions, provenance, and command transport. CultCache
owns durable typed state. Odin owns discovery and aggregation. Idunn owns
continuity. Bifrost owns operator/public crossings when a command leaves the
private machine and touches humans, governance, money, publishing, or social
surface.

No organ gets the whole throne. This is how the machine stays legible after it
starts doing real work.

## Eve Is The Terminal, Not The App

Eve is not remote desktop. Eve is not a browser shoved into every product until
everything smells like a compromise. Eve is the portable control surface layer.

A provider publishes a retained semantic surface:

- stable component ids;
- layout intent;
- bindings to typed state;
- available commands;
- health and freshness;
- style tokens;
- provenance;
- capability requirements.

Each runtime lowers that surface locally. Web uses web controls. iOS uses native
views. Android uses Android views. Fensalir can lower the same surface into
Direct2D/DirectWrite. Unity can mount the same surface into a game scene.
Framebuffer and TUI clients can lower it into dense operator instruments.

Same semantic surface. Different flesh.

That is the point. Fensalir should not have to integrate AquaSynth as a private
synth library just to make sound. It should talk to AquaSynth as a provider:
discover the service, submit patch and control documents, receive instrument
handles or stream descriptors, and render the operator surface locally.

## Daemons Own Capabilities

A daemon is not just a background process. In this architecture, a daemon is a
capability owner with a typed public edge.

A good GameCult daemon publishes:

- service id and Verse id;
- schema catalog or document bindings;
- command documents it accepts;
- receipt documents it emits;
- health and freshness state;
- durable `.cc` witness locations;
- Eve/CultUI surfaces for operator inspection and control;
- explicit boundaries naming what it does not own.

AquaSynth is the current synth example. AquaSynth owns patch parsing, Faust
emission, native compilation, compiled instrument sessions, sample rendering,
bounded stream receipts, and synth-control state. A client can send
`instrument.sample` or `instrument.stream` as typed command state through the
local CultNet database. AquaSynth compiles or opens the instrument, writes
sample artifacts and receipts, and publishes the result. The client consumes
the capability; it does not become the synth.

That lets one AquaSynth daemon sit on this machine or another machine in the
Verse and serve multiple apps. The same engine can support Fensalir, Eve
operator panels, music agents, render workers, game tools, and future live
performance clients without each one embedding its own incompatible synth
brain.

The same pattern applies elsewhere:

- Mimir owns observation and synchronization interpretation.
- VoidBot owns its local swarm state and Persona/tool surfaces.
- Weksa owns utterance intent and lowering.
- StreamPixels owns creator/render pipeline state.
- Odin owns discovery and provider aggregation.
- Idunn owns daemon continuity and restart witnesses.

Each daemon is a small sovereign machine. The swarm is the application.

## The Script Starts The Swarm

When a GameCult script says "run the app," it often means "start the local
swarm profile."

That may start one daemon. It may start ten. It may start a provider, a broker,
a sensor receiver, a renderer, a bridge to a device, a keepalive process, and a
browser reference client. The important part is that every process has a named
authority.

The script should not become the owner of state. The script is an ignition
ritual with logging. It starts the bodies, records enough handles to stop or
inspect them, and gets out of the way. Long-running work belongs in durable
processes with health signals, typed state, and restart policy.

If the app only works while one attached shell window stays open, the machine is
still a prototype. Sometimes that is fine. It should not be confused with the
architecture.

## Fast Local Does Not Mean In-Process

Same-machine providers should feel native. That does not require shoving every
capability into the UI process.

The split is:

- typed state and commands over CultNet/CultMesh;
- realtime data over the fastest honest local lane;
- visible controls through Eve;
- domain work inside the provider daemon.

For AquaSynth, that means patch requests, compile receipts, session state, and
stream descriptors are typed documents. Audio blocks should eventually move
through a low-latency local transport such as shared-memory rings, platform
audio graph nodes, memory-mapped packet buffers, or another fixed-block lane
that does not drag filesystem writes or chatty command protocols into the audio
callback. The command surface still owns intent and receipts. The realtime lane
just moves sound fast.

That distinction matters. Transport is not authority. A ring buffer should not
decide what instrument exists. A renderer should not decide which command
landed. A cache should not pretend to be the current audio clock. Every layer
gets one job sharp enough to inspect.

## What This Buys Us

The daemonic swarm approach gives GameCult a few practical advantages:

- **Shared capability:** one local or network daemon can serve many apps.
- **Native feeling:** each platform gets a real local renderer instead of the
  lowest common UI.
- **Inspectable state:** commands, receipts, health, schemas, and surfaces are
  typed documents, not private process memory.
- **Crash containment:** a renderer can die without erasing provider truth; a
  provider can fail with receipts instead of silent UI weirdness.
- **Portability:** a service publishes one semantic surface; each runtime lowers
  it.
- **Operator clarity:** Odin can discover what exists, and Idunn can keep
  expected daemons alive without guessing private truth.
- **Coherent growth:** new apps compose existing capabilities instead of
  cloning them.

The tradeoff is discipline. This architecture punishes fuzzy ownership. If a
daemon, renderer, script, cache, and bridge can all decide the same result, the
swarm becomes mush with sockets. Fast mush, maybe. Still mush.

## The Rule

When building a GameCult app, ask first:

- What daemon owns the capability?
- What typed state proves what happened?
- What command documents does it accept?
- What receipts does it emit?
- What Eve surface makes it visible and controllable?
- What local runtime lowers that surface for this user?
- What fast lane is needed for realtime data?
- What does this process explicitly not own?

If the answer is "the window owns everything," the design is probably still
too small or too young.

If the answer is "the provider owns truth, Eve owns the local touch surface,
CultMesh carries typed state, Odin sees it, and Idunn keeps it alive," then the
machine is beginning to have the right anatomy.

That is how we build at GameCult: not one app with a private dashboard, but a
swarm of provider daemons, typed memory, portable surfaces, and native bodies.
The user touches a window. The window touches the swarm. The swarm does the
work.
