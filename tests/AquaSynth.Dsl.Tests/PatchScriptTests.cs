using AquaSynth.Dsl;

namespace AquaSynth.Dsl.Tests;

public sealed class PatchScriptTests
{
    private const string WobbleTalker =
        "d w=saw f=55 g=.18 s=.8 d=.25 l=.34 h=.02 drv=.3 fl=.08 fm=2 fmi=.8 fmd=.7 fs=520:90:.7,1250:170:1,2600:320:.45 fmix=.35;" +
        "mod n=wob hz=4 w=tri g=.42 l=.48 fmix=.38 fmi=1.6 drv=.2 fl=.14;" +
        "v;" +
        "v f=110 g=.08 du=.42";

    [Fact]
    public void ParserExpandsDefaultsModBusAndVoices()
    {
        var patch = PatchScript.Parse(WobbleTalker);

        Assert.Equal(2, patch.Voices.Count);
        Assert.Equal(6, patch.Controls.Count);
        Assert.Equal(Waveform.Sawtooth, patch.Voices[0].Oscillator.Waveform);
        Assert.Equal(3, patch.Voices[0].Formants.Count);
    }

    [Fact]
    public void ParserSupportsRouteListModBus()
    {
        var patch = PatchScript.Parse("bus n=sway w=tri hz=2 to=g:.12,l:-.2,fmix:.35,fmi:1.4;v w=saw f=80");

        Assert.Equal(4, patch.Controls.Count);
        Assert.Contains(patch.Controls, lane => lane.Name == "sway_gain" && lane.Modulator.Target == ModTarget.Gain);
        Assert.Contains(patch.Controls, lane => lane.Name == "sway_lpf" && lane.Modulator.Target == ModTarget.LowPass);
        Assert.Contains(patch.Controls, lane => lane.Name == "sway_formant_mix" && lane.Modulator.Target == ModTarget.FormantMix);
        Assert.Contains(patch.Controls, lane => lane.Name == "sway_fm_index" && lane.Modulator.Target == ModTarget.FmIndex);
    }

    [Fact]
    public void ParserFoldsFieldOnlyLinesIntoPreviousCommand()
    {
        var patch = PatchScript.Parse("""
            voice
                wave=square
                freq=80
                gain=0.2
                sustain=0.1
                decay=0.2
            """);

        var voice = Assert.Single(patch.Voices);
        Assert.Equal(Waveform.Square, voice.Oscillator.Waveform);
        Assert.Equal(80, voice.Oscillator.FrequencyHz);
        Assert.Equal(0.2f, voice.Gain, 5);
    }

    [Fact]
    public void ParserSupportsExplicitControlLane()
    {
        var patch = PatchScript.Parse("lfo n=wob t=p w=sin hz=6 d=.04 ph=.25 b=.01;v w=saw f=80");

        var lane = Assert.Single(patch.Controls);
        Assert.Equal("wob", lane.Name);
        Assert.Equal(ModTarget.Pitch, lane.Modulator.Target);
        Assert.Equal(6, lane.Modulator.FrequencyHz);
        Assert.Equal(.04f, lane.Modulator.Depth, 5);
        Assert.Equal(.25f, lane.Modulator.Phase, 5);
        Assert.Equal(.01f, lane.Modulator.Bias, 5);
    }

    [Fact]
    public void ParserSupportsVoiceVowelFrames()
    {
        var patch = PatchScript.Parse("v w=saw f=90 fmix=.7 vowel_hz=.8 vowels=600:90:1,1200:160:.7|500:80:1,900:120:.8");

        var voice = Assert.Single(patch.Voices);
        Assert.Equal(2, voice.FormantFrames.Count);
        Assert.Equal(2, voice.FormantFrames[0].Formants.Count);
        Assert.Equal(.8f, voice.FormantFrameRateHz, 5);

        var faust = FaustEmitter.Emit(patch).Source;
        Assert.Contains("wrap01(age * 0.8)", faust);
        Assert.Contains("fi.resonbp(600", faust);
        Assert.Contains("fi.resonbp(500", faust);
    }

    [Fact]
    public void ParserSupportsPinkTromboneStyleTractVoice()
    {
        var patch = PatchScript.Parse("""
            param path=/pink/tenseness default=.6 min=0 max=1 step=.001
            tract freq=140 intensity=.72 tenseness=@/pink/tenseness tongue_index=18 tongue_diameter=1.4 velum=.2 constriction_index=32 constriction_diameter=.45 turbulence=.8
            """);

        var voice = Assert.Single(patch.Voices);
        Assert.NotNull(voice.Tract);
        Assert.Equal(44, voice.Tract.Sections);
        Assert.Equal(28, voice.Tract.NoseSections);
        Assert.Equal(18, voice.Tract.TongueIndex);
        Assert.Equal(.45f, voice.Tract.ConstrictionDiameter, 5);
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/voices/0/tract/tenseness");

        var faust = FaustEmitter.Emit(patch, new FaustExportOptions("tract_voice")).Source;
        Assert.Contains("tract_lf", faust);
        Assert.Contains("tract_frication", faust);
        Assert.Contains("tract_nose", faust);
        Assert.Contains("fi.resonbp", faust);
    }

    [Fact]
    public void ParserPreservesDeclaredPatchParameters()
    {
        var patch = PatchScript.Parse("param name=brightness path=/macro/brightness default=.45 min=0 max=1 step=.001 unit=normalized rate=control;v w=saw f=80");

        var parameter = Assert.Single(patch.Parameters);
        Assert.Equal("/macro/brightness", parameter.Path);
        Assert.Equal("brightness", parameter.Label);
        Assert.Equal(.45f, parameter.Default, 5);
        Assert.Equal(0, parameter.Min);
        Assert.Equal(1, parameter.Max);
        Assert.Equal(.001f, parameter.Step, 5);
        Assert.Equal("normalized", parameter.Unit);
        Assert.Equal("control", parameter.AutomationRate);
    }

    [Fact]
    public void ParserRejectsDuplicatePatchParameterPaths()
    {
        var exception = Assert.Throws<PatchScriptException>(() =>
            PatchScript.Parse("param path=/macro/brightness;param path=/macro/brightness;v w=sin"));

        Assert.Contains("duplicate parameter path", exception.Message);
    }

