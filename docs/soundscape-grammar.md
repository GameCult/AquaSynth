# Soundscape grammar for the Ratatoskr generative symphony (spec)

Serves follow-up `aqua-body:follow_up:chat-s938-943`. **Grammar only.** No notes,
no motifs, no pitches, no instrument choices. Nothing here was rendered, run,
listened to, or measured; every number marked *proposed* is a design choice
awaiting a listening trial and a device measurement. Nothing in this document
may be cited as heard.

## 0. Inputs not seen

Two inputs named in the brief could not be read when this was written, and the
grammar is drafted without them. Each is a place to reconcile before the doc is
treated as in force.

- `heimdall-body:follow_up:ratatoskr-creature-projection-v2`: `huginn view`
  from instance `aqua` still answers `absent` (re-checked 2026-11-02), and a
  search of the Heimdall checkout found no file mentioning it. Section 2 takes
  its constraints as relayed in `aqua-body:follow_up:chat-s946-948`: rule O1
  (a Neutral participant gets no orbit and no voice), the `estate` split,
  coarse size buckets with a separate neutral flag, activity fed by the
  Heimdall session. Section 2 must be reconciled against that follow-up's
  actual field list once it can be read.
- `ratatoskr:follow_up:symphony-grammar-harvest-mimir` (Mimir birdsong
  research): also `absent` from aqua's mind, and `GameCult/Mimir` holds no
  symphony or voice-separation note (grep of `notes/`, `research/`, `docs/`).
  Section 4 is therefore built from this repo's own `research/syrinx-modeling`
  plus the general auditory-scene-analysis and acoustic-niche literature named
  there, *recalled, not re-read for this draft*. The harvest, when it exists,
  replaces or confirms section 4; it is the likeliest thing to change.

Context actually read: `docs/gamecult/ratatoskr-target.md` in `GameCult/Ratatoskr`
at `b33d8de` (the agreement that one activity vocabulary drives both sound and
motion, the member-safe projection, "kind, Mind, time and intensity, never
briefs or mind contents"), and `docs/agent-activity-grammar.md` (an earlier,
unmerged AquaSynth draft for the Dings event source; not part of this change).

## 1. Purpose and owner

One typed **activity frame** per Mind, produced once, read by two consumers:

- the **orbit renderer** (Persona motion around Yggdrasil), and
- the **voice scheduler** (which instrument voice speaks, how densely).

Neither consumer owns the vocabulary; neither adds a field. The vocabulary owner
is this grammar. The frame's producer is the Heimdall session (section 2). A
consumer that needs a distinction the vocabulary lacks raises it as a change to
this document, not as a private side channel.

## 2. The activity frame

Typed CultNet/CultCache document, projected member-safe. No private pipe: the
frame reaches both consumers by the same subscription, and a consumer that
cannot read it plays nothing and draws nothing.

| field | type | values | note |
|---|---|---|---|
| `mind` | id | participant identity | stable identity, not a run/session label; for a human, the member's stable id |
| `estate` | enum, 3 | `human`, `mind`, `persona` | selects the vocabulary this participant may use; see below |
| `kind` | enum, 7 (humans: reduced) | `Idle`, `Reading`, `Thinking`, `Working`, `Speaking`, `Waiting`, `Alert` | coarse; see below |
| `energy` | enum, 4 (humans: 3) | `Low`, `Mid`, `High`, `Peak` | coarse bucket of rate/intensity, never a float; see below |
| `size` | enum, 3 | `Small`, `Medium`, `Large` | coarse size bucket for the creature; scale per bucket is not fixed here |
| `neutral` | bool | | separate flag; overrides any `size` bucket, see O1 |
| `seq` | u32 | monotonic per `mind` | ordering and staleness; consumers drop `seq <= last` |
| `at` | time bucket | 1 s resolution | producer's observation time, for the staleness rule |
| `override` | enum, 2 | `None`, `Muted` | see rules O2, O3; Neutral is the `neutral` flag, not an override value |

Why this small: 7 x 4 = 28 states (the Mind/Persona set) fit in one byte on the
wire and in a lookup table on a phone; both consumers map state to behaviour by
table, no arithmetic on unbounded values. `kind` and `energy` are orthogonal on purpose, because
orbit radius/speed wants energy and voice choice wants kind.

### Estate

`estate` is set by the producer, never inferred by a consumer, and bounds what
the other fields may carry (per
`heimdall-body:follow_up:ratatoskr-creature-projection-v2`):

