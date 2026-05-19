using MessagePack;

namespace AquaSynth.Dsl;

public enum PhoneticManner
{
    Vowel,
    Stop,
    Nasal,
    Fricative,
    Affricate,
    Approximant,
    LateralApproximant,
    LateralFricative,
    Trill,
    TapFlap
}

public enum PhoneticPlace
{
    None,
    Bilabial,
    Labiodental,
    Dental,
    Alveolar,
    Postalveolar,
    Retroflex,
    Palatal,
    Velar,
    Uvular,
    Pharyngeal,
    Glottal
}

public enum Phonation
{
    Voiceless,
    Voiced,
    Breathy,
    Creaky
}

public enum AirstreamMechanism
{
    PulmonicEgressive,
    Ejective,
    Implosive,
    Click
}

public enum VowelHeight
{
    None,
    Close,
    NearClose,
    CloseMid,
    Mid,
    OpenMid,
    NearOpen,
    Open
}

public enum VowelBackness
{
    None,
    Front,
    NearFront,
    Central,
    NearBack,
    Back
}

[MessagePackObject]
public sealed record PhoneticFeatures(
    [property: Key(0)]
    PhoneticManner Manner,
    [property: Key(1)]
    PhoneticPlace Place = PhoneticPlace.None,
    [property: Key(2)]
    Phonation Phonation = Phonation.Voiced,
    [property: Key(3)]
    AirstreamMechanism Airstream = AirstreamMechanism.PulmonicEgressive,
    [property: Key(4)]
    VowelHeight Height = VowelHeight.None,
    [property: Key(5)]
    VowelBackness Backness = VowelBackness.None,
    [property: Key(6)]
    bool Rounded = false,
    [property: Key(7)]
    bool Long = false,
    [property: Key(8)]
    bool Nasalized = false,
    [property: Key(9)]
    bool Lateral = false);

[MessagePackObject]
public sealed record PhoneticProsody(
    [property: Key(0)]
    float Stress = 0,
    [property: Key(1)]
    float PitchTarget = 0,
    [property: Key(2)]
    float Intensity = 1,
    [property: Key(3)]
    string Register = "");

public sealed record PhoneticEvent(
    string Id,
    string Ipa,
    PhoneticFeatures Features,
    double StartSeconds = 0,
    double DurationSeconds = 0,
    PhoneticProsody Prosody = null!)
{
    public PhoneticProsody Prosody { get; init; } = Prosody ?? new();
}

public sealed record PhoneticIntent(
    string Id,
    string Language,
    string SourceText,
    IReadOnlyList<PhoneticEvent> Events);

public enum ArticulatoryCapability
{
    OralTract,
    PulmonicPressure,
    Voicing,
    BilabialClosure,
    LabiodentalConstriction,
    DentalConstriction,
    AlveolarConstriction,
    PostalveolarConstriction,
    RetroflexConstriction,
    PalatalConstriction,
    VelarConstriction,
    UvularConstriction,
    PharyngealConstriction,
    GlottalConstriction,
    NasalBranch,
    LateralChannel,
    EjectivePressure,
    ImplosivePressure,
    ClickCavity,
    LipRounding,
    SecondarySource
}

public sealed record VocalTractMorphology(
    string Id,
    string Name,
    float TractLengthCentimeters,
    int SectionCount,
    IReadOnlyList<ArticulatoryCapability> Capabilities);

public enum ArticulatoryDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ArticulatoryDiagnostic(
    ArticulatoryDiagnosticSeverity Severity,
    string Code,
    string Message,
    string EventId = "",
    string Ipa = "",
    ArticulatoryCapability? RequiredCapability = null);

public sealed record ArticulatoryConstraintReport(
    string IntentId,
    string MorphologyId,
    IReadOnlyList<ArticulatoryDiagnostic> Diagnostics)
{
    public bool Accepted => Diagnostics.All(diagnostic => diagnostic.Severity != ArticulatoryDiagnosticSeverity.Error);
}