    [Fact]
    public void ParserBindsParameterReferencesAtFieldSites()
    {
        var patch = PatchScript.Parse("param path=/macro/brightness default=.45 min=0 max=1 step=.001;v w=saw f=80 lpf=@/macro/brightness");

        var binding = Assert.Single(patch.ParameterBindings);
        Assert.Equal("/voices/0/filter/lpf", binding.FieldPath);
        Assert.Equal("/macro/brightness", binding.ParameterPath);
        Assert.Equal(.45f, patch.Voices[0].Filter.LowPass, 5);
    }

    [Fact]
    public void ParserRejectsUnknownParameterReferences()
    {
        var exception = Assert.Throws<PatchScriptException>(() =>
            PatchScript.Parse("v w=saw f=80 lpf=@/macro/brightness"));

        Assert.Contains("unknown parameter `/macro/brightness`", exception.Message);
    }

    [Fact]
    public void BuiltInExampleParsesAndExportsFaust()
    {
        var patch = PatchScript.Parse(BuiltInScripts.PatchScriptExample);
        var export = FaustEmitter.Emit(patch);

        Assert.Equal(3, patch.Voices.Count);
        Assert.Equal(5, patch.Controls.Count);
        Assert.Contains("patch_mod_formant_mix", export.Source);
    }

    [Fact]
    public void SfxrAtomsAndMutationsParse()
    {
        var named = PatchScript.Parse("laser");
        var verbose = PatchScript.Parse("sfxr preset=laser mutate_seed=9 mutate=0.01");
        var golfed = PatchScript.Parse("s p=laser ms=9 m=0.01");

        Assert.Single(named.Voices);
        Assert.Single(verbose.Voices);
        Assert.Single(golfed.Voices);
        Assert.Equal(verbose.Voices[0].Oscillator.FrequencyHz, golfed.Voices[0].Oscillator.FrequencyHz);
    }

    [Fact]
    public void ScriptMetricsScoreTerseAndReadableInputs()
    {
        var terse = PatchScriptScoring.Measure("v w=sq f=80 g=.2 s=.1 d=.2");
        var readable = PatchScriptScoring.Measure("""
            voice wave=square freq=80 gain=0.2 sustain=0.1 decay=0.2
            """);

        Assert.True(terse.TerseScore > readable.TerseScore);
        Assert.True(readable.ReadabilityScore > terse.ReadabilityScore);
        Assert.InRange(terse.BalancedScore, 0, 1);
    }

    [Fact]
    public void AudioAnalyzerComparesSimpleBuffers()
    {
        var samples = Enumerable.Range(0, 2048)
            .Select(i => MathF.Sin(i * MathF.Tau * 440 / 44100) * 0.2f)
            .ToArray();

        var comparison = new AudioAnalyzer().Compare(samples, samples);

        Assert.True(comparison.Reference.Features.Peak > 0.19f);
        Assert.True(comparison.Score > 0.99f);
    }

    [Fact]
    public void AquaSynthPresetsExportFaust()
    {
        foreach (var patch in new[] { Presets.AquaSynthPluck(), Presets.AquaSynthHeartbeat(), Presets.AquaSynthVoice(), Presets.Sfxr("pickup") })
        {
            var export = FaustEmitter.Emit(patch);
            Assert.Contains("process =", export.Source);
        }
    }

    [Fact]
    public void ClassicAbstractGolfScriptParsesAndExportsFaust()
    {
        var patch = PatchScript.Parse(BuiltInScripts.ClassicSfxrAbstractGolfScript);
        var export = FaustEmitter.Emit(patch);

        Assert.Equal(7, patch.Voices.Count);
        Assert.Contains("process =", export.Source);
    }

    [Fact]
    public void BuiltInReferenceScriptsParseAndExportFaust()
    {
        foreach (var (family, name, script) in BuiltInScripts.ReferenceScripts())
        {
            var exception = Record.Exception(() =>
            {
                var patch = PatchScript.Parse(script);
                var export = FaustEmitter.Emit(patch, new FaustExportOptions($"{family}_{name}".Replace('-', '_')));
                Assert.Contains("process =", export.Source);
            });

            Assert.Null(exception);
        }
    }