- **`human`: coarse only.** `energy` is capped at 3 buckets (`Low`, `Mid`,
  `High`; no `Peak`) and `kind` is a reduced list, a subset of the seven (which
  subset is a reconcile item against the projection-v2 field list; this
  document does not guess it). A producer must not emit a finer value for a
  human; a consumer that receives one clamps it to the coarse set.
- **`mind` and `persona`: the finer 7 x 4 set** (all seven `kind`, all four
  `energy` buckets).

`size` is three coarse buckets (`Small`, `Medium`, `Large`) for every estate.
The scale each bucket draws at is **not fixed by this grammar**: the values
0.7 / 1 / 1.4 are Ratatoskr placeholders, owned by the orbit renderer, and may
change without touching the frame.

Rules:

- **A1. Source.** The frame comes from the Heimdall session and only from it.
  Neither consumer infers activity from Matrix presence, typing, message
  traffic, audio, or room membership. Activity is a stub fed by the Heimdall
  session; being in a room says nothing about being active. (The target already notes presence/typing are too coarse
  for Minds.) *Reconcile the exact kind list against the projection-v2
  follow-up before use.*
- **A2. Coarse by construction.** There is no continuous field in the frame. A
  renderer that wants smooth motion interpolates between buckets locally; it
  does not request finer data.
- **A3. Member-safe.** The frame carries kind, Mind, time and energy bucket.
  Never brief text, document ids, claims, or mind contents.
- **A4. Missing is Idle.** No frame within the staleness window (section 5, S2)
  means `Idle`/`Low`: no voice, and the orbit at rest. Silence is the default
  state, not a failure state. Missing is not `neutral`: an absent frame is an
  idle participant, a `neutral` participant is not drawn at all (O1).

### Overrides

- **O1. `neutral`** (the flag, set by the session or by the member's own
  setting): a Neutral participant gets **no orbit and no voice**. It draws
  nothing and sounds nothing; there is no "neutral pose" to render. The flag
  overrides every `size` bucket, `kind` and `energy`. Neutral is not an error;
  it hides activity, it does not pretend there is none to other consumers.
- **O2. `Muted`**: no voice, orbit unchanged. Lets a listener silence one Mind
  without hiding its motion.
- **O3.** `neutral` and `override` are data in the frame, set upstream. A consumer never
  applies a local override and then reports it as the Mind's state. (A *user's*
  own mute/volume is a client preference and stays local; it never rewrites a
  frame.)

## 3. Mapping tables (shape only)

The grammar defines the mapping *axes*, not the values. Each consumer owns one
table keyed by the 28 Mind/Persona states (humans use its coarse subset); the tables are checked in beside this document
when values exist.

| axis | owner | driven by | notes |
|---|---|---|---|
| orbit **existence** (drawn or not) | orbit renderer | `neutral` | `neutral` set: no orbit |
| orbit radius / speed / phase coupling | orbit renderer | `energy`, `kind` | Nibu's side; not specified here |
| creature scale | orbit renderer | `size` | per-bucket scales are renderer placeholders, not fixed here |
| voice **presence** (sounding or silent) | voice scheduler | `neutral`, `kind` | `neutral` set: no voice, regardless of `kind`; `Idle`, `Waiting` default silent |
| voice **density** (events per window) | voice scheduler | `energy` | capped, section 5 |
| voice **role** (lead / support / texture) | voice scheduler | `kind` | role, not instrument |
| voice **identity** (which instrument) | voice scheduler | `mind` | never `kind`; section 4 |

The one rule that ties the two consumers: **`mind` selects identity, `kind`
selects behaviour, `energy` selects amount.** A given Mind sounds like itself
in every state; what it does changes.

## 4. Keeping voices distinct when mixed

Principle, from the acoustic-niche and auditory-scene-analysis literature
(recalled, to be confirmed by the Mimir harvest): voices that share a niche in
frequency, time and space mask each other; voices that differ on several
independent axes stay separable even at high density. Assign each Mind a
distinct **niche** on independent axes, and make the scheduler protect those
niches. Axes:

1. **Register band.** Each Mind owns a band; bands do not overlap among
   concurrently audible voices. Most robust single cue.
