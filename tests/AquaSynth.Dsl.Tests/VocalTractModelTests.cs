using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

public sealed class VocalTractModelTests
{
    [Fact]
    public void PhoneticIntentKeepsWeksaHandoffInspectable()
    {
        var intent = new PhoneticIntent(
            "weksa-utterance-1",
            "weksa",
            "/qʼoru/",
            [
                new PhoneticEvent(
                    "phone-1",
                    "qʼ",
                    new PhoneticFeatures(
                        PhoneticManner.Stop,
                        PhoneticPlace.Uvular,
                        Phonation.Voiceless,
                        AirstreamMechanism.Ejective),
                    StartSeconds: 0.0,
                    DurationSeconds: 0.08,
                    Prosody: new PhoneticProsody(Stress: 1.0f, PitchTarget: 0.2f, Intensity: 0.8f))
            ]);

        var phone = Assert.Single(intent.Events);
        Assert.Equal("weksa", intent.Language);
        Assert.Equal("qʼ", phone.Ipa);
        Assert.Equal(PhoneticPlace.Uvular, phone.Features.Place);
        Assert.Equal(AirstreamMechanism.Ejective, phone.Features.Airstream);
        Assert.Equal(0.08, phone.DurationSeconds);
        Assert.Equal(1.0f, phone.Prosody.Stress);
    }

    [Fact]
    public void MorphologyVetoesPhysicallyImpossibleBilabial()
    {
        var intent = new PhoneticIntent(
            "beak-test",
            "weksa",
            "/pa/",
            [
                new PhoneticEvent(
                    "phone-p",
                    "p",
                    new PhoneticFeatures(
                        PhoneticManner.Stop,
                        PhoneticPlace.Bilabial,
                        Phonation.Voiceless),
                    DurationSeconds: 0.06)
            ]);
        var beak = new VocalTractMorphology(
            "beak",
            "Beaked speaker",
            22.0f,
            44,
            [
                ArticulatoryCapability.OralTract,
                ArticulatoryCapability.PulmonicPressure,
                ArticulatoryCapability.Voicing,
                ArticulatoryCapability.AlveolarConstriction,
                ArticulatoryCapability.VelarConstriction
            ]);

        var report = VocalTractConstraintEvaluator.Evaluate(intent, beak);

        Assert.False(report.Accepted);
        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal("phone-p", diagnostic.EventId);
        Assert.Equal("p", diagnostic.Ipa);
        Assert.Equal("missing_bilabial_capability", diagnostic.Code);
        Assert.Equal(ArticulatoryCapability.BilabialClosure, diagnostic.RequiredCapability);
    }

    [Fact]
    public void HumanBaselineAcceptsBilabialWhenCapabilityExists()
    {
        var intent = new PhoneticIntent(
            "human-test",
            "ipa",
            "/pa/",
            [
                new PhoneticEvent(
                    "phone-p",
                    "p",
                    new PhoneticFeatures(
                        PhoneticManner.Stop,
                        PhoneticPlace.Bilabial,
                        Phonation.Voiceless),
                    DurationSeconds: 0.06)
            ]);
        var human = new VocalTractMorphology(
            "human",
            "Human baseline",
            17.0f,
            44,
            [
                ArticulatoryCapability.OralTract,
                ArticulatoryCapability.PulmonicPressure,
                ArticulatoryCapability.Voicing,
                ArticulatoryCapability.BilabialClosure
            ]);

        var report = VocalTractConstraintEvaluator.Evaluate(intent, human);

        Assert.True(report.Accepted);
        Assert.Empty(report.Diagnostics);
    }

    [Fact]
    public void BeakBilabialPlanResultIsHostActionable()
    {
        var result = VocalTractPlanner.Plan(
            new PhoneticIntent(
                "beak-pa",
                "weksa",
                "/pa/",
                [
                    new PhoneticEvent(
                        "phone-p",
                        "p",
                        new PhoneticFeatures(
                            PhoneticManner.Stop,
                            PhoneticPlace.Bilabial,
                            Phonation.Voiceless),
                        DurationSeconds: 0.06),
                    new PhoneticEvent(
                        "phone-a",
                        "a",
                        new PhoneticFeatures(
                            PhoneticManner.Vowel,
                            Height: VowelHeight.Open,
                            Backness: VowelBackness.Front),
                        StartSeconds: 0.06,
                        DurationSeconds: 0.14)
                ]),
            new VocalTractMorphology(
                "beak",
                "Beaked speaker",
                22.0f,
                44,
                [
                    ArticulatoryCapability.OralTract,
                    ArticulatoryCapability.PulmonicPressure,
                    ArticulatoryCapability.Voicing,
                    ArticulatoryCapability.AlveolarConstriction,
                    ArticulatoryCapability.VelarConstriction
                ]));

        Assert.False(result.Accepted);
        Assert.Null(result.Plan);
        Assert.Equal(VocalTractHostReactionKind.RejectIntent, result.HostReaction.Kind);
        Assert.Contains("missing_bilabial_capability", result.HostReaction.DiagnosticCodes);
        Assert.Contains("phone-p", result.Report.Diagnostics.Select(diagnostic => diagnostic.EventId));
        Assert.Contains("`p`", result.HostReaction.Summary);
    }

    [Fact]
    public void AcceptedVowelPlanEmitsInspectableGestures()
    {
        var result = VocalTractPlanner.Plan(
            new PhoneticIntent(
                "human-a",
                "ipa",
                "/a/",
                [
                    new PhoneticEvent(
                        "phone-a",
                        "a",
                        new PhoneticFeatures(
                            PhoneticManner.Vowel,
                            Height: VowelHeight.Open,
                            Backness: VowelBackness.Front),
                        DurationSeconds: 0.2,
                        Prosody: new PhoneticProsody(Intensity: 0.75f))
                ]),
            new VocalTractMorphology(
                "human",
                "Human baseline",
                17.0f,
                44,
                [
                    ArticulatoryCapability.OralTract,
                    ArticulatoryCapability.PulmonicPressure,
                    ArticulatoryCapability.Voicing
                ]));

        Assert.True(result.Accepted);
        Assert.Equal(VocalTractHostReactionKind.RenderPlan, result.HostReaction.Kind);
        var plan = Assert.IsType<ArticulatoryPlan>(result.Plan);
        Assert.Empty(plan.Report.Diagnostics);
        Assert.Contains(plan.Gestures, gesture =>
            gesture.Kind == ArticulatoryGestureKind.GlottalSource &&
            gesture.SourceEventId == "phone-a" &&
            gesture.Amount == 0.75f);
        Assert.Contains(plan.Gestures, gesture =>
            gesture.Kind == ArticulatoryGestureKind.TractAreaTarget &&
            gesture.SourceEventId == "phone-a" &&
            gesture.Target == "vowel:open:front:unrounded");
    }
}
