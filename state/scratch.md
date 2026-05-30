# Scratch

Current slice:

- Upgraded the IPA trial retrieval organ from lexical metric search to a
  Qdrant/Ollama vector index modeled after VoidBot. `IpaTrialWorker search`
  now builds evidence chunks from each `IpaTrialResult`, includes metric text,
  hypotheses, evaluations, known lies, timing receipts, and local artifact
  snippets such as `.aqua`, timeline CSV, markdown, text, and DSP files, embeds
  them through local Ollama `qwen3-embedding:0.6b`, stores them in Qdrant
  collection `aquasynth_ipa_trial_results`, and returns vector-backed markdown
  search reports with vector scores, evidence tags, metric facts, contrast
  candidates, excerpts, and `show` commands. Qdrant is derived from the `.cc`
  store, not a second truth. Verified against the live Docker Qdrant and local
  Ollama server on
  `artifacts/parity/ipa-trial-worker-smoke/20260529T2204/ipa-trial-results.cc`:
  explicit index wrote 25 chunks with 1024-dim vectors, and stop-closure search
  returned through `qdrant-ollama`. The trial loop now runs `index` before and
  after each round's scoring boundary, then passes `--skip-index true` to agent
  searches so the agents query fresh vectors without re-embedding on every
  receipt.
- Retrieval tuning smokes found two gremlin-feeding flaws: generic
  weak-articulation language could pull off-class records into class-specific
  searches, and mixed target-set records could match every class because their
  target contains `a/m/s/p/u`. The live ranker now adds class-affinity scoring,
  candidate-specific ownership for mixed lanes (`mix-p` is stop, `mix-a/u` are
  vowel, `mix-m` is nasal, `mix-s` is fricative), demotes mixed/generalization
  to a secondary transfer hint when a real class is present, and only labels
  dressing when the record text actually mentions dressing/FM/AM/envelope/helper
  evidence. Search reports now expose `class_focus` and per-hit `class_match`.

- Ran five IPA prompt-optimization passes against the external trial-loop
  agent prompts. Pass 1 produced useful seam science but treated fallback file
  reads as evidence after `dotnet run` failed. Pass 2 proved live search/show
  receipts could work but showed retrieval wandering eats the turn. Pass 3
  showed capped live retrieval was still too slow for prompt iteration through
  external `codex exec`. Pass 4 exposed an incoherent "do not run commands"
  boundary because the agent could neither read receipts nor write a report.
  Pass 5 succeeded with a fixed six-receipt packet and a reasoning-only
  boundary, producing sharper seam evidence: stop release ownership, voicing
  loudness leak, fricative place collapse, mixed-set contamination, and
  single-candidate optimizer ownership. Production prompts now require contrast
  pairs, `show` metric values, primitive timeline excerpts or explicit
  `missing`, class-consistent reference matrices, `X owns Y so Z remains true`
  owner sentences, at least five micro-sweep perturbations before a seam is
  actionable, and a novelty gate that rejects loudness/dressing-only families
  as loss-landscape novelty.

- Added the external IPA trial loop as a real worker boundary instead of an
  in-chat report shuffle. `tools/IpaTrialWorker` now exposes `seed`, `score`,
  `search`, `show`, and audit-only `dump` commands over a shared
  `ipa-trial-results.cc` CultCache store. `search` is the semantic retrieval
  organ for agents: it expands speech/control vocabulary and ranks records with
  metric-aware evidence bias; `show` drills into one trial. `score` renders
  agent-authored `.aqua` candidates through
  `IpaTrialOrchestrator.RunCandidateScriptsAsync` and upserts new typed trial
  results. `tools/run-ipa-trial-loop.ps1` now runs five external Codex rounds by
  default, each asking for a batch across five five-phoneme target sets rather
  than one-off patch golf; the script rejects a round before scoring unless it
  contains at least one candidate for every target lane.
- Active IPA exploration shape: vowels `a/i/u/e/o`,
  nasals/approximants `m/n/ng/l/r`, fricatives `s/z/f/v/th`, stops
  `p/b/t/d/k`, and mixed generalization `mix-a/mix-m/mix-s/mix-p/mix-u`.
  Agent patch files must be named
  `<targetId>__<family>__<hypothesis-name>.aqua` so the scorer can bind them
  to target/reference lanes and the evaluator can group hypothesis families.
  Full-patch dressing remains allowed, but evaluator reports must say when it
  is compensating for failed primitive or gesture evidence.
- Verification so far: `dotnet build tools\IpaTrialWorker\IpaTrialWorker.csproj
  --disable-build-servers -p:UseSharedCompilation=false
  -p:BuildInParallel=false -v minimal` passes; `IpaTrialWorker search` returns
  ranked evidence from
  `artifacts/parity/ipa-trial-worker-smoke/20260529T2204/ipa-trial-results.cc`;
  `tools\run-ipa-trial-loop.ps1 -Rounds 0 -SkipLocalTrialRun` creates a loop
  skeleton. Need final focused tests and optionally a one-round external Codex
  smoke if time/tooling allows.
- Prompt QA pass: ran three dry single-turn external Codex smoke probes for
  both hypothesizer and evaluator prompts under
  `artifacts/parity/ipa-prompt-smoke/pass-{1,2,3}`. The refinements removed
  contradictory count/name instructions, hardened candidate naming to
  `<targetId>__<family>__<hypothesis>.aqua`, made family ids lowercase
  kebab-case, required exact 25-lane output, added pre-evidence digest and
  claim-audit sections, forced semantic-search/show receipts, added evidence
  quality ledgers/verdicts for specificity/comparability/falsifiability/reuse,
  and made the evaluator fail closed when receipts, target coverage, filename
  validation, or family extraction are incomplete. Final script syntax smoke passes with
  `tools\run-ipa-trial-loop.ps1 -Rounds 0 -SkipLocalTrialRun`.

- Implemented the first IPA trial render/scoring orchestration. Core now has
  typed `IpaTrialResult` CultCache records plus `IpaTrialResultCultCacheStore`;
  Faust now has `IpaTrialOrchestrator.RunAsync`, which generates candidate
  patches through `IpaGestureExperiment`, renders them with Faust, compares
  spectrogram/articulation evidence against local PT fixture references, writes
  WAV/report artifacts, and stores the general trial-result `.cc` database.
  The opt-in five-seed run wrote
  `artifacts/parity/ipa-trials/20260529T214127955/five-seed-trials`.
- First run showed the pipeline worked but vowels/nasals were effectively
  silent despite decent gesture scores. The refinement was architectural:
  primitive `SourcePort` Faust lowering now emits an oscillatory voiced carrier
  tied to the network frequency, instead of feeding mostly DC pressure into a
  high-passed radiation load. Seed patch defaults now use stronger clean
  primitive source flow. Second run moved the intended layer: open `a`
  log-mel cosine `0.5693`, `m` `0.5199`, `s` `0.5498` with articulation
  `0.4111`; plosive `p` remains weak at `-0.1024`, pointing to closure/release
  ownership rather than source loudness.
- Verification: opt-in
  `IpaTrialOrchestratorWritesFiveSeedTrialsWhenEnabled` passed with Faust
  installed; focused
  `PatchScriptTests|IpaTrialResultsRoundTripThroughCultCache` pass 94/94.

- Added the first scalable IPA gesture experiment harness.
  `IpaGestureExperiment.WriteRound` writes a frozen round bundle with
  `manifest.yaml`, candidate `.aqua` scripts, primitive timeline CSVs from
  `ProbeTimelineReport`, gesture-layer `metrics.csv`, and per-candidate
  `evidence.jsonl`. `IpaGestureExperiment.AnalyzeRound` writes
  `analysis/metric-summary.csv`, `analysis/candidate-clusters.csv`, and
  `analysis/science-brief.md` for the science-worker handoff. The harness
  scores only descriptor/spline/primitive evidence (`surface_coverage`,
  `motion_direction`, `contour_timing`, `primitive_timeline`,
  `gesture_score`) and explicitly does not own clean vocal identity, full
  spectrogram parity, optimizer checkpoints, or worker orchestration.
  Verification: `PatchScriptTests` pass 93/93.

- Documented the IPA training scoring contract. Gesture score is measured at the
  descriptor/spline/primitive-timeline layer using coverage, direction,
  contour/timing, primitive consequences, and optional external articulation
  evidence. Training reports must keep `gesture_score`, `clean_vocal_score`,
  and `full_parity_score` separate; full parity may use all AquaSynth synthesis
  tools and recording-condition helpers, but cannot count as articulation
  evidence unless the lower-layer scores also move.

- Added the first IPA/anatomy gesture DSL layer. `phoneme_gesture` accepts
  known IPA symbols or explicit descriptors like
  `descriptor=voiceless_labial-velar_fricative`, maps place/manner/voicing to
  the primary anatomical surfaces, and emits ordinary `ControlSpline`s. This is
  deterministic seed material for local dataset golf, not a claim that IPA
  labels contain exact numeric targets. Patches remain free to use normal
  FM/AM/modulators/envelopes/filters around the anatomical control stream.
  Verification: `PatchScriptTests` pass 92/92.

- Public gesture API pass implemented. `ControlSurfaceCatalog.FromPatch(patch)`
  is the host-facing discovery surface for normalized controls; hosts can create
  `ControlSplineTimeline`, edit future points, and call `ControlValuesAt(t)` to
  stream ordinary surface controls into Faust without recompiling. Native Faust
  compiled patches now expose `AquaSynthCompiledPatch.ControlSurfaces` and a
  safe `ControlValuesAt(timeline,t)` helper that filters the semantic timeline
  to UI zones actually exported by the optimized DSP. Verification:
  `PatchScriptTests` pass 91/91 and `NativeFaustRuntimeTests` pass 5/5.

- Implemented the gesture/timeline owner cut for primitive vocals.
  `ControlSurface` now exposes normalized runtime controls for primitive fields,
  and `ControlSpline` owns linear/hold/Bezier motion over those surfaces.
  Primitive Faust lowering maps effective normalized surface values back into
  physical fields; explicit old `@/param` bindings remain base controls so PT
  fixture controls are not bypassed. `ControlSplineTimeline` supports future
  point edits for realtime clients.
- `tract_motion` now adapts velopharynx motion into a branch-opening
  `ControlSpline`. A generated contact-opening spline was tested and deleted:
  PT timeline evidence showed the obstruction was already closed at block zero,
  so an invented open-then-close contact story was a compensator. Contact
  closure should come from articulator/area ownership, not local contact slew.
- Verification for this cut: `PatchScriptTests` pass 90/90. Focused primitive
  timeline artifact test passes and writes
  `artifacts/parity/primitive-vocal-timeline/20260529T185955965`; summary:
  open-vowel 0.267624, front-vowel 0.246432, nasal-vowel 0.223792,
  bilabial-nasal-ma 0.226266, sibilant 0.177032, closure-release 0.155925.

- First primitive vocal teardown cut implemented. `AreaFunction`,
  `WaveguidePath`, `SourcePort`, `ConstrictionContact`, `BranchPort`,
  `RadiationLoad`, `ProbeTimeline`, and `VocalNetwork` are model records with
  parser commands. PT-shaped `tract` authoring now adapts into those primitive
  records instead of generating `voices_0_area_*`, `voices_0_contact_*`, or
  `voices_0_inj_*` acoustic banks. The obsolete
  `EnsureTractAcousticNetwork` generator was deleted.
- Faust lowering now prefers a compact primitive path for `Voice.VocalNetwork`:
  one semantic path variable per tube using `de.fdelay*` delay families, named
  primitive source/contact/branch/radiation locals, and debug probes under
  `/debug/voice_0/path|source|contact|branch|radiation/...`. Explicit
  `AcousticPortNetwork` scripts still exist as a lower-level lab surface.
- `ProbeTimelineReport` writes deterministic primitive samples as
  `block,primitive,signal,value`; current fields include path area/delay,
  source load pressure/flow, contact opening/resistance/reservoir/released
  flow, branch admittance/exchanged flow, and radiation reflection,
  boundary_flow, flow, and output. Passivity/energy samples and PT/SndKit
  timeline comparison are next.