public enum ArticulatoryGestureKind
{
    TractAreaTarget,
    Constriction,
    Closure,
    Release,
    Turbulence,
    NasalCoupling,
    GlottalSource,
    PressureEvent
}

public sealed record ArticulatoryGesture(
    string Id,
    string SourceEventId,
    ArticulatoryGestureKind Kind,
    double StartSeconds,
    double DurationSeconds,
    string Target = "",
    float Amount = 0);

public sealed record ArticulatoryPlan(
    string Id,
    PhoneticIntent Intent,
    VocalTractMorphology Morphology,
    IReadOnlyList<ArticulatoryGesture> Gestures,
    ArticulatoryConstraintReport Report);

public enum VocalTractPlanStatus
{
    Accepted,
    Rejected
}

public enum VocalTractHostReactionKind
{
    RenderPlan,
    RejectIntent
}

public sealed record VocalTractHostReaction(
    VocalTractHostReactionKind Kind,
    string Summary,
    IReadOnlyList<string> DiagnosticCodes);

public sealed record VocalTractPlanResult(
    VocalTractPlanStatus Status,
    PhoneticIntent Intent,
    VocalTractMorphology Morphology,
    ArticulatoryConstraintReport Report,
    VocalTractHostReaction HostReaction,
    ArticulatoryPlan? Plan = null)
{
    public bool Accepted => Status == VocalTractPlanStatus.Accepted;
}

public static class VocalTractPlanner
{
    public static VocalTractPlanResult Plan(PhoneticIntent intent, VocalTractMorphology morphology)
    {
        var report = VocalTractConstraintEvaluator.Evaluate(intent, morphology);
        if (!report.Accepted)
        {
            var codes = report.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct(StringComparer.Ordinal).ToArray();
            var firstError = report.Diagnostics.First(diagnostic => diagnostic.Severity == ArticulatoryDiagnosticSeverity.Error);
            var reaction = new VocalTractHostReaction(
                VocalTractHostReactionKind.RejectIntent,
                $"Rejected `{intent.Id}` for morphology `{morphology.Id}`: {firstError.Code} on `{firstError.Ipa}`.",
                codes);
            return new VocalTractPlanResult(VocalTractPlanStatus.Rejected, intent, morphology, report, reaction);
        }

        var gestures = BuildGestures(intent);
        var plan = new ArticulatoryPlan($"{intent.Id}:{morphology.Id}:plan", intent, morphology, gestures, report);
        var acceptedReaction = new VocalTractHostReaction(
            VocalTractHostReactionKind.RenderPlan,
            $"Accepted `{intent.Id}` for morphology `{morphology.Id}` with {gestures.Count} articulatory gesture(s).",
            []);
        return new VocalTractPlanResult(VocalTractPlanStatus.Accepted, intent, morphology, report, acceptedReaction, plan);
    }

