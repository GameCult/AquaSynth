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
            tract_shape name=human length_cm=17 diameters=.6,.7,.9,1.1,1.3,1.5,1.4,1.2
            glottis name=modal intensity=.72 tenseness=@/pink/tenseness aspiration=.14 reflection=.72 skew=.5
            tract_injection name=sibilant position=6 diameter=.45 turbulence=.8 burst=.3 width=.8
            nasal_branch name=nose junction=3 velum=.2 diameters=.01,.6,1.2,1.4
            tract_motion name=quick diameter_slew=20 constriction_slew=30 velum_slew=12 obstruction_threshold=.08
            tract shape=human glottis=modal injection=sibilant nasal_branch=nose motion=quick propagation=graph sections=8 nose_sections=4 loss=.998 freq=140 tongue_index=4 tongue_diameter=1.4 velum=.2
            """);

        var shape = Assert.Single(patch.TractShapes);
        Assert.Equal("human", shape.Name);
        Assert.Equal(8, shape.AreaFunction.Sections);
        Assert.Equal(17, shape.AreaFunction.LengthCentimeters);
        Assert.Equal(7, shape.AreaFunction.ReflectionCoefficients.Count);
        Assert.Contains(shape.AreaFunction.ReflectionCoefficients, coefficient => MathF.Abs(coefficient) > 0.01f);
        var glottis = Assert.Single(patch.GlottalSources);
        Assert.Equal("modal", glottis.Name);
        Assert.Equal(.14f, glottis.Aspiration, 5);
        var injection = Assert.Single(patch.TractInjections);
        Assert.Equal("sibilant", injection.Name);
        Assert.Equal(.3f, injection.Burst, 5);
        var nasal = Assert.Single(patch.NasalBranches);
        Assert.Equal("nose", nasal.Name);
        Assert.Equal(4, nasal.AreaFunction?.Sections);
        var motion = Assert.Single(patch.TractMotions);
        Assert.Equal("quick", motion.Name);
        Assert.Equal(.08f, motion.ObstructionThreshold, 5);

        var voice = Assert.Single(patch.Voices);
        Assert.NotNull(voice.Tract);
        Assert.Equal(8, voice.Tract.Sections);
        Assert.Equal(4, voice.Tract.NoseSections);
        Assert.Equal(4, voice.Tract.TongueIndex);
        Assert.Equal(.45f, voice.Tract.ConstrictionDiameter, 5);
        Assert.Same(shape.AreaFunction, voice.Tract.AreaFunction);
        Assert.Equal(glottis, voice.Tract.Glottis);
        Assert.Equal(injection, voice.Tract.Injection);
        Assert.Equal(nasal, voice.Tract.Nasal);
        Assert.Equal(motion, voice.Tract.Motion);
        Assert.Equal(TractPropagationMode.Graph, voice.Tract.Propagation);
        Assert.Equal(.998f, voice.Tract.PropagationLoss, 5);
        Assert.NotNull(voice.AcousticNetwork);
        Assert.NotNull(voice.Tract.AcousticNetwork);
        Assert.Contains(patch.AcousticPaths, path => path.Name == "voices_0_oral");
        Assert.Contains(patch.AcousticSourcePorts, port => port.Name == "voices_0_modal" && port.Kind == AcousticSourceKind.Glottal);
        Assert.Contains(patch.AcousticSourcePorts, port => port.Name.StartsWith("voices_0_sibilant_", StringComparison.Ordinal) && port.Kind == AcousticSourceKind.TurbulenceJet);
        Assert.Contains(patch.AcousticBranches, branch => branch.Name == "voices_0_nose" && branch.Kind == AcousticBranchKind.Nasal);
        Assert.Contains(patch.AcousticRadiationPorts, port => port.Name == "voices_0_lip" && port.Kind == AcousticRadiationKind.Lip);
        Assert.Contains(patch.AcousticTerminals, terminal => terminal.Name.StartsWith("voices_0_area_", StringComparison.Ordinal) && terminal.Kind == AcousticTerminalKind.Contact);
        Assert.Contains(patch.WaveClocks, clock => clock.Name == "voices_0_clock" && clock.Strategy == WaveClockDelayStrategy.FractionalLinear);
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/glottis/0/tenseness");

        var faust = FaustEmitter.Emit(patch, new FaustExportOptions("tract_voice")).Source;
        Assert.Contains("acoustic_graph_radiated", faust);
        Assert.Contains("graph_loop ~ si.bus", faust);
        Assert.Contains("graph_source_voices_0_modal", faust);
        Assert.Contains("graph_source_voices_0_sibilant_", faust);
        Assert.Contains("graph_terminal_area_voices_0_area_", faust);
        Assert.Contains("graph_connection_reflection_voices_0_nose_connection", faust);
        Assert.Contains("graph_contact_voices_0_area_", faust);
        Assert.Contains("de.fdelay", faust);
        Assert.Contains("_reservoir_drive", faust);
        Assert.Contains("_release_pulse", faust);
        Assert.DoesNotContain("tract_lf", faust);
        Assert.DoesNotContain("wg_", faust);
    }

    [Fact]
    public void ParserSupportsReusableAcousticPortNetworkPrimitives()
    {
        var patch = PatchScript.Parse("""
            param path=/voice/left_pressure default=.7 min=0 max=1 step=.001
            param path=/voice/source_load default=.65 min=0 max=2 step=.001
            param path=/voice/left_mass default=.32 min=.02 max=2 step=.001
            param path=/voice/left_damping default=.16 min=0 max=2 step=.001
            param path=/voice/left_stiffness default=.03 min=0 max=1 step=.001
            param path=/voice/left_drive default=1.2 min=0 max=4 step=.001
            param path=/voice/left_load_coupling default=.44 min=0 max=2 step=.001
            param path=/voice/left_rest_opening default=.03 min=0 max=1 step=.001
            param path=/voice/throat_opening default=.8 min=0 max=1 step=.001
            param path=/voice/mouth_opening default=1.2 min=0 max=2 step=.001
            path name=trachea length_cm=12 diameters=.4,.7,1,1
            path name=oral length_cm=17 diameters=.6,1.1,1.6,1.2,.8
            source_port name=left_labium path=trachea model=tissue_valve position=0 pressure=@/voice/left_pressure tension=.55 opening=.4 noise=.02 impedance=@/voice/source_load mass=@/voice/left_mass damping=@/voice/left_damping stiffness=@/voice/left_stiffness saturation=.9 drive=@/voice/left_drive load_coupling=@/voice/left_load_coupling rest_opening=@/voice/left_rest_opening
            source_port name=right_labium path=trachea kind=labial position=0 pressure=.65 tension=.5 opening=.45 noise=.03 balance=.8 position_index=.12 position_width=.05 position_index_scale=1
            branch name=throat from_path=trachea from_position=.9 to_path=oral kind=bronchial opening=@/voice/throat_opening coupling=.7
            radiation_port name=mouth path=oral kind=lip position=1 opening=@/voice/mouth_opening reflection=-.82
            wave_clock name=continuous strategy=thiran order=1 max_delay=4096 smoothing_ms=3
            acoustic_network name=syrinxish path=trachea wave_clock=continuous sources=left_labium,right_labium branches=throat radiation=mouth
            acoustic network=syrinxish freq=140 gain=.1
            """);

        var network = Assert.Single(patch.AcousticNetworks);
        Assert.Equal("syrinxish", network.Name);
        Assert.Equal("trachea", network.PrimaryPath);
        Assert.Equal(["left_labium", "right_labium"], network.SourcePorts);
        Assert.Equal(["throat"], network.Branches);
        Assert.Equal(["mouth"], network.RadiationPorts);
        Assert.Contains("left_labium", network.Terminals);
        Assert.Contains("mouth", network.Terminals);
        Assert.Contains("throat_from", network.Terminals);
        Assert.Contains("throat_to", network.Terminals);
        Assert.Contains("throat_connection", network.Connections);

        Assert.Equal(2, patch.AcousticPaths.Count);
        Assert.Equal(5, patch.AcousticTerminals.Count);
        Assert.Single(patch.AcousticConnections);
        Assert.Equal(AcousticSourceKind.Glottal, patch.AcousticSourcePorts[0].Kind);
        Assert.Equal(AcousticSourceKind.Labial, patch.AcousticSourcePorts[1].Kind);
        Assert.All(patch.AcousticSourcePorts, port => Assert.Equal(AcousticSourceModel.TissueValve, port.Model));
        Assert.Equal(.9f, patch.AcousticSourcePorts[0].Saturation, 5);
        Assert.NotNull(patch.AcousticSourcePorts[1].PositionControl);
        Assert.Equal(AcousticBranchKind.Bronchial, Assert.Single(patch.AcousticBranches).Kind);
        Assert.Equal(AcousticRadiationKind.Lip, Assert.Single(patch.AcousticRadiationPorts).Kind);
        Assert.Equal(WaveClockDelayStrategy.FractionalThiran, Assert.Single(patch.WaveClocks).Strategy);
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/0/pressure");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/0/impedance");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/0/mass");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/0/damping");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/0/stiffness");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/0/drive");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/0/load_coupling");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/0/rest_opening");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/branches/0/opening");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/radiation/0/opening");

        var voice = Assert.Single(patch.Voices);
        Assert.Equal(network, voice.AcousticNetwork);

        var export = FaustEmitter.Emit(patch, new FaustExportOptions("syrinxish"));
        Assert.Contains("acoustic_graph_radiated", export.Source);
        Assert.Contains("graph_loop", export.Source);
        Assert.Contains("graph_connection_reflection_throat_connection", export.Source);
        Assert.Contains("graph_connection_energy_in_throat_connection", export.Source);
        Assert.DoesNotContain("graph_connection_pressure_throat_connection", export.Source);
        Assert.Contains("graph_terminal_area_throat_from", export.Source);
        Assert.Contains("graph_node_area_left_labium_right_labium", export.Source);
        Assert.Contains("graph_source_right_labium", export.Source);
        Assert.Contains("_load_pressure", export.Source);
        Assert.Contains("_pressure_drive", export.Source);
        Assert.Contains("_velocity", export.Source);
        Assert.Contains("_displacement", export.Source);
        Assert.Contains("_aperture", export.Source);
        Assert.DoesNotContain("os.phasor(1.0, syrinxish_freq", export.Source);
        Assert.Contains("de.fdelay1a", export.Source);
        Assert.Contains("patch_param_0", export.Source);
        Assert.Contains("patch_param_1", export.Source);
        Assert.Contains("patch_param_2", export.Source);
    }

    [Fact]
    public void SyrinxVoiceUsesPairedLoadedLabialSourcesThroughOneGraph()
    {
        var patch = PatchScript.Parse("""
            param path=/bird/left/pressure default=.78 min=0 max=1 step=.001
            param path=/bird/right/pressure default=.66 min=0 max=1 step=.001
            param path=/bird/left/opening default=.34 min=0 max=1 step=.001
            param path=/bird/right/opening default=.29 min=0 max=1 step=.001
            param path=/bird/load default=.85 min=0 max=2 step=.001
            param path=/bird/beak/opening default=.9 min=0 max=1.5 step=.001
            path name=left_bronchus length_cm=3.8 diameters=.22,.30,.36,.42
            path name=right_bronchus length_cm=3.6 diameters=.20,.28,.34,.40
            path name=trachea length_cm=8.4 diameters=.38,.48,.56,.46
            source_port name=left_labium path=left_bronchus kind=syrinx position=0 pressure=@/bird/left/pressure tension=.42 opening=@/bird/left/opening noise=.025 impedance=@/bird/load
            source_port name=right_labium path=right_bronchus kind=syrinx position=0 pressure=@/bird/right/pressure tension=.49 opening=@/bird/right/opening noise=.02 balance=.96 impedance=@/bird/load
            terminal name=left_merge path=left_bronchus position=1 kind=junction area_scale=1
            terminal name=right_merge path=right_bronchus position=1 kind=junction area_scale=1
            terminal name=trachea_base path=trachea position=0 kind=junction area_scale=1
            connect name=syrinx_merge terminals=left_merge,right_merge,trachea_base law=area_scatter coupling=1
            radiation_port name=beak path=trachea kind=beak position=1 opening=@/bird/beak/opening reflection=-.72
            wave_clock name=bird_clock strategy=linear max_delay=1024 smoothing_ms=2
            acoustic_network name=bird_syrinx path=trachea wave_clock=bird_clock sources=left_labium,right_labium radiation=beak terminals=left_merge,right_merge,trachea_base connections=syrinx_merge
            acoustic network=bird_syrinx freq=820 gain=.22 sustain=.42 decay=.08
            """);

        Assert.Equal(3, patch.AcousticPaths.Count);
        Assert.Equal(2, patch.AcousticSourcePorts.Count);
        Assert.All(patch.AcousticSourcePorts, port => Assert.Equal(AcousticSourceKind.Labial, port.Kind));
        Assert.All(patch.AcousticSourcePorts, port => Assert.Equal(AcousticSourceModel.TissueValve, port.Model));
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/0/impedance");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/1/impedance");
        Assert.Equal(AcousticRadiationKind.Beak, Assert.Single(patch.AcousticRadiationPorts).Kind);
        Assert.Equal(AcousticConnectionLaw.AreaScattering, Assert.Single(patch.AcousticConnections).Law);

        var export = FaustEmitter.Emit(patch, new FaustExportOptions("bird_syrinx"));
        Assert.Contains("acoustic_graph_radiated", export.Source);
        Assert.Contains("graph_connection_reflection_syrinx_merge", export.Source);
        Assert.Contains("graph_source_left_labium", export.Source);
        Assert.Contains("graph_source_right_labium", export.Source);
        Assert.Contains("_load_pressure", export.Source);
        Assert.Contains("_pressure_drive", export.Source);
        Assert.Contains("_velocity", export.Source);
        Assert.Contains("_displacement", export.Source);
        Assert.Contains("_aperture", export.Source);
        Assert.Contains("graph_radiation_flow_beak", export.Source);
        Assert.Contains("patch_param_0", export.Source);
        Assert.DoesNotContain("os.phasor(1.0, bird_syrinx_freq", export.Source);
        Assert.DoesNotContain("Tract", export.Source);
    }

    [Fact]
    public void ParserSupportsTypedAcousticPathGraph()
    {
        var patch = PatchScript.Parse("""
            param path=/voice/velopharynx default=.18 min=0 max=1 step=.001
            path name=trachea length_cm=12 diameters=.4,.7,1,1
            path name=oral length_cm=17 diameters=.6,1.1,1.6,1.2,.8
            path name=nasal length_cm=10 diameters=.05,.3,.8,.5
            source_port name=folds path=trachea kind=glottal position=1 pressure=.7 tension=.55 opening=.45
            radiation_port name=mouth path=oral kind=lip position=1 opening=1.4 reflection=-.82
            radiation_port name=nostril path=nasal kind=nostril position=1 opening=.5 reflection=-.45
            terminal name=trachea_bottom path=trachea position=0 kind=closed reflection=.75
            terminal name=oral_back path=oral position=0 kind=junction area_scale=1
            terminal name=nasal_gate path=nasal position=0 kind=junction area_scale=@/voice/velopharynx
            connect name=velopharynx terminals=folds,oral_back,nasal_gate law=area_scatter coupling=@/voice/velopharynx
            acoustic_network name=humanish path=oral sources=folds radiation=mouth,nostril terminals=trachea_bottom,oral_back,nasal_gate connections=velopharynx
            acoustic network=humanish freq=130 gain=.1
            """);

        var network = Assert.Single(patch.AcousticNetworks);
        Assert.Equal(["folds"], network.SourcePorts);
        Assert.Equal(["mouth", "nostril"], network.RadiationPorts);
        Assert.Equal(["trachea_bottom", "oral_back", "nasal_gate", "folds", "mouth", "nostril"], network.Terminals);
        Assert.Equal(["velopharynx"], network.Connections);

        var connection = Assert.Single(patch.AcousticConnections);
        Assert.Equal(AcousticConnectionLaw.AreaScattering, connection.Law);
        Assert.Equal(["folds", "oral_back", "nasal_gate"], connection.Terminals);
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/terminals/5/area_scale");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/connections/0/coupling");

        var export = FaustEmitter.Emit(patch, new FaustExportOptions("humanish"));
        Assert.Contains("acoustic_graph_radiated", export.Source);
        Assert.Contains("graph_connection_reflection_velopharynx", export.Source);
        Assert.Contains("graph_connection_energy_in_velopharynx", export.Source);
        Assert.DoesNotContain("graph_connection_pressure_velopharynx", export.Source);
        Assert.Contains("graph_terminal_area_nasal_gate", export.Source);
        Assert.Contains("sqrt(clip01", export.Source);
        Assert.Contains("graph_source_folds", export.Source);
        Assert.Contains("graph_next_r", export.Source);
        Assert.Contains("graph_next_l", export.Source);
        Assert.Contains("de.fdelay", export.Source);
        Assert.Contains("max(0.000001, 0.000", export.Source);
        Assert.Contains("patch_param_0", export.Source);
    }

    [Fact]
    public void TractVoiceCanLowerThroughAcousticGraph()
    {
        var patch = PatchScript.Parse("""
            tract_shape name=human length_cm=17 diameters=.6,.8,1.2,1.5,1.2,.8
            tract shape=human propagation=graph freq=140 gain=.1 sustain=.2
            """);

        var voice = Assert.Single(patch.Voices);
        Assert.Equal(TractPropagationMode.Graph, voice.Tract?.Propagation);
        var network = Assert.Single(patch.AcousticNetworks);
        Assert.NotEmpty(network.Terminals);
        Assert.Contains("voices_0_source", network.Terminals);
        Assert.Contains("voices_0_lip", network.Terminals);
        Assert.Equal(WaveClockDelayStrategy.FractionalLinear, Assert.Single(patch.WaveClocks).Strategy);

        var export = FaustEmitter.Emit(patch, new FaustExportOptions("tract_graph"));
        Assert.Contains("acoustic_graph_radiated", export.Source);
        Assert.Contains("de.fdelay", export.Source);
        Assert.Contains("max(0.000001,", export.Source);
        Assert.DoesNotContain("tract_lf", export.Source);
    }

    [Fact]
    public void GeneratedTractGraphKeepsBoundaryDimensionsSeparate()
    {
        var patch = PatchScript.Parse("""
            param path=/pink/velum default=.25 min=0 max=1 step=.001
            param path=/pink/lip/opening default=1.4 min=0 max=2 step=.001
            param path=/pink/lip/reflection default=-.82 min=-1 max=0 step=.001
            tract_shape name=human length_cm=17 diameters=.6,.8,1.2,1.5,1.2,.8
            nasal_branch name=nose length_cm=12 junction=3 velum=@/pink/velum diameters=.01,.35,.6,.8
            tract shape=human nasal_branch=nose propagation=graph freq=140 gain=.1 sustain=.2 velum=@/pink/velum lip_opening=@/pink/lip/opening lip_reflection=@/pink/lip/reflection
            """);

        Assert.Contains(patch.AcousticConnections, connection =>
            connection.Name.Contains("_nose_connection", StringComparison.Ordinal) &&
            MathF.Abs(connection.Coupling - 1f) < 0.0001f);
        Assert.DoesNotContain(patch.ParameterBindings, binding =>
            binding.FieldPath.StartsWith("/acoustic/connections/", StringComparison.OrdinalIgnoreCase) &&
            binding.FieldPath.EndsWith("/coupling", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(patch.ParameterBindings, binding =>
            binding.FieldPath.StartsWith("/acoustic/terminals/", StringComparison.OrdinalIgnoreCase) &&
            binding.FieldPath.EndsWith("/area_scale", StringComparison.OrdinalIgnoreCase) &&
            binding.ParameterPath == "/pink/velum" &&
            binding.Transform == ParameterBindingTransform.Square &&
            binding.Scale > 1000f);
        Assert.DoesNotContain(patch.ParameterBindings, binding =>
            binding.FieldPath.StartsWith("/acoustic/radiation/", StringComparison.OrdinalIgnoreCase) &&
            binding.FieldPath.EndsWith("/opening", StringComparison.OrdinalIgnoreCase) &&
            binding.ParameterPath == "/pink/velum");
        Assert.DoesNotContain(patch.ParameterBindings, binding =>
            binding.FieldPath.StartsWith("/acoustic/terminals/", StringComparison.OrdinalIgnoreCase) &&
            binding.FieldPath.EndsWith("/area_scale", StringComparison.OrdinalIgnoreCase) &&
            binding.ParameterPath == "/pink/lip/opening");

        var export = FaustEmitter.Emit(patch, new FaustExportOptions("tract_graph_boundaries"));
        Assert.Contains("graph_connection_reflection_voices_0_nose_connection", export.Source);
        Assert.Contains("graph_connection_energy_in_voices_0_nose_connection", export.Source);
        Assert.Contains("graph_connection_energy_out_voices_0_nose_connection", export.Source);
        Assert.Contains("graph_node_incident_pressure_", export.Source);
        Assert.Contains("graph_node_source_", export.Source);
        Assert.Contains("graph_area_reflection_", export.Source);
        Assert.Contains("graph_area_energy_in_", export.Source);
        Assert.Contains("graph_area_energy_out_", export.Source);
        Assert.Contains("graph_radiation_reference_area_voices_0_lip", export.Source);
        Assert.Contains("graph_radiation_differentiation_voices_0_lip", export.Source);
        Assert.Contains("graph_radiation_admittance_voices_0_lip", export.Source);
        Assert.Contains("graph_radiation_flow_voices_0_lip", export.Source);
        Assert.Contains("patch_param_0) * (patch_param_0", export.Source);

        var debugExport = FaustEmitter.Emit(patch, new FaustExportOptions("tract_graph_boundaries_debug", DebugProbeUi: true));
        Assert.Contains("vbargraph(\"/debug/voice_0/node/", debugExport.Source);
        Assert.Contains("vbargraph(\"/debug/voice_0/connection/voices_0_nose_connection/energy_in\"", debugExport.Source);
        Assert.Contains("vbargraph(\"/debug/voice_0/radiation/voices_0_lip/flow\"", debugExport.Source);
        Assert.Contains("process = ", debugExport.Source);
    }

    [Fact]
    public void GeneratedGraphInjectionSourcesOccupyCellCenters()
    {
        var patch = PatchScript.Parse("""
            tract_shape name=human length_cm=17 diameters=.6,.8,1.2,1.5,1.2,.8
            tract_injection name=inj position=5 width=1 turbulence=.5 burst=.8
            tract shape=human injection=inj propagation=graph sections=6 constriction_index=5
            """);

        var injectionSources = patch.AcousticSourcePorts
            .Where(port => port.Kind == AcousticSourceKind.TurbulenceJet)
            .OrderBy(port => port.Position)
            .ToArray();

        Assert.Equal(5, injectionSources.Length);
        Assert.Equal(0.25f, injectionSources[0].Position, 3);
        Assert.Equal(0.9167f, injectionSources[^1].Position, 3);
        Assert.All(injectionSources, source =>
        {
            Assert.NotNull(source.PositionControl);
            Assert.Equal(1f / 6f, source.PositionControl!.IndexScale, 4);
        });
    }

    [Fact]
    public void TractAreaFunctionOwnsContinuousMorphology()
    {
        var shape = new TractAreaFunction([1, 3, 1], LengthCentimeters: 18);

        Assert.Equal(0.18f, shape.LengthMeters, 5);
        Assert.Equal(2, shape.DiameterAt(.25f), 5);
        Assert.Equal(3, shape.DiameterAt(.5f), 5);

        var resampled = shape.Resample(5);
        Assert.Equal(5, resampled.Sections);
        Assert.Equal(18, resampled.LengthCentimeters);
        Assert.Equal([1, 2, 3, 2, 1], resampled.Diameters.Select(value => MathF.Round(value, 5)).ToArray());

        var pinkTromboneCellDelay = new TractAreaFunction(Enumerable.Repeat(1f, 44).ToArray(), LengthCentimeters: 17)
            .CellDelaySamples(44100);
        Assert.InRange(pinkTromboneCellDelay, .49f, .51f);
    }

    [Fact]
    public void ParserReadsTractShapeLengthAsPhysicalGeometry()
    {
        var patch = PatchScript.Parse("""
            tract_shape name=longform length_cm=24 diameters=.5,1,2,1,.5
            nasal_branch name=side length_cm=9 diameters=.01,.4,.8,.4
            tract shape=longform nasal_branch=side propagation=graph
            """);

        var shape = Assert.Single(patch.TractShapes);
        Assert.Equal(24, shape.AreaFunction.LengthCentimeters);
        Assert.Equal(1.5f, shape.AreaFunction.DiameterAt(.375f), 5);

        var branch = Assert.Single(patch.NasalBranches);
        Assert.Equal(9, branch.AreaFunction?.LengthCentimeters);
        Assert.Same(shape.AreaFunction, Assert.Single(patch.Voices).Tract?.AreaFunction);
    }

    [Fact]
    public void TractVoiceCanChooseLoweringGridWithoutChangingMorphology()
    {
        var patch = PatchScript.Parse("""
            tract_shape name=curve length_cm=17 diameters=.5,1,2,1,.5
            tract shape=curve sections=9 propagation=graph
            """);

        var shape = Assert.Single(patch.TractShapes);
        var tract = Assert.Single(patch.Voices).Tract;

        Assert.NotNull(tract?.AreaFunction);
        Assert.Equal(5, shape.AreaFunction.Sections);
        Assert.Equal(9, tract.AreaFunction.Sections);
        Assert.Equal(17, tract.AreaFunction.LengthCentimeters);
        Assert.Equal(shape.AreaFunction.DiameterAt(.375f), tract.AreaFunction.DiameterAt(.375f), 5);
    }

    [Fact]
    public void GraphTractPreservesDeclaredMorphologyGrid()
    {
        var patch = PatchScript.Parse("""
            tract_shape name=human length_cm=17 diameters=.6,.6,.8,1,1.2,1.4,1.5,1.5,1.5,1.5,1.4,1.2,1,.8,.7,.6,.6,.8,1,1.2,1.4,1.5,1.5,1.5,1.5,1.4,1.2,1,.8,.7,.6,.6,.8,1,1.2,1.4,1.5,1.5,1.5,1.5,1.4,1.2,1,.8
            nasal_branch name=nose length_cm=12 junction=17 diameters=.01,.35,.5,.65,.8,.95,1.1,1.25,1.4,1.55,1.7,1.8,1.9,1.9,1.85,1.75,1.65,1.55,1.45,1.35,1.25,1.15,1.05,.95,.85,.75,.65,.55
            tract_injection name=inj position=32 width=1
            tract shape=human nasal_branch=nose injection=inj propagation=graph tongue_index=12.9 constriction_index=32
            """);

        var tract = Assert.Single(patch.Voices).Tract;

        Assert.NotNull(tract?.AreaFunction);
        Assert.Equal(44, tract.Sections);
        Assert.Equal(28, tract.NoseSections);
        Assert.Equal(44, tract.AreaFunction.Sections);
        Assert.Equal(28, tract.Nasal?.AreaFunction?.Sections);
        Assert.Equal(17, tract.Nasal?.JunctionIndex);
        Assert.Equal(1, tract.IndexScale, 2);
        Assert.Equal(12.9f, tract.TongueIndex, 2);
        Assert.Equal(32, tract.ConstrictionIndex, 2);
        Assert.Equal(32, tract.Injection?.Position);
        Assert.Equal(1, tract.Injection?.Width);
        Assert.Equal(WaveClockDelayStrategy.FractionalLinear, Assert.Single(patch.WaveClocks).Strategy);

        var faust = FaustEmitter.Emit(patch, new FaustExportOptions("graph_declared_grid")).Source;
        Assert.Contains("graph_loop ~ si.bus", faust);
        Assert.Contains("de.fdelay", faust);
        Assert.DoesNotContain("_wg_", faust);
    }

    [Fact]
    public void ParserRejectsRemovedTractWaveguideFields()
    {
        var propagation = Assert.Throws<PatchScriptException>(() => PatchScript.Parse("tract propagation=waveguide"));
        Assert.Contains("legacy tract propagation mode", propagation.Message);

        var loss = Assert.Throws<PatchScriptException>(() => PatchScript.Parse("tract waveguide_loss=.999"));
        Assert.Contains("waveguide_loss", loss.Message);

        var substeps = Assert.Throws<PatchScriptException>(() => PatchScript.Parse("tract substeps=2"));
        Assert.Contains("substeps", substeps.Message);
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
    public void ParserSupportsRealtimeBlendableControlCurvesOnParameters()
    {
        var patch = PatchScript.Parse("""
            param path=/bird/left/pressure default=.2 min=0 max=1 step=.001 rate=audio
            curve name=left_pressure path=/bird/left/pressure points=0:.05,.08:.9,.24:.35 mode=blend depth=.8 loop=true rate=audio
            path name=left_bronchus length_cm=3.8 diameters=.22,.30,.36,.42
            path name=trachea length_cm=8.4 diameters=.38,.48,.56,.46
            source_port name=left_labium path=left_bronchus kind=syrinx position=0 pressure=@/bird/left/pressure opening=.2 tension=.4
            terminal name=left_merge path=left_bronchus position=1 kind=junction
            terminal name=trachea_base path=trachea position=0 kind=junction
            connect name=merge terminals=left_merge,trachea_base
            radiation_port name=beak path=trachea kind=beak position=1 opening=.9
            acoustic_network name=bird path=trachea sources=left_labium radiation=beak terminals=left_merge,trachea_base connections=merge
            acoustic network=bird freq=1200
            """);

        var curve = Assert.Single(patch.ControlCurves);
        Assert.Equal("left_pressure", curve.Name);
        Assert.Equal("/bird/left/pressure", curve.ParameterPath);
        Assert.Equal(ControlCurveMode.Blend, curve.Mode);
        Assert.Equal(ControlCurveInterpolation.Linear, curve.Interpolation);
        Assert.Equal(.8f, curve.Depth, 5);
        Assert.True(curve.Loop);
        Assert.Equal("audio", curve.AutomationRate);
        Assert.Equal([0f, .08f, .24f], curve.Points.Select(point => point.TimeSeconds).ToArray());
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/0/pressure");
    }

    [Fact]
    public void ParserSupportsLayeredValveAreaCurveLossRadiationAndGestureGroups()
    {
        var patch = PatchScript.Parse("""
            param path=/human/subglottal default=.82 min=0 max=1.5 step=.001 rate=audio
            param path=/human/opening default=.18 min=0 max=1 step=.001 rate=audio
            param path=/human/lip default=1.2 min=0 max=2 step=.001
            curve name=pressure_path path=/human/subglottal points=0:.12,.08:.82,.28:.55 mode=blend depth=.7 rate=audio
            curve name=opening_path path=/human/opening points=0:.04,.1:.22,.3:.12 mode=blend depth=.6 rate=audio
            gesture name=vowel_onset curves=pressure_path,opening_path depth=.9
            area_curve name=oral_vowel length_cm=17 diameters=.55,.75,1.15,1.55,1.35,.8
            path name=trachea length_cm=12 diameters=.35,.52,.68,.72 loss_model=viscous
            path name=oral area_curve=oral_vowel loss=.997 loss_model=birkholz_2024 tongue_index=3 tongue_diameter=1.1 constriction_index=4 constriction_diameter=.8 lip=@/human/lip
            source_port name=folds path=trachea kind=glottal position=1 pressure=@/human/subglottal tension=.58 opening=@/human/opening noise=.03 law=two_mass upper_mass=.22 lower_mass=.36 upper_stiffness=.04 lower_stiffness=.06 coupling_stiffness=.09 collision_stiffness=.62 collision_damping=.18 vertical_phase=.24 reservoir_pressure=.95 downstream_pressure=.08 load_coupling=.42
            terminal name=trachea_top path=trachea position=1 kind=junction
            terminal name=oral_back path=oral position=0 kind=junction
            connect name=larynx_to_tract terminals=folds,trachea_top,oral_back coupling=.92
            radiation_port name=mouth path=oral kind=lip model=lip_piston position=1 opening=@/human/lip reflection=-.82
            wave_clock name=continuous strategy=linear max_delay=4096
            acoustic_network name=human_layered path=oral wave_clock=continuous sources=folds radiation=mouth terminals=trachea_top,oral_back connections=larynx_to_tract
            acoustic network=human_layered freq=150 gain=.12
            """);

        var areaCurve = Assert.Single(patch.AcousticAreaCurves);
        Assert.Equal("oral_vowel", areaCurve.Name);
        Assert.Equal(17, areaCurve.AreaFunction.LengthCentimeters);

        var oralPath = patch.AcousticPaths.Single(path => path.Name == "oral");
        Assert.Equal("oral_vowel", oralPath.AreaCurve);
        Assert.Equal(AcousticLossModel.Birkholz2024, oralPath.LossModel);
        Assert.NotNull(oralPath.AreaControl);
        Assert.Equal(AcousticLossModel.Viscous, patch.AcousticPaths.Single(path => path.Name == "trachea").LossModel);

        var source = Assert.Single(patch.AcousticSourcePorts);
        Assert.Equal(AcousticSourceModel.TissueValve, source.Model);
        Assert.Equal(AcousticValveLaw.TwoMass, source.Law);
        Assert.Equal(.22f, source.UpperMass, 5);
        Assert.Equal(.36f, source.LowerMass, 5);
        Assert.Equal(.09f, source.CouplingStiffness, 5);
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/0/pressure");
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/sources/0/opening");

        var radiation = Assert.Single(patch.AcousticRadiationPorts);
        Assert.Equal(AcousticRadiationModel.LipPiston, radiation.Model);
        Assert.Contains(patch.ParameterBindings, binding => binding.FieldPath == "/acoustic/radiation/0/opening");

        var gesture = Assert.Single(patch.GestureGroups);
        Assert.Equal("vowel_onset", gesture.Name);
        Assert.Equal(["pressure_path", "opening_path"], gesture.Curves);
        Assert.Equal(.9f, gesture.Depth, 5);

        var export = FaustEmitter.Emit(patch, new FaustExportOptions("layered_voice"));
        Assert.Contains("_reservoir_pressure", export.Source);
        Assert.Contains("_downstream_pressure", export.Source);
        Assert.Contains("_upper_displacement", export.Source);
        Assert.Contains("_lower_displacement", export.Source);
        Assert.Contains("_coupling_stiffness", export.Source);
        Assert.Contains("_modal_frequency", export.Source);
        Assert.Contains("_modal_tissue", export.Source);
        Assert.Contains("_voicing", export.Source);
        Assert.Contains("graph_segment_loss_", export.Source);
        Assert.Contains("graph_radiation_model_mouth", export.Source);
        Assert.Contains("hslider(\"/curves/pressure_path/depth\"", export.Source);
        Assert.Contains("hslider(\"/curves/opening_path/depth\"", export.Source);
        Assert.DoesNotContain("os.phasor(1.0, layered_voice_freq", export.Source);
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
        Assert.True(comparison.Articulation.ArticulationScore > 0.99f);
        Assert.True(comparison.Articulation.EnvelopeCosineSimilarity > 0.99f);
        Assert.Equal(0, comparison.Articulation.SilenceMismatch, precision: 6);
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
    public void PinkTromboneReferenceDeclaresGraphAuthority()
    {
        var reference = PinkTromboneReference.ToReferencePatch();
        var rebuild = Assert.Single(ReferenceRebuildCatalog.PinkTromboneRebuilds);
        var patch = PatchScript.Parse(rebuild.Script);
        var export = FaustEmitter.Emit(patch, new FaustExportOptions("pink_trombone_graph"));

        Assert.Equal("pink-trombone", rebuild.Family);
        Assert.Contains(reference.Features, feature => feature.Name == "main_tract_waveguide_cells" && feature.Value == "44");
        Assert.Contains(reference.Features, feature => feature.Name == "nose_waveguide_cells" && feature.Value == "28");
        Assert.Contains(reference.Features, feature => feature.Name == "tract_sample_rate" && feature.Value == "2x-audio-sample-rate");
        Assert.Contains(reference.Features, feature => feature.Name == "reflection_formula");
        Assert.Contains(reference.Parameters, parameter => parameter.Path == "/pink/tongue/index");
        Assert.Contains(reference.Parameters, parameter => parameter.Path == "/pink/constriction/diameter");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "acoustic_graph_authority");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "tract_area_function");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "diameter_to_reflection_coefficients");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "glottal_source_primitive");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "positioned_injection_primitive");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "main_tract_waveguide_cells");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "reflection_coefficients_applied_to_waveguide");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "nose_waveguide_cells");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "nose_junction");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "positioned_turbulence_applied_to_waveguide_cells");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "tract_shape_motion");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "diameter_authority");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "closure_transients");
        Assert.Contains(rebuild.MatchedFeatures, feature => feature.Name == "tract_sample_rate");
        Assert.Contains(rebuild.MissingFeatures, feature => feature.Name == "contact_aware_closure_state");
        Assert.NotEmpty(patch.TractShapes);
        Assert.NotEmpty(patch.GlottalSources);
        Assert.NotEmpty(patch.TractInjections);
        Assert.NotEmpty(patch.NasalBranches);
        Assert.NotEmpty(patch.TractMotions);
        Assert.NotEmpty(patch.Parameters);
        Assert.Contains("process =", export.Source);
    }

    [Fact]
    public void PinkTromboneParityFixturesUseReusableLowLevelPrimitives()
    {
        Assert.True(PinkTromboneParityFixtures.All.Count >= 5);
        foreach (var fixture in PinkTromboneParityFixtures.All)
        {
            var patch = PatchScript.Parse(fixture.AquaScript);
            var voice = Assert.Single(patch.Voices);
            var export = FaustEmitter.Emit(patch, new FaustExportOptions($"pt_{fixture.Id.Replace('-', '_')}"));

            Assert.NotNull(voice.Tract);
            Assert.NotEmpty(patch.TractShapes);
            Assert.NotEmpty(patch.GlottalSources);
            Assert.NotEmpty(patch.TractInjections);
            Assert.NotEmpty(patch.NasalBranches);
            Assert.NotEmpty(patch.TractMotions);
            Assert.Equal(TractPropagationMode.Graph, voice.Tract.Propagation);
            Assert.Contains(patch.AcousticPaths, path => path.Name == "voices_0_oral" && path.AreaControl is not null);
            Assert.Contains(patch.AcousticSourcePorts, port => port.Name.StartsWith("voices_0_inj_", StringComparison.Ordinal) && port.Transient > 0 && port.PositionControl is not null);
            Assert.Contains(patch.AcousticTerminals, terminal => terminal.Name.StartsWith("voices_0_area_", StringComparison.Ordinal));
            Assert.Contains(patch.AcousticConnections, connection => connection.Name.Contains("_nose_connection", StringComparison.Ordinal) && MathF.Abs(connection.Coupling - 1f) < 0.0001f);
            Assert.DoesNotContain(patch.ParameterBindings, binding => binding.FieldPath.StartsWith("/acoustic/connections/", StringComparison.OrdinalIgnoreCase) && binding.FieldPath.EndsWith("/coupling", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("graph_connection_reflection_voices_0_nose_connection", export.Source);
            Assert.Contains("acoustic_graph_radiated", export.Source);
            Assert.Contains("graph_terminal_area_voices_0_area_", export.Source);
            Assert.Contains("graph_source_", export.Source);
            Assert.Contains("_reservoir_drive", export.Source);
            Assert.Contains("_reservoir", export.Source);
            Assert.Contains("_release_pulse", export.Source);
            Assert.DoesNotContain("wg_diameter_target_", export.Source);
            Assert.NotEmpty(fixture.ReferenceFeatures);
        }

        var identical = AudioAnalyzer.CosineSimilarity([1, 2, 3], [1, 2, 3]);
        var different = AudioAnalyzer.CosineSimilarity([1, 0, 0], [0, 1, 0]);
        Assert.True(identical > .999f);
        Assert.True(different < .001f);
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
    public void FaustEmitterKeepsControlCurveParametersRealtimeDrivable()
    {
        var export = FaustEmitter.EmitScript("""
            param path=/bird/left/pressure default=.2 min=0 max=1 step=.001
            curve name=left_pressure path=/bird/left/pressure points=0:.05,.08:.9,.24:.35 mode=blend depth=.8 loop=true
            path name=left_bronchus length_cm=3.8 diameters=.22,.30,.36,.42
            path name=trachea length_cm=8.4 diameters=.38,.48,.56,.46
            source_port name=left_labium path=left_bronchus kind=syrinx position=0 pressure=@/bird/left/pressure opening=.2 tension=.4
            terminal name=left_merge path=left_bronchus position=1 kind=junction
            terminal name=trachea_base path=trachea position=0 kind=junction
            connect name=merge terminals=left_merge,trachea_base
            radiation_port name=beak path=trachea kind=beak position=1 opening=.9
            acoustic_network name=bird path=trachea sources=left_labium radiation=beak terminals=left_merge,trachea_base connections=merge
            acoustic network=bird freq=1200
            """, new FaustExportOptions("curve_voice"));

        Assert.Contains("patch_param_0_base = hslider(\"/bird/left/pressure\", 0.2, 0, 1, 0.001) : si.smoo;", export.Source);
        Assert.Contains("patch_param_0_curve_depth = hslider(\"/curves/left_pressure/depth\", 0.8, 0, 1, 0.001) : si.smoo;", export.Source);
        Assert.Contains("patch_param_0_curve_time = wrap01((max(0.0, age * 1 - 0)) / 0.24) * 0.24;", export.Source);
        Assert.Contains("patch_param_0 = min(1, max(0, patch_param_0_base * (1.0 - patch_param_0_curve_depth) + patch_param_0_curve_value * patch_param_0_curve_depth));", export.Source);
        Assert.Contains("clip01(patch_param_0)", export.Source);
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
    public async Task FaustCompilerValidatesControlCurvePatchWhenInstalled()
    {
        var export = FaustEmitter.EmitScript("""
            param path=/macro/brightness default=.2 min=0 max=1 step=.001
            curve name=brightness_gesture path=/macro/brightness points=0:.1,.05:.8,.2:.25 mode=blend depth=.75 loop=true
            v w=saw f=80 lpf=@/macro/brightness sustain=.15
            """);
        var validation = await FaustCompiler.ValidateAsync(export.Source);

        if (validation is null)
        {
            return;
        }

        Assert.True(validation.Success, validation.Stderr);
    }

    [Fact]
    public async Task FaustCompilerValidatesAcousticPathGraphWhenInstalled()
    {
        var export = FaustEmitter.EmitScript("""
            path name=trachea length_cm=12 diameters=.4,.7,1,1
            path name=oral length_cm=17 diameters=.6,1.1,1.6,1.2,.8
            path name=nasal length_cm=10 diameters=.05,.3,.8,.5
            source_port name=folds path=trachea kind=glottal position=1 pressure=.7 tension=.55 opening=.45
            radiation_port name=mouth path=oral kind=lip position=1 opening=1.4 reflection=-.82
            radiation_port name=nostril path=nasal kind=nostril position=1 opening=.5 reflection=-.45
            terminal name=trachea_bottom path=trachea position=0 kind=closed reflection=.75
            terminal name=oral_back path=oral position=0 kind=junction area_scale=1
            terminal name=nasal_gate path=nasal position=0 kind=junction area_scale=.18
            connect name=velopharynx terminals=folds,oral_back,nasal_gate law=area_scatter coupling=.18
            wave_clock name=continuous strategy=thiran order=1 max_delay=4096 smoothing_ms=3
            acoustic_network name=humanish path=oral wave_clock=continuous sources=folds radiation=mouth,nostril terminals=trachea_bottom,oral_back,nasal_gate connections=velopharynx
            acoustic network=humanish freq=130 gain=.1
            """, new FaustExportOptions("acoustic_graph_validation", DebugProbeUi: true));
        var validation = await FaustCompiler.ValidateAsync(export.Source);

        if (validation is null)
        {
            return;
        }

        Assert.True(validation.Success, validation.Stderr);
    }

    [Fact]
    public async Task FaustCompilerRendersTissueValveGraphWhenInstalled()
    {
        var export = FaustEmitter.EmitScript("""
            path name=left_bronchus length_cm=3.8 diameters=.22,.30,.36,.42
            path name=right_bronchus length_cm=3.6 diameters=.20,.28,.34,.40
            path name=trachea length_cm=8.4 diameters=.38,.48,.56,.46
            source_port name=left_labium path=left_bronchus kind=syrinx position=0 pressure=.82 tension=.44 opening=.22 noise=.015 impedance=.7 mass=.32 damping=.14 stiffness=.025 saturation=.9 drive=1.2 load_coupling=.42 rest_opening=.025
            source_port name=right_labium path=right_bronchus kind=syrinx position=0 pressure=.70 tension=.51 opening=.20 noise=.015 balance=.96 impedance=.7 mass=.34 damping=.16 stiffness=.03 saturation=.9 drive=1.1 load_coupling=.42 rest_opening=.025
            terminal name=left_merge path=left_bronchus position=1 kind=junction area_scale=1
            terminal name=right_merge path=right_bronchus position=1 kind=junction area_scale=1
            terminal name=trachea_base path=trachea position=0 kind=junction area_scale=1
            connect name=syrinx_merge terminals=left_merge,right_merge,trachea_base law=area_scatter coupling=1
            radiation_port name=beak path=trachea kind=beak position=1 opening=.95 reflection=-.72
            wave_clock name=bird_clock strategy=linear max_delay=1024 smoothing_ms=2
            acoustic_network name=bird_syrinx path=trachea wave_clock=bird_clock sources=left_labium,right_labium radiation=beak terminals=left_merge,right_merge,trachea_base connections=syrinx_merge
            acoustic network=bird_syrinx freq=1120 gain=.32 sustain=.18 decay=.04
            """, new FaustExportOptions("tissue_valve_render_validation"));
        var render = await FaustCompiler.RenderAsync(export.Source, new FaustRenderOptions(DurationSeconds: .18f));

        if (render is null)
        {
            return;
        }

        Assert.True(render.Samples.Length > 0, render.Stderr);
        Assert.All(render.Samples, sample => Assert.True(float.IsFinite(sample), "tissue-valve render produced a non-finite sample"));
        Assert.True(render.Samples.Select(MathF.Abs).DefaultIfEmpty(0).Max() > 0.000001f, render.Stderr);
        Assert.True(render.Samples.Select(MathF.Abs).DefaultIfEmpty(0).Max() <= 1.2f, render.Stderr);
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

