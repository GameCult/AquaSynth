namespace AquaSynth.Dsl;

public sealed record ReferenceRebuild(
    string Id,
    string Family,
    string Name,
    string ReferenceId,
    string Script,
    IReadOnlyList<ReferenceFeature> MatchedFeatures,
    IReadOnlyList<ReferenceFeature> MissingFeatures,
    string Notes);

public static class ReferenceRebuildCatalog
{
    public static readonly IReadOnlyList<ReferenceRebuild> ZynRebuilds =
    [
        new(
            "zyn/project-additive-lead/aquasynth",
            "zynaddsubfx",
            "Project additive lead",
            "zyn/project/additive-lead",
            BuiltInScripts.ZynStyleAdditiveLead,
            [
                new("engine_add", "1", "Represented as additive sine partials under named layer authority."),
                new("named_layers", "body,shine", "Layer declarations group partial voices under reusable ADD engine intent."),
                new("additive_harmonic_bank", "2 banks / 4 partials", "Harmonic-bank syntax records authored ADD partial sets before lowering to ordinary voices."),
                new("partial_voice_count", "4", "The additive shape lowers to four AquaSynth partial voices."),
                new("envelope_count", "2", "Amplitude and pitch contour pressure maps to ADSR plus vibrato in the current patch surface."),
                new("lfo_count", "1", "The fixture's amplitude/frequency motion is represented as patch-level vibrato/macro motion.")
            ],
            [
                new("zyn_addsynth_oscillator_detail", "Zyn ADDsynth harmonic phase/bandwidth details", "AquaSynth records harmonic ratios and gains, but not Zyn's full ADDsynth oscillator table semantics."),
                new("zyn_free_envelope_exactness", "Zyn envelope parameter semantics", "The rebuild uses AquaSynth ADSR/motion controls rather than Zyn's exact envelope timing model.")
            ],
            "This is the easy Zyn pressure case: AquaSynth can now say the sound as named harmonic banks while keeping the emitted voice graph plain enough to inspect."),
        new(
            "zyn/project-pad-texture/aquasynth",
            "zynaddsubfx",
            "Project PAD texture",
            "zyn/project/pad-texture",
            BuiltInScripts.ZynStylePadTexture,
            [
                new("engine_pad", "1", "Approximated as slow layered oscillators plus an air/noise layer."),
                new("named_layers", "pad_low,pad_high,air", "Layer declarations separate PAD body from air/noise texture."),
                new("pad_spectral_source", "2 spectral banks / 3 partials", "PAD layers declare spectral partial clouds that emit PADsynth-style FFT wavetable sources before ordinary voice treatment."),
                new("filter_count", "1", "Represented with low-pass/high-pass field sites."),
                new("free_envelope_count", "1", "Approximated with staged voice rate/level envelopes and macro spread."),
                new("voice_rate_level_envelope", "3", "PAD body and air layers use staged rate/level envelopes attached through layer defaults."),
                new("slow_modulation", "patch_lfo", "The rebuild adds slow patch modulation because PAD texture wants motion even though the minimal fixture only marks PAD/filter/free-envelope pressure.")
            ],
            [
                new("padsynth_wavetable_exactness", "Full Zyn PAD engine behavior", "AquaSynth now uses frequency-domain spreading, deterministic random phase, and one inverse FFT, but does not cover every Zyn harmonic profile, randomness control, or pitch-zone table behavior."),
                new("free_envelope_shape", "arbitrary Zyn envelope points", "AquaSynth has staged voice envelopes now, but not arbitrary point/free-mode Zyn envelope curves.")
            ],
            "This is pressure, not a claim of full Zyn PAD parity. AquaSynth can now state a layered FFT spectral cloud, while exact Zyn PAD profile coverage remains separate pressure."),
        new(
            "zyn/project-vocal-layer/aquasynth",
            "zynaddsubfx",
            "Project vocal layer",
            "zyn/project/vocal-layer",
            BuiltInScripts.ZynStyleVocalLayer,
            [
                new("engine_add", "1", "The air/formant layer is represented as a formant-filtered oscillator voice."),
                new("engine_sub", "1", "The body layer is represented as a lower triangle voice."),
                new("layered_instrument", "yes", "Multiple Zyn kit items map to multiple AquaSynth voices."),
                new("named_layers", "air,body,breath", "Layer declarations preserve source identity before voice expansion."),
                new("voice_rate_level_envelope", "3", "Each vocal layer uses a staged rate/level voice envelope for asymmetric onset and release shape."),
                new("formant_filter_count", "1", "Represented with AquaSynth formant filters and a vowel macro."),
                new("effect_count", "1", "Approximated with local drive/soft clipping rather than a Zyn effect slot.")
            ],
            [
                new("kit_item_authority", "Zyn per-kit-item engine/effect routing", "AquaSynth has layered voices but no named kit-item/layer contract with per-layer effect sends."),
                new("formant_motion", "Zyn formant/vowel morph details", "AquaSynth can mix static formants, but richer vowel morphing remains pressure.")
            ],
            "This rebuild keeps the useful part: layered source plus formant intent. It also exposes that repeated Zyn-style kit targets will need explicit layer naming and routing rather than anonymous voice piles.")
    ];

