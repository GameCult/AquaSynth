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

public sealed record PhoneticFeatures(
    PhoneticManner Manner,
    PhoneticPlace Place = PhoneticPlace.None,
    Phonation Phonation = Phonation.Voiced,
    AirstreamMechanism Airstream = AirstreamMechanism.PulmonicEgressive,
    VowelHeight Height = VowelHeight.None,
    VowelBackness Backness = VowelBackness.None,
    bool Rounded = false,
    bool Long = false,
    bool Nasalized = false,
    bool Lateral = false);

public sealed record PhoneticProsody(
    float Stress = 0,
    float PitchTarget = 0,
    float Intensity = 1,
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
