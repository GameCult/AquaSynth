using GameCult.Caching;
using GameCult.Caching.MessagePack;
using MessagePack;

namespace AquaSynth.Dsl;

[CultDocument("aquasynth.sound_claims", "aquasynth.sound_claims.v1")]
[CultGlobal]
[MessagePackObject]
public sealed record CultCachePatchDocument(
    [property: Key(0)] string Type,
    [property: Key(1)] string Schema,
    [property: Key(2)] int Version,
    [property: Key(3)] string[] GeneratedFrom,
    [property: Key(4)] CultCachePatchDatabase PatchDatabase,
    [property: Key(5)] CultCachePatchClaimCard[] PatchClaims,
    [property: Key(6)] CultCacheSpeechClaimCard[] SpeechClaims);

[MessagePackObject]
public sealed record CultCachePatchDatabase(
    [property: Key(0)] string SourcePath,
    [property: Key(1)] CultCachePatchDatabaseEntry[] Entries);

[MessagePackObject]
public sealed record CultCachePatchDatabaseEntry(
    [property: Key(0)] string Path,
    [property: Key(1)] string Family,
    [property: Key(2)] string Name,
    [property: Key(3)] string Source,
    [property: Key(4)] string Notes);

[MessagePackObject]
public sealed record CultCachePatchClaimCard(
    [property: Key(0)] string Id,
    [property: Key(1)] string? PatchPath,
    [property: Key(2)] CultCacheReferenceTruth Reference,
    [property: Key(3)] CultCacheIntentTruth Intent,
    [property: Key(4)] string Tier,
    [property: Key(5)] CultCacheProofTruth Proof);

[MessagePackObject]
public sealed record CultCacheSpeechClaimCard(
    [property: Key(0)] string Id,
    [property: Key(1)] CultCacheUtteranceSourceTruth UtteranceSource,
    [property: Key(2)] CultCacheSpeechTargetTruth Target,
    [property: Key(3)] CultCacheIntentTruth Intent,
    [property: Key(4)] string Tier,
    [property: Key(5)] CultCacheProofTruth Proof);

[MessagePackObject]
public sealed record CultCacheReferenceTruth(
    [property: Key(0)] string SynthOrSource,
    [property: Key(1)] string FixtureOrArtifact,
    [property: Key(2)] string LicenseScope);

[MessagePackObject]
public sealed record CultCacheUtteranceSourceTruth(
    [property: Key(0)] string TextOrIpa,
    [property: Key(1)] string PhonemeSource,
    [property: Key(2)] string ReferenceRenderer);

[MessagePackObject]
public sealed record CultCacheSpeechTargetTruth(
    [property: Key(0)] string AnatomyOrProfile,
    [property: Key(1)] string[] PhoneticConstraints);

[MessagePackObject]
public sealed record CultCacheIntentTruth(
    [property: Key(0)] string PerceptualClaim,
    [property: Key(1)] string UseContext);

[MessagePackObject]
public sealed record CultCacheProofTruth(
    [property: Key(0)] string LatestRenderArtifact,
    [property: Key(1)] CultCacheListeningReceipt LatestListeningReceipt,
    [property: Key(2)] string MetricSummary,
    [property: Key(3)] string[] KnownLies);

[MessagePackObject]
public sealed record CultCacheListeningReceipt(
    [property: Key(0)] string Subject,
    [property: Key(1)] string TouchedSurface,
    [property: Key(2)] string RemainingContamination,
    [property: Key(3)] string WitnessSentence);

public static class CultCachePatchDocumentStore
{
    public static async Task WriteDefaultAsync(string filePath)
    {
        using var cache = CultCacheMessagePack.Create(filePath, new CultCacheOpenOptions
        {
            PullOnOpen = false
        });
        await cache.UpsertAsync(
            CultCachePatchDocumentCatalog.CreateDefault(),
            new CultRecordHandle<CultCachePatchDocument>(new CultRecordKey(CultCachePatchDocumentCatalog.Key)))
            .ConfigureAwait(false);
        await cache.FlushAsync().ConfigureAwait(false);
    }