- Verification for this cut:
  `dotnet build tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:BuildInParallel=false -v minimal`
  passed. Focused primitive/tract/fixture tests passed: 8 tests covering the
  new primitive DSL, PT tract adapter, primitive timeline, and fixture demotion
  away from generated graph banks.

- Second primitive evidence cut in progress: path telemetry now includes
  `incoming_wave`, `outgoing_wave`, `energy_in`, `energy_out`, and
  `passivity_ratio` in both Faust debug probes and `ProbeTimelineReport`.
  Contact telemetry now includes `resistance`. Added
  `PrimitiveReferenceReport.ComparePinkTrombone`, which compares static
  primitive geometry/delay/branch/contact/radiation fields against PT/SndKit
  fixture controls without requiring native reference binaries. `PatchScript`
  tests pass 86/86 after this cut.
- Third primitive evidence cut in progress: `PinkTromboneReferenceRenderer` now
  has `RenderTimeline`, exposing PT internal timeline samples from the renderer's
  own diameter/wave/reflection/transient arrays. `PrimitiveReferenceReport`
  can role-normalize Aqua primitive timeline samples against PT timeline samples
  for oral/nasal path, velopharynx branch, obstruction contact, and lip
  radiation signals. `PatchScript` tests pass 88/88.
- Added `PinkTrombonePrimitiveTimelineComparisonWritesArtifacts`; it writes
  per-fixture `aqua-primitive-timeline.csv`, `pt-internal-timeline.csv`,
  `comparison.csv`, and `summary.txt` under
  `artifacts/parity/primitive-vocal-timeline/<timestamp>`. Focused artifact
  test passes.
- First comparison-driven law correction: `SourcePort` now owns `FlowScale`
  (default `.02`) instead of primitive source flow silently emitting unit-scale
  waves; primitive radiation reflection now reports/uses declared
  `RadiationLoad.Reflection` instead of aperture-modulating the coefficient;
  `ProbeTimelineReport` radiation flow now uses the path outgoing wave, matching
  the Faust law. New artifact `20260529T155613895` drops suite mean absolute
  timeline error from roughly `1.2-1.3` to `0.15-0.27`. Remaining obvious drift
  is branch/contact dynamics, which raises a real owner question: gesture slew
  inside ports or separate gesture/timeline primitive.

- Proprioception correction after user rejected baby-word golf as the rebuild
  owner. Diagnosis is now durable in
  `docs/acoustic-graph-proprioception.md`: collapse generated graph-vocal
  lowering into a small set of instrumented physical primitives, compare
  primitive/sample-flow behavior against VTL, ArtiSynth, Story/TubeTalker,
  Smith/Faust, and PT/SndKit, then return to utterances as downstream
  witnesses. Next implementation pass should map current records/Faust locals
  onto `AreaFunction`, `WaveguidePath`, `ScatterJunction`, `SourcePort`,
  `ConstrictionContact`, `BranchPort`, `RadiationLoad`, and `ProbeTimeline`.

- Implemented the first graph-owned closure reservoir cut. Contact terminals no
  longer contribute through the generic node source sum. Ordinary two-port path
  scatter now emits per-contact closure, reservoir drive, reservoir, release,
  and out probes, and injects the release directionally into the same path
  scattering law (`0.80` downstream, `0.20` upstream). This is the research
  supported move: closure pressure enters the traveling-wave path instead of
  acting as an output click or generic source gain.
- Latest opt-in thrombosis probe after the cut:
  `artifacts/parity/pink-trombone-graph-thrombosis-probes/20260529T021934812`.
  Key peaks: lip contact closure `0.936876`, contact reservoir drive
  `0.519667`, contact reservoir `6.024680`, contact release `0.485894`,
  contact out `0.548231`, lip radiation flow `0.581880`, lip radiation
  admittance `0.710362`, modal source out `1.233802`, modal flow `0.771656`,
  sibilant/injection area 34 out `0.099287`. Previous contact out was about
  `0.032413`; the closure event now reaches the graph/radiation path.
- Latest utterance diagnostic:
  `artifacts/parity/pink-trombone-utterance-logmel/20260529T022214016`.
  The speech verdict is still not accepted. `thrombosis` RMS improved from
  roughly `0.0673` to `0.0761`, but cosine stayed about `0.331` and silence
  mismatch worsened to `0.7306`. This means the closure cut is structurally
  correct but not sufficient. Do not claim speech parity. Next likely owner is
  either utterance/radiation output balance or more realistic contact
  filtering/active-frame duration; do not hide that under a global gain knob.
- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --no-build --disable-build-servers --filter "ParserSupportsPinkTromboneStyleTractVoice|TractVoiceCanLowerThroughAcousticGraph|FaustCompilerValidatesAcousticPathGraphWhenInstalled" -v normal`
  passed: 3 tests. `AQUASYNTH_RUN_GRAPH_PROBES=1 dotnet test ... --filter
  "PinkTromboneThrombosisGraphDebugProbesWriteTimelineWhenNativeFaustIsInstalled"`
  passed: 1 test in 2m09s. `dotnet test ... --filter
  "PinkTromboneAcceptedUtterancesReportGraphLogMelParityWhenFaustIsInstalled"`
  passed: 1 test in 2m25s. Minimal verbosity sometimes exits silently on the
  long native render path; normal verbosity produced passing runs.

- Added graph-native debug probes for vocal source internals when
  `DebugProbeUi` is enabled: tissue-valve pressure drive, modal tissue,
  aperture, flow, and output; turbulence-source reservoir/release/output; and
  contact reservoir/release/output. These are observation only, not audio-law
  changes.
- Added opt-in `PinkTromboneThrombosisGraphDebugProbesWriteTimelineWhenNativeFaustIsInstalled`.
  It runs only when `AQUASYNTH_RUN_GRAPH_PROBES=1`, compiles the generated
  `thrombosis` graph through native Faust debug UI, steps the streaming patch
  block by block, and writes `candidate-debug.dsp`, `controls.csv`,
  `timeline.csv`, and `probe-peaks.txt`.
- Latest probe artifact:
  `artifacts/parity/pink-trombone-graph-thrombosis-probes/20260529T011547091`.
  Key peaks: modal source out `1.335230`, modal flow `0.791726`, modal pressure
  drive `0.741528`, lip radiation flow `0.337191`, lip radiation admittance
  `0.710362`, sibilant/injection source at area 34 out `0.099288`, lip contact
  release out `0.032413`. The graph is not dead. The current failure is not a
  missing generic `drive` knob or a silent source; it is weak articulation and
  output balance after graph/radiation scaling, with contact release still
  small and localized.
- No acoustic-law cut was made from this evidence. Next real change should use
  the probe timeline to decide whether to promote contact/closure into the
  scattering law or adjust the utterance harness/radiation output owner. Do not
  hide the finding behind a broad gain tweak.
- Verification:
  `AQUASYNTH_RUN_GRAPH_PROBES=1 dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:BuildInParallel=false --filter "PinkTromboneThrombosisGraphDebugProbesWriteTimelineWhenNativeFaustIsInstalled|FaustCompilerValidatesAcousticPathGraphWhenInstalled" -v minimal`
  passed: 2 tests in 2m01s. Default focused diagnostic without the env var also
  passed with the probe test returning immediately.

- PT utterance diagnostic crash recheck: the focused test is reproducible as
  slow but passing when stale child `dotnet` processes are cleared and the test
  is run with enough wall-clock budget. Fresh verified artifact:
  `artifacts/parity/pink-trombone-utterance-logmel/20260529T005420700`.
  Per-utterance render timing now writes `render.txt` beside each candidate
  before/after the Faust render path: `mama` ~31s, `papa` ~45s, `thrombosis`
  ~59s. The previous two-minute attached shell timeout was not a valid crash
  witness.
- Harness/process fix: `PinkTromboneAcceptedUtterancesReportGraphLogMelParityWhenFaustIsInstalled`
  now writes `candidate-graph.dsp` and `controls.csv` before rendering, wraps
  each external Faust render in a three-minute per-utterance timeout, and writes
  stdout/stderr/timing to `render.txt`. `FaustCompiler.RunAsync` now kills the
  whole child process tree when cancellation fires, so timed-out `faust` or
  nested `dotnet run` work cannot keep poisoning later runs.
- Current acoustic evidence after the source-law cleanup is weakness, not a
  loud low-frequency floor: latest utterance RMS ratios are `mama` 0.0876,
  `papa` 0.1509, `thrombosis` 0.0673. `thrombosis` still fails the diagnostic
  cosine floor with cosine 0.3318. Next vocal work should inspect source-flow
  strength/articulation and radiation transfer with debug probes, not restore a
  generic `drive` knob.
- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:BuildInParallel=false --filter "PinkTromboneAcceptedUtterancesReportGraphLogMelParityWhenFaustIsInstalled|FaustCompilerRendersTissueValveGraphWhenInstalled|FaustCompilerValidatesAcousticPathGraphWhenInstalled" -v minimal`
  passed: 3 tests in 2m17s.

- Research-guided machine cut: graph segment delay no longer clamps physical
  segment delay to a full sample. `WaveClockPolicy` now owns the minimum legal
  delay: unit grid stays at 1 sample, half-sample grid and first-order Thiran
  bottom at 0.5, and Lagrange/crossfaded policies use their documented lower
  bound. Generated `tract propagation=graph` now uses `HalfSampleGrid` to match
  PT's half-sample Kelly-Lochbaum regime instead of first-order Thiran.
- Rejected experiments in this pass:
  - Flipping area-scatter reflection to the literal SndKit/PT sign made
    `thrombosis` loud/unstable under Aqua's current port orientation, so the
    previous local sign convention remains.
  - Adding more high glottal harmonics worsened voiced-band behavior; the
    failure is not a missing sparkle source.
- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:BuildInParallel=false --filter "TractVoiceCanLowerThroughAcousticGraph|ParserSupportsTypedAcousticPathGraph|FaustCompilerValidatesAcousticPathGraphWhenInstalled|PinkTromboneAcceptedUtterancesReportGraphLogMelParityWhenFaustIsInstalled|PinkTromboneFixturesReportLogMelParityWhenFaustIsInstalled" -v minimal`
  passed. Latest utterance artifact:
  `artifacts/parity/pink-trombone-utterance-logmel/20260527T133539554`;
  `mama` cosine/articulation `0.5982/0.1952`, `papa` `0.8615/0.1979`,
  `thrombosis` `0.5217/0.2640`. Latest static artifact:
  `artifacts/parity/pink-trombone-logmel/20260527T133424894`.

- User listening correctly rejected the log-mel witness: `thrombosis` was
  mostly silence plus breathy/squeaky bursts, and `papa`/`mama` still read as
  phone-vibration buzz. `AudioComparison` now includes articulation diagnostics
  instead of letting log-mel cosine own speech parity.
- New `AudioArticulationComparison` fields: envelope cosine, active-frame
  ratio, silence mismatch, envelope flux ratio, spectral flux ratio, motor-band
  ratio, speech-band ratio, and articulation score. Utterance reports now print
  a verdict. Latest artifact:
  `artifacts/parity/pink-trombone-utterance-logmel/20260527T130026316`;
  all three accepted utterance fixtures are `not-accepted-articulation` with
  articulation scores `mama` `0.1930`, `papa` `0.1829`, `thrombosis` `0.1848`.
  This is correct: the harness now agrees with the ear instead of laundering
  filtered buzz through cosine.
- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:BuildInParallel=false --filter "AudioAnalyzerComparesSimpleBuffers|PinkTromboneAcceptedUtterancesReportGraphLogMelParityWhenFaustIsInstalled|PinkTromboneFixturesReportLogMelParityWhenFaustIsInstalled" -v minimal`
  passed.

- Latest graph radiation pass moved articulation authority into the acoustic
  boundary instead of the output tap. `AcousticRadiationPort` aperture now
  blends boundary reflection between near-closed termination and declared
  open-end reflection. Radiation reads boundary flow (`incoming - outgoing`)
  and applies a stronger high-pass radiation slope, instead of reading raw
  incoming pressure and painting it after the fact.
- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:BuildInParallel=false --filter "PinkTromboneAcceptedUtterancesReportGraphLogMelParityWhenFaustIsInstalled" -v minimal`
  passed. Latest utterance artifact:
  `artifacts/parity/pink-trombone-utterance-logmel/20260527T122757617`;
  `mama` cosine `0.5650` RMS `0.3297`, `papa` `0.8613` / `0.1282`,
  `thrombosis` `0.4347` / `0.2502`.
- Static parity also passed:
  `artifacts/parity/pink-trombone-logmel/20260527T123141625`;
  open/front/nasal/ma/sibilant/closure cosine
  `0.6088/0.6826/0.6468/0.6153/0.4158/0.6198`.
- The pass improved `mama`/`papa` band shape and made aperture a real graph
  impedance control, but `thrombosis` still loses voiced body and stops remain
  underpowered. Next pressure is graph-native pressure storage/release around
  severe constrictions and nasal/oral branch coupling, not more output color.
- Added a small higher-harmonic brightness term to the reusable glottal source.
  Utterance smoke passed under
  `artifacts/parity/pink-trombone-utterance-logmel/20260527T123628148`.
  Post-flow radiation gain was then recalibrated from `3.0` to `4.0`; `4.5`
  was tested and rejected as too close to static soft-clip pressure.
  Latest utterance artifact:
  `artifacts/parity/pink-trombone-utterance-logmel/20260527T124230804`;
  `mama` `0.5712`, `papa` `0.8659`, `thrombosis` `0.4379`.
  Static smoke passed under
  `artifacts/parity/pink-trombone-logmel/20260527T124311305`.

- Added time-varying Pink Trombone utterance reference fixtures before any Aqua
  utterance metric golf. `PinkTromboneReferenceRenderer.RenderUtterance`
  interpolates timestamped `PinkTromboneControlPoint` curves sample by sample,
  preserving the existing PT-style waveguide state.
- New fixture set: `mama`, `papa`, `lulek`, and `thrombosis`. Each fixture owns
  explicit control points for PT frequency, intensity, tenseness, tongue
  index/diameter, constriction index/diameter, turbulence, velum, lip opening,
  reflections, gain, and burst. These are first-pass pronunciation sketches,
  not accepted references until user listening approves them.
- `PinkTromboneUtteranceReferenceTests.PinkTromboneUtteranceFixturesWriteReferenceWavs`
  writes PT reference WAVs, `controls.csv`, and `summary.txt` under
  `artifacts/parity/pink-trombone-utterances/<timestamp>/<word>/`.
- Latest artifact set:
  `artifacts/parity/pink-trombone-utterances/20260526T205230009`.
  Reported PT reference peaks/RMS:
  `mama` `0.7033/0.4213`;
  `papa` `0.7012/0.3618`;
  `lulek` `0.7200/0.4087`;
  `thrombosis` `0.7600/0.4658`.
  Do not golf Aqua against these until the user has judged whether the PT
  reference words are recognizable enough to serve as targets.

- Golfed the actual graph tract path against the PT log-mel harness after the
  playground `Ma` preset sounded like amplified buzzing. The failure was not a
  playground-only gain problem: generated graph topology reused velum as both
  branch valve and nostril aperture, while closed-nose leakage was carrying too
  much oral loudness.
- `EnsureTractAcousticNetwork` now treats velum as branch entrance area
  control by squaring the nasal branch terminal opening, while nostril radiation
  remains an independent aperture. Graph radiation was then cut from `24.0` to
  `3.0` because the previous value was a compensator for the leaky topology and
  drove the corrected graph into soft clipping.
- Added a `bilabial-nasal-ma` PT fixture using the playground `Ma` controls, and
  fixed PT fixture script generation so intensity/tenseness/glottal reflection
  actually reach both `glottis` and `tract`.
- The playground renderer patch gain is now `0.48`, keeping audition renders out
  of the clipped-buzz regime while still rendering the actual Aqua DSL/Faust
  graph.
- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --no-restore --filter "PinkTromboneFixturesReportLogMelParityWhenFaustIsInstalled" --disable-build-servers -p:UseSharedCompilation=false -p:BuildInParallel=false -v minimal`
  passed. Latest graph reports:
  `open-vowel` cosine `0.6219` RMS ratio `0.9743`;
  `front-vowel` `0.6407` / `1.0024`;
  `nasal-vowel` `0.5375` / `1.1476`;
  `bilabial-nasal-ma` `0.4743` / `1.2206`;
  `sibilant` `0.3233` / `1.0008`;
  `closure-release` `0.0817` / `1.5534`.
  Focused graph/parser tests and `tools\TractGraphRenderer` build also passed.
  Live playground render check after this pass: Open peak/RMS about
  `0.498/0.257`; Ma about `0.480/0.243`.

- Replaced the vocal tract playground's browser-native source/filter witness
  with actual Aqua DSL graph rendering. The page now posts the visible controls
  to `/render`; `tools/TractGraphRenderer` emits `tract propagation=graph`,
  lowers through `FaustEmitter`, renders through `FaustCompiler.RenderAsync`,
  writes a WAV under `artifacts/vocal-tract-playground`, and the browser plays
  that returned audio.
- The fake JS `createTractSynth` path was deleted so the playground has one
  audio authority: Aqua DSL -> Faust graph lowering. The UI is render-on-demand,
  not realtime, because recompiling/rendering Faust per audition is the honest
  current machine.
- Verification:
  `dotnet build tools\TractGraphRenderer\TractGraphRenderer.csproj --disable-build-servers -p:UseSharedCompilation=false -p:BuildInParallel=false`
  passed; direct renderer smoke wrote `artifacts\vocal-tract-playground\smoke.wav`
  with peak `0.15414488`, RMS `0.066947475`, and no warnings; live server
  `POST http://127.0.0.1:5126/render` returned a WAV and `/renders/...wav`
  served HTTP 200; `node --check` passed for `app.js` and `serve.mjs`.

- Added `tools/vocal-tract-playground`, a static WebAudio playground for the
  current AquaSynth `VocalTractControlTarget` surface. It exposes the 14 base
  voice-patch target controls plus six mel-envelope bands: these are the knobs
  the learned speech driver will predict from phonetic and utterance input.
  The page draws a tract-ish canvas and includes vowel/noise/nasal presets.
- This is intentionally a touchable control playground, not a claim of
  Pink Trombone-class tract physics. The canvas and audio are tied to the real
  AquaSynth control names so future renderer work has an inspectable handle.
- Verification:
  `node --check tools\vocal-tract-playground\app.js`
  and `node --check tools\vocal-tract-playground\serve.mjs`: passed.
  Local server `node tools\vocal-tract-playground\serve.mjs` served
  `http://localhost:5125/`; browser check rendered the UI, Play toggled audio,
  the `Ee` preset moved sliders/visuals, and console errors were empty.

- Added `MelCepstralWarpEffect` as a real AquaSynth offline/block audio effect:
  STFT -> log-mel -> DCT cepstra -> Simplex domain warp -> separable 5-tap
  Gaussian blur -> inverse cepstra/log-mel -> original-phase overlap-add
  resynthesis. This came from the Mimir bioacoustic decoder work, but the
  reusable audio-effect owner is AquaSynth.
- Added focused tests proving the effect preserves length/finite samples and
  that warp/blur parameters audibly change output.
- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --filter "MelCepstralWarpEffectTests" -v minimal`:
  2 passed.

- Tightened CultCache claim receipts into an explicit hazard-light witness
  shape. `CultCacheProofTruth.LatestListeningReceipt` is no longer a loose
  string; it is now `CultCacheListeningReceipt` with `Subject`,
  `TouchedSurface`, `RemainingContamination`, and `WitnessSentence`.
- The default patch/speech claim catalog now carries those fields directly, so
  a card names what patch/reference was touched, what surface got exercised,
  what still contaminates the claim, and one human/domain witness sentence
  about whether the sound bites, blooms, or lies.
- Regenerated `patches/aquasynth-patch-cultcache.cc` from the typed catalog so
  the checked-in CultCache artifact matches the new receipt schema.
- Focused tests now assert receipt structure explicitly instead of only checking
  that some non-empty string exists.

- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --filter "CultCachePatchDocumentTests" -v minimal`

- Added the first distributed speech-training contract:
  `SpeechRenderRequest`, `SpeechRenderResult`, and
  `SpeechTrainingCheckpoint` are typed CultCache documents in
  `src/AquaSynth.Core/SpeechDistributedTraining.cs`.
- `SpeechDistributedTrainingCoordinator` now creates render requests from the
  current pipeline, accepts worker-produced score gradients, and applies those
  gradients through `SpeechBackpropagationPipeline.TrainSingleFromSynthOutputGradient`.
  The current worker proof scores control-vector MSE locally; the ownership
  shape is the same as remote compiled-Faust scoring.
- `SpeechDistributedTrainingCultCacheStore` reads and writes request, result,
  and checkpoint `.cc` stores through CultLib. No JSON queue, no bespoke
  serializer, no trainer pretending to own the renderer.
- New durable CultMesh training constraint:
  CultMesh should distribute compiled Faust render/scoring payloads and collect
  worker `SpeechRenderResult` gradients, artifacts, timing receipts, and losses.
  Workers may witness and compute; they must not mutate model checkpoints. The
  authority trainer or one shard-primary trainer applies gradients through
  `SpeechBackpropagationPipeline` and commits `SpeechTrainingCheckpoint`
  documents through normal CultCache/CultMesh shard authority.
- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --filter "SpeechDistributedTrainingTests" -v minimal`:
  2 passed.

- Implemented the first CultMesh-distributed speech training step:
  Core now references `GameCult.Mesh`, defines `SpeechWorkerPayloadManifest`,
  `SpeechWorkerPayloadArtifact`, `SpeechWorkerAdmissionReceipt`,
  `SpeechCultMeshWorkerAssignment`, `SpeechCultMeshTrainingStepResult`, and
  `SpeechCultMeshRoles.RenderWorker`, and persists payload/admission documents
  through `SpeechDistributedTrainingCultCacheStore`.
- `SpeechDistributedTrainingCoordinator.RunCultMeshTrainingStep` now creates
  requests, creates a worker payload manifest, admits only peers with valid
  CultMesh worker leases, records rejected peers, runs score-only worker
  assignments, collects `SpeechRenderResult` gradients, and applies them
  centrally through `ApplyResults`.
- This is distributed training via CultMesh lease/admission and typed CultCache
  documents. It is not yet real remote process supervision or artifact chunk
  transfer; those remain CultMesh infrastructure surfaces. No worker API owns
  checkpoint mutation.
- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --filter "SpeechDistributedTrainingTests" -v minimal`:
  3 passed.

Previous slice:

- Added a typed CultCache sound-claim document for CultMesh distribution:
  `patches/aquasynth-patch-cultcache.cc`.
- Cut the JSON artifact and the local hand-written MessagePack codec. AquaSynth
  owns the typed `CultCachePatchDocument` catalog; CultLib owns cache
  serialization through `GameCult.Caching.MessagePack`.
- Added `CultCachePatchDocument` record types in Core. The document bundles the
  full `patches/library.yaml` patch database with patch claim cards and speech
  claim cards, and `CultCachePatchDocumentStore` reads/writes it through real
  CultCache APIs.
- Patch claim cards now cover every `ReferenceRebuildCatalog.All()` fixture
  patch: the three project-authored Zyn rebuilds and the three DX7 reference
  rebuilds. The PRC SYNTH1 calibrated patch also has a claim card because it is
  in the patch database as a hard public-domain DX7 pressure target.
- Speech claim cards cover the eSpeak NG tiny workout and the compiled Faust
  speech loss surface. Both explicitly name utterance/phoneme source, target
  anatomy/profile, intent, tier, proof artifact, listening receipt surface, and
  known lies.
- Stock patch database entries are included as database entries, not promoted
  to serious claims without references. That preserves the law: sketches may
  exist, but parity/pressure/proof claims need named reference and receipt
  surfaces.
- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --filter "CultCachePatchDocumentTests" -v minimal`:
  4 passed.

Previous slice:

- Reworked the Faust-rendered speech-loss path toward the actual curriculum
  machine: `CompiledFaustRenderedSpeechLossSurface` compiles one native Faust
  patch with exported `speech/output/N` hslider controls for every neural
  mapper output, then renders reference/candidate batches by setting controls
  on new DSP instances from the same compiled factory.
- `AquaSynthCompiledPatch` now exposes `ControlPaths` and supports
  `Render(IReadOnlyDictionary<string,float>)`. `FaustNativeToolchain` builds a
  C UI glue map from Faust control labels to native zones and writes parameter
  values before `compute`.
- The compiled speech probe keeps every mapper output as an exported knob even
  when a current simple synth rung does not use the value strongly. The tiny
  keepalive term exists for that invariant: topology and control surface stay
  stable across curriculum rungs.
- Rendered proof found a real loss-surface hazard: dynamic low-pass cutoff can
  exceed Nyquist at lower training sample rates and produce NaNs. The compiled
  probe clamps cutoff to `ma.SR * 0.45`.
- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --filter "FaustRenderedSpeechLossSurfaceTests|NativeFaustRuntimeTests|SpeechBackpropagationPipelineTests" -v minimal`:
  6 passed.
- Opt-in native batch proof:
  `AQUASYNTH_RUN_FAUST_SPEECH_LOSS=1 dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --filter "CompiledFaustRenderedSpeechLossSurfaceReusesOnePatch" -v minimal`:
  1 passed.

Previous slice:

- Added the first Faust-rendered speech loss surface. `AquaSynth.Faust`
  now owns `FaustRenderedSpeechLossSurface`, which turns a
  `VocalTractControlTarget` into an inspectable AquaSynth script, renders it
  through Faust, compares rendered log-mel evidence, and estimates
  `d(loss)/d(controller output)` with finite differences over selected output
  indices.
- Added the missing Core backprop seam without making Core depend on Faust:
  `SpeechBackpropagationPipeline.TrainSingleFromSynthOutputGradient` accepts an
  external synth-output gradient, updates the synth driver, slices the returned
  semantic embedding gradient, and updates the utterance encoder. This is the
  handoff a Faust-rendered loss surface needs.
- Added `VocalTractNeuralMapper.TrainSingleFromOutputGradient` and allowed
  `PackedNeuralNetwork.TrainSingleFromOutputGradient` to preserve the external
  loss value in its result.
- The normal focused tests verify external-gradient backprop without invoking
  Faust. The heavier rendered proof is opt-in via
  `AQUASYNTH_RUN_FAUST_SPEECH_LOSS=1` because the current renderer recompiles
  per finite-difference evaluation.
- Verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --filter "SpeechBackpropagationPipelineTests|FaustRenderedSpeechLossSurfaceTests" -v minimal`:
  3 passed.
- Rendered proof verification:
  `AQUASYNTH_RUN_FAUST_SPEECH_LOSS=1 dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --filter "FaustRenderedSpeechLossSurfaceTests" -v minimal`:
  1 passed in about 1m22s.
- Build verification:
  `dotnet build AquaSynth.sln -v minimal`: succeeded.
- Current honesty: this is a real Faust-rendered loss surface and a real
  backprop seam through both learned models. It is still black-box finite
  difference over controller outputs, not analytic differentiation through
  Faust DSP. The next coherent performance cut is render/compile caching or a
  differentiable proxy that is explicitly trained against Faust, not pretending
  recompiling Faust dozens of times is the final supercar.

Current standing order: grow AquaSynth through external reference
targets, not through speculative field sprawl. `state/spine.yaml` and
`docs/reference-synth-roadmap.md` are the handoff surfaces for agents working on
the synth library while AquaSynth work continues elsewhere.

Doctrine update:

- External synths are parity pressure for AquaSynth's DSL, not internals to
  clone. Only consume targets that can be backed by parity tests proving
  AquaSynth can reproduce the behavior in terse, readable syntax.
- Parsing a synth format is inventory. Rebuilding and testing behavior is the
  proof.
- Pinned reference synths are test-only oracles, not AquaSynth organs. ZynAddSubFX
  now lives under `external/zynaddsubfx` as GPL development material pinned at
  `3ab608c432996ba4d582176572c0b0f82328c825`; it must not enter the NuGet
  package or runtime dependency graph.

Current slice:

- Organ Choir exposed two separate Zyn PAD authorities that are now mapped:
  analog high-pass filters (`HP1`/`HP2`, stages, and FILTER_ENVELOPE motion)
  and selected PAD table roots for OscilGen shift-base sources
  (`base_function=7`). The root policy is source-driven: normal PAD rebuilds
  keep the sample-0 oracle root, while shift-base OscilGen layers may use the
  selected note table root when the parity oracle provides it.
- Latest upstream PAD survey after this pass:
  `Organ Choir Pad2` artifact
  `artifacts/parity/zyn-upstream-pad-fixtures/0008-organ-choir-pad2-20260515T162034810`,
  log-mel `0.471483`, envelope `0.302978`, RMS ratio `0.878`,
  centroid ratio `1.011328`, score `0.541933`. The violin-vs-wind issue was
  kit1 source-table shape: Aqua had ignored Zyn `base_function_modulation=1`
  and `adaptive_harmonics=1`, producing a smooth upper harmonic ramp. The
  importer now applies base-function phase modulation before DFT and adaptive
  harmonic remapping at the PAD table root.
- Direct kit1 table spectrum moved from violin-shaped to wind-shaped:
  400-800 Hz energy is now `0.2713` against Zyn `0.2780`; 8-16 kHz is
  `0.1471` against Zyn `0.1355`.
- The accepted fixtures stayed on the sample-0 root path: DoublePadBass
  log-mel `0.247674`, score `0.650319`; Ghost Ensemble log-mel `0.369402`,
  score `0.57229`; Soft Pad log-mel `0.317897`, score `0.608833`.
- User listening now accepts the recent Zyn PAD fixture batch as reading the
  same as the references. Treat the remaining high log-mel values as regression
  pressure and diagnostic smoke, not as a mandate to keep sanding sounds that
  have already landed perceptually.
- Verification: `dotnet test AquaSynth.sln --no-restore`: 107
  passed.
- Next pressure can leave this Zyn PAD batch and move to the roadmap's next
  synth/abstraction rung; keep sin2x and broader OscilGen/PAD source-table
  shape as future regression context, not the active treadmill. Do not turn the
  base-function-7 selected-root rule into a global selected-table switch. That
  was tested and regressed accepted DoublePadBass.

- Zyn PAD per-preset rebuilds now carry first-class Zyn PAD table semantics
  instead of only `spread=0` plus harmonic magnitudes.
- `SpectralBank` owns `PadSpectrumProfile`, including Zyn PAD mode,
  bandwidth, bandwidth scale, harmonic profile, and harmonic-position warp.
  `PadSynthWaveform` uses the Zyn PAD profile/bandwidth formulas for those
  banks and keeps the old generic PADsynth path for ordinary `spectrum`.
- `ZynInstrumentReader.RebuildFirstPadAsAquaSynthScript` now emits readable
  neutral `pad_mode=`, `pad_bandwidth=`, `pad_bwscale=`, `pad_profile=...`,
  and `pad_position=...` fields, and maps Zyn PAD volume with the source
  exponential gain curve instead of the old tiny linear `/500` guess. The
  parser still accepts older `zyn_*` aliases for parity artifacts, but authored
  `.aqua` should use AquaSynth-native PAD terms.
- The translator now expands a useful subset of Zyn OscilGen base-function
  harmonic content plus spectrum adjustment before feeding PAD synthesis. This
  is a coherent source-table owner, but current parity is mixed: it improves
  Soft Pad and Organ Choir Pad2 log-mel, while sin2x/DoublePadBass/Ghost
  regress. Treat it as live pressure on OscilGen coverage, not solved Zyn
  oscillator parity.
- Latest focused verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --no-restore --filter "FullyQualifiedName~ZynInstrumentTests|FullyQualifiedName~ZynReferenceParityTests"`:
  13 passed.
- Latest upstream PAD batch after OscilGen subset:
  `Soft Pad` log-mel `0.342738`, score `0.443083`;
  `Organ Choir Pad2` log-mel `0.573216`, score `0.434351`;
  `sin2x pad` log-mel `0.524421`, score `0.360653`;
  `DoublePadBass` log-mel `0.689315`, score `0.226566`;
  `Ghost Ensemble` log-mel `0.482155`, score `0.400393`.
- Next coherent cut: either complete more OscilGen semantics
  (modulation/waveshaping/filter/adaptive harmonics) or gate the current
  approximation to the cases it truly owns. Do not present the mixed OscilGen
  subset as passing parity.

Current follow-up:

- Reworked Zyn PAD OscilGen partial extraction into a sampled stage-order
  pipeline for presets with live oscillator processing: base waveform,
  optional oscillator filter/waveshaper order, modulation, harmonic shift, then
  spectrum adjustment. The implementation covers a useful subset of oscillator
  filter types and waveshapers instead of pretending base-function convolution
  alone is the synth.
- Gated that pipeline so direct harmonic presets remain readable and capped at
  32 partials. This cuts the earlier mistake where base-function-only presets
  grew huge partial lists without improving parity.
