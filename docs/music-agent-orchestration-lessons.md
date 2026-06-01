# Music Agent Orchestration Lessons

## Purpose

This report generalizes what the music agents and the AquaSynth orchestration
loop have learned so far. It is not a victory lap. Most of the useful learning
came from watching agents do weak, plausible things and then changing the
machine so those weak things stopped counting as progress.

The live lesson is simple: a music agent does not learn because it produced a
rendered patch. It learns when the system can say what role the patch attempted,
what evidence moved, what failed, what transferred to another target, and what
must be cut before the next agent repeats the same little ritual with new
filenames.

## The Shape Of The Current Machine

The current song curriculum is an orchestration loop, not a single patch writer.
It prepares local song or snippet challenges, extracts reference evidence,
dispatches external musician agents, scores rendered AquaSynth candidates,
keeps render failures as negative evidence, asks curator agents to distill
novel lessons, writes those lessons into CultCache, and indexes them into
Qdrant/Ollama so later agents retrieve evidence packets instead of stale chat
summaries.

The loop now treats these records as separate authorities:

- Challenge evidence owns the target: frozen audio path, tempo, register, scale
  hints, spectral motion, RMS motion, autocorrelation, and local intelligence
  reports.
- Candidate evidence owns what the agent actually built: `.aqua` script,
  rendered WAV or render failure, analysis artifacts, evaluator report, and
  trial metrics.
- Studio evidence owns the agent's reasoning: producer brief, hypotheses,
  listening journal, Aqua gap ledger, studio lesson, abstraction ledger, and
  instrument conventions.
- Curator evidence owns promotion into doctrine: only novel, actionable,
  evidence-backed lessons should enter the cumulative music knowledge store.
- The cumulative music-production CultCache store owns reusable curriculum
  memory. Qdrant is a derived retrieval index, not a second truth.

That separation matters. Earlier loops let a metric, a prompt, or a summary
pretend to be the whole mind. The current loop is less glamorous and more
honest: every stage has a job, and failures are first-class records.

## What The Music Agents Learned

The agents first learned that timbre tricks are not arrangement. Early winners
could get weak log-mel agreement by writing a bright lead burst and then hiding
the rest of the clip under low-motion noise, pads, or air. That looked like
activity to the scorer and sounded like a loop giving up. The curriculum now
treats full-form continuity as an invariant: distinct musical events or motif
mutations must happen after the opening, with audible activity across the
beginning, middle, and ending.

They learned that roles need owners. A patch that names `kick`, `hat`, `pad`,
or `syrinx` has not earned those names. Drums need pitched body plus filtered
skin. Leads need pitch, gate, contour, and a source/filter/modulation identity.
Pads need harmonic fields and entrance/exit or brightness motion. Texture needs
band limits, gates, and a musical job. A role name without behavior is just a
little costume on a sine wave.

They learned that texture is support, not composition. Background air, dust,
codec grit, room tone, tape hiss, and vinyl-style beds can be useful scene
voices, but full-duration broadband noise is failed evidence unless it is
shaped, gated, quiet, and subordinate to musical structure.

They learned that instrument ownership matters for future memory. Simple
oscillator blips were producing "8-bit distress signal" candidates that could
score just enough to contaminate the curriculum. The scorer now records
syrinx/acoustic voice evidence, subtractive drum evidence, additive/PAD
evidence, musical instrument score, and chip-distress risk so the system can
distinguish a musical sketch from an emergency beep wearing a hat.

They learned that a production attempt needs a studio loop. One-shot patches
are sample data. Useful agents now render their own candidate, read the same
feature vocabulary used by the target, revise several times, and leave a
listening journal explaining what sounded alive, fake, static, missing, or too
generic.

They learned that a future DSL abstraction is not magic accepted syntax. Agents
may propose role sugar, but today's candidate must use implemented AquaSynth
syntax. Proposed abstractions belong in the abstraction ledger with owner,
lowering, expected benefit, and keep/cut verdict. This stopped agents from
hallucinating syntax and calling the parser's refusal a mystery.

They learned that current AquaSynth can support composition only when musical
time is explicit. The small Strudel-ish surface now includes `meter`,
`sequence`, `scale`, `chords`, `mix`, `pattern`, and `texture`, all lowering to
visible parameters, curves, and voices. The lesson is not "add more sugar." The
lesson is "sugar may own brevity and defaults; the lowered graph still owns the
truth."

## What The Orchestrator Learned

The orchestrator learned that metrics can be fooled by cheap continuity. It now
records active coverage, motion coverage, first-second energy share, tail energy
share, and mode-collapse risk. A one-second riff followed by static texture is
not allowed to become a good lesson merely because its spectrum is vaguely in
the neighborhood.

It learned that render failure is evidence. Earlier passes could abort or lose
failed candidates, which taught nothing and hid syntax pressure. Now failed
renders write failure files, appear in evaluator reports, and can be promoted
as failure-mode knowledge when they expose bad prompt instructions, bad manual
examples, missing parser support, or environment problems.

It learned that the worker environment is part of the machine. Renderer builds
must not depend on user-profile NuGet or dotnet config. Temporary render
projects now use local HOME, APPDATA, LOCALAPPDATA, DOTNET_CLI_HOME, and
NUGET_PACKAGES paths so spawned workers do not fail for reasons unrelated to
music.

It learned that hot trial storage cannot be a monolithic archive. Full-song
stores exceeded 2 GB and whole-file MessagePack rewriting became the owner of
failure. CultCache trial-result stores moved to paged directory backing: the
`.cc` file is a manifest, records live as individual MessagePack pages, and hot
writes no longer drag cold history through RAM.

