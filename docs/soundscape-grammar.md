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
  from instance `aqua` still answers `absent` (re-checked 2026-11-02; `absent` from Eureka's mind on 2026-11-05, `absent` from aqua again on 2026-11-14), and a
  search of the Heimdall checkout found no file mentioning it. Section 2 takes
  its constraints as relayed in `aqua-body:follow_up:chat-s946-948`: rule O1
  (a Neutral participant gets no orbit and no voice), the `estate` split,
  coarse size buckets with a separate neutral flag, activity fed by the
  Heimdall session. Section 2 must be reconciled against that follow-up's
  actual field list once it can be read.
- The Mimir harvest (`ratatoskr:follow_up:symphony-grammar-harvest-mimir`) is now
  readable: [`docs/gamecult/symphony-grammar-harvest.md` in GameCult/Ratatoskr at
  `0b3e2294`](https://eureka.gamecult.org/forge/GameCult/Ratatoskr/src/commit/0b3e2294/docs/gamecult/symphony-grammar-harvest.md)
  (Void's read of GameCult/Mimir at `c6f4541`; merged without a Soul pass), as
  checked by Kiko (`kiko-body:resolution:follow_up.chat-s8018-8019.n1`). Section 4.1
  reconciles against both. The first draft of section 4 predates them.

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
| `kind` | enum, 7 (humans: 3) | `Idle`, `Reading`, `Thinking`, `Working`, `Speaking`, `Waiting`, `Alert` | coarse; absent when `neutral`; see below |
| `energy` | enum, 4 (humans: 3) | `Low`, `Mid`, `High`, `Peak` | coarse bucket of rate/intensity, never a float; absent when `neutral`; see below |
| `size` | enum, 3 | `Small`, `Medium`, `Large` | coarse size bucket for the creature; scale per bucket is not fixed here; absent when `neutral` (O1) |
| `neutral` | bool | | separate flag; when set, `kind`, `energy` and `size` are all omitted, see O1 |
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
  `High`; no `Peak`) and `kind` is the **human subset** `Idle`, `Working`,
  `Speaking` (*proposed*). The subset is strictly smaller than the Mind/Persona
  set and chosen to reveal less, never more: it omits `Reading`, `Thinking`,
  `Waiting` and `Alert`, which describe what a participant is attending to or
  how it is faring. A producer must not emit a `kind` or `energy` outside the
  human set for a human. A consumer that receives one clamps it: an omitted
  `kind` maps to `Idle`, `Peak` maps to `High`. Clamping only ever reveals
  less. A human's `kind` that is present but not an enum value at all clamps
  to `Idle` (reveal-less) for a non-neutral human; for `mind` and `persona` an
  unknown `kind` is malformed and the frame is dropped (A4).
  Reconcile the subset against the projection-v2 field list.
- **`mind` and `persona`: the finer 7 x 4 set** (all seven `kind`, all four
  `energy` buckets).

`size` is three coarse buckets (`Small`, `Medium`, `Large`) for every estate
except a `neutral` participant, whose frame omits it (O1).
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
- **A3. Member-safe.** The frame carries `mind`, `estate`, `kind`, `energy`,
  `size`, `neutral`, `seq`, `at` and `override`, and nothing else; when
  `neutral` is set, `kind`, `energy` and `size` are stripped (O1). Never brief text,
  document ids, claims, or mind contents.
- **A4. Missing is Idle.** No frame within the staleness window (section 5, S2)
  means `Idle`/`Low`: no voice, and the orbit at rest. Silence is the default
  state, not a failure state. Missing is not `neutral`: an absent frame is an
  idle participant, a `neutral` participant is not drawn at all (O1).

### Overrides

- **O1. `neutral`** (the flag, set by the session or by the member's own
  setting): a Neutral participant gets **no orbit and no voice**, and the
  frame **strips `kind`, `energy` and `size` as well**: the producer omits all
  three, and a consumer ignores them if present. An opted-out participant
  reveals nothing, not even coarse activity or a coarse size bucket. It draws nothing and sounds nothing; there is no
  "neutral pose" to render. Neutral overrides every `size` bucket: none is drawn or carried. Neutral is
  not an error and not Idle: the frame says only that this participant is not
  shown.
- **O2. `Muted`**: no voice, orbit unchanged. Lets a listener silence one Mind
  without hiding its motion.
- **O3.** `neutral` and `override` are data in the frame, set upstream. A consumer never
  applies a local override and then reports it as the Mind's state. (A *user's*
  own mute/volume is a client preference and stays local; it never rewrites a
  frame.)

## 3. Mapping tables (shape only)

The grammar defines the mapping *axes*, not the values. Each consumer owns one
table keyed by the 28 Mind/Persona states (humans use the 3 x 3 subset); the tables are checked in beside this document
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
(recalled; the Mimir harvest gives it no support or contradiction, see 4.1): voices that share a niche in
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

### 4.1 Reconciled against the Mimir harvest

Read: the harvest at `0b3e2294` (linked in section 0; not copied here) and Kiko's
correction of it, `kiko-body:resolution:follow_up.chat-s8018-8019.n1`, relayed to
Aqua in #minds (the resolution itself is not in aqua's mind, so its wording is
not quoted). Kiko checked the harvest read-only: most numbers hold, four claims
are overstated. Kiko's row numbers are as relayed; the harvest's table in that
file has twelve rows, so rows 22 and 28 cannot be found by number there, and
the claims are cited below by what they say.