2. **Timbre family.** Distinct spectral envelope and onset shape. Identity is
   carried by instrument (`ratatoskr-target.md`: "the instrument says who
   spoke"); this grammar does not choose instruments.
3. **Rhythmic slot.** Each Mind owns a phase offset within a shared pulse grid,
   so onsets interleave instead of colliding. Collisions are the main cause of
   masking at low density.
4. **Stereo position.** Fixed pan per Mind, stable across sessions. Free on
   phones with headphones, nearly useless on speakers; never the only cue.
5. **Role contrast.** At most one `lead` voice sounding at a time; the rest
   support or texture (quieter, sparser, narrower band).

Rules:

- **V1. Identity is stable.** The niche (axes 1-4) is a function of `mind`
  only. It never moves with `kind`, `energy`, or which other Minds are present.
  A listener learns a Mind once.
- **V2. Niche table is data**, held beside the Mind's other typed state
  (the target already says the instrument per Mind belongs in a typed document
  on the Mind side, not hardcoded). Not defined here.
- **V3. Voice cap.** At most **4** voices audible at once (*proposed*; the
  human limit on following concurrent streams is small and phones are
  small). Over the cap, the lowest-`energy` voices drop first, never the
  `lead`. The cap is a requirement to measure, not a finding.
- **V4. Collision backoff.** If two voices would start within one pulse of each
  other in the same register band, the lower-priority one slips to its next
  slot. It does not stack.
- **V5. More Minds than niches.** When Minds exceed available niches, the
  excess are *not* squeezed in by shrinking bands. They share a `texture`
  voice or stay silent. Distinctness beats completeness.
- **V6. Identification is unproven.** Nominal separation (different bands,
  different families) is not human discriminability. No Mind's niche is
  accepted until a listener-identification trial passes (the existing
  notification ledger requires the same). That trial has not happened.

## 5. Timing, staleness and the callback deadline

Hard rule: **the audio callback deadline wins over everything in this
document.** The callback never waits on the grammar, the network, a decode, an
allocation, or a lock.

- **C1. Frame handoff is a snapshot.** The network/projection thread writes the
  latest frame per `mind` into a preallocated table; the scheduler thread reads
  it and writes a preallocated voice-command queue; the audio callback only
  reads that queue. No thread but the callback touches the output buffer; the
  callback never blocks on, or is blocked by, the others (single-producer
  single-consumer, fixed capacity).
- **C2. Drop, never delay.** If the queue is full or the scheduler is late, the
  frame or command is dropped. A late sound is not played to catch up and its
  internal timing is never stretched.
- **S1. Latest wins.** Per `mind`, only the newest `seq` matters. Intermediate
  frames missed during a stall are not replayed.
- **S2. Staleness.** A frame older than **10 s** (*proposed*) is treated as
  absent (rule A4). Sounding on stale activity is misleading.
- **S3. Rate.** Voice density per `energy` is bounded by a ceiling per Mind per
  window (*proposed*, set with the device measurement); rule V3 bounds the
  total. Bursts are coalesced, not queued.
- **S4. Pre-rendered material.** The client ships rendered audio assets, no
  synth in the browser (`ratatoskr-target.md` agreements). The scheduler
  selects and positions assets; it generates nothing at callback time. Browsers
  block audio until a first user gesture: before it, the scheduler runs but the
  output is silent, and no state is lost.
- **S5. Backgrounded or hidden page.** Voice scheduler pauses with the orbit
  renderer's pause (the target already pauses the background when hidden). On
  resume, S1/S2 apply; there is no catch-up burst.
- **S6. Failure is silent.** No frame source, a dead scheduler, a refused
  autoplay, or a full queue all produce silence, with the orbit at rest. A
  failure is not a `neutral` participant and does not remove the creature. Nothing
  retries, alerts, or alters the frame source.

## 6. What this grammar does not decide

- Motifs, notes, pitches, instruments, envelopes. (Next layer; needs a
  listening trial per the repo's audio doctrine.)
- Orbit geometry and shader work (Nibu's side).
- The feed transport. `ratatoskr:question:activity-feed-source` owns where the
  frame comes from; this document fixes only its *shape*, and that it is a
  typed CultNet document, not a private channel.
- Per-device budgets. All numbers marked *proposed* need a phone measurement.

## 7. Verification owed

None run. To be done before the grammar leaves draft:

1. Reconcile section 2 with `heimdall-body:follow_up:ratatoskr-creature-projection-v2`
   (field list, the reduced human `kind` subset, the size and neutral fields).
2. Reconcile section 4 with the Mimir harvest.
3. Round-trip the frame as a CultCache document and check the size claim (one
   byte per state, 28 states).
4. Measure voice cap, density ceilings and scheduler cost on a real phone.
5. Listener-identification trial for any niche table (rule V6).

## 8. Status

Draft. Not implemented. No code changed. No audio was produced and none is
claimed.