It learned that lexical "semantic" search was mostly theater. Music knowledge
retrieval is now Qdrant/Ollama-backed and returns full packets: owner, summary,
transfer rules, AquaSynth patterns, failure modes, retrieved chunks, and source
paths. Lexical fallback is a debugging path, not the normal agent memory.

It learned that manuals must seed the curriculum, but not be re-copied every
pass. The loop now starts with repo-native manuals once, then appends learned
worker and curator evidence into a cumulative store. Static doctrine remains a
spine; pass-specific lessons remain pass-specific until they earn promotion.

It learned that curation must be a separate job. Musician agents are too close
to their own patches. A curator agent reads the scored phase, extracts only
novel actionable lessons, cites evidence paths, and writes rejected-copy notes.
This keeps raw effort from becoming doctrine by emotional proximity.

It learned that local signal intelligence beats provider fantasy. Current
source maps treat Every Noise as genre vocabulary context, Echo Nest as gone,
Spotify audio features as restricted/deprecated and not a training substrate,
AcousticBrainz as historical, and local extraction as the primary signal path.
The machine stopped waiting for a convenient external oracle and started
measuring the audio it actually has.

## Mistakes We Have Learned To Stop Making

Do not count syntax attendance as musicianship. A patch can use `meter`,
`sequence`, `chords`, and `mix` and still be a dead loop. The evaluator must ask
whether time, roles, and mix motion stayed alive.

Do not let background texture impersonate arrangement. Noise can preserve RMS,
tail energy, and spectral coverage while the music collapses. Texture is a
scene role, not a substitute for phrases, sections, groove, or harmonic motion.

Do not let one metric become the ear. Log-mel cosine, envelope distance, RMS,
centroid, and autocorrelation are evidence. They are not music. The user's ear
remains the higher court, annoyingly analog and therefore expensive to fool.

Do not promote weak winners blindly. A best candidate in a weak pass may be
only the least bad failure. Distillation must preserve "pressure" and
"failure-mode" status instead of laundering every top-ranked row into a pattern
future agents should copy.

Do not make agents solve invisible targets. They need challenge reports,
candidate-side analysis, local music intelligence, and prior knowledge packets.
Without that context they optimize from vibes, and vibes have no stack trace.

Do not ask agents to learn from the previous pass if retrieval cannot actually
surface the previous pass. The store, index, search report, and prompt injection
must form one working memory path.

Do not hide parser or renderer faults behind musical criticism. If the manual
teaches `env=ad attack=... decay=...`, the parser should accept it or the manual
should be corrected. If the renderer cannot run because HOME points somewhere
forbidden, fix the environment owner before blaming the patch.

Do not let full-song scale reuse snippet storage assumptions. Long artifacts
force storage, indexing, and progress-reporting discipline. Whole-store rewrites
are not a cute implementation detail; they are a delayed outage.

Do not treat proposed abstractions as accepted reality. Agents may mine sugar
from repeated pain, but the live candidate must lower through implemented
syntax. Future-syntax proposals belong in ledgers until source work gives them
an owner.

Do not confuse previous-agent prose with durable knowledge. Raw reports are
evidence. Curated, source-linked, scored, retrievable records are memory.

## Reusable Principles

Every musical role should have an owner sentence: "X owns Y so that Z remains
true." Examples:

- `sequence` owns kick gate timing so groove accents stay inspectable.
- A subtractive drum body owns low-frequency impact so filtered noise does not
  pretend to be a kick.
- A texture voice owns band-limited recording color so full-band hiss does not
  become composition.
- `mix` owns section gain motion so roles can enter, leave, and trade attention.
- A curator document owns promotion so raw candidate reports do not become
  doctrine by default.

Every pass should separate four verdicts:

- Did the patch render?
- Did it preserve audible form across time?
- Did it use role owners that transfer?
- Did it teach anything worth retrieving later?

Every negative result should be named at the layer that failed:

- Syntax failure: parser/manual/prompt mismatch.
- Render failure: compiler/runtime/environment/toolchain owner.
- Sound failure: source, filter, envelope, timing, role, or mix owner.
- Composition failure: meter, motif, harmony, section, continuity, or attention
  owner.
- Curriculum failure: distillation, indexing, retrieval, curation, or storage
  owner.

This is the difference between a training loop and a pile of scored artifacts.
The pile has numbers. The loop has memory with teeth.

## Current Doctrine For Future Music Agents

Start with a producer brief. Name the target's meter feel, tonal center or
progression, energy contour, section map, role map, and mix priorities before
touching timbre.

Use target evidence as constraints, not decoration. Tempo/autocorrelation
informs gates. Register/root/scale informs pitch lanes. Band stats inform
filters. Motion and RMS coverage inform density and arrangement.

Build separate role owners. Leads, bass, drums, pads, and texture need separate
lanes so the next agent can adjust them without opening the whole patch like a
badly packed suitcase.

Iterate locally. Render, score, inspect the candidate analysis, revise one
hypothesis at a time, and write down what changed.

Leave useful studio evidence. The next agent needs the failed trick, the kept
trick, the source path, the metrics, the listening observation, and the Aqua gap
more than it needs a paragraph saying the process was promising.

Promote less than you produce. Most candidates are pressure. A few become
patterns. Some become failure doctrine. The rest can stay as artifacts without
being invited into the house.

## Bottom Line

The music agents have not "learned music" in the grand sense. They have learned
several important humiliations, which is better. They have learned that role
names are not roles, texture is not composition, metrics are not ears, syntax is
not musicianship, and memory that cannot retrieve its own evidence is just
archive-shaped fog.

Our orchestration has learned the matching lesson: do not ask an agent to become
a musician while feeding it weak memory, hidden failures, monolithic storage,
and one-number rewards. Give it evidence, roles, iteration, curation,
retrieval, and hard negative pressure. Then let the next patch be wrong in a
newer, more informative way.