    [Fact]
    public void PatchLibraryScriptsParseAndExportFaust()
    {
        var libraryRoot = Path.Combine(RepositoryRoot(), "patches");
        var files = Directory.GetFiles(libraryRoot, "*.aqua", SearchOption.AllDirectories);

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var script = File.ReadAllText(file);
            var patch = PatchScript.Parse(script);
            var export = FaustEmitter.Emit(
                patch,
                new FaustExportOptions(Path.GetFileNameWithoutExtension(file).Replace('-', '_')));

            Assert.True(patch.Voices.Count > 0 || patch.OperatorGraphs.Count > 0, file);
            Assert.Contains("process =", export.Source);
        }
    }

    [Fact]
    public void AdvancedReferenceScriptsExerciseLayeredPatchFeatures()
    {
        foreach (var (name, script) in BuiltInScripts.AdvancedReferenceScripts)
        {
            var patch = PatchScript.Parse(script);

            Assert.True(patch.Voices.Count >= 4, $"{name} should demonstrate layered voices.");
            Assert.True(patch.Controls.Count >= 1, $"{name} should demonstrate modulation.");
            Assert.Contains(patch.Voices, voice => voice.Fm.Index > 0 || voice.Formants.Count > 0 || voice.Color.NoiseMix > 0);
        }
    }

    [Fact]
    public void Dx7StyleReferenceRebuildsParseExportAndDeclarePressure()
    {
        foreach (var rebuild in ReferenceRebuildCatalog.Dx7Rebuilds)
        {
            var patch = PatchScript.Parse(rebuild.Script);
            var export = FaustEmitter.Emit(patch, new FaustExportOptions(rebuild.Id.Replace('/', '_').Replace('-', '_')));

            Assert.Contains("process =", export.Source);
            Assert.NotEmpty(rebuild.MatchedFeatures);
            Assert.NotEmpty(rebuild.MissingFeatures);
            Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "operator_envelope_approximation");
            Assert.Contains(rebuild.MissingFeatures, feature => feature.Name == "operator_envelope_exactness");
            Assert.All(rebuild.MissingFeatures, feature => Assert.False(string.IsNullOrWhiteSpace(feature.Notes)));
        }
    }

    [Fact]
    public void ZynStyleReferenceRebuildsParseExportAndDeclarePressure()
    {
        foreach (var rebuild in ReferenceRebuildCatalog.ZynRebuilds)
        {
            var patch = PatchScript.Parse(rebuild.Script);
            var export = FaustEmitter.Emit(patch, new FaustExportOptions(rebuild.Id.Replace('/', '_').Replace('-', '_')));

            Assert.Contains("process =", export.Source);
            Assert.NotEmpty(patch.Layers);
            Assert.NotEmpty(rebuild.MatchedFeatures);
            Assert.NotEmpty(rebuild.MissingFeatures);
            Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "named_layers");
            if (rebuild.ReferenceId == "zyn/project/pad-texture")
            {
                Assert.NotEmpty(patch.SpectralBanks);
                Assert.Contains(patch.SpectralBanks, bank => bank.Treatment.RateLevelEnvelope is not null);
            }
            if (rebuild.ReferenceId == "zyn/project/vocal-layer")
            {
                Assert.Contains(patch.Voices, voice => voice.RateLevelEnvelope is not null);
            }
            Assert.All(rebuild.MissingFeatures, feature => Assert.False(string.IsNullOrWhiteSpace(feature.Notes)));
        }
    }

    [Fact]
    public void PinkTromboneReferenceDeclaresMissingWaveguideAuthority()
    {
        var reference = PinkTromboneReference.ToReferencePatch();
        var rebuild = Assert.Single(ReferenceRebuildCatalog.PinkTromboneRebuilds);
        var patch = PatchScript.Parse(rebuild.Script);
        var export = FaustEmitter.Emit(patch, new FaustExportOptions("pink_trombone_proxy"));

        Assert.Equal("pink-trombone", rebuild.Family);
        Assert.Contains(reference.Features, feature => feature.Name == "main_tract_waveguide_cells" && feature.Value == "44");
        Assert.Contains(reference.Features, feature => feature.Name == "nose_waveguide_cells" && feature.Value == "28");
        Assert.Contains(reference.Features, feature => feature.Name == "tract_sample_rate" && feature.Value == "2x-audio-sample-rate");
        Assert.Contains(reference.Features, feature => feature.Name == "reflection_formula");
        Assert.Contains(reference.Parameters, parameter => parameter.Path == "/pink/tongue/index");
        Assert.Contains(reference.Parameters, parameter => parameter.Path == "/pink/constriction/diameter");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "tract_voice_authority");
        Assert.Contains(rebuild.MissingFeatures, feature => feature.Name == "main_tract_waveguide_cells");
        Assert.Contains(rebuild.MissingFeatures, feature => feature.Name == "diameter_to_reflection_coefficients");
        Assert.Contains(rebuild.MissingFeatures, feature => feature.Name == "nose_waveguide_cells");
        Assert.NotEmpty(patch.Parameters);
        Assert.Contains("process =", export.Source);
    }

    [Fact]
    public void ZynReferenceRebuildsTrackFixtureFeaturePressure()
    {
        var fixtures = new Dictionary<string, string>
        {
            ["zyn/project/additive-lead"] = Path.Combine("ZynAddSubFX", "ProjectAuthored", "additive-lead.xiz"),
            ["zyn/project/pad-texture"] = Path.Combine("ZynAddSubFX", "ProjectAuthored", "pad-texture.xiz"),
            ["zyn/project/vocal-layer"] = Path.Combine("ZynAddSubFX", "ProjectAuthored", "vocal-layer.xiz")
        };

        foreach (var rebuild in ReferenceRebuildCatalog.ZynRebuilds)
        {
            var instrument = ZynInstrumentReader.ParseFile(FixturePath(fixtures[rebuild.ReferenceId]));
            var sourceFeatures = instrument.Features();

            foreach (var matched in rebuild.MatchedFeatures)
            {
                if (sourceFeatures.Any(feature => feature.Name == matched.Name))
                {
                    Assert.Contains(sourceFeatures, feature => feature.Name == matched.Name && feature.Value == matched.Value);
                }
            }
        }
    }

    [Fact]
    public void LayerSyntaxNamesReusableVoiceGroups()
    {
        var patch = PatchScript.Parse("""
            layer name=kick engine=sub min_key=36 max_key=36 gain=.4 wave=sine freq=55 attack=.001 sustain=.04 decay=.18
            layer name=air engine=add min_key=60 max_key=84 gain=.12 wave=saw lpf=.72
            voice layer=kick
            voice layer=air freq=440
            voice layer=air freq=660 gain=.08
            """);

        Assert.Equal(2, patch.Layers.Count);
        Assert.Equal(3, patch.Voices.Count);
        Assert.Equal("kick", patch.Voices[0].Layer?.Name);
        Assert.Equal("sub", patch.Voices[0].Layer?.Engine);
        Assert.Equal(36, patch.Voices[0].Layer?.MinKey);
        Assert.Equal(36, patch.Voices[0].Layer?.MaxKey);
        Assert.Equal(.4f, patch.Voices[0].Gain, 5);
        Assert.Equal("air", patch.Voices[1].Layer?.Name);
        Assert.Equal("air", patch.Voices[2].Layer?.Name);
        Assert.Equal(.08f, patch.Voices[2].Gain, 5);
    }

    [Fact]
    public void LayerSyntaxRejectsUnknownOrDuplicateLayers()
    {
        Assert.Throws<PatchScriptException>(() => PatchScript.Parse("""
            layer name=body
            layer name=body
            voice layer=body
            """));

        Assert.Throws<PatchScriptException>(() => PatchScript.Parse("voice layer=missing"));
    }

    [Fact]
    public void HarmonicBankSyntaxExpandsNamedLayerPartials()
    {
        var patch = PatchScript.Parse("""
            layer name=drawbar engine=add gain=.2 wave=sine attack=.01
            harmonics layer=drawbar root=110 partials=1:.5,2:.25,3:.125
            """);
        var export = FaustEmitter.Emit(patch);

        var bank = Assert.Single(patch.HarmonicBanks);
        Assert.Equal("drawbar", bank.LayerName);
        Assert.Equal(110, bank.RootFrequencyHz);
        Assert.Equal(3, bank.Partials.Count);
        Assert.Equal(3, patch.Voices.Count);
        Assert.All(patch.Voices, voice => Assert.Equal("drawbar", voice.Layer?.Name));
        Assert.Equal([110, 220, 330], patch.Voices.Select(voice => voice.Oscillator.FrequencyHz).ToArray());
        Assert.Equal(.5f, patch.Voices[0].Gain, 5);
        Assert.Equal(.25f, patch.Voices[1].Gain, 5);
        Assert.Equal(.125f, patch.Voices[2].Gain, 5);
        Assert.Contains("process =", export.Source);
    }

    [Fact]
    public void HarmonicBankSyntaxRejectsUnknownLayerOrBadPartials()
    {
        Assert.Throws<PatchScriptException>(() =>
            PatchScript.Parse("harmonics layer=missing root=110 partials=1:.5"));

        Assert.Throws<PatchScriptException>(() => PatchScript.Parse("""
            layer name=drawbar
            harmonics layer=drawbar root=110 partials=1
            """));
    }

    [Fact]
    public void LayeredVoiceRateLevelEnvelopeParsesAndExports()
    {
        var patch = PatchScript.Parse("""
            param path=/macro/gate default=.9 min=.1 max=3 step=.01
            layer name=pad engine=pad gain=.08 env=rl rates=.1,.2,.3,.4 levels=1,.8,.5,0 curves=lin,exp,exp,lin gate=.9
            voice layer=pad freq=220 gate=@/macro/gate
            """);
        var export = FaustEmitter.Emit(patch);

        var voice = Assert.Single(patch.Voices);
        Assert.NotNull(voice.RateLevelEnvelope);
        Assert.Equal(.9f, voice.Note.GateSeconds, 5);
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/voices/0/note/gate");
        Assert.Equal(.1f, voice.Envelope.AttackSeconds, 5);
        Assert.Equal(.5f, voice.Envelope.SustainLevel, 5);
        Assert.Equal(RateLevelCurve.Exponential, voice.RateLevelEnvelope.Curve2);
        Assert.Contains("rl4_env(0.1, 1, 0, 0.2, 0.8, 1, 0.3, 0.5, 1, 0.4, 0, 0, patch_param_0)", export.Source);
    }

    [Fact]
    public void SpectralBankSyntaxEmitsPadWavetableSource()
    {
        var patch = PatchScript.Parse("""
            layer name=pad engine=pad gain=.08 wave=saw env=rl rates=.1,.2,.3,.4 levels=1,.8,.5,0 gate=.9
            spectrum layer=pad root=100 spread=.01 partials=1:.08,1.5:.04
            """);
        var export = FaustEmitter.Emit(patch);

        var bank = Assert.Single(patch.SpectralBanks);
        Assert.Equal("pad", bank.LayerName);
        Assert.Equal(100, bank.RootFrequencyHz);
        Assert.Equal(.01f, bank.SpreadRatio, 5);
        Assert.Equal(2, bank.Partials.Count);
        Assert.Empty(patch.Voices);
        Assert.Equal("pad", bank.Treatment.Layer?.Name);
        Assert.NotNull(bank.Treatment.RateLevelEnvelope);
        Assert.Equal(100, bank.Treatment.Oscillator.FrequencyHz);
        Assert.Equal(.08f, bank.Treatment.Gain, 5);
        Assert.Contains("spectral_0_wave = waveform", export.Source);
        Assert.Contains("spectral_0_read_frac", export.Source);
        Assert.Contains("spectral_0_wavetable = (spectral_0_wave, spectral_0_read_index : rdtable)", export.Source);
        Assert.Contains("process =", export.Source);
    }

    [Fact]
    public void LayerLowPassQParsesAndExportsAsExplicitFilterDamping()
    {
        var patch = PatchScript.Parse("""
            layer name=pad engine=pad gain=.08 lpf=.3 lpf_q=.5 lpf_order=4
            voice layer=pad freq=220
            """);
        var export = FaustEmitter.Emit(patch);

        var voice = Assert.Single(patch.Voices);
        Assert.Equal(.5f, voice.Filter.LowPassQ, 5);
        Assert.Contains("fi.resonlp(max(20.0, clip01(0.3", export.Source);
        Assert.Contains("max(0.1, 0.5)", export.Source);
        Assert.Contains(") : fi.resonlp(max(20.0, clip01(0.3", export.Source);
    }

    [Fact]
    public void BandPassAndNotchParseAndExportAsFilterAuthority()
    {
        var patch = PatchScript.Parse("v w=saw f=220 bpf=.35 bpf_q=4 bpf_order=3 notch=.6 notch_q=8 notch_order=2");
        var export = FaustEmitter.Emit(patch);

        var voice = Assert.Single(patch.Voices);
        Assert.Equal(.35f, voice.Filter.BandPass, 5);
        Assert.Equal(4, voice.Filter.BandPassQ, 5);
        Assert.Equal(3, voice.Filter.BandPassOrder);
        Assert.Equal(.6f, voice.Filter.Notch, 5);
        Assert.Equal(8, voice.Filter.NotchQ, 5);
        Assert.Equal(2, voice.Filter.NotchOrder);
        Assert.Contains("fi.resonbp(max(20.0, clip01(0.35) * 18000.0), max(0.1, 4), 1.0)", export.Source);
        Assert.Contains("fi.notchw(max(1.0, (max(20.0, clip01(0.6) * 18000.0)) / max(0.1, 8)), max(20.0, clip01(0.6) * 18000.0))", export.Source);
    }

    [Fact]
    public void SpectralBankSeparatesTableRootFromPlaybackFrequency()
    {
        var patch = PatchScript.Parse("""
            layer name=pad engine=pad gain=.08
            spectrum layer=pad root=77.7813 freq=261.6256 spread=0 partials=1:.08
            """);

        var bank = Assert.Single(patch.SpectralBanks);
        Assert.Equal(77.7813f, bank.RootFrequencyHz, 4);
        Assert.Equal(261.6256f, bank.Treatment.Note.FrequencyHz, 4);
    }

    [Fact]
    public void SpectralBankParsesPadProfileFields()
    {
        var patch = PatchScript.Parse("""
            layer name=pad engine=pad gain=.08
            spectrum layer=pad root=77.7813 freq=261.6256 pad_mode=bandwidth pad_bandwidth=485 pad_bwscale=3 pad_profile=gaussian:99:8:12:55:127:sine:mult:80:20:yes:full pad_position=sine:20:40:60 partials=1:.08,2:.04
            """);

        var bank = Assert.Single(patch.SpectralBanks);
        Assert.Equal(PadSpectrumMode.Bandwidth, bank.Profile.Mode);
        Assert.Equal(485, bank.Profile.Bandwidth);
        Assert.Equal(3, bank.Profile.BandwidthScale);
        Assert.Equal(PadProfileBaseType.Gaussian, bank.Profile.HarmonicProfile.BaseType);
        Assert.Equal(8, bank.Profile.HarmonicProfile.FrequencyMultiplier);
        Assert.Equal(PadProfileAmplitudeType.Sine, bank.Profile.HarmonicProfile.AmplitudeType);
        Assert.Equal(PadProfileAmplitudeMode.Mult, bank.Profile.HarmonicProfile.AmplitudeMode);
        Assert.Equal(PadHarmonicPositionType.Sine, bank.Profile.HarmonicPosition.Type);
        Assert.Equal(60, bank.Profile.HarmonicPosition.Parameter3);
    }

    [Fact]
    public void SpectralBankStillParsesLegacyZynPadProfileFields()
    {
        var patch = PatchScript.Parse("""
            layer name=pad engine=pad gain=.08
            spectrum layer=pad root=77.7813 freq=261.6256 zyn_mode=bandwidth zyn_bandwidth=485 zyn_bwscale=3 zyn_profile=gaussian:99:8:12:55:127:sine:mult:80:20:yes:full zyn_position=sine:20:40:60 partials=1:.08,2:.04
            """);

        var bank = Assert.Single(patch.SpectralBanks);
        Assert.Equal(PadSpectrumMode.Bandwidth, bank.Profile.Mode);
        Assert.Equal(485, bank.Profile.Bandwidth);
        Assert.Equal(3, bank.Profile.BandwidthScale);
        Assert.Equal(PadProfileAmplitudeType.Sine, bank.Profile.HarmonicProfile.AmplitudeType);
        Assert.Equal(PadHarmonicPositionType.Sine, bank.Profile.HarmonicPosition.Type);
    }

    [Fact]
    public void SpectralBankSyntaxRejectsUnknownLayerOrBadSpread()
    {
        Assert.Throws<PatchScriptException>(() =>
            PatchScript.Parse("spectrum layer=missing root=110 partials=1:.5"));

        Assert.Throws<PatchScriptException>(() => PatchScript.Parse("""
            layer name=pad
            spectrum layer=pad root=110 spread=-.01 partials=1:.5
            """));

        Assert.Throws<PatchScriptException>(() => PatchScript.Parse("""
            layer name=pad
            spectrum layer=pad root=110 spread=1 partials=1:.5
            """));
    }

    [Fact]
    public void Dx7Algorithm32RebuildMatchesAdditiveCarrierShape()
    {
        var rebuild = ReferenceRebuildCatalog.Dx7Rebuilds.Single(item => item.ReferenceId == "dx7/algo32-additive-organ");
        var patch = PatchScript.Parse(rebuild.Script);
        var topology = Dx7SysEx.AlgorithmTopology(32);

        Assert.Equal(topology.CarrierOperators.Count, patch.Voices.Count);
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "carrier_operators" && feature.Value == "1,2,3,4,5,6");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "modulation_edge_count" && feature.Value == "0");
        Assert.Equal(2, patch.Parameters.Count);
        Assert.Equal(2, patch.ParameterBindings.Count);
    }

    [Fact]
    public void Dx7Algorithm8RebuildRecordsMissingOperatorGraph()
    {
        var rebuild = ReferenceRebuildCatalog.Dx7Rebuilds.Single(item => item.ReferenceId == "dx7/algo8-bright-pair");
        var patch = PatchScript.Parse(rebuild.Script);
        var topology = Dx7SysEx.AlgorithmTopology(8);

        Assert.Equal(2, topology.CarrierOperators.Count);
        var graph = Assert.Single(patch.OperatorGraphs);
        Assert.Equal([1, 3], graph.Carriers);
        Assert.Equal(6, graph.Operators.Count);
        Assert.Contains(graph.Edges, edge => edge.SourceId == 6 && edge.TargetId == 5);
        Assert.Contains(graph.Edges, edge => edge.SourceId == 5 && edge.TargetId == 3);
        Assert.Contains(graph.Edges, edge => edge.SourceId == 4 && edge.TargetId == 3);
        Assert.Contains(graph.Edges, edge => edge.SourceId == 2 && edge.TargetId == 1);
        Assert.Contains(graph.Operators, op => op.Id == 4 && op.Feedback > 0);
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "modulation_edges");
        Assert.Contains(rebuild.MissingFeatures, feature => feature.Name == "dx7_feedback_register");
        Assert.Equal(2, patch.Parameters.Count);
        Assert.Equal(3, patch.ParameterBindings.Count);
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/voices/0/fm/index");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/voices/0/env/release");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/voices/1/env/release");
    }

    [Fact]
    public void OperatorGraphScriptParsesAndExportsFaust()
    {
        var patch = PatchScript.Parse("opgraph name=pair freq=220 gain=.2 carriers=1 ops=2:2:.8,1:1:1 edges=2>1:1.4");
        var graph = Assert.Single(patch.OperatorGraphs);
        var export = FaustEmitter.Emit(patch);

        Assert.Equal("pair", graph.Name);
        Assert.Equal(2, graph.Operators.Count);
        Assert.Single(graph.Edges);
        Assert.Empty(patch.Voices);
        Assert.Contains("opgraph_0_op_2", export.Source);
        Assert.Contains("opgraph_0_op_1", export.Source);
        Assert.Contains("opgraph_0 = (opgraph_0_op_1) * 0.2;", export.Source);
    }

    [Fact]
    public void ReadableOperatorGraphSyntaxParsesRoutesCarriersAndEnvelopes()
    {
        var patch = PatchScript.Parse("""
            opgraph name=pair freq=220 gain=.2 vibrato=.004 vibrato_hz=6 vibrato_delay=.2
            operator name=op2 ratio=2 level=.8 env=ad:.01:.2
            operator name=op1 ratio=1 level=1 env=adsr:.02:.1:.65:.3
            route from=op2 to=op1 index=1.4
            carrier name=op1
            """);
        var export = FaustEmitter.Emit(patch);

        var graph = Assert.Single(patch.OperatorGraphs);
        var op2 = graph.Operators.Single(op => op.Id == 2);
        var op1 = graph.Operators.Single(op => op.Id == 1);

        Assert.Equal("pair", graph.Name);
        Assert.Equal([1], graph.Carriers);
        Assert.Single(graph.Edges);
        Assert.Equal(.004f, graph.VibratoDepth, 5);
        Assert.Equal(6, graph.VibratoHz);
        Assert.Equal(.2f, graph.VibratoDelaySeconds, 5);
        Assert.Equal(.01f, op2.Envelope.AttackSeconds, 5);
        Assert.Equal(.2f, op2.Envelope.DecaySeconds, 5);
        Assert.Equal(.02f, op1.Envelope.AttackSeconds, 5);
        Assert.Equal(.1f, op1.Envelope.DecaySeconds, 5);
        Assert.Equal(.65f, op1.Envelope.SustainLevel, 5);
        Assert.Equal(.3f, op1.Envelope.ReleaseSeconds, 5);
        Assert.Equal(.21f, op2.Note.GateSeconds, 5);
        Assert.Contains("lfo_sin(6, 0.0)", export.Source);
        Assert.Contains("clip01(age / max(0.0001, 0.2))", export.Source);
    }

    [Fact]
    public void OperatorGraphSyntaxParsesReadableRateLevelEnvelope()
    {
        var patch = PatchScript.Parse("""
            opgraph name=pair freq=220 gain=.2
            operator name=op2 ratio=2 level=.8 env=rl rates=.004,.12,.2,.4 levels=1,.7,.25,0 gate=.75
            operator name=op1 ratio=1 level=1 env=adsr:.02:.1:.65:.3
            route from=op2 to=op1 index=.8
            carrier name=op1
            """);
        var export = FaustEmitter.Emit(patch);

        var op2 = Assert.Single(patch.OperatorGraphs[0].Operators, op => op.Id == 2);
        Assert.NotNull(op2.RateLevelEnvelope);
        var envelope = op2.RateLevelEnvelope;

        Assert.Equal(.004f, envelope.Rate1Seconds, 5);
        Assert.Equal(1, envelope.Level1);
        Assert.Equal(.12f, envelope.Rate2Seconds, 5);
        Assert.Equal(.7f, envelope.Level2, 5);
        Assert.Equal(.2f, envelope.Rate3Seconds, 5);
        Assert.Equal(.25f, envelope.Level3, 5);
        Assert.Equal(.4f, envelope.Rate4Seconds, 5);
        Assert.Equal(0, envelope.Level4);
        Assert.Equal(.75f, op2.Note.GateSeconds, 5);
        Assert.Equal(RateLevelCurve.Linear, envelope.Curve1);
        Assert.Contains("rl4_env(0.004, 1, 0, 0.12, 0.7, 0, 0.2, 0.25, 0, 0.4, 0, 0, 0.75)", export.Source);
    }

    [Fact]
    public void OperatorGraphSyntaxParsesCurvedRateLevelEnvelope()
    {
        var patch = PatchScript.Parse("""
            opgraph name=pair freq=220 gain=.2
            operator name=op2 ratio=2 level=.8 env=rl rates=.004,.12,.2,.4 levels=1.2,.7,.25,0 curves=lin,exp,exp,lin gate=.75
            operator name=op1 ratio=1 level=1 env=ad:.01:.08
            route from=op2 to=op1 index=.8
            carrier name=op1
            """);
        var export = FaustEmitter.Emit(patch);

        var envelope = Assert.Single(patch.OperatorGraphs[0].Operators, op => op.Id == 2).RateLevelEnvelope;
        Assert.NotNull(envelope);
        Assert.Equal(RateLevelCurve.Linear, envelope.Curve1);
        Assert.Equal(RateLevelCurve.Exponential, envelope.Curve2);
        Assert.Equal(RateLevelCurve.Exponential, envelope.Curve3);
        Assert.Equal(RateLevelCurve.Linear, envelope.Curve4);
        Assert.Contains("rl4_env(0.004, 1.2, 0, 0.12, 0.7, 1, 0.2, 0.25, 1, 0.4, 0, 0, 0.75)", export.Source);
    }

    [Fact]
    public void ReadableOperatorGraphSyntaxBindsParametersAtFieldSites()
    {
        var patch = PatchScript.Parse("""
            param path=/macro/brightness default=.6 min=0 max=1 step=.001
            param path=/macro/strike default=.08 min=.01 max=.5 step=.001
            opgraph name=pair freq=220 gain=@/macro/brightness
            operator name=op2 ratio=2 level=@/macro/brightness env=ad:.01:@/macro/strike
            operator name=op1 ratio=1 level=1 env=adsr:.02:.1:.65:.3
            route from=op2 to=op1 index=@/macro/brightness
            carrier name=op1
            """);
        var export = FaustEmitter.Emit(patch);

        Assert.Equal(4, patch.ParameterBindings.Count);
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/opgraphs/0/gain" && binding.ParameterPath == "/macro/brightness");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/opgraphs/0/operators/2/level" && binding.ParameterPath == "/macro/brightness");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/opgraphs/0/operators/2/env/decay" && binding.ParameterPath == "/macro/strike");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/opgraphs/0/routes/2>1/index" && binding.ParameterPath == "/macro/brightness");
        Assert.Contains("opgraph_0_op_2 * patch_param_0", export.Source);
        Assert.Contains("oneshot_adsr(0.01, patch_param_1, 0, 0", export.Source);
        Assert.Contains("opgraph_0 = (opgraph_0_op_1) * patch_param_0;", export.Source);
        Assert.Empty(export.Warnings);
    }

    [Fact]
    public void FaustEmitterProducesWobbleSource()
    {
        var export = FaustEmitter.EmitScript(WobbleTalker, new FaustExportOptions("wobble_talker", Stereo: true));

        Assert.Contains("declare name \"wobble_talker\";", export.Source);
        Assert.Contains("patch_mod_fm_index", export.Source);
        Assert.Contains("fi.resonbp", export.Source);
        Assert.Contains("process =", export.Source);
        Assert.Contains("<: _,_;", export.Source);
    }

    [Fact]
    public void FaustEmitterExposesDeclaredPatchParametersAsControls()
    {
        var export = FaustEmitter.EmitScript("param path=/macro/brightness default=.45 min=0 max=1 step=.001;v w=saw f=80");

        Assert.Contains("patch_param_0 = hslider(\"/macro/brightness\", 0.45, 0, 1, 0.001) : si.smoo;", export.Source);
        Assert.Contains("patch_param_0", export.Source);
        Assert.Contains("declared but not bound", Assert.Single(export.Warnings));
    }

    [Fact]
    public void FaustEmitterUsesParameterReferencesAtBoundFieldSites()
    {
        var export = FaustEmitter.EmitScript("param path=/macro/brightness default=.45 min=0 max=1 step=.001;v w=saw f=80 lpf=@/macro/brightness");

        Assert.Contains("patch_param_0 = hslider(\"/macro/brightness\", 0.45, 0, 1, 0.001) : si.smoo;", export.Source);
        Assert.Contains("clip01(patch_param_0 * (1.0 + 0 * age * 1.8) + patch_mod_lpf + 0.0)", export.Source);
        Assert.Empty(export.Warnings);
    }

    [Fact]
    public void VoiceLowPassOrderSelectsFaustFilterOrder()
    {
        var patch = PatchScript.Parse("v w=saw f=80 lpf=.1 lpf_order=2");
        var export = FaustEmitter.Emit(patch);

        Assert.Equal(2, patch.Voices[0].Filter.LowPassOrder);
        Assert.Contains("fi.lowpass(2, max(20.0,", export.Source);
    }

    [Fact]
    public void VoiceLowPassRateLevelEnvelopeParsesAndExports()
    {
        var patch = PatchScript.Parse("v w=saw f=80 lpf=.1 lpf_env=rl lpf_start=.4 lpf_rates=.1,.2,.3,.4 lpf_levels=.3,.2,0,0 lpf_curves=lin,exp,lin,lin");
        var export = FaustEmitter.Emit(patch);

        var envelope = patch.Voices[0].Filter.LowPassEnvelope;
        Assert.NotNull(envelope);
        Assert.Equal(.4f, envelope.StartLevel, 5);
        Assert.Equal(.3f, envelope.Level1, 5);
        Assert.Contains("rl4_env_from(0.4", export.Source);
    }

    [Fact]
    public void VoiceEnvelopeUsesStandardAdsrAndNoteGate()
    {
        var patch = PatchScript.Parse("v w=saw f=220 gate=.4 attack=.01 env_decay=.08 sustain_level=.6 release=.3");
        var voice = Assert.Single(patch.Voices);
        var export = FaustEmitter.Emit(patch);

        Assert.Equal(220, voice.Note.FrequencyHz);
        Assert.Equal(.4f, voice.Note.GateSeconds, 5);
        Assert.Equal(.01f, voice.Envelope.AttackSeconds, 5);
        Assert.Equal(.08f, voice.Envelope.DecaySeconds, 5);
        Assert.Equal(.6f, voice.Envelope.SustainLevel, 5);
        Assert.Equal(.3f, voice.Envelope.ReleaseSeconds, 5);
        Assert.Contains("oneshot_adsr", export.Source);
    }

    [Fact]
    public void LegacyPunchMapsToTransientPeakOverSustain()
    {
        var patch = PatchScript.Parse("v w=saw f=220 gain=.2 punch=.5");
        var voice = Assert.Single(patch.Voices);

        Assert.Equal(.3f, voice.Gain, 5);
        Assert.Equal(2f / 3f, voice.Envelope.SustainLevel, 5);
    }

    [Fact]
    public void MidiVoiceEmitsHostNoteControls()
    {
        var patch = PatchScript.Parse("v w=saw f=220 midi=true attack=.01 env_decay=.08 sustain_level=.6 release=.3");
        var export = FaustEmitter.Emit(patch);

        Assert.Equal(NoteSource.Host, patch.Voices[0].Note.Source);
        Assert.Equal(PlaybackMode.Mono, patch.Playback.Mode);
        Assert.True(patch.Playback.Midi);
        Assert.Contains("declare options \"[midi:on][nvoices:1]\";", export.Source);
        Assert.Contains("freq = nentry(\"freq\", 220", export.Source);
        Assert.Contains("gain = nentry(\"gain\", 1", export.Source);
        Assert.Contains("gate = button(\"gate\")", export.Source);
        Assert.Contains("en.adsr", export.Source);
    }

    [Fact]
    public void PolyphonicPatchUsesFaustStandardMidiSurface()
    {
        var patch = PatchScript.Parse("instrument midi=true polyphony=8; v w=saw f=330 attack=.01 env_decay=.08 sustain_level=.6 release=.3");
        var export = FaustEmitter.Emit(patch);

        Assert.Equal(PlaybackMode.Poly, patch.Playback.Mode);
        Assert.Equal(8, patch.Playback.Voices);
        Assert.Contains("declare options \"[midi:on][nvoices:8]\";", export.Source);
        Assert.Contains("freq = nentry(\"freq\", 440", export.Source);
        Assert.Contains("gain = nentry(\"gain\", 1", export.Source);
        Assert.Contains("gate = button(\"gate\")", export.Source);
        Assert.DoesNotContain("/voices/0/note/frequency", export.Source);
        Assert.DoesNotContain("/voices/0/note/gate", export.Source);
    }

    [Fact]
    public async Task FaustCompilerValidatesGeneratedSourceWhenInstalled()
    {
        var export = FaustEmitter.EmitScript(WobbleTalker);
        var validation = await FaustCompiler.ValidateAsync(export.Source);

        if (validation is null)
        {
            return;
        }

        Assert.True(validation.Success, validation.Stderr);
    }

    [Fact]
    public async Task FaustCompilerValidatesParameterizedPatchWhenInstalled()
    {
        var export = FaustEmitter.EmitScript("param path=/macro/brightness default=.45 min=0 max=1 step=.001;v w=saw f=80 lpf=@/macro/brightness");
        var validation = await FaustCompiler.ValidateAsync(export.Source);

        if (validation is null)
        {
            return;
        }

        Assert.True(validation.Success, validation.Stderr);
    }

    [Fact]
    public async Task FaustCompilerValidatesSpectralBankWhenInstalled()
    {
        var export = FaustEmitter.EmitScript("""
            layer name=pad engine=pad gain=.08 env=rl rates=.1,.2,.3,.4 levels=1,.8,.5,0 gate=.9
            spectrum layer=pad root=100 spread=.01 partials=1:.08,2:.04,3:.02
            """);
        var validation = await FaustCompiler.ValidateAsync(export.Source);

        if (validation is null)
        {
            return;
        }

        Assert.True(validation.Success, validation.Stderr);
    }

    [Fact]
    public async Task FaustCompilerValidatesMidiGatePatchWhenInstalled()
    {
        var export = FaustEmitter.EmitScript("instrument midi=true polyphony=8; v w=saw f=220 attack=.01 env_decay=.08 sustain_level=.6 release=.3");
        var validation = await FaustCompiler.ValidateAsync(export.Source);

        if (validation is null)
        {
            return;
        }

        Assert.True(validation.Success, validation.Stderr);
    }

    [Fact]
    public async Task FaustCompilerCanEmitCSharpWhenInstalled()
    {
        var export = FaustEmitter.EmitScript("v w=sin f=440 g=.2 s=.1 d=.2");
        var output = Path.Combine(Path.GetTempPath(), $"aquasynth-{Guid.NewGuid():N}.cs");
        try
        {
            var validation = await FaustCompiler.CompileAsync(
                export.Source,
                new FaustCompileOptions(FaustTargetLanguage.CSharp, output));

            if (validation is null)
            {
                return;
            }

            Assert.True(validation.Success, validation.Stderr);
            Assert.True(File.Exists(output));
            Assert.Contains("class", await File.ReadAllTextAsync(output));
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public async Task FaustCompilerCanEmitWebAssemblyWhenInstalled()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"aquasynth-wasm-{Guid.NewGuid():N}");
        try
        {
            var manifest = await FaustCompiler.CompileWebAssemblyScriptAsync(
                "v w=sin f=440 g=.2 s=.1 d=.2",
                new FaustWebAssemblyCompileOptions(outputDir, "ui_bloom"));

            if (manifest is null)
            {
                return;
            }

            Assert.True(manifest.Success, manifest.Stderr);
            Assert.True(File.Exists(manifest.DspPath));
            Assert.True(File.Exists(manifest.WasmPath));
            Assert.True(File.Exists(manifest.JsonPath));
            Assert.True(new FileInfo(manifest.WasmPath).Length > 0);
            Assert.Contains("\"name\"", await File.ReadAllTextAsync(manifest.JsonPath));
        }
        finally
        {
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task FaustCompilerRendersGeneratedSourceWhenInstalled()
    {
        var export = FaustEmitter.EmitScript("v w=sin f=440 gain=.2 sustain=.08 decay=.04");
        var render = await FaustCompiler.RenderAsync(export.Source, new FaustRenderOptions(DurationSeconds: .12f));

        if (render is null)
        {
            return;
        }

        Assert.True(render.Samples.Length > 1000, render.Stderr);
        Assert.True(render.Samples.Max(MathF.Abs) > 0.001f, render.Stderr);

        var comparison = new AudioAnalyzer(new AudioAnalysisConfig(SampleRate: render.SampleRate))
            .Compare(render.Samples, render.Samples);
        Assert.True(comparison.Score > 0.99f);
    }

    [Fact]
    public async Task FaustCompilerRendersOperatorFeedbackWhenInstalled()
    {
        var export = FaustEmitter.EmitScript("""
            opgraph name=fb freq=220 gain=.2
            operator name=op1 ratio=1 level=1 feedback=.2 env=ad:.01:.1
            carrier name=op1
            """);
        var render = await FaustCompiler.RenderAsync(export.Source, new FaustRenderOptions(DurationSeconds: .1f));

        if (render is null)
        {
            return;
        }

        Assert.True(render.Samples.Length > 1000, render.Stderr);
        Assert.True(render.Samples.Max(MathF.Abs) > 0.001f, render.Stderr);
    }

    [Fact]
    public async Task FaustCompilerRendersRateLevelOperatorEnvelopeWhenInstalled()
    {
        var export = FaustEmitter.EmitScript("""
            opgraph name=rl freq=220 gain=.2
            operator name=op2 ratio=2 level=.8 env=rl rates=.004,.08,.12,.16 levels=1,.7,.25,0
            operator name=op1 ratio=1 level=1 env=adsr:.004:.08:.7:.12
            route from=op2 to=op1 index=.4
            carrier name=op1
            """);
        var render = await FaustCompiler.RenderAsync(export.Source, new FaustRenderOptions(DurationSeconds: .25f));

        if (render is null)
        {
            return;
        }

        Assert.True(render.Samples.Length > 1000, render.Stderr);
        Assert.True(render.Samples.Max(MathF.Abs) > 0.001f, render.Stderr);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AquaSynth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("could not find repository root");
    }

    private static string FixturePath(string path) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", path);
}