    public static async Task<CultCachePatchDocument> ReadAsync(string filePath)
    {
        using var cache = await CultCacheMessagePack.OpenAsync(filePath).ConfigureAwait(false);
        if (!cache.TryGet(new CultRecordKey(CultCachePatchDocumentCatalog.Key), out CultCachePatchDocument? document))
        {
            throw new InvalidOperationException($"CultCache document '{CultCachePatchDocumentCatalog.Key}' was not found in {filePath}.");
        }

        return document ?? throw new InvalidOperationException($"CultCache document '{CultCachePatchDocumentCatalog.Key}' resolved as null.");
    }
}

public static class CultCachePatchDocumentCatalog
{
    public const string Type = "cultcache.document.aquasynth.sound_claims";
    public const string Schema = "cultcache/aquasynth/sound-claims.v1";
    public const string Key = "aquasynth:sound-claims";

    public static CultCachePatchDocument CreateDefault() =>
        new(
            Type,
            Schema,
            1,
            [
                "patches/library.yaml",
                "src/AquaSynth.Core/ReferenceRebuilds.cs",
                "docs/ipa-vocal-tract-roadmap.md",
                "state/scratch.md"
            ],
            new CultCachePatchDatabase("patches/library.yaml", [.. PatchEntries]),
            [.. PatchClaims],
            [.. SpeechClaims]);