    private static IReadOnlyList<ArticulatoryGesture> BuildGestures(PhoneticIntent intent)
    {
        var gestures = new List<ArticulatoryGesture>();
        foreach (var phoneticEvent in intent.Events)
        {
            if (phoneticEvent.Features.Phonation != Phonation.Voiceless)
            {
                gestures.Add(new ArticulatoryGesture(
                    $"{phoneticEvent.Id}:glottal",
                    phoneticEvent.Id,
                    ArticulatoryGestureKind.GlottalSource,
                    phoneticEvent.StartSeconds,
                    phoneticEvent.DurationSeconds,
                    "glottis",
                    phoneticEvent.Prosody.Intensity));
            }

            switch (phoneticEvent.Features.Manner)
            {
                case PhoneticManner.Vowel:
                    gestures.Add(new ArticulatoryGesture(
                        $"{phoneticEvent.Id}:vowel-area",
                        phoneticEvent.Id,
                        ArticulatoryGestureKind.TractAreaTarget,
                        phoneticEvent.StartSeconds,
                        phoneticEvent.DurationSeconds,
                        VowelTarget(phoneticEvent.Features),
                        1));
                    break;
                case PhoneticManner.Stop:
                    gestures.Add(new ArticulatoryGesture(
                        $"{phoneticEvent.Id}:closure",
                        phoneticEvent.Id,
                        ArticulatoryGestureKind.Closure,
                        phoneticEvent.StartSeconds,
                        phoneticEvent.DurationSeconds * 0.7,
                        phoneticEvent.Features.Place.ToString(),
                        1));
                    gestures.Add(new ArticulatoryGesture(
                        $"{phoneticEvent.Id}:release",
                        phoneticEvent.Id,
                        ArticulatoryGestureKind.Release,
                        phoneticEvent.StartSeconds + phoneticEvent.DurationSeconds * 0.7,
                        phoneticEvent.DurationSeconds * 0.3,
                        phoneticEvent.Features.Place.ToString(),
                        1));
                    break;
                case PhoneticManner.Fricative:
                case PhoneticManner.LateralFricative:
                    gestures.Add(new ArticulatoryGesture(
                        $"{phoneticEvent.Id}:constriction",
                        phoneticEvent.Id,
                        ArticulatoryGestureKind.Constriction,
                        phoneticEvent.StartSeconds,
                        phoneticEvent.DurationSeconds,
                        phoneticEvent.Features.Place.ToString(),
                        0.85f));
                    gestures.Add(new ArticulatoryGesture(
                        $"{phoneticEvent.Id}:turbulence",
                        phoneticEvent.Id,
                        ArticulatoryGestureKind.Turbulence,
                        phoneticEvent.StartSeconds,
                        phoneticEvent.DurationSeconds,
                        phoneticEvent.Features.Place.ToString(),
                        phoneticEvent.Prosody.Intensity));
                    break;
                case PhoneticManner.Nasal:
                    gestures.Add(new ArticulatoryGesture(
                        $"{phoneticEvent.Id}:nasal",
                        phoneticEvent.Id,
                        ArticulatoryGestureKind.NasalCoupling,
                        phoneticEvent.StartSeconds,
                        phoneticEvent.DurationSeconds,
                        "velum",
                        1));
                    break;
            }
        }

        return gestures;
    }

    private static string VowelTarget(PhoneticFeatures features)
    {
        var rounded = features.Rounded ? "rounded" : "unrounded";
        return $"vowel:{features.Height.ToString().ToLowerInvariant()}:{features.Backness.ToString().ToLowerInvariant()}:{rounded}";
    }
}