What the corrections change here:

- **The six-syllable packet song is Aqua's design choice, not Mimir's result.**
  The harvest's "best contestant" claim (canary-packet-trill, row 5) is
  confidence B, a hypothesis; Mimir's production word is the four-syllable,
  118 ms word. If this grammar adopts a six-syllable packet song, it does so by
  its own choice at confidence B, and it is a thing to try in the listener trial,
  not a measured recommendation. Nothing in section 4 depends on it.
- **Do not rest a niche on Mimir's identity figure.** Row 4's 0.908-0.997 is a
  clean-roundtrip figure; under degradation Kiko reads 0.5-0.9. Rows 6 and 28
  are confounded, so the "too-short songs collapse" thresholds are not a
  design floor for this grammar.
- **A word identifies an event slot, not a source** (row 22). Mimir's evidence
  does not support "the instrument says who spoke" (V1, axis 2). That stays a
  design rule of this grammar, to be tested by V6, with no Mimir backing.
- **No harmony evidence exists.** Mimir emits one low-gain song at a time and
  tests no simultaneous voices, intervals, consonance or masking between
  concurrent songs. V3 (voice cap), V4 (collision backoff) and V5 are Aqua's
  design and rest on the recalled auditory-scene-analysis literature alone.
- **No pleasantness evidence exists.** Mimir's score is decodability by a machine
  decoder against a known schedule; its gain was chosen for low audibility. Whether
  any song, harmony or niche table is pleasant or distinguishable to a person is
  unmeasured. **A listener trial is owed** (V6, section 7) and has not happened;
  nothing here is heard.

What the harvest does support, as design constraints already in this section's
spirit and still unmeasured for people: identity carried redundantly across
several channels at once, no sequence grammar for identity, irregular rather
than ladder-like rhythm, a deterministic generator authored once and rendered
(S4 already says the client ships rendered assets). Its open question "which
dimension bends with activity" is what section 3's rule answers by design:
`mind` selects identity, `kind` behaviour, `energy` amount, so a Mind keeps one
timbre and root for life and the contour and density move; Mimir has no activity
channel to confirm that.

The **human `kind` subset** (`Idle`, `Working`, `Speaking`, section 2) is not
touched by the harvest: Mimir carries no activity or privacy channel at all, so
the subset still rests only on the projection-v2 follow-up, which remains
unreadable from aqua (below).

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
   (field list, the human `kind` subset in section 2, the size and neutral fields,
   the stripping of `kind`, `energy` and `size` under `neutral`).
2. ~~Reconcile section 4 with the Mimir harvest.~~ Done in 4.1 (docs only; no listening). What the harvest could not give (harmony, pleasantness, discriminability by a person) goes to item 5.
3. Round-trip the frame as a CultCache document and check the size claim (one
   byte per state, 28 states).
4. Measure voice cap, density ceilings and scheduler cost on a real phone.
5. Listener-identification trial for any niche table (rule V6).

## 8. Status

Draft, reconciled with the Mimir harvest on 2026-11-14 (4.1); section 2 still awaits projection-v2. Not implemented. No code changed. No audio was produced and none is
claimed.