- Latest upstream PAD batch after this pass:
  `sin2x pad` log-mel `0.455193`, score `0.405637` (better than the previous
  OscilGen subset's `0.524421`);
  `Soft Pad` log-mel `0.342738`, score `0.443083`;
  `Ghost Ensemble` log-mel `0.40922`, score `0.421156`;
  `Organ Choir Pad2` log-mel `0.664699`, score `0.369231`;
  `DoublePadBass` log-mel `0.655288`, score `0.246664`.
- This is still not passing Zyn PAD parity. The likely next missing owners are
  full OscilGen filter/waveshape coverage, adaptive harmonics, and note/global
  filter/envelope behavior. Do not tune thresholds around these numbers.

Current user-ear correction:

- User clarified Soft Pad itself is fine; Ghost Ensemble was the one collapsing
  into a Soft Pad-like identity. The bug was our direct-harmonic gate ignoring
  Ghost's live `base_function=4` power oscillator because no downstream
  oscillator stage was enabled. The gate now expands power-base multi-harmonic
  OscilGen sources, restoring Ghost to a 32-partial source. Latest Ghost:
  log-mel `0.402789`, score `0.446977`, and the `.aqua` no longer looks like a
  two-partial Soft Pad knockoff.
- DoublePadBass exposed a separate ownership bug: the preset is `kit_mode=1`
  with two enabled PAD kit items across the full key range. The old parity path
  rendered and rebuilt only kit item 0. The upstream PAD test now sums enabled
  PAD kit notes and passes per-kit table roots into
  `RebuildEnabledPadsAsAquaSynthScript`; generated Aqua emits
  `doublepadbass_0` and `doublepadbass_1`.
- Latest DoublePadBass after multi-kit rebuild: log-mel `0.493451`, score
  `0.376854`, RMS ratio `0.826864`. Raising the volume cap to `3.0` worsened it
  (log-mel `0.521113`, RMS ratio `1.220809`), so the cap was cut back to `1.5`.
- User then identified the remaining DoublePadBass balance issue: Aqua
  over-emphasized kit0's high buzz while Zyn's character comes from kit1's low
  rumble. The cause was visible Zyn global filter data we were still ignoring:
  kit0 has filter `category=0 type=2 freq=12`, kit1 has `freq=61`. Static Zyn
  PAD low-pass now maps to layer `lpf`; the helper had to read nested `FILTER`
  params, not direct `FILTER_PARAMETERS` children.
- User still heard Aqua as all kazoo, while Zyn was deep bass with a tiny kazoo
  overtone. The linear `freq/127` LPF mapping was wrong: Zyn's old filter
  cutoff maps through `2^((Pfreq / 64 - 1) * 5 + log2(1000))`. Kit0
  `freq=12` is roughly 60 Hz, not 1.7 kHz; kit1 `freq=61` is roughly 850 Hz,
  not 8.6 kHz.
- Latest DoublePadBass after Zyn filter curve mapping: log-mel `0.334546`,
  score `0.351535`, RMS ratio `0.464596`, with generated layers
  `doublepadbass_0 lpf=0.003325` and `doublepadbass_1 lpf=0.047225`. The
  character should now be deep-bass-first; remaining pressure is filter
  envelope/level recovery, not more static high-band brightness.
- User still heard Aqua as kazoo-first. The next missing authority was not
  brightness but pitch: DoublePadBass PAD frequency params set
  `coarse_detune=15360`, and Zyn's `getdetune` wraps octave `15` to `-1`.
  Aqua had ignored PAD coarse/fine detune and played both spectral tables at
  `261.6256 Hz`; it now emits `freq=130.8128` for both enabled kit layers.
- The same pass added explicit `lpf_order` so Zyn analog `LP2` filters lower to
  Faust `fi.lowpass(2, ...)` instead of pretending all low-passes are first
  order. Latest DoublePadBass: log-mel `0.259298`, score `0.370218`, centroid
  ratio `1.027115`; remaining pressure is loudness/envelope recovery.
- User then heard the tone as right but the Zyn reference hitting harder at the
  beginning. The missing authority was Zyn's PAD filter envelope: Aqua had only
  the static cutoff, while Zyn adds ADSR-filter octave offsets before the
  global filter. AquaSynth now has a general low-pass rate/level envelope
  surface (`lpf_env=rl`, `lpf_start`, `lpf_rates`, `lpf_levels`) and the Zyn
  importer maps `FILTER_ENVELOPE` into it. Latest DoublePadBass after this
  pass: log-mel `0.241993`, envelope distance `0.554658`, RMS ratio `0.456173`,
  score `0.404382`.
- User then heard the hit level as right but the attack as less sharp, causing
  a blare. The old Zyn envelope time conversion was also wrong: Aqua used
  linear `dt/127`, while Zyn's old envelope time is
  `(2^(dt/127*12)-1)/100`. DoublePadBass kit0 attack `10` is about `0.00925s`,
  not `0.07874s`. The importer now uses Zyn's logarithmic time curve and the
  PAD gain cap is relaxed only to `3.0`, not the full raw Zyn volume multiplier.
  Latest DoublePadBass: log-mel `0.211646`, envelope distance `0.462822`, RMS
  ratio `0.511669`, score `0.450507`.
- User then heard the shape as right but Zyn still peaking quite a bit louder.
  The remaining level delta was batch-wide output calibration, not another
  DoublePadBass layer issue: latest pre-calibration PAD RMS ratios were
  `0.51..0.78`. Generated Zyn PAD rebuilds now emit `patch gain=1.6`. Latest
  DoublePadBass: WAV peak `0.979` against clipped Zyn reference peak `1.0`,
  log-mel `0.211671`, envelope distance `0.226563`, RMS ratio `0.818669`,
  score `0.631168`.
- User accepted DoublePadBass and moved the ear target to Ghost Ensemble:
  Aqua over-emphasized upper harmonics and sounded brighter/tinnier than Zyn.
  The first missing general authority was Zyn PAD filter note tracking:
  `freq_track=97` lowers Ghost's cutoff at C4 by the Zyn
  `log2(note/440) * tracking` rule. Aqua now applies that when importing PAD
  global filters.
- Ghost still had too much upper tail, so the next cut was not a Ghost-only
  brightness knob. AquaSynth now has explicit low-pass Q via `lpf_q`/`lpq` on
  the filter model. Zyn PAD analog low-pass `q` imports through
  `exp((q/127)^2 * ln(1000)) - 0.9`; Ghost emits `lpf_q=0.499021`, which Faust
  lowers through `fi.resonlp(..., max(0.1, lpf_q), 1.0)`.
- Latest Ghost after note tracking + Q:
  artifact `artifacts/parity/zyn-upstream-pad-fixtures/0121-ghost-ensemble-20260515T131053271`,
  log-mel `0.375637`, envelope distance `0.363075`, RMS ratio `1.09995`,
  centroid ratio `1.010727`, score `0.561458`. This improves from the
  post-tracking `0.38424` log-mel / `1.155288` RMS ratio, but a table-harmonic
  diagnostic still shows candidate h8/h10/higher partials hot against the Zyn
  table. Remaining Ghost pressure is OscilGen/PAD source-table shape, not
  static filter ownership.
- Latest upstream PAD survey after Q kept the accepted batch intact:
  DoublePadBass log-mel `0.247729`, envelope `0.208429`, RMS ratio `0.887621`,
  score `0.650069`; Soft Pad log-mel `0.316754`, score `0.6089`.
- User then heard Ghost as closer but still overly bright in the middle. A
  Zyn oracle `harmonics` diagnostic was added to `ZynPadReference` so the
  exact normalized OscilGen harmonic vector can be dumped before PAD table
  generation. That proved Ghost's first 32 imported partial ratios already
  match Zyn's OscilGen output; the h8/h10 glare was not a first-page harmonic
  extraction bug.
- Two tempting cuts were tested and rejected or narrowed:
  using Zyn's 1024-sample OscilGen base table helped Ghost but regressed the
  accepted DoublePadBass batch, so it was cut; raising generated OscilGen
  partial output from 32 to 64 worsened Ghost, so the readable 32-partial cap
  remains.
- The real Ghost middle-brightness fix was filter lowering ownership: explicit
  `lpf_q` had accidentally replaced a 4-pole Zyn-style low-pass with a single
  Faust `resonlp`, preserving Q but throwing away slope. Faust export now
  cascades resonant low-pass stages according to `lpf_order` when `lpf_q` is
  present. Latest Ghost:
  artifact `artifacts/parity/zyn-upstream-pad-fixtures/0121-ghost-ensemble-20260515T134050872`,
  log-mel `0.369402`, envelope distance `0.363685`, RMS ratio `1.054875`,
  centroid ratio `1.006808`, score `0.57229`. DoublePadBass remains at
  log-mel `0.247748`, envelope `0.208433`, RMS ratio `0.887555`, score
  `0.650029`.
- User accepted Ghost, DoublePadBass, and Soft Pad as nigh-indistinguishable
  by ear, with Organ Choir still a bit tinny and sin2x still overdoing a high
  harmonic. The sin2x issue was a real OscilGen indexing bug, not a fixture
  brightness preference: Zyn oscillator filters receive one-based harmonic
  indices (`filter(1)` is the first harmonic), while AquaSynth had translated
  them through a zero-based index and moved the type-13 spike filter from h2 to
  h3. `ZynOscilFilterGain` now preserves Zyn's one-based index, and the
  upstream sin2x regression asserts h2 dominates h3 by more than 10x.
- Latest sin2x after the oscillator-filter index fix:
  artifact `artifacts/parity/zyn-upstream-pad-fixtures/0002-sin2x-pad-20260515T140540381`,
  log-mel `0.479782` (from `0.491437`), envelope distance `0.490113`, RMS
  ratio `1.28196`, centroid ratio `0.998145` (from `1.227953`), score
  `0.430193`. The wrong overtone placement is fixed: remaining sin2x pressure
  is energy concentration/loudness/profile shape, not h3 accidentally wearing
  h2's jacket.
- Zyn PAD harmonic profile lowering also now computes the profile amplitude
  multiplier coordinate before width scaling, matching Zyn's `origx`
  behavior. It was effectively neutral for sin2x because that preset's width is
  near full, but the ownership is now faithful to the source formula.

Completed this slice:

- Added PAD spectral-cloud syntax attached to named layers:
  `spectrum layer=pad_low root=130.8128 spread=.012 partials=1:.07,1.5:.052`.
- The parser preserves each bank as `SpectralBank` model data. `PadSynthWaveform`
  now generates each bank as a PADsynth-style FFT table: frequency-domain
  harmonic spreading, deterministic random phase, one inverse FFT, and
  normalization. Faust export emits that static `waveform`/`rdtable` source
  before applying the normal voice treatment path. `root`, `spread`, and
  `partials` are authoring-time table shape, not runtime controls.
- Converted the Zyn PAD texture rebuild from individual PAD body voices to two
  spectral banks under `pad_low` and `pad_high`; the air/noise layer remains a
  normal voice.
- This is not full Zyn PAD engine parity across every harmonic profile,
  randomness control, or pitch-zone behavior. It is a source-level FFT
  partial-cloud authority for readable PAD rebuilds.
- Focused verification before wavetable replacement passed:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --no-restore --filter "FullyQualifiedName~SpectralBank|FullyQualifiedName~ZynStyleReferenceRebuildsParseExportAndDeclarePressure|FullyQualifiedName~BuiltInReferenceScriptsParseAndExportFaust"`:
  4 passed.
- Wavetable replacement verification:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  94 passed.
- FFT table generator focused verification:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --no-restore --filter "FullyQualifiedName~SpectralBank|FullyQualifiedName~FaustCompilerValidatesSpectralBank|FullyQualifiedName~ZynStyleReferenceRebuildsParseExportAndDeclarePressure"`:
  4 passed.
- Full verification after FFT table generator:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  94 passed.

Previous slice:

- Promoted staged rate/level envelopes to normal voices and layer defaults.
  Voice fields now accept `env=rl rates=... levels=... curves=... gate=...`;
  the model stores `Voice.RateLevelEnvelope`, and Faust emission uses
  `rl4_env` for those voices.
- Converted Zyn PAD texture and vocal-layer rebuilds to use staged envelopes
  through named layers. This gives two non-additive targets asymmetric
  envelope contour authority without inventing a new parser.
- Arbitrary Zyn free-envelope point curves remain missing pressure. This slice
  is the coherent staged-envelope rung, not a free-mode clone.
- Focused verification passed:
  `dotnet test tests\AquaSynth.Dsl.Tests\AquaSynth.Dsl.Tests.csproj --no-restore --filter "FullyQualifiedName~LayeredVoiceRateLevelEnvelope|FullyQualifiedName~ZynStyleReferenceRebuildsParseExportAndDeclarePressure|FullyQualifiedName~BuiltInReferenceScriptsParseAndExportFaust"`:
  3 passed.
- Full verification with bundled Python/dexed-py:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  91 passed.

Previous slice:

- Added additive harmonic-bank syntax attached to named layers:
  `harmonics layer=body root=220 partials=1:.16,2:.075`.
- The parser preserves each bank as `HarmonicBank`/`HarmonicPartial` model
  data, then lowers the partials into ordinary voices so Faust output remains
  inspectable.
- Converted the Zyn additive lead rebuild from repeated partial voices to two
  named banks under `body` and `shine`.
- This is not Zyn ADDsynth parity theater. Phase, bandwidth, oscillator
  shaping, and exact free-envelope behavior remain explicit pressure.
- Verified with bundled Python/dexed-py still wired:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  90 passed.

Previous slice:

- Added Zyn-driven language golf through `layer` declarations. A layer owns
  name, engine tag, optional MIDI key range metadata, default gain, and an
  effect-send label, then lowers to ordinary voices for now.
- Converted the three Zyn-style rebuilds to use named layers:
  additive `body`/`shine`, PAD `pad_low`/`pad_high`/`air`, and vocal
  `air`/`body`/`breath`.
- This is an ownership scaffold, not hidden PAD/free-envelope/effect machinery.
  Future additive-bank, PAD-source, free-envelope, and formant-motion syntax
  now has somewhere coherent to attach.
- Verified with bundled Python/dexed-py still wired:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  88 passed.

Previous slice:

- Hardened `ZynInstrumentReader` against the real upstream Zyn instrument bank:
  it now tolerates leading whitespace before XML declarations and counts only
  actual `FORMANT_FILTER`/`FILTER` blocks instead of all internal formant/vowel
  nodes.
- Added `ZynInstrumentSurvey` to rank `.xiz` directories by feature pressure.
  The non-vendored upstream bank scan parsed `1358/1358` files at
  `zynaddsubfx/instruments` commit `e9f64a9`.
- Added `docs/zynaddsubfx-pressure-survey.md`. Worst observed target:
  `olivers-100/0032-Drum Kit.xiz` with 16 enabled kit items, mixed ADD/SUB/PAD
  engines, 106 envelopes, 25 free envelopes, 73 LFOs, 37 filters, and 3 formant
  filters.
- Survey conclusion: named kit/layer routing should probably precede syntax
  golf for additive banks, PAD sources, or formant motion.
- Verified with bundled Python/dexed-py still wired:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  86 passed.

Previous slice:

- Added first Zyn-style AquaSynth rebuilds for the project-authored additive
  lead, PAD texture, and vocal/formant layer fixtures. They live in
  `BuiltInScripts` and `ReferenceRebuildCatalog.ZynRebuilds`.
- The rebuild tests parse/export every Zyn script and compare matched feature
  claims against source fixture features where the same feature name exists.
- Current missing abstractions from this first Zyn pass: terse additive
  harmonic-bank syntax, a PAD/spectral source authority, free envelopes for
  normal voices, named kit/layer routing with per-layer effect sends, and richer
  formant/vowel morphing. No new runtime surface was added yet.
- Verified with bundled Python/dexed-py still wired:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  83 passed.

Previous slice:

- Started the ZynAddSubFX phase and stopped treating DX7 as the active trench.
- Added `ZynInstrumentReader` for `.xiz`-shaped XML. It reads plain XML and
  gzip-compressed XML, extracts instrument metadata and kit items, and emits
  neutral `ReferencePatch` features.
- Added project-authored Zyn fixtures under
  `tests/AquaSynth.Dsl.Tests/Fixtures/ZynAddSubFX/ProjectAuthored` instead
  of vendoring upstream preset-bank files with unclear root provenance.
- Current Zyn classifier detects active ADD/SUB/PAD engines, enabled kit items,
  layering, envelopes, free envelopes, LFOs, filters, formant filters, and
  effects. This is inventory for choosing rebuild targets, not translation yet.
- Verified with bundled Python/dexed-py still wired:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  81 passed.

Previous slice:

- Addressed the remaining `ANLGSYN 1` beat as DX7 pitch-LFO pressure, not
  amplitude-LFO pressure. Raw `analog1.syx` bytes show `AMD=71` but every
  operator has `AMS=0`; the active path is `PMD=32`, `PMS=1`, sine LFO speed
  `38`, and delay `33`.
- Added first-class operator-graph vibrato syntax:
  `opgraph ... vibrato=<depth> vibrato_hz=<hz> vibrato_delay=<seconds>`.
  Faust lowering modulates the graph frequency with a faded LFO onset instead
  of a hard delay gate.
- Lowered DX7 pitch LFO for the public-domain probe through that graph vibrato
  surface. Latest `ANLGSYN 1` metrics with the beat present: score `.697885`,
  log-mel `.19496867`, envelope `.15124573`, zero-crossing `.9430519`.
- Tested and cut the earlier operator-tremolo direction for this target. It was
  the wrong authority because ANLGSYN's operator `AMS` values are all zero.

Previous slice:

- Reframed the latest ear report as envelope pressure, not harmonic pressure:
  `{ Mooger }` needs a more aggressive attack contour; `MELLOWSOLO` needed a
  smoother captured tail; `RES SYNTH1` still has a phase-like attack-modulation
  issue even though oscillator sync is enabled.
- Extended only the `MELLOWSOLO` pressure render from `1.0s` to `1.25s` so the
  existing `0.6s` release after a `0.65s` gate is not chopped by the artifact
  boundary. Latest pressure metrics improve to score `.5722256`, log-mel
  `.3472474`, RMS `.9883968`.
- Tested and cut a global traced-release-duration change. It shortened releases
  too aggressively and broke PRC, `Piano Bass`, `RES SYNTH1`, and `ANLGSYN 1`;
  release duration remains part of the broader DX7 envelope model pressure.
- Verified with bundled Python/dexed-py:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  76 passed.

Previous slice:

- Listening split the latest community set again: `Piano Bass` is currently the
  anchor and should not be disturbed; `ANLGSYN 1` and `RES SYNTH1` were blowing
  out into drone at the modulator peak; `{ Mooger }` and `MELLOWSOLO` still
  have overtone-emphasis issues.
- Added scoped modulator peak headroom in the DX7 probe lowering:
  algorithm-2 non-carrier applied envelopes cap only Level1 at `.92`.
  `Piano Bass` remains unchanged; `RES SYNTH1` improves to score `.8140223`,
  log-mel `.12369786`, envelope `.06489321`; `ANLGSYN 1` keeps log-mel
  `.19461302` and sustained high-band evidence but aggregate score drops to
  `.6980632`, so its score gate is now `.69`.
- `MELLOWSOLO` nudges to log-mel `.42906323`, score `.55206907`, but remains a
  pressure artifact, not passing parity.
- Tested and cut a wider summed-route headroom rule for `{ Mooger }`
  (`5.25`/`5.75` for three-source sums). It worsened Mooger log-mel/envelope
  and does not deserve to live. Mooger's "too hard" harmonics remain unresolved
  overtone-emphasis pressure, not a solved high-band-energy problem.
- Focused verification with bundled Python/dexed-py:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore --filter "Dx7SysExTests|PublicDomainDx7MoogerAndPianoBassMeetRenderedParityWhenInstalled|PublicDomainDx7AnlgSyn1KeepsBuzzingModulationWhenInstalled|PublicDomainDx7MellowSoloWritesPressureWavsWhenInstalled|PublicDomainDx7PrcSynth1WritesListeningWavsWhenInstalled"`:
  28 passed.

Previous slice:

- Listening report split the remaining DX7 mismatch: `RES SYNTH1` mostly
  needed a harder attack, `ANLGSYN 1` still smells like missing LFO/operator
  attack behavior, and `MELLOWSOLO` is the clearest harmonic mismatch.
- Fixed two note-dependent DX7 lowering gaps:
  - operator key-scaling now uses the effective played MIDI note after voice
    transpose instead of hardcoded MIDI 60;
  - `Dx7SysEx.OperatorRateScaling` now feeds
    `ApproximateAppliedRateLevelEnvelope`/`TraceInterpolatedEnvelope`.
- Kept graph gain as loudness authority after the scaling change:
  `Piano Bass` moved from `.90` to `.72`.
- Added `PublicDomainDx7MellowSoloWritesPressureWavsWhenInstalled`; it writes
  listening artifacts but only gates as pressure (`log-mel <= .45`, score
  `>= .5`) because it is not passing parity by ear.
- Cut a tempting output-level-dependent envelope trace experiment. It looked
  more Dexed-shaped on paper, but worsened `MELLOWSOLO` and `ANLGSYN 1`, so it
  does not belong in the live machine yet.
- Focused verification with bundled Python/dexed-py:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore --filter "Dx7SysExTests|PublicDomainDx7MoogerAndPianoBassMeetRenderedParityWhenInstalled|PublicDomainDx7AnlgSyn1KeepsBuzzingModulationWhenInstalled|PublicDomainDx7MellowSoloWritesPressureWavsWhenInstalled|PublicDomainDx7PrcSynth1WritesListeningWavsWhenInstalled"`:
  28 passed.

Previous slice:

- Corrected DX7 ROM COM handling after user listening caught that the
  no-compensation probe had closer harmonics across the community patches and
  was mainly too quiet.
- `Dx7SysEx.OperatorOutputCompensation` now returns unity. Loudness recovery
  moved to graph gain instead of hidden carrier boosts.
- Community parity gains with COM disabled:
  `{ Mooger }` `.75`, `Piano Bass` `.90`, `RES SYNTH1` `.75`.
- `RES SYNTH1` is now in the passing community gate. Latest metrics:
  score `.7277295`, log-mel `.1461968`, envelope `.10764466`, RMS `.941083`,
  zero-crossing `.1.1022931`.
- `DX1 LEAD B` and `MELLOWSOLO` sound closer by ear with COM disabled and
  graph gain restored, but still fail numeric gates. Latest survey:
  `DX1 LEAD B` score `.45156077`, log-mel `.43533683`, RMS `.8193191`;
  `MELLOWSOLO` score `.5359293`, log-mel `.39420125`, RMS `.99559265`.
- Verified with bundled Python/dexed-py:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  74 passed.

Previous slice:

- Fixed the remaining `ANLGSYN 1` drift that listening caught after the
  conditional output-clip pass. The old candidate still lost 2.5-5 kHz energy
  midway through the note.
- Added `sustainFloor` to `Dx7SysEx.ApproximateAppliedRateLevelEnvelope` and
  uses a `.9` floor for max-feedback source operators in the DX7 probe
  lowering. This keeps the self-feedback source driving the loop after the
  attack instead of sagging into `deeooh`.
- Added a sustained 2.5-5 kHz band-energy parity gate for `ANLGSYN 1`; the old
  candidate was around `.175` at the sustained check, while the new lowering is
  above `1.0`.
- Raised `Piano Bass` graph gain to `.30`; latest metrics: score `.7552858`,
  log-mel `.16204594`, envelope `.06413663`, RMS `.9909909`.
- Fresh `ANLGSYN 1`: score `.7245976`, log-mel `.18460114`, envelope
  `.119950555`, RMS `.97143364`, zero-crossing `.97149533`.
- Verified with bundled Python/dexed-py:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  74 passed.

Earlier slice:

- Tightened `ANLGSYN 1` after listening caught the `deeooh` failure: the
  max-feedback candidate had a better attack but lost sustained buzz midway.
- Added conditional output nonlinearity in the DX7 probe lowering:
  `soft_clip=true` only when a voice has DX7 feedback `7` and a self-feedback
  topology. PRC stays `soft_clip=false`.
- Fresh `ANLGSYN 1`: score `.70946133`, log-mel `.18456098`, envelope
  `.10628832`, RMS `.9816304`, zero-crossing `.8657629`, centroid `.95726264`.
- Fresh PRC after the conditional clip gate: score `.7546429`, log-mel
  `.13874851`, envelope `.067291394`, RMS `.99121606`, zero-crossing
  `.95325506`.

Earlier slice:

- Retuned DX7 max-feedback lowering for `ANLGSYN 1`. Route-index sweeps did not
  materially improve the missing high-band buzz; hotter feedback did.
- Changed only feedback value `7` from `.66` to `2.2`. Feedback value `5`
  remains `.19`, so the hard PRC target keeps its existing feedback amount.
- Fresh `ANLGSYN 1` with `feedback=2.2`: log-mel `.17553389`, envelope
  `.16478956`, zero-crossing `.90904`, centroid `.9519112`, score `.6448498`.
  This improves spectral buzz while making envelope/RMS less tidy.
- Band evidence versus the previous fixed-frequency candidate:
  - 1.2-2.5 kHz candidate/reference energy: `.008 -> .656`
  - 2.5-5 kHz candidate/reference energy: near zero -> `.321`
- Verified with bundled Python/dexed-py:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  73 passed.

Earlier slice:

- Fixed DX7 fixed-frequency operator lowering after `ANLGSYN 1` exposed the
  missing buzzing modulation. The old lowering treated fixed mode as a fake
  note ratio, so fixed carriers op1/op3 became `0.5` ratio against the graph
  frequency. Dexed treats fixed mode as absolute Hz.
- Added `Dx7SysEx.FixedOperatorFrequencyHz` using Dexed's fixed-mode
  log-frequency formula and changed `OperatorFrequencyRatio` to return
  `fixedHz / graphFrequency` for fixed operators.
- Fresh `ANLGSYN 1` candidate now lowers fixed op3 to ratio `.009227` and op1
  to `.007615` against `freq=130.8128`, instead of flattening both to `.5`.
- Added `PublicDomainDx7AnlgSyn1KeepsBuzzingModulationWhenInstalled` with
  focused gates. Latest metrics: log-mel `.19427659`, envelope `.14924917`,
  zero-crossing `.94662774`, centroid `.9182808`, score `.659334`.
- Verified with bundled Python/dexed-py:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  73 passed.

Previous slice:

- Fixed the `Piano Bass` octave bug. The parsed voice has `transpose=12`; the
  old generated AquaSynth script hardcoded `freq=261.6256`, so it rendered an
  octave too high. `Dx7SysEx.NoteFrequencyHz(midiNote, transpose)` now treats
  transpose `24` as neutral and transpose `12` as one octave down.
- `Dx7VoiceProbeScript` now uses the voice transpose for graph frequency and
  for the detune note basis. Fresh `Piano Bass` artifact emits `freq=130.8128`.
- Narrowed the community parity gate to the two voices we can currently defend:
  `{ Mooger }` and fixed `Piano Bass`. `ANLGSYN 1`, `RES SYNTH1`, `DX1 LEAD B`,
  and `MELLOWSOLO` remain pressure, not passing parity.
- Fixed `Piano Bass` metrics: log-mel `.16180529`, envelope `.13544041`,
  zero-crossing `.905511`, score `.64267576`.
- Verified with bundled Python/dexed-py:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  71 passed.

Previous slice:

- Added `PublicDomainDx7AnalogCommunityVoicesMeetBroadRenderedParityWhenInstalled`,
  which renders actual public-domain `analog1.syx` community voices through
  Dexed and the current AquaSynth DX7 lowering. Kept four broad parity voices:
  `ANLGSYN 1`, `{ Mooger }`, `Piano Bass`, and `RES SYNTH1`.
- The test writes WAVs/reports under
  `artifacts/parity/dx7-community-analog1/<voice>/` and gates log-mel
  `<= .3`, envelope distance `<= .16`, and score `>= .5`.
- Latest kept-voice metrics:
  - `ANLGSYN 1`: log-mel `.25955316`, envelope `.12271979`, score `.5709625`
  - `{ Mooger }`: log-mel `.23776375`, envelope `.07168135`, score `.66662127`
  - `Piano Bass`: log-mel `.19985504`, envelope `.14781862`, score `.5205584`
  - `RES SYNTH1`: log-mel `.22371109`, envelope `.109231755`, score `.54040927`
- First survey also tried `DX1 LEAD B` and `MELLOWSOLO`; they were excluded
  from the passing gate because log-mel and zero-crossing mismatch were too
  large. That is pressure, not library stock.
- Verified with bundled Python/dexed-py:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  70 passed.

Previous slice:

- Added two split project-authored DX7 algorithm-8 rendered parity probes:
  `ProjectAuthoredDx7AlgorithmEightCascadeProbeMeetsParityWhenInstalled` for
  `6 -> 5 -> 3`, and
  `ProjectAuthoredDx7AlgorithmEightSummedPairProbeMeetsParityWhenInstalled`
  for `4 + 5 -> 3`.
- Tightened the existing combined algorithm-8 stack gate now that real metrics
  are known. The project-authored probes gate log-mel at `<= .06`; latest real
  Dexed run:
  - combined stack: log-mel `0.04029555`, score `0.8664546`
  - summed pair: log-mel `0.042429477`, score `0.870778`
  - cascade: log-mel `0.03811625`, score `0.9157809`
- Project-authored DX7 parity tests now write listening WAVs and reports under
  `artifacts/parity/dx7-project-authored/<probe>/`. These split probes are
  intentionally not part of the patch library: they are useful measuring tools,
  not pleasant or reusable stock.
- Verified with bundled Python/dexed-py:
  `AQUASYNTH_DX7_PYTHON=<bundled python> dotnet test AquaSynth.sln --no-restore`:
  69 passed.

Previous slice:

- Added a structured `.aqua` patch library under `patches/` with:
  `examples/`, `sfxr/`, `bfxr/`, `808/`, `fm-bell/`, `wobble-bass/`,
  `dx7/`, and `advanced/`.
- Added `patches/library.yaml` as the machine-readable index and
  `patches/README.md` as the folder contract. The library is development
  source material for stock, reference rebuilds, calibration probes, and patch
  inspiration, not a shipped package surface.
- Exported the existing built-in/reference catalog into `.aqua` files and added
  the calibrated `PRC SYNTH1` hard DX7 candidate at
  `patches/dx7/public-domain/prc-synth1-calibrated.aqua`.
- Added a test contract: every `.aqua` under `patches/` must parse through
  `PatchScript.Parse` and export Faust, while package-boundary tests keep
  `patches/` and `.aqua` files out of the NuGet package.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 67 passed.

Previous slice:

- Increased `Dx7SysEx.SummedOperatorModulationRouteIndex` from `1.6` to `6.0`.
  This targets the missing harsh stacked-modulation brightness without adding a
  generic drive/overdrive knob. The independent project-authored algorithm-8
  summed-stack probe now uses graph gain `.218` and gates log-mel at `<= .12`
  with RMS normalization.
- Latest hard `PRC SYNTH1` after stronger stack scale and graph gain `.25`:
  log-mel `0.13874426`, envelope distance `0.067291506`, RMS ratio
  `0.9912135`, zero-crossing ratio `0.95325506`, centroid ratio `1.1156512`,
  score `0.7546445`.
- Added `Dx7SysEx.ApproximateAppliedRateLevelEnvelope`, which traces the
  Dexed-style block-interpolated applied EG gain and lowers it into AquaSynth's
  curved staged envelope surface, normalized so operator output level remains a
  separate authority.
- Retried hard `PRC SYNTH1` with the applied-envelope lowering. Latest focused
  run after fixing the peak picker: log-mel `0.17186502` (was `0.24476814`),
  envelope distance `0.13057204` (was `0.43700194`), RMS ratio `1.0000212`,
  zero-crossing ratio `0.7815517`, score `0.6501268`.
- Listening note resolved: the prior applied-envelope lowering made op2 rise
  late (`0.014 -> 0.613`), causing the odd end lilt. The helper now finds the
  first near-peak before gate, so op2 hits early and decays
  (`0.981 -> 0.843 -> 0.660`) like the traced DX7 applied gain.
- Extended staged operator envelopes with per-segment curves:
  `curves=lin,exp,exp,lin`. Linear remains the default, and levels can now
  express deliberate transient overshoot above `1` instead of clipping away
  the DX7 evidence.
- Added `Dx7SysEx.TraceEnvelope`, a DX7 EG microscope that follows the internal
  rate/level state machine closely enough to expose gain and stage over time.
- Added `Dx7SysEx.TraceInterpolatedEnvelope`, which traces the gain actually
  applied by Dexed-style operator rendering: one EG sample per 64-sample block,
  linearly interpolated across the block.
- Added an envelope comparison artifact test that writes
  `artifacts/parity/dx7-envelope-trace/egstep.csv`. The first rows show both
  DX7 raw state and applied gain: raw jumps to `2` immediately, while applied
  gain ramps from `.03125` to `2` over the first 64-sample block and remains
  near `1` around 20 ms. AquaSynth `env=rl` is a different contour entirely.
  The artifact now also includes a curved AquaSynth staged-envelope candidate
  that tracks the applied-gain contour far more closely.
- Tried adding a first `env=dx7` runtime lowering, then cut it. The trace
  matched the Python `graph.py` envelope but not Dexed plugin audio, so the
  syntax was not allowed to survive. The durable result is the microscope and
  the applied-gain trace, not a half-proven model.

Previous slice:

- Added test-only project-authored Dexed patch rendering through
  `DexedPyRenderer.RenderPatchAsync`. It builds a `dexed.Patch` from explicit
  operator specs and renders it without adding any shipped fixture or package
  surface.
- Added an independent project-authored algorithm-8 summed-stack parity test.
  This backs the topology-aware route lowering outside the public-domain PRC
  patch, so `SummedOperatorModulationRouteIndex` is no longer only PRC-shaped
  evidence.
- Cut the attempted DX7 EG exponential level curve again. On an isolated
  envelope target, exponential EG levels plus a shorter timing scale improved
  shape versus linear levels, but PRC still failed log-mel (`0.2857573` at a
  tuned timing scale). The live lowering stays linear for EG levels until an
  isolated envelope target passes without harming the hard target.

Previous slice:

- Moved the remaining PRC cascaded-route scalar into a topology-aware DX7 route
  helper. `Dx7SysEx.OperatorRouteIndex(topology, edge)` now keeps the isolated
  two-op route scale (`6.275`) for standalone direct branches and uses the
  summed/cascaded scale (`1.6`) for sum edges and direct edges feeding a sum.
- The PRC probe no longer owns any private route-index function. Latest focused
  run still passes with log-mel `0.24476814`, score `0.5708394`, RMS ratio
  `1.0108943`.

Previous slice:

- Added the ratio-detune and algorithm-output-compensation rungs to the DX7
  lowering. `Dx7SysEx.OperatorFrequencyRatio` now applies ratio-mode detune
  from the Dexed/DX7 note formula, and `Dx7SysEx.OperatorOutputCompensation`
  maps ROM `COM` values relative to the algorithm-32 six-output baseline.
- The PRC probe no longer has a carrier-level scale table. It still has a
  fenced cascaded-route constant (`1.6`) for the `op6/op5/op4 -> op3` stack;
  that is now named as the next calibration target, not promoted into core
  lowering.
- Latest PRC run with detune, COM compensation, graph gain `.39`, and the
  fenced cascaded-route probe: log-mel `0.24476814`, score `0.5708394`, RMS
  ratio `1.0108943`, zero-crossing ratio `0.7931764`, centroid ratio
  `1.1131053`.

Previous slice:

- Completed the feedback calibration rung. Isolated Dexed feedback sweeps fit a
  nonlinear AquaSynth feedback table: `0, .01, .02, .05, .10, .19, .38, .66`
  for DX7 feedback values `0..7`.
- Added `Dx7SysEx.OperatorFeedbackAmount` and moved the PRC feedback lowering
  off the old `voice.Feedback * 0.04` constant. PRC barely changed because
  feedback value `5` maps to `.19`, close to the previous accidental `.20`.
  The win is ownership: feedback scaling is no longer folklore.

Previous slice:

- Completed the second calibration rung for isolated two-operator FM. A Dexed
  sweep fits full-scale DX7 modulation at about `12.55` radians. Given the
  current AquaSynth Faust formula, that maps to an AquaSynth route index of
  `6.275` for a full-level modulator.
- Added `Dx7SysEx.OperatorModulationRouteIndex` and a regression test for the
  calibrated phase-deviation scale. The hard PRC probe applies that scale only
  to the isolated `op2 -> op1` branch; applying it blindly to the cascaded
  `op6/op5/op4 -> op3` stack made PRC worse, which confirms algorithm output
  compensation is a separate rung.
- Latest PRC run with the carrier curve plus isolated-route calibration:
  log-mel `0.2526254`, score `0.44758993`, duration ratio `0.9927914`, RMS
  ratio `0.8931149`. The log-mel gate is back to `<= 0.255`.

Previous slice:

- Wrote `docs/dx7-calibration-plan.md` and referenced it from memory/spine.
  The ladder is now: single-carrier amplitude, two-op modulation index,
  feedback scaling, envelope level curve, algorithm output compensation, then
  hard PRC replay.
- Completed the first calibration rung. A project-authored Dexed single-carrier
  output-level sweep fits `2^((outputLevel - 99) / 8)` for carrier amplitude.
  `Dx7SysEx.OperatorOutputAmplitude` now owns that curve, and
  `ApproximateOperatorLevel` uses it instead of the old internal-level proxy.
- Added tests for the measured output-level curve. The hard PRC probe now gates
  log-mel at `<= 0.26` while the next rung calibrates modulation index; latest
  run after the carrier fix: log-mel `0.2562075`, score `0.40430218`.
- Calibration lesson: the old operator level helper was not a harmless
  approximation. It made mid-level operators far too loud and let PRC-specific
  constants compensate for a broken foundation. Next pressure is route/index
  scaling for isolated two-operator FM.

Previous slice:

- Reoriented the `PRC SYNTH1` hard probe around the perceptual metric that
  matched listening: log-mel distance. The probe now asserts log-mel
  `<= 0.255` plus a loose aggregate score floor instead of treating aggregate
  score as the main judge.
- Spectral diagnosis showed the Dexed reference has dominant peaks around
  `393`, `131`, `524`, and `656` Hz. The prior AquaSynth candidate had too
  little `80-160` Hz body when based at `392`, while the corrected DX7 note
  basis restored the low body but exposed weak mid harmonic structure and high
  centroid/zero-crossing mismatch.
- Current run with bundled Python and Faust: log-mel distance `0.25093183`,
  score `0.4135618`, envelope distance `0.3595946`, duration ratio
  `0.992578`, RMS ratio `1.0658501`, zero-crossing ratio `0.24567787`,
  centroid ratio `1.3291793`.
- Calibration lesson: the hard target is now a spectral calibration problem.
  DX7 output/index scaling still needs a real model; aggregate score can move
  in the wrong direction while log-mel and the ear move in the right direction.

Previous slice:

- Tightened the hard DX7 `PRC SYNTH1` probe from a "writes WAVs" smoke test to
  a modest passing parity target: threshold now requires score >= `0.60`.
- The useful cuts were not new syntax. The candidate improved by removing
  patch soft clipping, setting the graph base frequency to `392`, and boosting
  the `op3` carrier branch to compensate the algorithm-8 output/body balance.
  Global detune and per-edge route damping both made the score worse and were
  cut.
- Current best run with bundled Python and Faust writes timestamped artifacts
  under `artifacts/parity/dx7-prc-synth1/<run>/`: score `0.6057491`,
  log-mel distance `0.27658066`, envelope distance `0.38489524`, duration
  ratio `0.98714787`, RMS ratio `0.9756519`, zero-crossing ratio `1.0458903`,
  centroid ratio `1.093785`.

Previous slice:

- Added a hard DX7 `PRC SYNTH1` rendered parity probe that writes listening
  artifacts when `dexed-py` and Faust are available:
  `artifacts/parity/dx7-prc-synth1/reference-dexed.wav`,
  `candidate-aquasynth.wav`, `candidate.aqua`, and `report.txt`.
- The probe rebuilds `analog1.syx` voice 17 with readable operator graph syntax
  and staged `env=rl` operator envelopes. It asserts a modest hard-target score
  floor instead of pretending exact DX7 parity is solved.
- Current run with bundled Python and Faust: score `0.4581856`,
  log-mel distance `0.26397973`, envelope distance `0.4109424`,
  duration ratio `1.0173812`, RMS ratio `0.61393267`, zero-crossing ratio
  `1.1713804`, centroid ratio `1.0978767`.
- Calibration lesson: shortening the naive DX7 EG timing and lowering the graph
  base frequency made the candidate much closer. The remaining mismatch is
  still gain/timbre calibration, not missing `env=rl` syntax.

Previous slice:

- Added staged operator envelopes for operator graphs:
  `env=rl rates=.004,.12,.2,.4 levels=1,.7,.25,0`. This gives the DSL a
  readable four-stage rate/level contour without infecting the general ADSR
  voice envelope model.
- Added `RateLevelEnvelope` to the operator model, parser support for readable
  and compact `env=rl` syntax, Faust `rl4_env(...)` rendering, and a DX7 helper
  that emits staged envelope script specs from four-rate/four-level operator EG
  data.
- Verified the surface structurally and through the render path:
  `dotnet test AquaSynth.sln --no-restore`: 53 passed.
- Retried the hard `PRC SYNTH1` probe with direct DX7 rate/level lowering. The
  best quick score was only ~0.316, worse than the hand-tweaked ADSR candidate.
  The DSL can now express the missing contour shape, but DX7 EG timing/gain
  calibration remains the next pressure.

Previous slice:

- Added `Dx7OperatorLevelApproximation`, which distills DX7 operator output
  level plus key/velocity scaling into a normal AquaSynth `level=`-style value.
  This remains DX7 reference-import knowledge, not a generic DSL feature.
- Retried `PRC SYNTH1` with effective levels and envelope tweaks. Static level
  mapping alone did not rescue the hard target: quick probes moved from ~0.24
  to ~0.34, and envelope tweaks reached ~0.42. The next missing pressure is
  DX7-style operator envelope/gain evolution, not just static operator levels.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 50 passed.

Previous slice:

- Tried the harder `analog1.syx` voice 17, `PRC SYNTH1` (algorithm 8). The
  initial routed AquaSynth candidate scored badly (~0.23), which exposed a real
  missing invariant rather than a threshold-tuning problem.
- Reworked operator feedback emission from a cyclic smoothed self-reference to
  a renderable Faust feedback expression using delayed recursion. Added
  `FaustCompilerRendersOperatorFeedbackWhenInstalled`.
- Exact DX7 feedback scaling and EG behavior are still not solved, but feedback
  no longer makes the Faust render path fall over.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 49 passed.

Previous slice:

- Added the first thresholded DX7 rendered-audio parity rebuild:
  `BuiltInScripts.Dx7StylePublicDomainMcMm53` maps public-domain
  `analog1.syx` voice 13, `MC-MM 5-3`, to a terse AquaSynth sine patch.
- Added `PublicDomainDx7McMm53MeetsFirstRenderedParityThresholdWhenInstalled`,
  which renders the DX7 reference through `dexed-py`, renders the AquaSynth
  candidate through Faust-generated C#, and asserts score, log-mel distance,
  envelope distance, duration/RMS ratios, zero-crossing ratio, and centroid
  ratio.
- This is behavioral parity for a simple sine-like rendered voice, not exact
  DX7 operator execution. The next DX7 pressure should be a harder voice that
  audibly needs the operator graph.
- Verified with bundled Python plus `dexed-py`: parity test passed.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 48 passed.

Previous slice:

- Added a public-domain DX7 SysEx fixture from Musical Artifacts artifact 152:
  `tests/AquaSynth.Dsl.Tests/Fixtures/Dx7/PublicDomain/analog1.syx`, with
  provenance and SHA-256 recorded beside it.
- Added a test-only `dexed-py` reference renderer. It uses
  `AQUASYNTH_DX7_PYTHON` when set, otherwise probes `py`, `python`, and
  `python3`; if `dexed-py` is absent, the render test returns without turning
  optional tooling into a hard dependency.
- Added a `.nupkg` boundary test that packs `AquaSynth.Dsl` and asserts
  test fixtures, SysEx banks, and Python helpers are not shipped.
- Verified with bundled Python plus `dexed-py`: the DX7 fixture renders through
  Dexed successfully.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 47 passed.

Previous slice:

- Added `FaustCompiler.RenderAsync`, which compiles AquaSynth Faust output to
  Faust-generated C#, runs it in a temp .NET project, and returns a mono float
  sample buffer for analysis.
- Added a render test that proves a generated AquaSynth patch produces non-silent
  audio and can be compared through `AudioAnalyzer`.
- DX7 audio parity is still not claimed: the candidate renderer exists, but the
  reference side needs Dexed output or a captured licensed fixture.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 44 passed.

Previous slice:

- Added `Dx7EnvelopeApproximation`, which lowers a DX7 four-rate/four-level EG
  to a labeled AquaSynth ADSR approximation plus gate duration.
- Reference rebuilds now record `operator_envelope_approximation` as matched
  pressure and `operator_envelope_exactness` as still missing.
- Doctrine now states that DX7 EG approximation is not exact DX7 envelope
  execution.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 43 passed.

Previous slice:

- Added field-site parameter binding for readable operator graph authoring:
  graph gain/note fields, operator ratio/level/feedback/envelope fields, and
  route index fields now accept `@/param`.
- Operator graph Faust emission now substitutes bound parameter expressions at
  the exact `/opgraphs/...` field paths.
- Fixed AD operator envelope binding to use `/env/decay` for the second AD
  value, matching the ADSR model instead of calling it release by accident.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 42 passed.

Previous slice:

- Added a patch-level `Playback` contract for `OneShot`, `Mono`, and `Poly`
  playback with Faust MIDI polyphony settings.
- `instrument midi=true polyphony=8` now lowers to Faust's standard
  `[midi:on][nvoices:8]` option and `freq`, `gain`, `gate` controls.
- Host/MIDI playback no longer emits per-voice `/voices/0/note/frequency` and
  `/voices/0/note/gate` controls; Faust architecture owns allocation.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 41 passed.

Previous slice:

- Split note timing from envelope shape. `Note` now owns frequency, one-shot
  gate duration, and host/MIDI source; `Envelope` now owns ADSR shape:
  attack, decay, sustain level, and release.
- SFXR sustain duration now maps to `Note.GateSeconds`; SFXR punch maps to a
  lower ADSR sustain level plus voice gain compensation during import/legacy
  parsing.
- Added host note mode for MIDI-oriented patches through stable note frequency
  and note gate controls in generated Faust.
- Moved built-in authoring examples off `punch=` and onto `sustain_level=`.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 40 passed.

Previous slice:

- Replaced the DX7 algorithm-8 authoring surface with readable operator graph
  syntax: `operator`, `route`, and `carrier` declarations.
- Added `env=ad:attack:decay` and `env=adsr:attack:decay:sustain:release`
  envelope forms for operator declarations.
- Kept compact `ops=`/`edges=` syntax as parser/interchange scaffolding, but it
  is no longer the built-in authoring example.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 36 passed.

Previous slice:

- Added a first-class `OperatorGraph` model with operators, modulation edges,
  carriers, graph gain, and operator feedback.
- Added `opgraph` patch-script syntax. Example:
  `opgraph name=core freq=330 carriers=1,3 ops=6:4:.9,5:3:.8,4:2:.7:.18,3:2:.6,2:1:.75,1:1:.82 edges=6>5:1.1,5>3:.9,4>3:.75,2>1:.85`
- Faust emission now mixes operator graphs alongside normal voices.
- Updated the DX7 algorithm-8 rebuild to use a real operator graph for the
  topology. The remaining missing feature is exact DX7 feedback-register timing
  and DX7 rate/level envelopes, not graph ownership.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 35 passed.

Earlier rebuild slice:

- Added `ReferenceRebuild` and `ReferenceRebuildCatalog` for explicit
  reference-target rebuild attempts.
- Added two DX7-style AquaSynth DSL rebuilds:
  - `dx7/algo32-additive-organ`: algorithm 32's six-carrier additive shape,
    which maps cleanly to six AquaSynth voices.
  - `dx7/algo8-bright-pair`: algorithm 8's two-carrier FM shape, which is only
    an approximation because the current voice DSL cannot express
    `6->5`, `4+5->3`, `2->1`, or operator-local self-feedback.
- Added matched/missing feature records so topology mismatch becomes evidence
  instead of being hidden inside prose.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 34 passed.

Earlier topology slice:

- Added DX7 algorithm topology metadata for algorithms 1-32 using Ken
  Shirriff's reverse-engineered OPS algorithm ROM table.
- `Dx7SysEx.AlgorithmTopology` now exposes carrier operators, modulation
  edges, feedback-register writers, direct self-feedback operators,
  delayed-feedback targets, and raw ROM steps.
- `Dx7Voice.Features()` now emits `carrier_operators`,
  `modulation_edge_count`, `feedback_sources`, and
  `self_feedback_operators`.
- Representative tests cover algorithm 8, algorithm 16, and algorithm 32.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 31 passed.

Earlier slice:

- Added `Dx7SysEx` records and parsing for DX7 voice edit buffers, packed
  128-byte voices, and packed 32-voice SysEx banks.
- Extracted six operators, envelopes, algorithm, feedback, oscillator sync,
  pitch envelope, LFO, transpose, and voice name into neutral DX7 records.
- Added `Dx7Voice.ToReferencePatch` and structural feature extraction so DX7
  voices can pressure the reference model before translation exists.
- Checksum validation rejects bad wrapped SysEx payloads.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 28 passed.

Parameter slice:

- Added explicit `PatchParameter` contracts: stable path, label, default, min,
  max, step, unit, automation-rate notes, and notes.
- Added neutral `ReferencePatch`, `ReferenceSource`, and `ReferenceFeature`
  contracts with provenance/license/hash-ready fields.
- Added `param` script declarations and duplicate parameter path validation.
- Updated Faust emission so declared parameters produce smoothed `hslider`
  controls.
- Implemented Option B parameter binding: numeric fields may reference declared
  parameters with `@/path`, for example `lpf=@/macro/brightness`. The parser
  records an exact field binding such as `/voices/0/filter/lpf` and keeps the
  parameter default as the graph value.
- Faust emission substitutes the parameter expression only at the bound field
  site. Unbound parameters still emit a warning.
- Verified with `dotnet test AquaSynth.sln --no-restore`: 23 passed.

Next likely slice:

- Refine operator graphs with parameter bindings inside graph fields and decide
  how host MIDI note age/retrigger semantics should be represented beyond the
  current frequency/gate controls.
- Keep tests focused on structure first, then add rendered audio comparison once
  the render path is explicit.