public static class VocalTractConstraintEvaluator
{
    public static ArticulatoryConstraintReport Evaluate(PhoneticIntent intent, VocalTractMorphology morphology)
    {
        var capabilities = morphology.Capabilities.ToHashSet();
        var diagnostics = new List<ArticulatoryDiagnostic>();

        foreach (var phoneticEvent in intent.Events)
        {
            Require(diagnostics, capabilities, phoneticEvent, ArticulatoryCapability.OralTract, "missing_oral_tract", "Morphology has no oral tract for phonetic realization.");
            if (phoneticEvent.Features.Phonation != Phonation.Voiceless)
            {
                Require(diagnostics, capabilities, phoneticEvent, ArticulatoryCapability.Voicing, "missing_voicing", "Morphology cannot produce voiced phonation for this event.");
            }

            if (phoneticEvent.Features.Rounded)
            {
                Require(diagnostics, capabilities, phoneticEvent, ArticulatoryCapability.LipRounding, "missing_lip_rounding", "Morphology cannot round or protrude a lip aperture for this event.");
            }

            if (phoneticEvent.Features.Nasalized || phoneticEvent.Features.Manner == PhoneticManner.Nasal)
            {
                Require(diagnostics, capabilities, phoneticEvent, ArticulatoryCapability.NasalBranch, "missing_nasal_branch", "Morphology cannot open a nasal branch for this event.");
            }

            if (phoneticEvent.Features.Lateral ||
                phoneticEvent.Features.Manner is PhoneticManner.LateralApproximant or PhoneticManner.LateralFricative)
            {
                Require(diagnostics, capabilities, phoneticEvent, ArticulatoryCapability.LateralChannel, "missing_lateral_channel", "Morphology cannot form a lateral channel for this event.");
            }

            if (PlaceCapability(phoneticEvent.Features.Place) is { } placeCapability)
            {
                Require(diagnostics, capabilities, phoneticEvent, placeCapability, MissingPlaceCode(phoneticEvent.Features.Place), MissingPlaceMessage(phoneticEvent.Features.Place));
            }

            switch (phoneticEvent.Features.Airstream)
            {
                case AirstreamMechanism.PulmonicEgressive:
                    Require(diagnostics, capabilities, phoneticEvent, ArticulatoryCapability.PulmonicPressure, "missing_pulmonic_pressure", "Morphology cannot produce pulmonic pressure for this event.");
                    break;
                case AirstreamMechanism.Ejective:
                    Require(diagnostics, capabilities, phoneticEvent, ArticulatoryCapability.EjectivePressure, "missing_ejective_pressure", "Morphology cannot produce ejective pressure for this event.");
                    break;
                case AirstreamMechanism.Implosive:
                    Require(diagnostics, capabilities, phoneticEvent, ArticulatoryCapability.ImplosivePressure, "missing_implosive_pressure", "Morphology cannot produce implosive pressure for this event.");
                    break;
                case AirstreamMechanism.Click:
                    Require(diagnostics, capabilities, phoneticEvent, ArticulatoryCapability.ClickCavity, "missing_click_cavity", "Morphology cannot form a click cavity for this event.");
                    break;
            }
        }

        return new ArticulatoryConstraintReport(intent.Id, morphology.Id, diagnostics);
    }

    private static void Require(
        List<ArticulatoryDiagnostic> diagnostics,
        HashSet<ArticulatoryCapability> capabilities,
        PhoneticEvent phoneticEvent,
        ArticulatoryCapability capability,
        string code,
        string message)
    {
        if (capabilities.Contains(capability))
        {
            return;
        }

        diagnostics.Add(new ArticulatoryDiagnostic(
            ArticulatoryDiagnosticSeverity.Error,
            code,
            message,
            phoneticEvent.Id,
            phoneticEvent.Ipa,
            capability));
    }

    private static ArticulatoryCapability? PlaceCapability(PhoneticPlace place) =>
        place switch
        {
            PhoneticPlace.Bilabial => ArticulatoryCapability.BilabialClosure,
            PhoneticPlace.Labiodental => ArticulatoryCapability.LabiodentalConstriction,
            PhoneticPlace.Dental => ArticulatoryCapability.DentalConstriction,
            PhoneticPlace.Alveolar => ArticulatoryCapability.AlveolarConstriction,
            PhoneticPlace.Postalveolar => ArticulatoryCapability.PostalveolarConstriction,
            PhoneticPlace.Retroflex => ArticulatoryCapability.RetroflexConstriction,
            PhoneticPlace.Palatal => ArticulatoryCapability.PalatalConstriction,
            PhoneticPlace.Velar => ArticulatoryCapability.VelarConstriction,
            PhoneticPlace.Uvular => ArticulatoryCapability.UvularConstriction,
            PhoneticPlace.Pharyngeal => ArticulatoryCapability.PharyngealConstriction,
            PhoneticPlace.Glottal => ArticulatoryCapability.GlottalConstriction,
            _ => null
        };

    private static string MissingPlaceCode(PhoneticPlace place) =>
        $"missing_{place.ToString().ToLowerInvariant()}_capability";

    private static string MissingPlaceMessage(PhoneticPlace place) =>
        $"Morphology cannot form the requested {place.ToString().ToLowerInvariant()} articulation.";
}