    public static readonly IReadOnlyList<CultCachePatchDatabaseEntry> PatchEntries =
    [
        new("patches/808/clap.aqua", "808", "clap", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/808/cowbell.aqua", "808", "cowbell", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/808/hat.aqua", "808", "hat", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/808/kick.aqua", "808", "kick", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/808/snare.aqua", "808", "snare", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/808/tom.aqua", "808", "tom", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/advanced/aurora-pad.aqua", "advanced", "aurora-pad", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/advanced/glass-creature.aqua", "advanced", "glass-creature", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/advanced/machine-breath.aqua", "advanced", "machine-breath", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/advanced/ritual-sequence.aqua", "advanced", "ritual-sequence", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/bfxr/coin-spark.aqua", "bfxr", "coin-spark", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/bfxr/portal-chirp.aqua", "bfxr", "portal-chirp", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/bfxr/shield-pop.aqua", "bfxr", "shield-pop", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/bfxr/ui-bloom.aqua", "bfxr", "ui-bloom", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/dx7/algorithm-32-additive-organ.aqua", "dx7", "Algorithm 32 additive organ", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/dx7/algorithm-8-bright-pair.aqua", "dx7", "Algorithm 8 bright pair", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/dx7/public-domain-mc-mm-5-3.aqua", "dx7", "Public-domain MC-MM 5-3", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/dx7/public-domain/prc-synth1-calibrated.aqua", "dx7/public-domain", "PRC SYNTH1 calibrated", "analog1.syx voice 17 parity probe", "Generated from latest calibrated DX7 lowering."),
        new("patches/examples/language-tour.aqua", "examples", "Language Tour", "BuiltInScripts.PatchScriptExample", "Parser and DSL feature tour."),
        new("patches/fm-bell/bell.aqua", "fm-bell", "bell", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/fm-bell/chime.aqua", "fm-bell", "chime", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/fm-bell/coin.aqua", "fm-bell", "coin", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/fm-bell/gong.aqua", "fm-bell", "gong", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/sfxr/blip.aqua", "sfxr", "blip", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/sfxr/classic/classic-sfxr-abstract-golf.aqua", "sfxr/classic", "Classic SFXR Abstract Golf", "BuiltInScripts.ClassicSfxrAbstractGolfScript", "Compressed SFXR-derived multi-sound golf fixture."),
        new("patches/sfxr/explosion.aqua", "sfxr", "explosion", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/sfxr/hit.aqua", "sfxr", "hit", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/sfxr/jump.aqua", "sfxr", "jump", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/sfxr/laser.aqua", "sfxr", "laser", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/sfxr/pickup.aqua", "sfxr", "pickup", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/sfxr/powerup.aqua", "sfxr", "powerup", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/wobble-bass/growl.aqua", "wobble-bass", "growl", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/wobble-bass/neuro.aqua", "wobble-bass", "neuro", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/wobble-bass/talker.aqua", "wobble-bass", "talker", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog."),
        new("patches/wobble-bass/yoy.aqua", "wobble-bass", "yoy", "BuiltInScripts.ReferenceScripts", "Stock reference patch exported from the in-code catalog.")
    ];

    public static readonly IReadOnlyList<CultCachePatchClaimCard> PatchClaims =
    [
        PatchClaim("zyn/project-additive-lead/aquasynth", null, "ZynAddSubFX project-authored fixture", "tests/AquaSynth.Dsl.Tests/Fixtures/ZynAddSubFX/ProjectAuthored/additive-lead.xiz", "project-authored development fixture", "A layered additive lead expressed as named harmonic banks rather than anonymous partial piles.", "Zyn ADDsynth pressure for AquaSynth harmonic-bank syntax.", "pressure", "ReferenceRebuildCatalog.ZynRebuilds", MissingReceipt("tests/AquaSynth.Dsl.Tests/Fixtures/ZynAddSubFX/ProjectAuthored/additive-lead.xiz vs AquaSynth additive lead rebuild", "harmonic-bank naming and rebuild topology", "No accepted listening pass yet; Zyn oscillator phase/bandwidth and free-envelope timing remain contaminated.", "No witness yet. The card is still waiting for ears instead of paperwork."), "structural pressure only", ["not exact Zyn ADDsynth oscillator phase/bandwidth semantics", "not exact Zyn free-envelope timing"]),
        PatchClaim("zyn/project-pad-texture/aquasynth", null, "ZynAddSubFX project-authored fixture", "tests/AquaSynth.Dsl.Tests/Fixtures/ZynAddSubFX/ProjectAuthored/pad-texture.xiz", "project-authored development fixture", "A slow layered PAD texture with body, shimmer, air, and explicit spectral-cloud source authority.", "PAD syntax and FFT wavetable pressure.", "pressure", "artifacts/parity/zyn-pad-reference/<run>/ when optional Zyn oracle is installed", new CultCacheListeningReceipt("tests/AquaSynth.Dsl.Tests/Fixtures/ZynAddSubFX/ProjectAuthored/pad-texture.xiz vs project-authored AquaSynth pad-texture rebuild", "optional Zyn PAD render comparison, log-mel/envelope report, and manual listening pass on the latest angry-bees candidate", "Early ear pass rejected the candidate; no accepted receipt exists yet, and full Zyn PAD profile/randomness/pitch-zone behavior still contaminates the claim.", "The thing still swarmed instead of blooming, so the witness sent it back into the tank."), "optional Zyn PAD render comparison writes log-mel/envelope report", ["not full Zyn PAD profile/randomness/pitch-zone behavior", "not arbitrary Zyn free-envelope curves"]),
        PatchClaim("zyn/project-vocal-layer/aquasynth", null, "ZynAddSubFX project-authored fixture", "tests/AquaSynth.Dsl.Tests/Fixtures/ZynAddSubFX/ProjectAuthored/vocal-layer.xiz", "project-authored development fixture", "A layered vocal-ish synth where air, body, and breath remain inspectable sound objects.", "Formant/layer pressure before the full speech lane.", "pressure", "ReferenceRebuildCatalog.ZynRebuilds", MissingReceipt("tests/AquaSynth.Dsl.Tests/Fixtures/ZynAddSubFX/ProjectAuthored/vocal-layer.xiz vs AquaSynth vocal-layer rebuild", "formant/layer pressure structure", "No accepted listening pass yet; Zyn kit-item/effect routing and moving formant behavior remain contaminated.", "No witness yet. It may sing, or it may commit fraud in a choir robe."), "structural pressure only", ["no exact Zyn kit-item/effect routing", "formant motion remains approximate"]),
        PatchClaim("dx7/algo32-additive-organ/aquasynth", "patches/dx7/algorithm-32-additive-organ.aqua", "DX7 algorithm topology", "in-code topology reference", "derived structural reference", "DX7 algorithm 32 as the easy six-carrier additive case.", "Operator topology pressure where AquaSynth should avoid inventing FM machinery it does not need.", "sketch", "parser/export tests", MissingReceipt("patches/dx7/algorithm-32-additive-organ.aqua against DX7 algorithm 32 topology notes", "parser/export structure checks for six visible carriers", "No listening claim exists; DX7 envelope execution and ROM output compensation still contaminate any audible verdict.", "No witness yet. This one is still topology paperwork pretending to be a sound."), "structural tests only", ["not exact DX7 envelope execution", "not ROM output compensation"]),
        PatchClaim("dx7/algo8-bright-pair/aquasynth", "patches/dx7/algorithm-8-bright-pair.aqua", "DX7 algorithm topology", "in-code topology reference", "derived structural reference", "DX7 algorithm 8 as an explicit AquaSynth operator graph with visible modulation routes.", "Operator graph pressure for summed stacks and feedback.", "pressure", "parser/export tests", MissingReceipt("patches/dx7/algorithm-8-bright-pair.aqua against DX7 algorithm 8 topology notes", "operator-graph route layout, summed stack structure, and feedback ownership", "No accepted listening pass exists; DX7 feedback calibration and exact EG execution still contaminate the audible claim.", "No witness yet. The graph reads clean, but nobody has signed off on the bite."), "structural tests only", ["not calibrated DX7 feedback register", "not exact DX7 EG"]),
        PatchClaim("dx7/public-domain-mc-mm-5-3/aquasynth", "patches/dx7/public-domain-mc-mm-5-3.aqua", "Dexed/dexed-py render of public-domain DX7 SysEx", "tests/AquaSynth.Dsl.Tests/Fixtures/Dx7/PublicDomain/analog1.syx#13", "public-domain development fixture; not packaged", "A short C3-derived sine-like DX7 voice rebuilt as an AquaSynth parity rung.", "Lawful rendered-audio parity proof for the DX7 lane.", "parity", "Dx7ReferenceParityTests optional dexed-py/Faust render report", new CultCacheListeningReceipt("tests/AquaSynth.Dsl.Tests/Fixtures/Dx7/PublicDomain/analog1.syx#13 versus patches/dx7/public-domain-mc-mm-5-3.aqua", "Dexed reference render, Faust candidate render, log-mel/envelope gate, and manual parity listening", "Metrics pass, but operator-level execution is still not exact DX7 and the receipt does not claim keyboard-feel parity.", "The rebuilt tone bites close enough to the reference to count, even if the machinery under the hood is still wearing AquaSynth clothes."), "score>=0.75 log_mel<0.12 envelope<0.10", ["matches rendered behavior, not exact operator-level execution", "uses AquaSynth ADSR timing matched to audio"]),
        PatchClaim("dx7/public-domain-prc-synth1-calibrated/aquasynth", "patches/dx7/public-domain/prc-synth1-calibrated.aqua", "Dexed/dexed-py render of public-domain DX7 SysEx", "tests/AquaSynth.Dsl.Tests/Fixtures/Dx7/PublicDomain/analog1.syx#17", "public-domain development fixture; not packaged", "A hard DX7 stacked-FM calibration probe that should keep body, attack, and harmonic grit believable.", "DX7 calibration pressure for route scaling, feedback, detune, operator levels, and envelope shape.", "pressure", "artifacts/parity/dx7-prc-synth1/<run>/ when optional dexed-py and Faust are installed", new CultCacheListeningReceipt("tests/AquaSynth.Dsl.Tests/Fixtures/Dx7/PublicDomain/analog1.syx#17 versus patches/dx7/public-domain/prc-synth1-calibrated.aqua", "timestamped Dexed/Faust render artifacts, calibration reports, and repeated user listening corrections recorded in state/scratch.md", "The claim remains contaminated by approximate DX7 EG behavior and unresolved hard-target drift; this is pressure, not accepted parity.", "Under the fingers it keeps some body and grit now, but it still lies about the attack when you stop being polite to it."), "latest recorded calibrated probe: log-mel around 0.17-0.24 depending rung; pressure target, not accepted parity", ["DX7 EG remains approximated", "hard target still calibration pressure"])
    ];

    public static readonly IReadOnlyList<CultCacheSpeechClaimCard> SpeechClaims =
    [
        new("speech/espeak-ng-tiny-workout", new("a, pa, ta, ka, sa, ma", "tests/AquaSynth.Dsl.Tests/EspeakNgGradientDescentTests.cs", "eSpeak NG when ESPEAK_NG or espeak/espeak-ng is installed"), new("neutral human-ish tract controller profile", ["bilabial stop pressure", "alveolar stop pressure", "velar stop pressure", "sibilant turbulence", "nasal velum opening", "open vowel baseline"]), new("AquaSynth should learn a tiny speech-control mapping against generated reference speech instead of hand-filled target paperwork.", "First curriculum rung for Weksa IPA/phonetic utterance rendering."), "pressure", new("artifacts/parity/espeak-ng-gradient-descent/<timestamp>-tiny/training-report.md when eSpeak NG is installed", MissingReceipt("eSpeak NG tiny workout reference utterances versus learned AquaSynth tiny curriculum outputs", "training-loss reduction, rendered utterance artifacts, and curriculum pressure listening", "No accepted human speech witness exists; intelligibility is still weak and the supervision remains eSpeak-shaped.", "No witness yet. The machine is learning mouth shapes, not earning applause."), "optional fixture asserts chained learning loss reduction and records log-mel means", ["not intelligible AquaSynth speech yet", "reference supervision still eSpeak-shaped"])),
        new("speech/compiled-faust-loss-surface", new("controller-output vector from learned phonetic pipeline", "SpeechBackpropagationPipeline + VocalTractNeuralMapper", "compiled native Faust speech-loss probe"), new("current minimal controllable speech synth; future curriculum target slot", ["all neural mapper outputs exported as speech/output/N controls", "cutoff clamped below Nyquist", "candidate batches render without recompiling DSP"]), new("The optimizer can blast many controller attempts through one compiled Faust surface and climb toward a reference match.", "Curriculum learning loss surface before richer anatomy and parity targets."), "proof", new("CompiledFaustRenderedSpeechLossSurfaceReusesOnePatchWithExportedOutputKnobsWhenToolchainIsAvailable", MissingReceipt("compiled Faust speech-loss probe candidates against the current reference target", "exported speech/output/N controls, batch render reuse, and loss-surface sanity checks", "This is still optimizer plumbing only; gradients are finite-difference and the synth rung is minimal.", "It does not sing yet. It just proves the lab bench is wired correctly."), "opt-in native batch proof renders multiple candidates through one compiled patch and checks lower loss for closer controls", ["finite-difference controller gradient, not analytic Faust DSP gradient", "minimal synth rung, not full vocal tract"]))
    ];

    private static CultCacheListeningReceipt MissingReceipt(
        string subject,
        string touchedSurface,
        string remainingContamination,
        string witnessSentence) =>
        new(subject, touchedSurface, remainingContamination, witnessSentence);

    private static CultCachePatchClaimCard PatchClaim(
        string id,
        string? patchPath,
        string synthOrSource,
        string fixtureOrArtifact,
        string licenseScope,
        string perceptualClaim,
        string useContext,
        string tier,
        string latestRenderArtifact,
        CultCacheListeningReceipt latestListeningReceipt,
        string metricSummary,
        IReadOnlyList<string> knownLies) =>
        new(
            id,
            patchPath,
            new CultCacheReferenceTruth(synthOrSource, fixtureOrArtifact, licenseScope),
            new CultCacheIntentTruth(perceptualClaim, useContext),
            tier,
            new CultCacheProofTruth(latestRenderArtifact, latestListeningReceipt, metricSummary, [.. knownLies]));
}