    public static readonly IReadOnlyList<ReferenceRebuild> Dx7Rebuilds =
    [
        new(
            "dx7/algo32-additive-organ/aquasynth",
            "dx7",
            "Algorithm 32 additive organ",
            "dx7/algo32-additive-organ",
            BuiltInScripts.Dx7StyleAlgorithm32AdditiveOrgan,
            [
                new("carrier_operators", "1,2,3,4,5,6", "Six independent carriers map cleanly to six AquaSynth voices."),
                new("modulation_edge_count", "0", "No operator-to-operator modulation is needed for the additive shape."),
                new("self_feedback_operators", "6", "Approximated with fold/drive on the highest partial."),
                new("operator_envelope_approximation", "DX7 EG -> ADSR", "DX7 four-rate/four-level envelopes can now be lowered to labeled ADSR approximations.")
            ],
            [
                new("operator_envelope_exactness", "per-operator four-stage DX7 EG", "AquaSynth has an ADSR approximation helper, not exact DX7 rate/level envelope execution."),
                new("output_compensation", "algorithm ROM COM", "AquaSynth voice gains are explicit and not derived from DX7 COM scaling.")
            ],
            "This is the honest easy case: DX7 algorithm 32 is mostly additive synthesis, so the current voice DSL can represent the carrier layout without pretending to own a full operator graph."),
        new(
            "dx7/algo8-bright-pair/aquasynth",
            "dx7",
            "Algorithm 8 bright pair",
            "dx7/algo8-bright-pair",
            BuiltInScripts.Dx7StyleAlgorithm8BrightPair,
            [
                new("carrier_operators", "1,3", "Represented as carriers in a first-class AquaSynth operator graph."),
                new("modulation_edges", "6->5,4+5->3,2->1", "Represented as explicit operator edges in the graph."),
                new("feedback_sources", "4", "Represented as operator-local feedback approximation on operator 4."),
                new("runtime_macros", "brightness,strike", "Field-site parameters expose useful sound controls without recompilation."),
                new("operator_envelope_approximation", "DX7 EG -> ADSR", "DX7 four-rate/four-level envelopes can now be lowered to labeled ADSR approximations.")
            ],
            [
                new("dx7_feedback_register", "DX7 feedback scaling/register details", "AquaSynth operator feedback is now renderable as a one-sample delayed self-reference, but not calibrated to exact DX7 feedback scaling."),
                new("operator_envelope_exactness", "independent DX7 rate/level envelopes", "AquaSynth has per-operator ADSR approximation, not exact DX7 EG execution.")
            ],
            "This patch is deliberately a pressure test. The new operator graph owns the topology; exact DX7 envelope and feedback-register behavior remain pressure for later refinement."),
        new(
            "dx7/public-domain-mc-mm-5-3/aquasynth",
            "dx7",
            "Public-domain MC-MM 5-3",
            "dx7/public-domain/analog1/13",
            BuiltInScripts.Dx7StylePublicDomainMcMm53,
            [
                new("reference_fixture", "analog1.syx#13", "Public-domain DX7 SysEx fixture from Musical Artifacts artifact 152."),
                new("audio_parity", "score>=0.75 log_mel<0.12 envelope<0.10", "First rendered DX7 parity target using dexed-py reference audio and Faust-rendered AquaSynth audio."),
                new("dominant_pitch", "654.0639Hz", "The rendered reference behaves as a short C3-derived 5x-ratio sine-like tone."),
                new("operator_envelope_approximation", "DX7 EG -> ADSR", "ADSR timing is fitted to the rendered reference envelope.")
            ],
            [
                new("operator_topology_exactness", "2->1 FM pair", "The first parity candidate matches the rendered behavior as a sine-like tone; it does not claim exact DX7 operator-level execution."),
                new("operator_envelope_exactness", "DX7 rate/level EG", "The candidate uses AquaSynth ADSR timing matched to rendered audio.")
            ],
            "This is the first thresholded audio parity rung. It proves the render/compare loop on a lawful fixture without pretending the DX7 internals are solved.")
    ];

    public static readonly IReadOnlyList<ReferenceRebuild> PinkTromboneRebuilds =
    [
        PinkTromboneReference.CurrentAquaSynthGraph()
    ];

    public static IEnumerable<ReferenceRebuild> All()
    {
        foreach (var rebuild in PinkTromboneRebuilds)
        {
            yield return rebuild;
        }

        foreach (var rebuild in ZynRebuilds)
        {
            yield return rebuild;
        }

        foreach (var rebuild in Dx7Rebuilds)
        {
            yield return rebuild;
        }
    }
}
