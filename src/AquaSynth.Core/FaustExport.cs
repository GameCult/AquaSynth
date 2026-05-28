using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AquaSynth.Dsl;

public sealed record FaustExportOptions(string Name = "aquasynth_patch", bool Stereo = false, bool DebugProbeUi = false);

public sealed record FaustExport(string Source, IReadOnlyList<string> Warnings);

public static class FaustEmitter
{
    public static FaustExport EmitScript(string script, FaustExportOptions? options = null) =>
        Emit(PatchScript.Parse(script), options ?? new FaustExportOptions());

    public static FaustExport Emit(SynthPatch patch, FaustExportOptions? options = null)
    {
        options ??= new FaustExportOptions();
        if (patch.Voices.Count == 0 && patch.SpectralBanks.Count == 0 && patch.OperatorGraphs.Count == 0) throw new ArgumentException("cannot export an empty patch", nameof(patch));

        var warnings = new List<string>();
        var parameters = new ParameterMap(patch, warnings);
        var source = new StringBuilder();
        source.AppendLine("import(\"stdfaust.lib\");");
        source.AppendLine($"declare name \"{Escape(options.Name)}\";");
        source.AppendLine($"declare options \"{FaustOptions(patch.Playback)}\";");
        source.AppendLine();
        source.AppendLine("time = ba.time / ma.SR;");
        if (patch.Repeat is { } repeat)
        {
            var interval = parameters.Expression("/patch/repeat", repeat.IntervalSeconds);
            source.AppendLine($"age = time - floor(time / {interval}) * {interval};");
        }
        else
        {
            source.AppendLine("age = time;");
        }
        source.AppendLine("clip01(x) = min(1.0, max(0.0, x));");
        source.AppendLine("wrap01(x) = x - floor(x);");
        source.AppendLine("slew(rate,x) = x : *(1.0 - exp((0.0 - max(0.0001, rate)) / ma.SR)) : + ~ *(exp((0.0 - max(0.0001, rate)) / ma.SR));");
        source.AppendLine("softclip(x) = ma.tanh(x * 1.35);");
        source.AppendLine("fold(x) = 2.0 * abs(2.0 * (x / 4.0 - floor(x / 4.0)) - 1.0) - 1.0;");
        source.AppendLine("release_start(a,d,g) = max(g, a + d);");
        source.AppendLine("oneshot_adsr(a,d,s,r,g) = select2(age < a, select2(age < a + d, select2(age < release_start(a,d,g), select2(age < release_start(a,d,g) + r, 0.0, s * (1.0 - (age - release_start(a,d,g)) / max(0.0001, r))), s), 1.0 - (1.0 - s) * ((age - a) / max(0.0001, d))), age / max(0.0001, a));");
        source.AppendLine("seg(t,t0,d,a,b) = a + (b - a) * clip01((t - t0) / max(0.0001, d));");
        source.AppendLine("smooth01(x) = clip01(x) * clip01(x) * (3.0 - 2.0 * clip01(x));");
        source.AppendLine("seg_smooth(t,t0,d,a,b) = a + (b - a) * smooth01((t - t0) / max(0.0001, d));");
        source.AppendLine("seg_exp(t,t0,d,a,b) = exp(log(max(0.00001, a)) + (log(max(0.00001, b)) - log(max(0.00001, a))) * clip01((t - t0) / max(0.0001, d)));");
        source.AppendLine("seg_curve(c,t,t0,d,a,b) = select2(c < 0.5, seg_exp(t,t0,d,a,b), seg(t,t0,d,a,b));");
        source.AppendLine("rl_release_start(r1,r2,r3,g) = max(g, r1 + r2 + r3);");
        source.AppendLine("rl4_env_from(s0,r1,l1,c1,r2,l2,c2,r3,l3,c3,r4,l4,c4,g) = select2(age < r1, select2(age < r1 + r2, select2(age < r1 + r2 + r3, select2(age < rl_release_start(r1,r2,r3,g), select2(age < rl_release_start(r1,r2,r3,g) + r4, l4, seg_curve(c4, age, rl_release_start(r1,r2,r3,g), r4, l3, l4)), l3), seg_curve(c3, age, r1 + r2, r3, l2, l3)), seg_curve(c2, age, r1, r2, l1, l2)), seg_curve(c1, age, 0, r1, s0, l1));");
        source.AppendLine("rl4_env(r1,l1,c1,r2,l2,c2,r3,l3,c3,r4,l4,c4,g) = rl4_env_from(0.0,r1,l1,c1,r2,l2,c2,r3,l3,c3,r4,l4,c4,g);");
        source.AppendLine("lfo_sin(hz, phase) = sin(2.0 * ma.PI * (age * hz + phase));");
        source.AppendLine("lfo_tri(hz, phase) = 1.0 - 4.0 * abs((age * hz + phase - floor(age * hz + phase)) - 0.5);");
        source.AppendLine("lfo_sq(hz, phase) = select2((age * hz + phase - floor(age * hz + phase)) < 0.5, -1.0, 1.0);");
        source.AppendLine("lfo_hold(hz, phase) = no.noise : ba.latch(os.oscrs(hz));");
        source.AppendLine();

        var hostPlayback = UsesHostPlayback(patch.Playback);
        if (hostPlayback)
        {
            EmitPlaybackControls(source, patch.Playback);
            source.AppendLine();
        }

        EmitParameterControls(source, patch, parameters);
        if (patch.Parameters.Count > 0)
        {
            source.AppendLine();
        }

        foreach (var (target, name) in ModTargets)
        {
            source.AppendLine($"patch_mod_{name} = {ModExpressionForTarget(patch.Controls, target)};");
        }
        source.AppendLine();

        var voices = new List<string>();
        for (var i = 0; i < patch.Voices.Count; i++)
        {
            var name = $"voice_{i}";
            EmitVoice(source, patch, patch.Voices[i], VoicePath(i), name, parameters, warnings, options);
            voices.Add(name);
        }
        for (var i = 0; i < patch.SpectralBanks.Count; i++)
        {
            var name = $"spectral_{i}";
            EmitSpectralBank(source, patch, patch.SpectralBanks[i], i, name, parameters, warnings, options);
            voices.Add(name);
        }
        for (var i = 0; i < patch.OperatorGraphs.Count; i++)
        {
            var name = $"opgraph_{i}";
            EmitOperatorGraph(source, patch.Playback, patch.OperatorGraphs[i], name, parameters, warnings);
            voices.Add(name);
        }

        var mix = voices.Count == 0 ? "0.0" : string.Join(" + ", voices);
        var final = $"({mix}) * {parameters.Expression("/patch/gain", patch.Gain)}";
        if (hostPlayback) final = $"({final}) * gain";
        if (patch.SoftClip) final = $"softclip({final})";
        var unbound = parameters.UnboundParameterIds().ToList();
        if (unbound.Count > 0)
        {
            final = $"({final}) + 0.0 * ({string.Join(" + ", unbound)})";
        }
        source.AppendLine(options.Stereo ? $"process = {final} <: _,_;" : $"process = {final};");
        return new FaustExport(source.ToString(), warnings);
    }

    private static void EmitParameterControls(StringBuilder source, SynthPatch patch, ParameterMap parameters)
    {
        for (var i = 0; i < patch.Parameters.Count; i++)
        {
            var parameter = patch.Parameters[i];
            var parameterId = ParameterIdentifier(i);
            var curve = patch.ControlCurves.FirstOrDefault(candidate => candidate.ParameterPath.Equals(parameter.Path, StringComparison.OrdinalIgnoreCase));
            if (curve is null)
            {
                source.AppendLine($"{parameterId} = hslider(\"{Escape(parameter.Path)}\", {F(parameter.Default)}, {F(parameter.Min)}, {F(parameter.Max)}, {F(parameter.Step)}) : si.smoo;");
                continue;
            }

            var baseId = $"{parameterId}_base";
            var curveId = $"{parameterId}_curve";
            var depthId = $"{parameterId}_curve_depth";
            source.AppendLine($"{baseId} = hslider(\"{Escape(parameter.Path)}\", {F(parameter.Default)}, {F(parameter.Min)}, {F(parameter.Max)}, {F(parameter.Step)}) : si.smoo;");
            EmitControlCurve(source, curve, parameter, curveId);
            source.AppendLine($"{depthId} = hslider(\"/curves/{Escape(curve.Name)}/depth\", {F(Math.Clamp(curve.Depth, 0, 1))}, 0, 1, 0.001) : si.smoo;");
            var blended = curve.Mode == ControlCurveMode.Add
                ? $"{baseId} + ({curveId}_value - {F(parameter.Default)}) * {depthId}"
                : $"{baseId} * (1.0 - {depthId}) + {curveId}_value * {depthId}";
            source.AppendLine($"{parameterId} = min({F(parameter.Max)}, max({F(parameter.Min)}, {blended}));");
        }
    }

    private static void EmitControlCurve(StringBuilder source, ControlCurve curve, PatchParameter parameter, string curveId)
    {
        var points = curve.Points
            .OrderBy(point => point.TimeSeconds)
            .ToArray();
        var lastTime = Math.Max(points[^1].TimeSeconds, 0.0001f);
        var scaledTime = $"max(0.0, age * {F(curve.TimeScale)} - {F(curve.TimeOffsetSeconds)})";
        var curveTime = curve.Loop && points.Length > 1
            ? $"wrap01(({scaledTime}) / {F(lastTime)}) * {F(lastTime)}"
            : scaledTime;
        source.AppendLine($"{curveId}_time = {curveTime};");
        source.AppendLine($"{curveId}_value = {ControlCurveValueExpression(curveId, points, curve.Interpolation, parameter.Default)};");
    }

    private static string ControlCurveValueExpression(
        string curveId,
        IReadOnlyList<ControlCurvePoint> points,
        ControlCurveInterpolation interpolation,
        float fallback)
    {
        if (points.Count == 0) return F(fallback);
        if (points.Count == 1) return F(points[0].Value);

        var expression = F(points[^1].Value);
        for (var i = points.Count - 2; i >= 0; i--)
        {
            var from = points[i];
            var to = points[i + 1];
            var segment = interpolation switch
            {
                ControlCurveInterpolation.Hold => F(from.Value),
                ControlCurveInterpolation.Smooth => $"seg_smooth({curveId}_time, {F(from.TimeSeconds)}, {F(Math.Max(0.0001f, to.TimeSeconds - from.TimeSeconds))}, {F(from.Value)}, {F(to.Value)})",
                _ => $"seg({curveId}_time, {F(from.TimeSeconds)}, {F(Math.Max(0.0001f, to.TimeSeconds - from.TimeSeconds))}, {F(from.Value)}, {F(to.Value)})"
            };
            expression = $"select2({curveId}_time < {F(to.TimeSeconds)}, {expression}, {segment})";
        }

        return expression;
    }

    private static string FaustOptions(Playback playback)
    {
        var voices = playback.Mode == PlaybackMode.Poly ? Math.Max(1, playback.Voices) : 1;
        var midi = playback.Midi || playback.Mode == PlaybackMode.Poly ? "[midi:on]" : "";
        return $"{midi}[nvoices:{voices}]";
    }

    private static bool UsesHostPlayback(Playback playback) =>
        playback.Midi || playback.Mode is PlaybackMode.Mono or PlaybackMode.Poly;

    private static void EmitPlaybackControls(StringBuilder source, Playback playback)
    {
        source.AppendLine($"freq = nentry(\"freq\", {F(playback.FrequencyHz)}, 20, 20000, 0.01) : si.smoo;");
        source.AppendLine($"gain = nentry(\"gain\", {F(playback.Gain)}, 0, 1, 0.001) : si.smoo;");
        source.AppendLine("gate = button(\"gate\");");
    }

    private static void EmitVoice(
        StringBuilder source,
        SynthPatch patch,
        Voice voice,
        string ownerPath,
        string name,
        ParameterMap parameters,
        List<string> warnings,
        FaustExportOptions options,
        string? oscillatorOverride = null)
    {
        if (voice.Filter.LowPassResonance != 0 ||
            voice.Filter.LowPassQ != 0 ||
            parameters.IsBound(OwnerField(ownerPath, "filter/resonance")) ||
            parameters.IsBound(OwnerField(ownerPath, "filter/lpf_q")))
        {
            warnings.Add($"{name}: low-pass resonance is approximated with Faust resonlp");
        }

        var pitch = ModExpressionForTarget(voice.Modulators, ModTarget.Pitch);
        var duty = ModExpressionForTarget(voice.Modulators, ModTarget.Duty);
        var gain = ModExpressionForTarget(voice.Modulators, ModTarget.Gain);
        var noise = ModExpressionForTarget(voice.Modulators, ModTarget.Noise);
        var drive = ModExpressionForTarget(voice.Modulators, ModTarget.Drive);
        var fold = ModExpressionForTarget(voice.Modulators, ModTarget.Fold);
        var fmIndexMod = ModExpressionForTarget(voice.Modulators, ModTarget.FmIndex);
        var formant = ModExpressionForTarget(voice.Modulators, ModTarget.FormantMix);
        var lpfMod = ModExpressionForTarget(voice.Modulators, ModTarget.LowPass);
        var hpfMod = ModExpressionForTarget(voice.Modulators, ModTarget.HighPass);

        var minFreq = parameters.Expression(OwnerField(ownerPath, "pitch/min_freq"), voice.Pitch.MinFrequencyHz);
        var noteFreq = NoteFrequencyExpression(source, patch.Playback, voice.Note, name, OwnerField(ownerPath, "note/frequency"), parameters);
        var noteGate = NoteGateExpression(source, patch.Playback, voice.Note, name, OwnerField(ownerPath, "note/gate"), parameters);
        var pitchRamp = parameters.Expression(OwnerField(ownerPath, "pitch/ramp"), voice.Pitch.RampPerSecond);
        var pitchDelta = parameters.Expression(OwnerField(ownerPath, "pitch/delta"), voice.Pitch.DeltaRampPerSecond);
        var vibratoDepth = parameters.Expression(OwnerField(ownerPath, "pitch/vibrato"), voice.Pitch.VibratoDepth);
        var vibratoHz = parameters.Expression(OwnerField(ownerPath, "pitch/vibrato_hz"), voice.Pitch.VibratoHz);
        var vibratoDelay = parameters.Expression(OwnerField(ownerPath, "pitch/vibrato_delay"), voice.Pitch.VibratoDelaySeconds);
        var baseFreq = $"max({minFreq}, {noteFreq} * pow(2.0, {pitchRamp} * age + 0.5 * {pitchDelta} * age * age))";
        var hasVibrato = voice.Pitch.VibratoDepth != 0 && voice.Pitch.VibratoHz > 0 ||
                         parameters.IsBound(OwnerField(ownerPath, "pitch/vibrato")) ||
                         parameters.IsBound(OwnerField(ownerPath, "pitch/vibrato_hz"));
        var vibrato = hasVibrato
            ? $" * (1.0 + select2(age < {vibratoDelay}, 0.0, sin(2.0 * ma.PI * (age - {vibratoDelay}) * {vibratoHz}) * {vibratoDepth}))"
            : "";
        var arpeggio = voice.Arpeggio is null
            ? "1.0"
            : $"select2(age < {parameters.Expression(OwnerField(ownerPath, "arpeggio/delay"), voice.Arpeggio.DelaySeconds)}, {parameters.Expression(OwnerField(ownerPath, "arpeggio/multiplier"), voice.Arpeggio.Multiplier)}, 1.0)";
        var frequency = $"(({baseFreq}){vibrato}) * {arpeggio} * pow(2.0, patch_mod_pitch + {pitch})";
        var dutyExpression = $"clip01({parameters.Expression(OwnerField(ownerPath, "osc/duty"), voice.Oscillator.Duty)} + {parameters.Expression(OwnerField(ownerPath, "duty/ramp"), voice.Duty.RampPerSecond)} * age + patch_mod_duty + {duty})";
        var fmIndex = $"max(0.0, {parameters.Expression(OwnerField(ownerPath, "fm/index"), voice.Fm.Index)} + patch_mod_fm_index + {fmIndexMod}) * {FmDecay(parameters.Expression(OwnerField(ownerPath, "fm/decay"), voice.Fm.IndexDecaySeconds), voice.Fm.IndexDecaySeconds, parameters.IsBound(OwnerField(ownerPath, "fm/decay")))}";
        var oscillator = oscillatorOverride ??
                        (voice.Tract is not null && voice.AcousticNetwork is { } tractGraph
                             ? AcousticNetworkExpression(source, patch, tractGraph, ownerPath, name, frequency, parameters, warnings, options)
                             : voice.Tract is not null
                             ? MissingGraphTractExpression(source, warnings, name)
                             : voice.AcousticNetwork is { } acousticNetwork
                             ? AcousticNetworkExpression(source, patch, acousticNetwork, ownerPath, name, frequency, parameters, warnings, options)
                             : OscillatorExpression(patch, voice, ownerPath, frequency, dutyExpression, fmIndex, parameters));
        var envelope = voice.RateLevelEnvelope is not null
            ? RateLevelEnvelopeExpression(voice.RateLevelEnvelope, noteGate)
            : EnvelopeExpression(
                voice.Envelope,
                noteGate,
                UsesHostPlayback(patch.Playback) || voice.Note.Source == NoteSource.Host,
                field => parameters.Expression(OwnerField(ownerPath, field), field switch
                {
                    "env/attack" => voice.Envelope.AttackSeconds,
                    "env/decay" => voice.Envelope.DecaySeconds,
                    "env/sustain_level" => voice.Envelope.SustainLevel,
                    "env/release" => voice.Envelope.ReleaseSeconds,
                    _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
                }));
        var tremoloDepth = parameters.Expression(OwnerField(ownerPath, "color/tremolo"), Math.Clamp(voice.Color.TremoloDepth, 0, 1));
        var tremoloHz = parameters.Expression(OwnerField(ownerPath, "color/tremolo_hz"), voice.Color.TremoloHz);
        var hasTremolo = voice.Color.TremoloDepth > 0 && voice.Color.TremoloHz > 0 ||
                         parameters.IsBound(OwnerField(ownerPath, "color/tremolo")) ||
                         parameters.IsBound(OwnerField(ownerPath, "color/tremolo_hz"));
        var tremolo = hasTremolo
            ? $" * (1.0 - {tremoloDepth} * (0.5 + 0.5 * lfo_sin({tremoloHz}, 0.0)))"
            : "";
        var noiseMix = $"clip01({parameters.Expression(OwnerField(ownerPath, "color/noise"), voice.Color.NoiseMix)} + patch_mod_noise + {noise})";
        var driveExpression = $"clip01({parameters.Expression(OwnerField(ownerPath, "color/drive"), voice.Color.Drive)} + patch_mod_drive + {drive})";
        var foldExpression = $"clip01({parameters.Expression(OwnerField(ownerPath, "color/fold"), voice.Color.Fold)} + patch_mod_fold + {fold})";
        var formantMix = $"clip01({parameters.Expression(OwnerField(ownerPath, "color/formant_mix"), voice.Color.FormantMix)} + patch_mod_formant_mix + {formant})";
        var lpfEnvelope = voice.Filter.LowPassEnvelope is { } filterEnvelope
            ? $" + {RateLevelEnvelopeExpression(filterEnvelope, noteGate)}"
            : "";
        var hpfEnvelope = voice.Filter.HighPassEnvelope is { } highPassEnvelope
            ? $" + {RateLevelEnvelopeExpression(highPassEnvelope, noteGate)}"
            : "";
        var lpf = $"clip01({parameters.Expression(OwnerField(ownerPath, "filter/lpf"), voice.Filter.LowPass)} * (1.0 + {parameters.Expression(OwnerField(ownerPath, "filter/lpf_ramp"), voice.Filter.LowPassRamp)} * age * 1.8){lpfEnvelope} + patch_mod_lpf + {lpfMod})";
        var hpf = $"clip01({parameters.Expression(OwnerField(ownerPath, "filter/hpf"), voice.Filter.HighPass)} * (1.0 + {parameters.Expression(OwnerField(ownerPath, "filter/hpf_ramp"), voice.Filter.HighPassRamp)} * age * 2.0){hpfEnvelope} + patch_mod_hpf + {hpfMod})";
        var bpf = $"clip01({parameters.Expression(OwnerField(ownerPath, "filter/bpf"), voice.Filter.BandPass)})";
        var notch = $"clip01({parameters.Expression(OwnerField(ownerPath, "filter/notch"), voice.Filter.Notch)})";
        var highPassOrder = Math.Clamp(voice.Filter.HighPassOrder, 1, 12);
        var hasExplicitQ = voice.Filter.LowPassQ > 0 || parameters.IsBound(OwnerField(ownerPath, "filter/lpf_q"));
        var hasResonance = voice.Filter.LowPassResonance > 0 || parameters.IsBound(OwnerField(ownerPath, "filter/resonance"));
        var resonance = parameters.Expression(OwnerField(ownerPath, "filter/resonance"), voice.Filter.LowPassResonance);
        var lowPassQ = parameters.Expression(OwnerField(ownerPath, "filter/lpf_q"), voice.Filter.LowPassQ);
        var lowPassOrder = Math.Clamp(voice.Filter.LowPassOrder, 1, 12);
        var lowpass = hasExplicitQ
            ? ResonantLowPassCascade($"max(20.0, {lpf} * 18000.0)", $"max(0.1, {lowPassQ})", lowPassOrder)
            : hasResonance
            ? $"fi.resonlp(max(20.0, {lpf} * 18000.0), 0.7 + clip01({resonance}) * 18.0, 1.0)"
            : $"fi.lowpass({lowPassOrder}, max(20.0, {lpf} * 18000.0))";
        var bandpass = BandPassCascade(
            $"max(20.0, {bpf} * 18000.0)",
            $"max(0.1, {parameters.Expression(OwnerField(ownerPath, "filter/bpf_q"), voice.Filter.BandPassQ)})",
            voice.Filter.BandPassOrder);
        var notchFilter = NotchCascade(
            $"max(20.0, {notch} * 18000.0)",
            $"max(1.0, (max(20.0, {notch} * 18000.0)) / max(0.1, {parameters.Expression(OwnerField(ownerPath, "filter/notch_q"), voice.Filter.NotchQ)}))",
            voice.Filter.NotchOrder);
        var bandPassStage = voice.Filter.BandPass > 0 || parameters.IsBound(OwnerField(ownerPath, "filter/bpf"))
            ? $" : {bandpass}"
            : "";
        var notchStage = voice.Filter.Notch > 0 || parameters.IsBound(OwnerField(ownerPath, "filter/notch"))
            ? $" : {notchFilter}"
            : "";

        source.AppendLine($"{name}_freq = {frequency};");
        source.AppendLine($"{name}_osc = {oscillator};");
        source.AppendLine($"{name}_colored = ({name}_osc * (1.0 - {noiseMix}) + no.noise * {noiseMix});");
        source.AppendLine($"{name}_driven = ma.tanh({name}_colored * (1.0 + {driveExpression} * 12.0)) / ma.tanh(1.0 + {driveExpression} * 12.0);");
        source.AppendLine($"{name}_folded = {name}_driven * (1.0 - {foldExpression}) + fold({name}_driven * (1.0 + {foldExpression} * 3.5)) * {foldExpression};");
        source.AppendLine($"{name}_filtered = {name}_folded : {lowpass} : fi.highpass({highPassOrder}, max(5.0, ({hpf}) * ({hpf}) * 7000.0)){bandPassStage}{notchStage};");
        if (voice.Phaser.OffsetSeconds != 0 || voice.Phaser.RampSecondsPerSecond != 0 ||
            parameters.IsBound(OwnerField(ownerPath, "phaser/offset")) ||
            parameters.IsBound(OwnerField(ownerPath, "phaser/ramp")))
        {
            var delay = $"min(2047.0, max(0.0, abs({parameters.Expression(OwnerField(ownerPath, "phaser/offset"), voice.Phaser.OffsetSeconds)} + {parameters.Expression(OwnerField(ownerPath, "phaser/ramp"), voice.Phaser.RampSecondsPerSecond)} * age) * ma.SR))";
            source.AppendLine($"{name}_phased = {name}_filtered + ({name}_filtered : de.fdelay(2048, {delay}));");
        }
        else
        {
            source.AppendLine($"{name}_phased = {name}_filtered;");
        }
        source.AppendLine($"{name}_formants = {FormantExpression(name, voice, ownerPath, parameters)};");
        source.AppendLine($"{name} = (({name}_phased * (1.0 - {formantMix}) + {name}_formants * {formantMix}) * {envelope}{tremolo} * max(0.0, 1.0 + patch_mod_gain + {gain}) * {parameters.Expression(OwnerField(ownerPath, "gain"), voice.Gain)});");
        source.AppendLine();
    }

    private static string ResonantLowPassCascade(string cutoff, string q, int order)
    {
        var stages = Math.Max(1, (order + 1) / 2);
        return string.Join(" : ", Enumerable.Repeat($"fi.resonlp({cutoff}, {q}, 1.0)", stages));
    }

    private static string BandPassCascade(string center, string q, int order)
    {
        var stages = Math.Max(1, (Math.Clamp(order, 1, 12) + 1) / 2);
        return string.Join(" : ", Enumerable.Repeat($"fi.resonbp({center}, {q}, 1.0)", stages));
    }

    private static string NotchCascade(string center, string width, int order)
    {
        var stages = Math.Max(1, (Math.Clamp(order, 1, 12) + 1) / 2);
        return string.Join(" : ", Enumerable.Repeat($"fi.notchw({width}, {center})", stages));
    }

    private static void EmitSpectralBank(
        StringBuilder source,
        SynthPatch patch,
        SpectralBank bank,
        int bankIndex,
        string name,
        ParameterMap parameters,
        List<string> warnings,
        FaustExportOptions options)
    {
        const int tableSize = 131072;
        var table = PadSynthWaveform.Generate(bank, tableSize);
        var frequency = parameters.Expression(OwnerField(SpectralPath(bankIndex), "note/frequency"), bank.Treatment.Note.FrequencyHz);
        var readFrequency = $"({F(PadSynthWaveform.SampleRate)} / {F(tableSize)} * ({frequency}) / {F(bank.RootFrequencyHz)})";
        source.AppendLine($"{name}_wave = waveform {{{string.Join(",", table.Select(F))}}};");
        source.AppendLine($"{name}_read_pos = os.phasor({tableSize}, {readFrequency});");
        source.AppendLine($"{name}_read_index = int({name}_read_pos);");
        source.AppendLine($"{name}_read_frac = {name}_read_pos - float({name}_read_index);");
        source.AppendLine($"{name}_read_next = ({name}_read_index + 1) % {tableSize};");
        source.AppendLine($"{name}_wavetable = ({name}_wave, {name}_read_index : rdtable) * (1 - {name}_read_frac) + ({name}_wave, {name}_read_next : rdtable) * {name}_read_frac;");
        EmitVoice(source, patch, bank.Treatment, SpectralPath(bankIndex), name, parameters, warnings, options, $"{name}_wavetable");
    }

    private static string NoteFrequencyExpression(StringBuilder source, Playback playback, Note note, string name, string fieldPath, ParameterMap parameters)
    {
        if (UsesHostPlayback(playback))
        {
            return "freq";
        }
        if (note.Source != NoteSource.Host)
        {
            return parameters.Expression(fieldPath, note.FrequencyHz);
        }

        var control = $"{name}_note_freq";
        source.AppendLine($"{control} = hslider(\"{Escape(fieldPath)}\", {F(note.FrequencyHz)}, 20, 20000, 0.01) : si.smoo;");
        return control;
    }

    private static string NoteGateExpression(StringBuilder source, Playback playback, Note note, string name, string fieldPath, ParameterMap parameters)
    {
        if (UsesHostPlayback(playback))
        {
            return "gate";
        }
        if (note.Source != NoteSource.Host)
        {
            return parameters.Expression(fieldPath, note.GateSeconds);
        }

        var control = $"{name}_note_gate";
        source.AppendLine($"{control} = button(\"{Escape(fieldPath)}\");");
        return control;
    }

    private static string EnvelopeExpression(Envelope envelope, string gate, bool hostGate, Func<string, string> value)
    {
        var attack = value("env/attack");
        var decay = value("env/decay");
        var sustain = value("env/sustain_level");
        var release = value("env/release");
        return hostGate
            ? $"en.adsr({attack}, {decay}, {sustain}, {release}, {gate})"
            : $"oneshot_adsr({attack}, {decay}, {sustain}, {release}, {gate})";
    }

    private static string RateLevelEnvelopeExpression(RateLevelEnvelope envelope, string gate) =>
        envelope.StartLevel == 0
            ? $"rl4_env({F(envelope.Rate1Seconds)}, {F(envelope.Level1)}, {Curve(envelope.Curve1)}, {F(envelope.Rate2Seconds)}, {F(envelope.Level2)}, {Curve(envelope.Curve2)}, {F(envelope.Rate3Seconds)}, {F(envelope.Level3)}, {Curve(envelope.Curve3)}, {F(envelope.Rate4Seconds)}, {F(envelope.Level4)}, {Curve(envelope.Curve4)}, {gate})"
            : $"rl4_env_from({F(envelope.StartLevel)}, {F(envelope.Rate1Seconds)}, {F(envelope.Level1)}, {Curve(envelope.Curve1)}, {F(envelope.Rate2Seconds)}, {F(envelope.Level2)}, {Curve(envelope.Curve2)}, {F(envelope.Rate3Seconds)}, {F(envelope.Level3)}, {Curve(envelope.Curve3)}, {F(envelope.Rate4Seconds)}, {F(envelope.Level4)}, {Curve(envelope.Curve4)}, {gate})";

    private static string Curve(RateLevelCurve curve) =>
        curve == RateLevelCurve.Exponential ? "1" : "0";

    private static string OscillatorExpression(SynthPatch patch, Voice voice, string ownerPath, string frequency, string duty, string fmIndex, ParameterMap parameters)
    {
        var hasFmMod = patch.Controls.Any(control => control.Modulator.Target == ModTarget.FmIndex) ||
                       voice.Modulators.Any(modulator => modulator.Target == ModTarget.FmIndex) ||
                       parameters.IsBound(OwnerField(ownerPath, "fm/index")) ||
                       parameters.IsBound(OwnerField(ownerPath, "fm/decay"));
        var phaseMod = voice.Fm.Index > 0 || voice.Fm.IndexDecaySeconds > 0 || hasFmMod
            ? $" + sin(2.0 * ma.PI * os.phasor(1.0, {frequency} * max(0.0, {parameters.Expression(OwnerField(ownerPath, "fm/ratio"), Math.Max(voice.Fm.Ratio, 0))}))) * ({fmIndex}) / ma.PI"
            : "";
        var phase = $"wrap01(os.phasor(1.0, {frequency}) + {parameters.Expression(OwnerField(ownerPath, "osc/phase"), voice.Oscillator.Phase)}{phaseMod})";
        return voice.Oscillator.Waveform switch
        {
            Waveform.Sine => $"sin(2.0 * ma.PI * {phase})",
            Waveform.Square => $"select2({phase} < {duty}, -0.5, 0.5)",
            Waveform.Sawtooth => $"2.0 * {phase} - 1.0",
            Waveform.Triangle => $"1.0 - 4.0 * abs({phase} - 0.5)",
            Waveform.Noise => "no.noise",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static string AcousticNetworkExpression(
        StringBuilder source,
        SynthPatch patch,
        AcousticPortNetwork network,
        string ownerPath,
        string name,
        string frequency,
        ParameterMap parameters,
        List<string> warnings,
        FaustExportOptions options)
    {
        var paths = patch.AcousticPaths.ToDictionary(path => path.Name, StringComparer.OrdinalIgnoreCase);
        var sources = patch.AcousticSourcePorts.ToDictionary(port => port.Name, StringComparer.OrdinalIgnoreCase);
        var branches = patch.AcousticBranches.ToDictionary(branch => branch.Name, StringComparer.OrdinalIgnoreCase);
        var radiation = patch.AcousticRadiationPorts.ToDictionary(port => port.Name, StringComparer.OrdinalIgnoreCase);
        var terminals = patch.AcousticTerminals.ToDictionary(terminal => terminal.Name, StringComparer.OrdinalIgnoreCase);
        var connections = patch.AcousticConnections.ToDictionary(connection => connection.Name, StringComparer.OrdinalIgnoreCase);
        var waveClocks = patch.WaveClocks.ToDictionary(clock => clock.Name, StringComparer.OrdinalIgnoreCase);
        if (!paths.TryGetValue(network.PrimaryPath, out var primaryPath))
        {
            warnings.Add($"{name}: acoustic network `{network.Name}` has unknown primary path `{network.PrimaryPath}`");
            return "0.0";
        }

        if (network.Terminals.Count > 0)
        {
            var graph = AcousticGraphExpression(source, patch, network, name, frequency, parameters, warnings, paths, sources, radiation, terminals, connections, waveClocks, options);
            if (graph.Length > 0)
            {
                return graph;
            }
        }

        warnings.Add($"{name}: acoustic network `{network.Name}` has no valid graph terminals; graph audio is silent");
        return "0.0";
    }

    private static string MissingGraphTractExpression(StringBuilder source, List<string> warnings, string name)
    {
        warnings.Add($"{name}: vocal tract has no acoustic graph network; graph audio is silent");
        source.AppendLine($"{name}_acoustic_graph_radiated = 0.0;");
        return $"{name}_acoustic_graph_radiated";
    }

    private static string AcousticGraphExpression(
        StringBuilder source,
        SynthPatch patch,
        AcousticPortNetwork network,
        string name,
        string frequency,
        ParameterMap parameters,
        List<string> warnings,
        IReadOnlyDictionary<string, AcousticPath> paths,
        IReadOnlyDictionary<string, AcousticSourcePort> sources,
        IReadOnlyDictionary<string, AcousticRadiationPort> radiation,
        IReadOnlyDictionary<string, AcousticTerminal> terminals,
        IReadOnlyDictionary<string, AcousticConnection> connections,
        IReadOnlyDictionary<string, WaveClockPolicy> waveClocks,
        FaustExportOptions options)
    {
        var waveClock = ResolveWaveClock(network, waveClocks, warnings, name);
        var networkTerminals = new List<AcousticTerminal>();
        foreach (var terminalName in network.Terminals)
        {
            if (terminals.TryGetValue(terminalName, out var terminal))
            {
                networkTerminals.Add(terminal);
            }
            else
            {
                warnings.Add($"{name}: acoustic network `{network.Name}` has unknown terminal `{terminalName}`");
            }
        }
        if (networkTerminals.Count < 2)
        {
            warnings.Add($"{name}: acoustic graph `{network.Name}` needs at least two valid terminals; graph audio is silent");
            return "";
        }

        var terminalIndexes = networkTerminals
            .Select((terminal, index) => (terminal.Name, index))
            .ToDictionary(item => item.Name, item => item.index, StringComparer.OrdinalIgnoreCase);
        var terminalNodes = new Dictionary<string, AcousticGraphNode>(StringComparer.OrdinalIgnoreCase);
        var graphNodes = new List<AcousticGraphNode>();
        var segments = new List<AcousticGraphSegment>();
        foreach (var group in networkTerminals.GroupBy(terminal => terminal.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (!paths.TryGetValue(group.Key, out var path))
            {
                warnings.Add($"{name}: acoustic graph `{network.Name}` terminal path `{group.Key}` is unknown");
                continue;
            }

            var groupTerminals = group.ToList();
            var topologyTerminals = groupTerminals
                .Where(terminal => terminal.Kind != AcousticTerminalKind.Source || terminal.Position <= 0.0001f || terminal.Position >= 0.9999f)
                .ToList();

            var ordered = topologyTerminals
                .GroupBy(terminal => MathF.Round(Math.Clamp(terminal.Position, 0, 1), 4))
                .OrderBy(positionGroup => positionGroup.Key)
                .ThenBy(positionGroup => positionGroup.Select(terminal => terminal.Name).Order(StringComparer.OrdinalIgnoreCase).First())
                .Select(positionGroup =>
                {
                    var nodeTerminals = positionGroup
                        .OrderBy(terminal => terminal.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var node = new AcousticGraphNode(
                        string.Join("_", nodeTerminals.Select(terminal => SafeIdentifier(terminal.Name))),
                        group.Key,
                        positionGroup.Key,
                        nodeTerminals);
                    graphNodes.Add(node);
                    foreach (var terminal in nodeTerminals)
                    {
                        terminalNodes[terminal.Name] = node;
                    }
                    return node;
                })
                .ToList();

            foreach (var terminal in groupTerminals.Except(topologyTerminals))
            {
                var nearest = ordered
                    .OrderBy(node => MathF.Abs(node.Position - Math.Clamp(terminal.Position, 0, 1)))
                    .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (nearest is not null)
                {
                    ((List<AcousticTerminal>)nearest.Terminals).Add(terminal);
                    terminalNodes[terminal.Name] = nearest;
                }
            }

            for (var i = 0; i < ordered.Count - 1; i++)
            {
                if (Math.Abs(ordered[i + 1].Position - ordered[i].Position) < 0.0001f)
                {
                    continue;
                }

                segments.Add(new AcousticGraphSegment(
                    segments.Count,
                    path,
                    ordered[i],
                    ordered[i + 1],
                    ordered[i].Position,
                    ordered[i + 1].Position));
            }
        }

        if (segments.Count == 0)
        {
            warnings.Add($"{name}: acoustic graph `{network.Name}` produced no path segments; graph audio is silent");
            return "";
        }

        var incident = graphNodes.ToDictionary(node => node.Name, _ => new List<AcousticGraphPort>(), StringComparer.OrdinalIgnoreCase);
        foreach (var segment in segments)
        {
            incident[segment.Start.Name].Add(new AcousticGraphPort(segment, true));
            incident[segment.End.Name].Add(new AcousticGraphPort(segment, false));
        }

        var terminalConnection = new Dictionary<string, AcousticConnection>(StringComparer.OrdinalIgnoreCase);
        var nodeConnection = new Dictionary<string, AcousticConnection>(StringComparer.OrdinalIgnoreCase);
        foreach (var connectionName in network.Connections)
        {
            if (!connections.TryGetValue(connectionName, out var connection))
            {
                warnings.Add($"{name}: acoustic network `{network.Name}` has unknown connection `{connectionName}`");
                continue;
            }
            foreach (var terminalName in connection.Terminals)
            {
                if (terminalIndexes.ContainsKey(terminalName))
                {
                    terminalConnection[terminalName] = connection;
                    if (terminalNodes.TryGetValue(terminalName, out var node))
                    {
                        nodeConnection[node.Name] = connection;
                    }
                }
            }
        }

        source.AppendLine($"{name}_graph_drive = 1.0;");
        foreach (var terminal in networkTerminals)
        {
            var terminalIndex = AcousticTerminalIndex(patch, terminal);
            var terminalPath = $"/acoustic/terminals/{terminalIndex}";
            source.AppendLine($"{name}_graph_terminal_area_{SafeIdentifier(terminal.Name)} = max(0.000001, {TerminalAreaExpression(patch, paths, parameters, terminal)} * max(0.0, {parameters.Expression(OwnerField(terminalPath, "area_scale"), terminal.AreaScale)}));");
            var reflection = parameters.Expression(OwnerField(terminalPath, "reflection"), terminal.Reflection);
            if (terminal.Kind == AcousticTerminalKind.Radiation &&
                radiation.TryGetValue(terminal.Port.Length == 0 ? terminal.Name : terminal.Port, out var terminalRadiation))
            {
                reflection = RadiationBoundaryReflectionExpression(patch, parameters, terminalRadiation, reflection);
            }

            source.AppendLine($"{name}_graph_terminal_reflection_{SafeIdentifier(terminal.Name)} = {reflection};");
        }
        foreach (var node in graphNodes)
        {
            var areaTerminals = node.Terminals.Where(terminal => terminal.Kind != AcousticTerminalKind.Source).ToList();
            if (areaTerminals.Count == 0)
            {
                areaTerminals = node.Terminals.ToList();
            }
            source.AppendLine($"{name}_graph_node_area_{node.Name} = max(0.000001, {string.Join(" + ", areaTerminals.Select(terminal => $"{name}_graph_terminal_area_{SafeIdentifier(terminal.Name)}"))});");
            source.AppendLine($"{name}_graph_node_reflection_{node.Name} = ({string.Join(" + ", areaTerminals.Select(terminal => $"{name}_graph_terminal_reflection_{SafeIdentifier(terminal.Name)}"))}) / {F(Math.Max(1, areaTerminals.Count))};");
        }
        foreach (var segment in segments)
        {
            source.AppendLine($"{name}_graph_segment_area_{segment.Index} = max(0.000001, {SegmentAreaExpression(patch, parameters, segment)});");
        }

        foreach (var connectionName in network.Connections)
        {
            if (!connections.TryGetValue(connectionName, out var connection))
            {
                continue;
            }
            var connectionIndex = AcousticConnectionIndex(patch, connection);
            var connectionPath = $"/acoustic/connections/{connectionIndex}";
            source.AppendLine($"{name}_graph_connection_coupling_{SafeIdentifier(connection.Name)} = clip01({parameters.Expression(OwnerField(connectionPath, "coupling"), connection.Coupling)});");
            source.AppendLine($"{name}_graph_connection_loss_{SafeIdentifier(connection.Name)} = clip01({parameters.Expression(OwnerField(connectionPath, "loss"), connection.Loss)});");
        }
        source.AppendLine($"{name}_graph_radiation_gain = 1.0;");

        foreach (var sourceName in network.SourcePorts)
        {
            if (sources.TryGetValue(sourceName, out var port))
            {
                var sourceExpression = IsTissueValveSource(port)
                    ? "0.0"
                    : AcousticSourceExpression(patch, port, frequency, parameters);
                source.AppendLine($"{name}_graph_source_{SafeIdentifier(sourceName)} = {sourceExpression};");
            }
        }

        var stateCount = segments.Count * 2;
        var stateInputs = segments.Select(segment => $"r{segment.Index}")
            .Concat(segments.Select(segment => $"l{segment.Index}"))
            .ToList();
        var nextStates = new List<string>(stateCount);
        var debugProbeKeepAlive = new List<string>();
        source.AppendLine($"{name}_graph(x) = {name}_graph_loop ~ si.bus({stateCount}) : (si.block({stateCount}), _) with {{");
        source.AppendLine($"  {name}_graph_loop({string.Join(", ", stateInputs)}) = {string.Join(", ", NextStatesPlaceholder(stateCount))}, {name}_graph_out with {{");

        foreach (var segment in segments)
        {
            source.AppendLine($"    {name}_graph_in_{segment.Index}_start = l{segment.Index};");
            source.AppendLine($"    {name}_graph_in_{segment.Index}_end = r{segment.Index};");
        }
        foreach (var node in graphNodes)
        {
            var ports = incident[node.Name];
            var incidentPressure = ports.Count == 0
                ? "0.0"
                : $"({string.Join(" + ", ports.Select(port => GraphIncoming(name, port)))}) / {F(ports.Count)}";
            var incidentName = $"{name}_graph_node_incident_pressure_{node.Name}";
            var nodeSourceName = NodeSourceIdentifier(name, node);
            var nodeSourceExpression = EmitNodeSourceExpression(source, patch, name, node, incident[node.Name], sources, frequency, parameters);
            source.AppendLine($"    {incidentName} = {ProbeSignal(options, $"/debug/{name}/node/{node.Name}/incident_pressure", incidentPressure, -2, 2)};");
            source.AppendLine($"    {nodeSourceName} = {ProbeSignal(options, $"/debug/{name}/node/{node.Name}/source", nodeSourceExpression, -2, 2)};");
            debugProbeKeepAlive.Add(incidentName);
            debugProbeKeepAlive.Add(nodeSourceName);
        }

        var connectionGroups = network.Connections
            .Select(connectionName => connections.TryGetValue(connectionName, out var connection) ? connection : null)
            .Where(connection => connection is not null)
            .Cast<AcousticConnection>()
            .ToList();
        foreach (var connection in connectionGroups)
        {
            var ports = connection.Terminals
                .Where(terminalNodes.ContainsKey)
                .Select(terminalName => networkTerminals.First(terminal => terminal.Name.Equals(terminalName, StringComparison.OrdinalIgnoreCase)))
                .SelectMany(terminal =>
                {
                    var node = terminalNodes[terminal.Name];
                    return incident[node.Name].Select(port => (node, terminal, port, nodePortCount: incident[node.Name].Count));
                })
                .ToList();
            if (ports.Count < 2)
            {
                continue;
            }

            var safeConnection = SafeIdentifier(connection.Name);
            EmitConnectionScatter(source, name, connection, safeConnection, ports, options, debugProbeKeepAlive);
        }

        var radiated = new List<string>();
        foreach (var node in graphNodes)
        {
            var ports = incident[node.Name];
            if (ports.Count == 0)
            {
                continue;
            }
            if (nodeConnection.ContainsKey(node.Name))
            {
                continue;
            }

            var sourceInjection = NodeSourceIdentifier(name, node);
            var reflection = $"{name}_graph_node_reflection_{node.Name}";
            if (TryEmitTwoPortPathScatter(source, name, node, ports, sourceInjection, options, debugProbeKeepAlive))
            {
                // Emitted as a local area-discontinuity junction.
            }
            else if (ports.Count > 1)
            {
                var nodePressure = $"{name}_graph_node_pressure_{node.Name}";
                source.AppendLine($"    {nodePressure} = 2.0 * ({string.Join(" + ", ports.Select(port => GraphIncoming(name, port)))}) / {F(ports.Count)};");
                foreach (var port in ports)
                {
                    var outgoing = GraphOutgoing(name, port);
                    var incoming = GraphIncoming(name, port);
                    source.AppendLine($"    {outgoing} = ({nodePressure} - {incoming}) * (1.0 - min(0.98, abs({reflection}))) + {incoming} * {reflection} + ({sourceInjection}) / {F(ports.Count)};");
                }
            }
            else foreach (var port in ports)
            {
                var outgoing = GraphOutgoing(name, port);
                var incoming = GraphIncoming(name, port);
                source.AppendLine($"    {outgoing} = {incoming} * {reflection} + ({sourceInjection}) / {F(Math.Max(1, ports.Count))};");
            }

            foreach (var terminal in node.Terminals.Where(terminal => terminal.Kind == AcousticTerminalKind.Radiation && radiation.ContainsKey(terminal.Port.Length == 0 ? terminal.Name : terminal.Port)))
            {
                var radiationPort = radiation[terminal.Port.Length == 0 ? terminal.Name : terminal.Port];
                var radiationIndex = AcousticRadiationPortIndex(patch, radiationPort);
                var radiationPath = $"/acoustic/radiation/{radiationIndex}";
                var opening = RadiationApertureExpression(patch, parameters, radiationPort);
                var loss = $"clip01({parameters.Expression(OwnerField(radiationPath, "loss"), radiationPort.Loss)})";
                var safeTerminal = SafeIdentifier(terminal.Name);
                var terminalArea = $"{name}_graph_terminal_area_{safeTerminal}";
                var referenceArea = $"{name}_graph_radiation_reference_area_{safeTerminal}";
                var differentiation = $"{name}_graph_radiation_differentiation_{safeTerminal}";
                var highpass = $"{name}_graph_radiation_highpass_{safeTerminal}";
                var admittance = $"{name}_graph_radiation_admittance_{safeTerminal}";
                var flow = $"({string.Join(" + ", ports.Select(port => $"({GraphIncoming(name, port)} - {GraphOutgoing(name, port)})"))})";
                var flowName = $"{name}_graph_radiation_flow_{safeTerminal}";
                source.AppendLine($"    {name}_graph_radiation_model_{safeTerminal} = {F((int)EffectiveRadiationModel(radiationPort))};");
                source.AppendLine($"    {referenceArea} = {RadiationReferenceAreaExpression(radiationPort, opening)};");
                source.AppendLine($"    {differentiation} = {RadiationDifferentiationExpression(radiationPort, opening)};");
                source.AppendLine($"    {highpass} = {RadiationHighpassExpression(radiationPort, opening, true)};");
                source.AppendLine($"    {admittance} = {ProbeSignal(options, $"/debug/{name}/radiation/{terminal.Name}/admittance", $"sqrt(clip01(({terminalArea}) / max(0.000001, ({terminalArea}) + ({referenceArea}))))", 0, 1)};");
                source.AppendLine($"    {flowName} = {ProbeSignal(options, $"/debug/{name}/radiation/{terminal.Name}/flow", flow, -2, 2)};");
                radiated.Add($"(({flowName} * (1.0 - ({differentiation})) + ({flowName} : fi.highpass(1, {highpass})) * ({differentiation})) * {loss} * {admittance})");
            }
        }

        foreach (var segment in segments)
        {
            var delayExpression = SegmentDelayExpression(segment);
            var maxDelay = SegmentMaxDelay(segment, waveClock);
            source.AppendLine($"    {name}_graph_delay_{segment.Index} = {delayExpression};");
            source.AppendLine($"    {name}_graph_segment_loss_{segment.Index} = {SegmentLossExpression(name, segment)};");
            source.AppendLine($"    {name}_graph_next_r{segment.Index} = {GraphOutgoing(name, new AcousticGraphPort(segment, true))} : {WaveClockDelayExpression(waveClock, maxDelay, $"{name}_graph_delay_{segment.Index}")} * {name}_graph_segment_loss_{segment.Index};");
            source.AppendLine($"    {name}_graph_next_l{segment.Index} = {GraphOutgoing(name, new AcousticGraphPort(segment, false))} : {WaveClockDelayExpression(waveClock, maxDelay, $"{name}_graph_delay_{segment.Index}")} * {name}_graph_segment_loss_{segment.Index};");
            nextStates.Add($"{name}_graph_next_r{segment.Index}");
        }
        nextStates.AddRange(segments.Select(segment => $"{name}_graph_next_l{segment.Index}"));
        source.Replace(string.Join(", ", NextStatesPlaceholder(stateCount)), string.Join(", ", nextStates));
        var graphOutput = $"({(radiated.Count == 0 ? "0.0" : string.Join(" + ", radiated))}) * {name}_graph_radiation_gain";
        if (options.DebugProbeUi && debugProbeKeepAlive.Count > 0)
        {
            graphOutput = $"attach(({graphOutput}), {string.Join(" + ", debugProbeKeepAlive)})";
        }
        source.AppendLine($"    {name}_graph_out = {graphOutput};");
        source.AppendLine("  };");
        source.AppendLine("};");
        source.AppendLine($"{name}_acoustic_graph_radiated = {name}_graph({name}_graph_drive);");
        return $"{name}_acoustic_graph_radiated";
    }

    private static string RadiationApertureExpression(
        SynthPatch patch,
        ParameterMap parameters,
        AcousticRadiationPort radiationPort)
    {
        var radiationIndex = AcousticRadiationPortIndex(patch, radiationPort);
        var radiationPath = $"/acoustic/radiation/{radiationIndex}";
        var rawOpening = $"max(0.0, {parameters.Expression(OwnerField(radiationPath, "opening"), radiationPort.Opening)})";
        return radiationPort.Kind is AcousticRadiationKind.Lip or AcousticRadiationKind.Beak
            ? $"pow(clip01(({rawOpening}) / 1.5), 2.0)"
            : rawOpening;
    }

    private static string RadiationBoundaryReflectionExpression(
        SynthPatch patch,
        ParameterMap parameters,
        AcousticRadiationPort radiationPort,
        string openReflection)
    {
        var aperture = RadiationApertureExpression(patch, parameters, radiationPort);
        var closedReflection = radiationPort.Kind is AcousticRadiationKind.Lip or AcousticRadiationKind.Beak
            ? "0.96"
            : "0.72";
        return $"(({closedReflection}) * (1.0 - clip01({aperture})) + ({openReflection}) * clip01({aperture}))";
    }

    private static AcousticRadiationModel EffectiveRadiationModel(AcousticRadiationPort port) =>
        port.Model != AcousticRadiationModel.SimpleReflection
            ? port.Model
            : port.Kind switch
            {
                AcousticRadiationKind.Beak => AcousticRadiationModel.Beak,
                AcousticRadiationKind.Nostril => AcousticRadiationModel.Nostril,
                AcousticRadiationKind.Membrane or AcousticRadiationKind.Vent => AcousticRadiationModel.Wall,
                _ => AcousticRadiationModel.SimpleReflection
            };

    private static string RadiationReferenceAreaExpression(AcousticRadiationPort port, string aperture) => EffectiveRadiationModel(port) switch
    {
        AcousticRadiationModel.LipPiston => $"max(0.18, 1.90 * 1.90 * (0.30 + 0.70 * clip01({aperture})))",
        AcousticRadiationModel.Beak => $"max(0.10, 1.10 * 1.10 * (0.25 + 0.75 * clip01({aperture})))",
        AcousticRadiationModel.Nostril => $"max(0.08, 0.70 * 0.70 * (0.50 + 0.50 * clip01({aperture})))",
        AcousticRadiationModel.Wall => "0.35",
        _ => "1.0"
    };

    private static string RadiationDifferentiationExpression(AcousticRadiationPort port, string aperture) => EffectiveRadiationModel(port) switch
    {
        AcousticRadiationModel.LipPiston => $"(0.62 + 0.24 * clip01({aperture}))",
        AcousticRadiationModel.Beak => $"(0.50 + 0.22 * clip01({aperture}))",
        AcousticRadiationModel.Nostril => $"(0.45 + 0.20 * clip01({aperture}))",
        AcousticRadiationModel.Wall => "0.18",
        _ => "0.50"
    };

    private static string RadiationHighpassExpression(AcousticRadiationPort port, string aperture, bool graph) => EffectiveRadiationModel(port) switch
    {
        AcousticRadiationModel.LipPiston => graph
            ? $"(120.0 + 180.0 * clip01({aperture}))"
            : $"(60.0 + 50.0 * clip01({aperture}))",
        AcousticRadiationModel.Beak => graph
            ? $"(180.0 + 220.0 * clip01({aperture}))"
            : $"(90.0 + 90.0 * clip01({aperture}))",
        AcousticRadiationModel.Nostril => graph
            ? $"(55.0 + 75.0 * clip01({aperture}))"
            : $"(30.0 + 30.0 * clip01({aperture}))",
        AcousticRadiationModel.Wall => "15.0",
        _ => "20.0"
    };

    private static string AcousticSourceExpression(
        SynthPatch patch,
        AcousticSourcePort port,
        string frequency,
        ParameterMap parameters,
        string? localPressure = null)
    {
        var index = AcousticSourcePortIndex(patch, port);
        var path = $"/acoustic/sources/{index}";
        var pressure = $"clip01({parameters.Expression(OwnerField(path, "pressure"), port.Pressure)})";
        var tension = $"clip01({parameters.Expression(OwnerField(path, "tension"), port.Tension)})";
        var opening = $"max(0.0, {parameters.Expression(OwnerField(path, "opening"), port.Opening)})";
        var noise = $"clip01({parameters.Expression(OwnerField(path, "noise"), port.Noise)})";
        var transient = $"clip01({parameters.Expression(OwnerField(path, "transient"), port.Transient)})";
        var balance = $"({parameters.Expression(OwnerField(path, "balance"), port.Balance)}) * ({SourcePositionWeightExpression(port, path, parameters)})";
        var impedance = $"max(0.0, {parameters.Expression(OwnerField(path, "impedance"), port.Impedance)})";
        var mass = $"max(0.02, {parameters.Expression(OwnerField(path, "mass"), port.Mass)})";
        var damping = $"max(0.0, {parameters.Expression(OwnerField(path, "damping"), port.Damping)})";
        var stiffness = $"max(0.0, {parameters.Expression(OwnerField(path, "stiffness"), port.Stiffness)})";
        var saturation = $"max(0.0, {parameters.Expression(OwnerField(path, "saturation"), port.Saturation)})";
        var drive = $"max(0.0, {parameters.Expression(OwnerField(path, "drive"), port.Drive)})";
        var loadCoupling = $"max(0.0, {parameters.Expression(OwnerField(path, "load_coupling"), port.LoadCoupling)})";
        var restOpening = $"max(0.0, {parameters.Expression(OwnerField(path, "rest_opening"), port.RestOpening)})";
        var effectivePressure = localPressure is null
            ? pressure
            : $"clip01(({pressure}) - 0.14 * ({impedance}) * ({localPressure}))";
        var load = localPressure is null
            ? "1.0"
            : $"(1.0 / (1.0 + 0.25 * ({impedance}) * abs({localPressure})))";
        var detune = port.Kind == AcousticSourceKind.Labial ? $" * (1.0 + ({balance} - 0.5) * 0.018)" : "";
        var phase = $"os.phasor(1.0, {frequency}{detune})";
        var closure = $"clip01((0.12 - ({opening})) / 0.12)";
        var release = $"(max(0.0, (({closure}) : mem) - ({closure})) : + ~ *(0.995))";
        var releasePressure = localPressure is null
            ? "1.0"
            : $"(0.20 + 2.80 * clip01((abs({localPressure}) * ({closure})) : + ~ *(0.992)))";
        var pressureDrive = localPressure is null
            ? "1.0"
            : $"(0.50 + 1.50 * clip01(abs({localPressure})))";
        var turbulence = $"(no.noise : fi.highpass(2, 900.0 + 2600.0 * clip01(1.0 - {opening})))";
        var apertureNoiseGate = $"min(clip01((0.85 - {opening}) / 0.85), clip01(25.0 * ({opening} - 0.04)))";
        var releaseBurstGate = $"clip01({opening} / 0.8) * {release}";
        if (IsTissueValveSource(port))
        {
            return TissueValveInlineExpression(frequency, pressure, tension, opening, noise, balance, mass, damping, stiffness, saturation, drive, loadCoupling, restOpening, localPressure);
        }

        return port.Kind switch
        {
            AcousticSourceKind.Glottal =>
                $"{GlottalSourceExpression(phase, tension, opening, effectivePressure, noise, balance)} * {load}",
            AcousticSourceKind.Labial =>
                $"(ma.tanh((sin(2.0 * ma.PI * {phase}) - {tension} * 0.35 * sin(4.0 * ma.PI * {phase})) * (2.0 + 8.0 * {effectivePressure})) * {effectivePressure} * {opening} + no.noise * {noise} * {effectivePressure} * (1.0 - {tension})) * {balance} * {load}",
            AcousticSourceKind.Reed =>
                $"(ma.tanh(sin(2.0 * ma.PI * {phase}) * (1.0 + {effectivePressure} * 8.0)) * {opening} + no.noise * {noise} * 0.2) * {balance} * {load}",
            AcousticSourceKind.TurbulenceJet =>
                $"(({turbulence}) * {noise} * {effectivePressure} * ({pressureDrive}) * ({apertureNoiseGate}) * 0.62 + ({turbulence}) * {noise} * {transient} * ({pressureDrive}) * ({releaseBurstGate}) * 1.2 + {transient} * {effectivePressure} * {release} * {releasePressure} * 0.58 * (0.8 + 0.8 * clip01({opening} / 0.8))) * {balance} * {load}",
            AcousticSourceKind.Click =>
                $"no.noise * {effectivePressure} * max({opening}, {transient}) * exp(0.0 - age * 120.0) * {balance} * {load}",
            AcousticSourceKind.Synthetic =>
                $"sin(2.0 * ma.PI * {phase}) * {effectivePressure} * {opening} * {balance} * {load}",
            _ => "0.0"
        };
    }

    private static bool IsTissueValveSource(AcousticSourcePort port) =>
        port.Model == AcousticSourceModel.TissueValve ||
        port.Model == AcousticSourceModel.Default && port.Kind is AcousticSourceKind.Labial or AcousticSourceKind.Glottal;

    private static string TissueValveInlineExpression(
        string frequency,
        string pressure,
        string tension,
        string opening,
        string noise,
        string balance,
        string mass,
        string damping,
        string stiffness,
        string saturation,
        string drive,
        string loadCoupling,
        string restOpening,
        string? localPressure)
    {
        var loadPressure = localPressure ?? "0.0";
        var stiffnessHint = $"max(0.00002, min(0.16, pow(2.0 * ma.PI * max(20.0, {frequency}) / ma.SR, 2.0)))";
        var effectiveStiffness = $"max(({stiffness}), ({stiffnessHint}) * (0.35 + 1.65 * ({tension})))";
        var pressureDrive = $"max(0.0, ({pressure}) * ({drive}) - ({loadCoupling}) * ({loadPressure}))";
        var velocityDecay = $"min(0.9995, max(0.20, 1.0 - ((0.008 + 0.08 * ({damping})) / ({mass}))))";
        var displacementDecay = $"min(0.9998, max(0.20, 1.0 - 0.0008 / ({mass})))";
        var velocity = $"((({pressureDrive}) * (0.0004 + 0.0024 * (1.0 - clip01({opening}))) - ({loadCoupling}) * ({loadPressure}) * 0.001) : + ~ *(({velocityDecay}) / (1.0 + 10.0 * ({effectiveStiffness}) + 0.4 * ({saturation}))))";
        var displacement = $"(({velocity}) : + ~ *(({displacementDecay}) / (1.0 + 1.5 * ({effectiveStiffness}))))";
        var aperture = $"max(0.0, ({restOpening}) + ({opening}) + ({displacement}) - ({saturation}) * pow(({displacement}), 3.0))";
        var turbulence = $"(no.noise : fi.highpass(2, 1200.0 + 2800.0 * clip01(1.0 - ({aperture}))))";
        return $"(ma.tanh(({pressureDrive}) * ({aperture}) * (2.0 + 10.0 * ({drive}))) + ({turbulence}) * ({noise}) * ({pressureDrive}) * clip01({aperture}) * (1.0 - 0.55 * ({tension}))) * ({balance})";
    }

    private static string GlottalSourceExpression(
        string phase,
        string tension,
        string opening,
        string pressure,
        string noise,
        string balance)
    {
        var legacy = LegacyGlottalExpression(phase, tension, opening);
        var brightness = $"(0.10 * (1.0 - 0.35 * ({tension})) * sin(6.0 * ma.PI * {phase}) + 0.05 * (1.0 - 0.50 * ({tension})) * sin(8.0 * ma.PI * {phase}))";
        var shaped = $"(({legacy}) + 1.4 * ({legacy}) * ({legacy}) * ({legacy}) + {brightness})";
        var normalized = $"ma.tanh(({shaped}) * 0.70)";
        return $"(({normalized}) * {pressure} * 0.72 + no.noise * {noise} * {pressure} * (1.0 - sqrt(max(0.0, {tension})))) * {balance}";
    }

    private static string LegacyGlottalExpression(string phase, string tension, string opening) =>
        $"(select2({phase} < (0.42 + clip01({opening}) * 0.36), -0.28 * sin(ma.PI * ({phase} - (0.42 + clip01({opening}) * 0.36)) / max(0.001, 1.0 - (0.42 + clip01({opening}) * 0.36))), sin(ma.PI * {phase} / max(0.001, 0.42 + clip01({opening}) * 0.36))) - (0.12 + {tension} * 0.62) * sin(4.0 * ma.PI * {phase}) + 0.18 * (sin(2.0 * ma.PI * {phase}) - {tension} * 0.35 * sin(4.0 * ma.PI * {phase}))) * (0.45 + 0.75 * pow(max(0.0, {tension}), 0.35))";

    private static string SourcePositionWeightExpression(
        AcousticSourcePort port,
        string path,
        ParameterMap parameters)
    {
        if (port.PositionControl is not { } control)
        {
            return "1.0";
        }

        var index = parameters.Expression(OwnerField(path, "position/index"), control.Index);
        var width = parameters.Expression(OwnerField(path, "position/width"), control.Width);
        var indexScale = parameters.Expression(OwnerField(path, "position/index_scale"), control.IndexScale);
        var target = $"clip01(({index}) * max(0.0, {indexScale}))";
        var radius = $"max(0.000001, ({width}) * max(0.0, {indexScale}))";
        return $"clip01(1.0 - abs({F(port.Position)} - ({target})) / ({radius}))";
    }

    private static void EmitOperatorGraph(StringBuilder source, Playback playback, OperatorGraph graph, string name, ParameterMap parameters, List<string> warnings)
    {
        if (graph.Operators.Count == 0)
        {
            warnings.Add($"{name}: empty operator graph was ignored");
            source.AppendLine($"{name} = 0.0;");
            return;
        }

        var ordered = TopologicalOperators(graph, warnings, name);
        var operatorIds = graph.Operators.Select(op => op.Id).ToHashSet();
        var carrierIds = graph.Carriers.Where(operatorIds.Contains).ToList();
        if (carrierIds.Count == 0)
        {
            warnings.Add($"{name}: operator graph has no valid carriers");
            source.AppendLine($"{name} = 0.0;");
            return;
        }

        var graphIndex = GraphIndex(name);
        var graphPath = $"/opgraphs/{graphIndex}";
        var graphNoteFreq = UsesHostPlayback(playback)
            ? "freq"
            : graph.Note.Source == NoteSource.Host
            ? $"{name}_note_freq"
            : parameters.Expression($"{graphPath}/note/frequency", graph.Note.FrequencyHz);
        var graphNoteGate = UsesHostPlayback(playback)
            ? "gate"
            : graph.Note.Source == NoteSource.Host
            ? $"{name}_note_gate"
            : parameters.Expression($"{graphPath}/note/gate", graph.Note.GateSeconds);
        if (!UsesHostPlayback(playback) && graph.Note.Source == NoteSource.Host)
        {
            source.AppendLine($"{name}_note_freq = hslider(\"/{name}/note/frequency\", {F(graph.Note.FrequencyHz)}, 20, 20000, 0.01) : si.smoo;");
            source.AppendLine($"{name}_note_gate = button(\"/{name}/note/gate\");");
        }
        var graphVibratoDepth = parameters.Expression($"{graphPath}/pitch/vibrato", graph.VibratoDepth);
        var graphVibratoHz = parameters.Expression($"{graphPath}/pitch/vibrato_hz", graph.VibratoHz);
        var graphVibratoDelay = parameters.Expression($"{graphPath}/pitch/vibrato_delay", graph.VibratoDelaySeconds);
        var hasGraphVibrato = graph.VibratoDepth > 0 && graph.VibratoHz > 0 ||
                              parameters.IsBound($"{graphPath}/pitch/vibrato") ||
                              parameters.IsBound($"{graphPath}/pitch/vibrato_hz");
        var graphPitchMod = hasGraphVibrato
            ? $" * max(0.0, 1.0 + clip01(age / max(0.0001, {graphVibratoDelay})) * {graphVibratoDepth} * lfo_sin({graphVibratoHz}, 0.0))"
            : "";
        source.AppendLine($"{name}_freq = {graphNoteFreq}{graphPitchMod};");
        foreach (var op in ordered)
        {
            var opName = $"{name}_op_{op.Id}";
            var operatorPath = $"{graphPath}/operators/{op.Id}";
            var incoming = graph.Edges
                .Where(edge => edge.TargetId == op.Id && operatorIds.Contains(edge.SourceId))
                .Select(edge => $"{name}_op_{edge.SourceId} * {parameters.Expression($"{graphPath}/routes/{edge.SourceId}>{edge.TargetId}/index", edge.Index)}")
                .ToList();
            var externalPhaseMod = incoming.Count == 0 ? "0.0" : string.Join(" + ", incoming);
            var envelope = op.RateLevelEnvelope is not null
                ? RateLevelEnvelopeExpression(
                    op.RateLevelEnvelope,
                    UsesHostPlayback(playback) || op.Note.Source == NoteSource.Host ? graphNoteGate : F(op.Note.GateSeconds))
                : EnvelopeExpression(
                    op.Envelope,
                    UsesHostPlayback(playback) || op.Note.Source == NoteSource.Host ? graphNoteGate : F(op.Note.GateSeconds),
                    UsesHostPlayback(playback) || op.Note.Source == NoteSource.Host,
                    field => field switch
                    {
                        "env/attack" => parameters.Expression($"{operatorPath}/env/attack", op.Envelope.AttackSeconds),
                        "env/decay" => parameters.Expression($"{operatorPath}/env/decay", op.Envelope.DecaySeconds),
                        "env/sustain_level" => parameters.Expression($"{operatorPath}/env/sustain_level", op.Envelope.SustainLevel),
                        "env/release" => parameters.Expression($"{operatorPath}/env/release", op.Envelope.ReleaseSeconds),
                        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
                    });
            if (op.Feedback != 0)
            {
                var feedback = parameters.Expression($"{operatorPath}/feedback", op.Feedback);
                source.AppendLine($"{opName} = ((_ * {feedback} + ({externalPhaseMod})) : \\(pm).(sin(2.0 * ma.PI * (os.phasor(1.0, {name}_freq * max(0.0, {parameters.Expression($"{operatorPath}/ratio", op.Ratio)})) + pm / ma.PI)) * {envelope} * {parameters.Expression($"{operatorPath}/level", op.Level)})) ~ _;");
            }
            else
            {
                source.AppendLine($"{opName} = sin(2.0 * ma.PI * (os.phasor(1.0, {name}_freq * max(0.0, {parameters.Expression($"{operatorPath}/ratio", op.Ratio)})) + ({externalPhaseMod}) / ma.PI)) * {envelope} * {parameters.Expression($"{operatorPath}/level", op.Level)};");
            }
        }

        source.AppendLine($"{name} = ({string.Join(" + ", carrierIds.Select(id => $"{name}_op_{id}"))}) * {parameters.Expression($"{graphPath}/gain", graph.Gain)};");
        source.AppendLine();
    }

    private static IReadOnlyList<OperatorNode> TopologicalOperators(OperatorGraph graph, List<string> warnings, string name)
    {
        var byId = graph.Operators.ToDictionary(op => op.Id);
        var emitted = new HashSet<int>();
        var ordered = new List<OperatorNode>();
        while (ordered.Count < graph.Operators.Count)
        {
            var ready = graph.Operators
                .Where(op => !emitted.Contains(op.Id))
                .Where(op => graph.Edges
                    .Where(edge => edge.TargetId == op.Id && byId.ContainsKey(edge.SourceId))
                    .All(edge => emitted.Contains(edge.SourceId)))
                .OrderByDescending(op => op.Id)
                .ToList();
            if (ready.Count == 0)
            {
                warnings.Add($"{name}: operator graph has a cycle; remaining operators use declaration order");
                ordered.AddRange(graph.Operators.Where(op => !emitted.Contains(op.Id)));
                break;
            }

            foreach (var op in ready)
            {
                ordered.Add(op);
                emitted.Add(op.Id);
            }
        }

        return ordered;
    }

    private static string FormantExpression(string name, Voice voice, string ownerPath, ParameterMap parameters)
    {
        if (voice.FormantFrames.Count > 0)
        {
            if (voice.FormantFrames.Count == 1)
            {
                return StaticFormantExpression(name, voice.FormantFrames[0].Formants);
            }

            var frameCount = voice.FormantFrames.Count;
            var rate = parameters.Expression(OwnerField(ownerPath, "color/vowel_rate"), voice.FormantFrameRateHz);
            var position = $"wrap01(age * {rate}) * {F(frameCount)}";
            var frames = voice.FormantFrames.Select((frame, index) =>
            {
                var distance = $"min(abs(({position}) - {F(index)}), {F(frameCount)} - abs(({position}) - {F(index)}))";
                var weight = $"max(0.0, 1.0 - {distance})";
                return $"({StaticFormantExpression(name, frame.Formants)}) * ({weight})";
            });
            return string.Join(" + ", frames);
        }

        return StaticFormantExpression(name, voice.Formants);
    }

    private static string StaticFormantExpression(string name, IReadOnlyList<Formant> formants)
    {
        if (formants.Count == 0) return $"{name}_phased";
        var gainSum = Math.Max(formants.Sum(formant => Math.Abs(formant.Gain)), 0.001f);
        var parts = formants.Select(formant =>
        {
            var q = Math.Clamp(formant.FrequencyHz / Math.Max(formant.BandwidthHz, 10), 0.2f, 40);
            return $"({name}_phased : fi.resonbp({F(formant.FrequencyHz)}, {F(q)}, 1.0)) * {F(formant.Gain)}";
        });
        return $"({string.Join(" + ", parts)}) / {F(gainSum)}";
    }

    private static string ModExpressionForTarget(IEnumerable<ControlLane> controls, ModTarget target)
    {
        var expressions = controls.Where(control => control.Modulator.Target == target)
            .Select(control => ModExpression(control.Modulator))
            .ToList();
        return expressions.Count == 0 ? "0.0" : string.Join(" + ", expressions);
    }

    private static string ModExpressionForTarget(IEnumerable<Modulator> modulators, ModTarget target)
    {
        var expressions = modulators.Where(modulator => modulator.Target == target)
            .Select(ModExpression)
            .ToList();
        return expressions.Count == 0 ? "0.0" : string.Join(" + ", expressions);
    }

    private static string ModExpression(Modulator modulator)
    {
        var wave = modulator.Waveform switch
        {
            ModWaveform.Sine => "lfo_sin",
            ModWaveform.Triangle => "lfo_tri",
            ModWaveform.Square => "lfo_sq",
            ModWaveform.SampleHold => "lfo_hold",
            _ => throw new ArgumentOutOfRangeException()
        };
        return $"{F(modulator.Bias)} + {F(modulator.Depth)} * {wave}({F(modulator.FrequencyHz)}, {F(modulator.Phase)})";
    }

    private static readonly (ModTarget Target, string Name)[] ModTargets =
    [
        (ModTarget.Gain, "gain"),
        (ModTarget.Pitch, "pitch"),
        (ModTarget.Duty, "duty"),
        (ModTarget.LowPass, "lpf"),
        (ModTarget.HighPass, "hpf"),
        (ModTarget.Noise, "noise"),
        (ModTarget.Drive, "drive"),
        (ModTarget.Fold, "fold"),
        (ModTarget.FormantMix, "formant_mix"),
        (ModTarget.FmIndex, "fm_index")
    ];

    private static string F(float value) =>
        float.IsFinite(value) ? value.ToString("0.########", CultureInfo.InvariantCulture) : "0.0";

    private static string F(double value) =>
        double.IsFinite(value) ? value.ToString("0.########", CultureInfo.InvariantCulture) : "0.0";

    private static string FmDecay(float seconds) => seconds > 0 ? $"exp(-age / {F(Math.Max(seconds, 0.0001f))})" : "1.0";

    private static string FmDecay(string seconds, float defaultSeconds, bool isBound) =>
        defaultSeconds > 0 || isBound ? $"exp(-age / max({seconds}, 0.0001))" : "1.0";

    private static string ParameterIdentifier(int index) => $"patch_param_{index}";

    private static string VoicePath(int voiceIndex) => $"/voices/{voiceIndex}";

    private static string SpectralPath(int spectralIndex) => $"/spectral/{spectralIndex}";

    private static string OwnerField(string ownerPath, string field) => $"{ownerPath}/{field}";

    private static int GraphIndex(string name)
    {
        const string prefix = "opgraph_";
        return name.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(name[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : 0;
    }

    private static string TerminalAreaExpression(
        SynthPatch patch,
        IReadOnlyDictionary<string, AcousticPath> paths,
        ParameterMap parameters,
        AcousticTerminal terminal)
    {
        if (!paths.TryGetValue(terminal.Path, out var path))
        {
            return "1.0";
        }

        var position = Math.Clamp(terminal.Position, 0, 1);
        var restDiameter = path.AreaFunction.DiameterAt(position);
        if (path.AreaControl is not { } area)
        {
            return F(Math.Max(0.000001f, restDiameter * restDiameter));
        }

        var pathIndex = AcousticPathIndex(patch, path);
        var areaPath = $"/acoustic/paths/{pathIndex}/area";
        var sections = Math.Max(2, path.AreaFunction.Sections);
        var index = F(position * (sections - 1));
        var tongueIndex = ScaledAreaIndex(parameters.Expression(OwnerField(areaPath, "tongue_index"), area.TongueIndex), area.IndexScale);
        var tongueDiameter = parameters.Expression(OwnerField(areaPath, "tongue_diameter"), area.TongueDiameter);
        var tongueWidth = $"max(0.001, {F(area.TongueWidth)} * {F(sections)})";
        var constrictionIndex = ScaledAreaIndex(parameters.Expression(OwnerField(areaPath, "constriction_index"), area.ConstrictionIndex), area.IndexScale);
        var constrictionDiameter = parameters.Expression(OwnerField(areaPath, "constriction_diameter"), area.ConstrictionDiameter);
        var constrictionWidth = $"max(0.001, {F(area.ConstrictionWidth)} * {F(sections)})";
        var lipOpening = parameters.Expression(OwnerField(areaPath, "lip_opening"), area.LipOpening);
        var lipWeight = MathF.Exp(0 - MathF.Pow((1 - position) / Math.Max(0.001f, area.LipWidth), 2));
        var tongueWeight = $"exp(0.0 - pow(({index} - ({tongueIndex})) / ({tongueWidth}), 2.0))";
        var constrictionWeight = $"exp(0.0 - pow(({index} - ({constrictionIndex})) / ({constrictionWidth}), 2.0))";
        var baseDiameter = $"({F(restDiameter)} + (({tongueDiameter}) - {F(restDiameter)}) * ({tongueWeight}) + (({lipOpening}) - {F(restDiameter)}) * {F(lipWeight)})";
        var diameter = $"max(0.0, min({baseDiameter}, ({constrictionDiameter}) + max(0.0, ({baseDiameter}) - ({constrictionDiameter})) * (1.0 - ({constrictionWeight}))))";
        return $"(({diameter}) * ({diameter}))";

        static string ScaledAreaIndex(string expression, float scale) =>
            scale == 1 ? expression : $"(({expression}) * {F(scale)})";
    }

    private static string SegmentAreaExpression(
        SynthPatch patch,
        ParameterMap parameters,
        AcousticGraphSegment segment)
    {
        var position = (segment.StartPosition + segment.EndPosition) * 0.5f;
        var restDiameter = segment.Path.AreaFunction.DiameterAt(position);
        if (segment.Path.AreaControl is not { } area)
        {
            return F(Math.Max(0.000001f, restDiameter * restDiameter));
        }

        var pathIndex = AcousticPathIndex(patch, segment.Path);
        var areaPath = $"/acoustic/paths/{pathIndex}/area";
        var sections = Math.Max(2, segment.Path.AreaFunction.Sections);
        var index = F(position * (sections - 1));
        var tongueIndex = ScaledAreaIndex(parameters.Expression(OwnerField(areaPath, "tongue_index"), area.TongueIndex), area.IndexScale);
        var tongueDiameter = parameters.Expression(OwnerField(areaPath, "tongue_diameter"), area.TongueDiameter);
        var tongueWidth = $"max(0.001, {F(area.TongueWidth)} * {F(sections)})";
        var constrictionIndex = ScaledAreaIndex(parameters.Expression(OwnerField(areaPath, "constriction_index"), area.ConstrictionIndex), area.IndexScale);
        var constrictionDiameter = parameters.Expression(OwnerField(areaPath, "constriction_diameter"), area.ConstrictionDiameter);
        var constrictionWidth = $"max(0.001, {F(area.ConstrictionWidth)} * {F(sections)})";
        var lipOpening = parameters.Expression(OwnerField(areaPath, "lip_opening"), area.LipOpening);
        var lipWeight = MathF.Exp(0 - MathF.Pow((1 - position) / Math.Max(0.001f, area.LipWidth), 2));
        var tongueWeight = $"exp(0.0 - pow(({index} - ({tongueIndex})) / ({tongueWidth}), 2.0))";
        var constrictionWeight = $"exp(0.0 - pow(({index} - ({constrictionIndex})) / ({constrictionWidth}), 2.0))";
        var baseDiameter = $"({F(restDiameter)} + (({tongueDiameter}) - {F(restDiameter)}) * ({tongueWeight}) + (({lipOpening}) - {F(restDiameter)}) * {F(lipWeight)})";
        var diameter = $"max(0.0, min({baseDiameter}, ({constrictionDiameter}) + max(0.0, ({baseDiameter}) - ({constrictionDiameter})) * (1.0 - ({constrictionWeight}))))";
        return $"(({diameter}) * ({diameter}))";

        static string ScaledAreaIndex(string expression, float scale) =>
            scale == 1 ? expression : $"(({expression}) * {F(scale)})";
    }

    private static WaveClockPolicy ResolveWaveClock(
        AcousticPortNetwork network,
        IReadOnlyDictionary<string, WaveClockPolicy> waveClocks,
        List<string> warnings,
        string voiceName)
    {
        if (network.WaveClock.Length == 0)
        {
            return new WaveClockPolicy("__default", WaveClockDelayStrategy.FractionalLinear, 1, 4096, 0);
        }

        if (waveClocks.TryGetValue(network.WaveClock, out var waveClock))
        {
            return waveClock;
        }

        warnings.Add($"{voiceName}: acoustic network `{network.Name}` references missing wave clock `{network.WaveClock}`; using fractional linear delay");
        return new WaveClockPolicy("__missing", WaveClockDelayStrategy.FractionalLinear, 1, 4096, 0);
    }

    private static string SegmentDelayExpression(AcousticGraphSegment segment)
    {
        var segmentLengthMeters = Math.Max(0.000001f, (segment.EndPosition - segment.StartPosition) * segment.Path.AreaFunction.LengthMeters);
        return $"max(0.000001, {F(segmentLengthMeters / segment.Path.PropagationSpeedMetersPerSecond)} * ma.SR)";
    }

    private static string SegmentLossExpression(string voiceName, AcousticGraphSegment segment)
    {
        var baseLoss = F(Math.Clamp(segment.Path.Loss, 0, 1));
        var area = $"{voiceName}_graph_segment_area_{segment.Index}";
        var apertureLoss = segment.Path.LossModel switch
        {
            AcousticLossModel.None => "1.0",
            AcousticLossModel.Viscous => $"sqrt(clip01(({area}) / max(0.000001, ({area}) + 0.020)))",
            AcousticLossModel.Birkholz2024 => $"sqrt(clip01(({area}) / max(0.000001, ({area}) + 0.012))) * (0.998 - 0.010 * clip01(0.080 / max(0.000001, ({area}) + 0.080)))",
            AcousticLossModel.Wall => $"(0.996 - 0.018 * clip01(0.050 / max(0.000001, ({area}) + 0.050)))",
            _ => $"sqrt(clip01(({area}) / max(0.000001, ({area}) + 0.02)))"
        };
        return $"clip01(({baseLoss}) * ({apertureLoss}))";
    }

    private static int SegmentMaxDelay(AcousticGraphSegment segment, WaveClockPolicy waveClock)
    {
        var maxAt44k = (int)MathF.Ceiling(Math.Max(1, (segment.EndPosition - segment.StartPosition) * segment.Path.AreaFunction.LengthMeters / segment.Path.PropagationSpeedMetersPerSecond * 44100) + 4);
        return Math.Max(maxAt44k, Math.Max(1, waveClock.MaxDelaySamples));
    }

    private static string WaveClockDelayExpression(WaveClockPolicy waveClock, int maxDelay, string delayExpression)
    {
        var delay = $"min({F(maxDelay - 1)}, max({F(WaveClockMinimumDelay(waveClock))}, {delayExpression}))";
        return waveClock.Strategy switch
        {
            WaveClockDelayStrategy.UnitGrid => $"de.delay({maxDelay}, int({delay}))",
            WaveClockDelayStrategy.HalfSampleGrid => $"de.fdelay({maxDelay}, round({delay} * 2.0) * 0.5)",
            WaveClockDelayStrategy.FractionalLagrange => $"de.fdelayltv({Math.Clamp(waveClock.FractionalOrder, 1, 5)}, {maxDelay}, {delay})",
            WaveClockDelayStrategy.FractionalThiran => $"de.fdelay{Math.Clamp(waveClock.FractionalOrder, 1, 4)}a({maxDelay}, {delay})",
            WaveClockDelayStrategy.CrossfadedVariable => $"de.fdelayltv({Math.Clamp(waveClock.FractionalOrder, 1, 5)}, {maxDelay}, {delay})",
            _ => $"de.fdelay({maxDelay}, {delay})"
        };
    }

    private static float WaveClockMinimumDelay(WaveClockPolicy waveClock) =>
        waveClock.Strategy switch
        {
            WaveClockDelayStrategy.UnitGrid => 1.0f,
            WaveClockDelayStrategy.HalfSampleGrid => 0.5f,
            WaveClockDelayStrategy.FractionalThiran => Math.Clamp(waveClock.FractionalOrder, 1, 4) - 0.5f,
            WaveClockDelayStrategy.FractionalLagrange => (Math.Clamp(waveClock.FractionalOrder, 1, 5) - 1) * 0.5f,
            WaveClockDelayStrategy.CrossfadedVariable => (Math.Clamp(waveClock.FractionalOrder, 1, 5) - 1) * 0.5f,
            _ => 0.000001f
        };

    private static string ConnectionPortAreaExpression(string voiceName, AcousticTerminal terminal, int nodePortCount) =>
        $"({voiceName}_graph_terminal_area_{SafeIdentifier(terminal.Name)} / {F(Math.Max(1, nodePortCount))})";

    private static void EmitConnectionScatter(
        StringBuilder source,
        string voiceName,
        AcousticConnection connection,
        string safeConnection,
        IReadOnlyList<(AcousticGraphNode node, AcousticTerminal terminal, AcousticGraphPort port, int nodePortCount)> ports,
        FaustExportOptions options,
        List<string> debugProbeKeepAlive)
    {
        if (connection.Law == AcousticConnectionLaw.Bypass)
        {
            EmitBypassConnection(source, voiceName, connection, safeConnection, ports);
            return;
        }

        var portItems = ports.ToList();
        var areaExpressions = new Dictionary<AcousticGraphPort, string>();
        foreach (var item in portItems)
        {
            areaExpressions[item.port] = ConnectionAreaExpression(voiceName, item);
        }

        var areaSum = string.Join(" + ", portItems.Select(item => areaExpressions[item.port]));
        var coupling = $"{voiceName}_graph_connection_coupling_{safeConnection}";
        var loss = $"{voiceName}_graph_connection_loss_{safeConnection}";
        foreach (var item in portItems)
        {
            var incoming = GraphIncoming(voiceName, item.port);
            var outgoing = GraphOutgoing(voiceName, item.port);
            var sourceInjection = NodeSourceIdentifier(voiceName, item.node);
            var reflection = $"{voiceName}_graph_connection_reflection_{safeConnection}_{SafeIdentifier(item.terminal.Name)}_{item.port.Segment.Index}_{(item.port.AtStart ? "start" : "end")}";
            var otherIncoming = string.Join(" + ", portItems.Where(other => other.port != item.port).Select(other => GraphIncoming(voiceName, other.port)));
            var scattered = $"{voiceName}_graph_connection_scattered_{safeConnection}_{SafeIdentifier(item.terminal.Name)}_{item.port.Segment.Index}_{(item.port.AtStart ? "start" : "end")}";
            source.AppendLine($"    {reflection} = (2.0 * ({areaExpressions[item.port]}) - ({areaSum})) / max(0.000001, {areaSum});");
            source.AppendLine($"    {scattered} = {reflection} * {incoming} + (1.0 + {reflection}) * ({otherIncoming});");
            source.AppendLine($"    {outgoing} = (({scattered}) * ({coupling}) + {incoming} * (1.0 - ({coupling})) + ({sourceInjection}) / {F(Math.Max(1, item.nodePortCount))}) * {loss};");
        }
        var energyIn = string.Join(" + ", portItems.Select(item => $"({areaExpressions[item.port]}) * pow({GraphIncoming(voiceName, item.port)}, 2.0)"));
        var energyOut = string.Join(" + ", portItems.Select(item => $"({areaExpressions[item.port]}) * pow({GraphOutgoing(voiceName, item.port)}, 2.0)"));
        source.AppendLine($"    {voiceName}_graph_connection_energy_in_{safeConnection} = {ProbeSignal(options, $"/debug/{voiceName}/connection/{connection.Name}/energy_in", energyIn, 0, 4)};");
        source.AppendLine($"    {voiceName}_graph_connection_energy_out_{safeConnection} = {ProbeSignal(options, $"/debug/{voiceName}/connection/{connection.Name}/energy_out", energyOut, 0, 4)};");
        debugProbeKeepAlive.Add($"{voiceName}_graph_connection_energy_in_{safeConnection}");
        debugProbeKeepAlive.Add($"{voiceName}_graph_connection_energy_out_{safeConnection}");
    }

    private static void EmitBypassConnection(
        StringBuilder source,
        string voiceName,
        AcousticConnection connection,
        string safeConnection,
        IReadOnlyList<(AcousticGraphNode node, AcousticTerminal terminal, AcousticGraphPort port, int nodePortCount)> ports)
    {
        var coupling = $"{voiceName}_graph_connection_coupling_{safeConnection}";
        var loss = $"{voiceName}_graph_connection_loss_{safeConnection}";
        foreach (var item in ports)
        {
            var incoming = GraphIncoming(voiceName, item.port);
            var outgoing = GraphOutgoing(voiceName, item.port);
            var sourceInjection = NodeSourceIdentifier(voiceName, item.node);
            var otherIncoming = ports.Count == 1
                ? incoming
                : $"(({string.Join(" + ", ports.Where(other => other.port != item.port).Select(other => GraphIncoming(voiceName, other.port)))}) / {F(ports.Count - 1)})";
            source.AppendLine($"    {outgoing} = (({otherIncoming}) * ({coupling}) + {incoming} * (1.0 - ({coupling})) + ({sourceInjection}) / {F(Math.Max(1, item.nodePortCount))}) * {loss};");
        }
    }

    private static string ConnectionAreaExpression(
        string voiceName,
        (AcousticGraphNode node, AcousticTerminal terminal, AcousticGraphPort port, int nodePortCount) item) =>
        item.terminal.Kind == AcousticTerminalKind.Junction &&
        item.terminal.Port.Length == 0 &&
        item.node.Terminals.Count == 1
            ? GraphPortArea(voiceName, item.port)
            : ConnectionPortAreaExpression(voiceName, item.terminal, item.nodePortCount);

    private static bool TryEmitTwoPortPathScatter(
        StringBuilder source,
        string voiceName,
        AcousticGraphNode node,
        IReadOnlyList<AcousticGraphPort> ports,
        string sourceInjection,
        FaustExportOptions options,
        List<string> debugProbeKeepAlive)
    {
        if (ports.Count != 2 ||
            !ports[0].Segment.Path.Name.Equals(ports[1].Segment.Path.Name, StringComparison.OrdinalIgnoreCase) ||
            node.Terminals.Any(terminal =>
                terminal.Kind != AcousticTerminalKind.Junction &&
                terminal.Kind != AcousticTerminalKind.Source &&
                terminal.Kind != AcousticTerminalKind.Contact))
        {
            return false;
        }

        var left = ports.OrderBy(port => port.Segment.StartPosition).First();
        var right = ports.OrderByDescending(port => port.Segment.EndPosition).First();
        var leftArea = GraphPortArea(voiceName, left);
        var rightArea = GraphPortArea(voiceName, right);
        var leftIncoming = GraphIncoming(voiceName, left);
        var rightIncoming = GraphIncoming(voiceName, right);
        var leftOutgoing = GraphOutgoing(voiceName, left);
        var rightOutgoing = GraphOutgoing(voiceName, right);
        var reflection = $"{voiceName}_graph_area_reflection_{node.Name}";

        source.AppendLine($"    {reflection} = (({leftArea}) - ({rightArea})) / max(0.000001, ({leftArea}) + ({rightArea}));");
        source.AppendLine($"    {rightOutgoing} = {leftIncoming} - {reflection} * ({leftIncoming} + {rightIncoming}) + ({sourceInjection}) / 2;");
        source.AppendLine($"    {leftOutgoing} = {rightIncoming} + {reflection} * ({leftIncoming} + {rightIncoming}) + ({sourceInjection}) / 2;");
        var energyIn = $"({leftArea}) * pow({leftIncoming}, 2.0) + ({rightArea}) * pow({rightIncoming}, 2.0)";
        var energyOut = $"({leftArea}) * pow({leftOutgoing}, 2.0) + ({rightArea}) * pow({rightOutgoing}, 2.0)";
        source.AppendLine($"    {voiceName}_graph_area_energy_in_{node.Name} = {ProbeSignal(options, $"/debug/{voiceName}/area/{node.Name}/energy_in", energyIn, 0, 4)};");
        source.AppendLine($"    {voiceName}_graph_area_energy_out_{node.Name} = {ProbeSignal(options, $"/debug/{voiceName}/area/{node.Name}/energy_out", energyOut, 0, 4)};");
        debugProbeKeepAlive.Add($"{voiceName}_graph_area_energy_in_{node.Name}");
        debugProbeKeepAlive.Add($"{voiceName}_graph_area_energy_out_{node.Name}");
        return true;
    }

    private static string ProbeSignal(FaustExportOptions options, string label, string expression, float min, float max) =>
        options.DebugProbeUi
            ? $"attach(({expression}), ({expression}) : vbargraph(\"{Escape(label)}\", {F(min)}, {F(max)}))"
            : expression;

    private static string MaxExpression(IEnumerable<string> expressions)
    {
        using var enumerator = expressions.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return "0.000001";
        }

        var expression = enumerator.Current;
        while (enumerator.MoveNext())
        {
            expression = $"max({expression}, {enumerator.Current})";
        }

        return expression;
    }

    private static string NodeSourceIdentifier(string voiceName, AcousticGraphNode node) =>
        $"{voiceName}_graph_node_source_{node.Name}";

    private static string EmitNodeSourceExpression(
        StringBuilder source,
        SynthPatch patch,
        string voiceName,
        AcousticGraphNode node,
        IReadOnlyList<AcousticGraphPort> nodePorts,
        IReadOnlyDictionary<string, AcousticSourcePort> sources,
        string frequency,
        ParameterMap parameters)
    {
        var contactTerms = node.Terminals
            .Where(terminal => terminal.Kind == AcousticTerminalKind.Contact)
            .Select(terminal => EmitGraphContactSourceExpression(source, voiceName, node, nodePorts, terminal))
            .ToList();
        var sourceTerms = node.Terminals
            .Where(terminal => terminal.Kind == AcousticTerminalKind.Source && sources.ContainsKey(terminal.Port.Length == 0 ? terminal.Name : terminal.Port))
            .Select(terminal =>
            {
                var sourceName = terminal.Port.Length == 0 ? terminal.Name : terminal.Port;
                var port = sources[sourceName];
                return port.Kind == AcousticSourceKind.TurbulenceJet
                    ? EmitGraphTurbulenceSourceExpression(source, patch, voiceName, node, nodePorts, sourceName, port, parameters)
                    : EmitGraphLoadedSourceExpression(source, patch, voiceName, node, sourceName, port, frequency, parameters);
            })
            .ToList();
        sourceTerms.AddRange(contactTerms);
        return sourceTerms.Count == 0 ? "0.0" : string.Join(" + ", sourceTerms);
    }

    private static string EmitGraphLoadedSourceExpression(
        StringBuilder source,
        SynthPatch patch,
        string voiceName,
        AcousticGraphNode node,
        string sourceName,
        AcousticSourcePort port,
        string frequency,
        ParameterMap parameters)
    {
        var safeSource = SafeIdentifier(sourceName);
        var safeNode = SafeIdentifier(node.Name);
        var prefix = $"{voiceName}_graph_source_{safeSource}_{safeNode}";
        var localPressure = $"{voiceName}_graph_node_incident_pressure_{node.Name}";
        source.AppendLine($"    {prefix}_load_pressure = {localPressure};");
        if (IsTissueValveSource(port))
        {
            EmitGraphTissueValveSourceExpression(source, patch, voiceName, node, sourceName, port, frequency, parameters, localPressure);
        }
        else
        {
            source.AppendLine($"    {prefix}_out = {AcousticSourceExpression(patch, port, frequency, parameters, localPressure)};");
        }
        return $"{prefix}_out";
    }

    private static void EmitGraphTissueValveSourceExpression(
        StringBuilder source,
        SynthPatch patch,
        string voiceName,
        AcousticGraphNode node,
        string sourceName,
        AcousticSourcePort port,
        string frequency,
        ParameterMap parameters,
        string localPressure)
    {
        var index = AcousticSourcePortIndex(patch, port);
        var path = $"/acoustic/sources/{index}";
        var safeSource = SafeIdentifier(sourceName);
        var safeNode = SafeIdentifier(node.Name);
        var prefix = $"{voiceName}_graph_source_{safeSource}_{safeNode}";
        var pressure = $"clip01({parameters.Expression(OwnerField(path, "pressure"), port.Pressure)})";
        var tension = $"clip01({parameters.Expression(OwnerField(path, "tension"), port.Tension)})";
        var opening = $"max(0.0, {parameters.Expression(OwnerField(path, "opening"), port.Opening)})";
        var noise = $"clip01({parameters.Expression(OwnerField(path, "noise"), port.Noise)})";
        var balance = $"({parameters.Expression(OwnerField(path, "balance"), port.Balance)}) * ({SourcePositionWeightExpression(port, path, parameters)})";
        var mass = $"max(0.02, {parameters.Expression(OwnerField(path, "mass"), port.Mass)})";
        var damping = $"max(0.0, {parameters.Expression(OwnerField(path, "damping"), port.Damping)})";
        var stiffness = $"max(0.0, {parameters.Expression(OwnerField(path, "stiffness"), port.Stiffness)})";
        var saturation = $"max(0.0, {parameters.Expression(OwnerField(path, "saturation"), port.Saturation)})";
        var drive = $"max(0.0, {parameters.Expression(OwnerField(path, "drive"), port.Drive)})";
        var loadCoupling = $"max(0.0, {parameters.Expression(OwnerField(path, "load_coupling"), port.LoadCoupling)})";
        var restOpening = $"max(0.0, {parameters.Expression(OwnerField(path, "rest_opening"), port.RestOpening)})";
        var upperMass = $"max(0.02, {parameters.Expression(OwnerField(path, "upper_mass"), port.UpperMass)})";
        var lowerMass = $"max(0.02, {parameters.Expression(OwnerField(path, "lower_mass"), port.LowerMass)})";
        var upperStiffness = $"max(0.0, {parameters.Expression(OwnerField(path, "upper_stiffness"), port.UpperStiffness)})";
        var lowerStiffness = $"max(0.0, {parameters.Expression(OwnerField(path, "lower_stiffness"), port.LowerStiffness)})";
        var couplingStiffness = $"max(0.0, {parameters.Expression(OwnerField(path, "coupling_stiffness"), port.CouplingStiffness)})";
        var collisionStiffness = $"max(0.0, {parameters.Expression(OwnerField(path, "collision_stiffness"), port.CollisionStiffness)})";
        var collisionDamping = $"max(0.0, {parameters.Expression(OwnerField(path, "collision_damping"), port.CollisionDamping)})";
        var verticalPhase = $"clip01({parameters.Expression(OwnerField(path, "vertical_phase"), port.VerticalPhase)})";
        var reservoirPressure = $"max(0.0, {parameters.Expression(OwnerField(path, "reservoir_pressure"), port.ReservoirPressure)})";
        var downstreamPressure = $"max(0.0, {parameters.Expression(OwnerField(path, "downstream_pressure"), port.DownstreamPressure)})";
        var hasExplicitStiffness = port.Stiffness > 0 || parameters.IsBound(OwnerField(path, "stiffness"));
        var effectiveStiffness = hasExplicitStiffness
            ? $"max(0.00002, ({stiffness}) * (0.55 + 1.10 * ({tension})))"
            : $"{prefix}_stiffness_hint * (0.35 + 1.65 * ({tension}))";

        source.AppendLine($"    {prefix}_stiffness_hint = max(0.00002, min(0.16, pow(2.0 * ma.PI * max(20.0, {frequency}) / ma.SR, 2.0)));");
        source.AppendLine($"    {prefix}_reservoir_pressure = ({pressure}) * ({drive}) * ({reservoirPressure});");
        source.AppendLine($"    {prefix}_downstream_pressure = ({downstreamPressure}) + ({loadCoupling}) * ma.tanh({prefix}_load_pressure);");
        source.AppendLine($"    {prefix}_pressure_drive = max(0.0, {prefix}_reservoir_pressure - {prefix}_downstream_pressure);");
        source.AppendLine($"    {prefix}_stiffness = {effectiveStiffness};");
        source.AppendLine($"    {prefix}_modal_frequency = max(45.0, min(10000.0, (ma.SR / (2.0 * ma.PI)) * sqrt(max(0.00002, {prefix}_stiffness) / ({mass}))));");
        source.AppendLine($"    {prefix}_load_detune = 1.0 - 0.035 * ma.tanh({prefix}_load_pressure * ({loadCoupling}));");
        source.AppendLine($"    {prefix}_oscillation_gate = clip01(({prefix}_pressure_drive - 0.035 - 0.18 * clip01({opening})) * (2.4 + 2.6 * ({drive})));");
        source.AppendLine($"    {prefix}_modal_q = 3.0 + 24.0 * clip01(({pressure}) * (1.0 - 0.45 * ({damping})) + 0.35 * ({tension}));");
        source.AppendLine($"    {prefix}_modal_seed = (no.noise * 0.018 + {prefix}_pressure_drive * max(0.0, 0.22 - clip01({opening})) * 0.16);");
        source.AppendLine($"    {prefix}_modal_ring = {prefix}_modal_seed : fi.resonbp({prefix}_modal_frequency, {prefix}_modal_q, 1);");
        source.AppendLine($"    {prefix}_modal_oscillator = os.osc({prefix}_modal_frequency * {prefix}_load_detune) * {prefix}_oscillation_gate;");
        source.AppendLine($"    {prefix}_modal_tissue = ({prefix}_modal_oscillator * (0.70 + 0.80 * ({tension})) + {prefix}_modal_ring * 0.35) * (1.0 - 0.35 * ({damping}));");
        switch (port.Law)
        {
            case AcousticValveLaw.TwoMass:
            case AcousticValveLaw.BodyCover:
                source.AppendLine($"    {prefix}_upper_stiffness_effective = max(({upperStiffness}), {prefix}_stiffness * (0.80 + 0.45 * ({tension})));");
                source.AppendLine($"    {prefix}_lower_stiffness_effective = max(({lowerStiffness}), {prefix}_stiffness * (1.05 + 0.55 * ({tension})));");
                source.AppendLine($"    {prefix}_coupling_stiffness = ({couplingStiffness}) * (1.0 + 1.5 * ({tension}));");
                source.AppendLine($"    {prefix}_lower_velocity_decay = min(0.9995, max(0.20, 1.0 - ((0.010 + 0.090 * ({damping}) + 0.040 * ({collisionDamping})) / ({lowerMass}))));");
                source.AppendLine($"    {prefix}_upper_velocity_decay = min(0.9995, max(0.20, 1.0 - ((0.012 + 0.105 * ({damping}) + 0.030 * ({collisionDamping})) / ({upperMass}))));");
                source.AppendLine($"    {prefix}_lower_force = ({prefix}_pressure_drive * (0.0005 + 0.0028 * (1.0 - clip01({opening}))) - ({loadCoupling}) * {prefix}_load_pressure * 0.0012) / ({lowerMass});");
                source.AppendLine($"    {prefix}_upper_force = ({prefix}_pressure_drive * (0.0003 + 0.0018 * (1.0 - clip01({opening}))) - ({loadCoupling}) * {prefix}_load_pressure * 0.0008) / ({upperMass});");
                source.AppendLine($"    {prefix}_lower_velocity = {prefix}_lower_force : + ~ *({prefix}_lower_velocity_decay / (1.0 + 10.0 * {prefix}_lower_stiffness_effective + {prefix}_coupling_stiffness + 0.4 * ({saturation})));");
                source.AppendLine($"    {prefix}_upper_velocity = ({prefix}_upper_force + {prefix}_lower_velocity * {prefix}_coupling_stiffness * 0.35) : + ~ *({prefix}_upper_velocity_decay / (1.0 + 10.0 * {prefix}_upper_stiffness_effective + {prefix}_coupling_stiffness + 0.4 * ({saturation})));");
                source.AppendLine($"    {prefix}_lower_displacement = {prefix}_lower_velocity : + ~ *(min(0.9998, max(0.20, 1.0 - 0.0008 / ({lowerMass}))));");
                source.AppendLine($"    {prefix}_upper_displacement = {prefix}_upper_velocity : + ~ *(min(0.9998, max(0.20, 1.0 - 0.0008 / ({upperMass}))));");
                source.AppendLine($"    {prefix}_collision = ({collisionStiffness}) * max(0.0, 0.0 - (({restOpening}) + ({opening}) + {prefix}_upper_displacement));");
                source.AppendLine($"    {prefix}_displacement = {prefix}_lower_displacement * (1.0 - ({verticalPhase})) + ({prefix}_upper_displacement - {prefix}_collision) * ({verticalPhase});");
                break;
            default:
                source.AppendLine($"    {prefix}_velocity_decay = min(0.9995, max(0.20, 1.0 - ((0.008 + 0.08 * ({damping})) / ({mass}))));");
                source.AppendLine($"    {prefix}_displacement_decay = min(0.9998, max(0.20, 1.0 - 0.0008 / ({mass})));");
                source.AppendLine($"    {prefix}_force = ({prefix}_pressure_drive * (0.0004 + 0.0024 * (1.0 - clip01({opening}))) - ({loadCoupling}) * {prefix}_load_pressure * 0.001) / ({mass});");
                source.AppendLine($"    {prefix}_velocity = {prefix}_force : + ~ *({prefix}_velocity_decay / (1.0 + 10.0 * {prefix}_stiffness + 0.4 * ({saturation})));");
                source.AppendLine($"    {prefix}_displacement = {prefix}_velocity : + ~ *({prefix}_displacement_decay / (1.0 + 1.5 * {prefix}_stiffness));");
                break;
        }
        source.AppendLine($"    {prefix}_aperture = max(0.0, ({restOpening}) + ({opening}) + {prefix}_displacement - ({saturation}) * pow({prefix}_displacement, 3.0));");
        source.AppendLine($"    {prefix}_turbulence = no.noise : fi.highpass(2, 1200.0 + 2800.0 * clip01(1.0 - {prefix}_aperture));");
        source.AppendLine($"    {prefix}_voicing = {prefix}_modal_tissue * (0.18 + 1.45 * ({tension})) + {prefix}_displacement * (0.03 + 0.18 * ({drive}));");
        source.AppendLine($"    {prefix}_flow = ma.tanh(({prefix}_pressure_drive * {prefix}_aperture * (1.2 + 5.0 * ({drive}))) + ({prefix}_voicing * {prefix}_pressure_drive * (0.55 + 2.6 * ({drive}))));");
        source.AppendLine($"    {prefix}_noise = {prefix}_turbulence * ({noise}) * {prefix}_pressure_drive * clip01({prefix}_aperture) * (1.0 - 0.55 * ({tension}));");
        source.AppendLine($"    {prefix}_out = ({prefix}_flow * (0.55 + 2.6 * {prefix}_pressure_drive) + {prefix}_noise) * ({balance});");
    }

    private static string EmitGraphContactSourceExpression(
        StringBuilder source,
        string voiceName,
        AcousticGraphNode node,
        IReadOnlyList<AcousticGraphPort> nodePorts,
        AcousticTerminal terminal)
    {
        var safeTerminal = SafeIdentifier(terminal.Name);
        var prefix = $"{voiceName}_graph_contact_{safeTerminal}_{node.Name}";
        var terminalArea = $"{voiceName}_graph_terminal_area_{safeTerminal}";
        var localPressure = $"{voiceName}_graph_node_incident_pressure_{node.Name}";
        var drive = ContactReservoirDriveExpression(voiceName, node, nodePorts, localPressure);
        source.AppendLine($"    {prefix}_closure = clip01((0.0144 - ({terminalArea})) / 0.0144);");
        source.AppendLine($"    {prefix}_release = (max(0.0, ({prefix}_closure : mem) - {prefix}_closure) : + ~ *(0.992));");
        source.AppendLine($"    {prefix}_reservoir_drive = {drive};");
        source.AppendLine($"    {prefix}_reservoir = {prefix}_reservoir_drive * {prefix}_closure : + ~ *(0.994);");
        source.AppendLine($"    {prefix}_release_pressure = 0.12 + 3.20 * clip01({prefix}_reservoir);");
        source.AppendLine($"    {prefix}_out = {prefix}_release * {prefix}_release_pressure * (0.35 + 0.65 * clip01(abs({voiceName}_graph_terminal_reflection_{safeTerminal})));");
        return $"{prefix}_out";
    }

    private static string EmitGraphTurbulenceSourceExpression(
        StringBuilder source,
        SynthPatch patch,
        string voiceName,
        AcousticGraphNode node,
        IReadOnlyList<AcousticGraphPort> nodePorts,
        string sourceName,
        AcousticSourcePort port,
        ParameterMap parameters)
    {
        var index = AcousticSourcePortIndex(patch, port);
        var path = $"/acoustic/sources/{index}";
        var safeSource = SafeIdentifier(sourceName);
        var safeNode = SafeIdentifier(node.Name);
        var prefix = $"{voiceName}_graph_source_{safeSource}_{safeNode}";
        var localPressure = $"{voiceName}_graph_node_incident_pressure_{node.Name}";
        var pressure = $"clip01({parameters.Expression(OwnerField(path, "pressure"), port.Pressure)})";
        var opening = $"max(0.0, {parameters.Expression(OwnerField(path, "opening"), port.Opening)})";
        var noise = $"clip01({parameters.Expression(OwnerField(path, "noise"), port.Noise)})";
        var transient = $"clip01({parameters.Expression(OwnerField(path, "transient"), port.Transient)})";
        var balance = $"({parameters.Expression(OwnerField(path, "balance"), port.Balance)}) * ({SourcePositionWeightExpression(port, path, parameters)})";
        var reservoirDrive = ClosureReservoirDriveExpression(voiceName, node, nodePorts, port, localPressure);

        source.AppendLine($"    {prefix}_closure = clip01((0.12 - ({opening})) / 0.12);");
        source.AppendLine($"    {prefix}_release = (max(0.0, ({prefix}_closure : mem) - {prefix}_closure) : + ~ *(0.995));");
        source.AppendLine($"    {prefix}_reservoir_drive = {reservoirDrive};");
        source.AppendLine($"    {prefix}_reservoir = {prefix}_reservoir_drive * {prefix}_closure : + ~ *(0.992);");
        source.AppendLine($"    {prefix}_pressure_drive = 0.50 + 1.50 * clip01(abs({localPressure}));");
        source.AppendLine($"    {prefix}_release_pressure = 0.20 + 2.80 * clip01({prefix}_reservoir);");
        source.AppendLine($"    {prefix}_noise_gate = min(clip01((0.85 - ({opening})) / 0.85), clip01(25.0 * (({opening}) - 0.04)));");
        source.AppendLine($"    {prefix}_release_gate = clip01(({opening}) / 0.8) * {prefix}_release;");
        source.AppendLine($"    {prefix}_turbulence = no.noise : fi.highpass(2, 900.0 + 2600.0 * clip01(1.0 - ({opening})));");
        source.AppendLine($"    {prefix}_sustained = {prefix}_turbulence * {noise} * {pressure} * {prefix}_pressure_drive * {prefix}_noise_gate * 0.62;");
        source.AppendLine($"    {prefix}_burst_noise = {prefix}_turbulence * {noise} * {transient} * {prefix}_pressure_drive * {prefix}_release_gate * 1.2;");
        source.AppendLine($"    {prefix}_release_pulse = {transient} * {pressure} * {prefix}_release * {prefix}_release_pressure * 0.58 * (0.8 + 0.8 * clip01(({opening}) / 0.8));");
        source.AppendLine($"    {prefix}_out = ({prefix}_sustained + {prefix}_burst_noise + {prefix}_release_pulse) * {balance};");
        return $"{prefix}_out";
    }

    private static string ClosureReservoirDriveExpression(
        string voiceName,
        AcousticGraphNode node,
        IReadOnlyList<AcousticGraphPort> nodePorts,
        AcousticSourcePort sourcePort,
        string fallbackPressure)
    {
        var samePathPorts = nodePorts
            .Where(port => port.Segment.Path.Name.Equals(sourcePort.Path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var upstream = samePathPorts
            .Where(port => !port.AtStart && port.Segment.EndPosition <= node.Position + 0.0001f)
            .OrderByDescending(port => port.Segment.StartPosition)
            .FirstOrDefault();
        var downstream = samePathPorts
            .Where(port => port.AtStart && port.Segment.StartPosition >= node.Position - 0.0001f)
            .OrderBy(port => port.Segment.EndPosition)
            .FirstOrDefault();
        return upstream is not null && downstream is not null
            ? $"max(0.0, {GraphIncoming(voiceName, upstream)} - {GraphIncoming(voiceName, downstream)})"
            : $"max(0.0, {fallbackPressure})";
    }

    private static string ContactReservoirDriveExpression(
        string voiceName,
        AcousticGraphNode node,
        IReadOnlyList<AcousticGraphPort> nodePorts,
        string fallbackPressure)
    {
        var upstream = nodePorts
            .Where(port => !port.AtStart && port.Segment.EndPosition <= node.Position + 0.0001f)
            .OrderByDescending(port => port.Segment.StartPosition)
            .FirstOrDefault();
        var downstream = nodePorts
            .Where(port => port.AtStart && port.Segment.StartPosition >= node.Position - 0.0001f)
            .OrderBy(port => port.Segment.EndPosition)
            .FirstOrDefault();
        return upstream is not null && downstream is not null
            ? $"max(0.0, {GraphIncoming(voiceName, upstream)} - {GraphIncoming(voiceName, downstream)})"
            : $"max(0.0, {fallbackPressure})";
    }

    private static string GraphIncoming(string voiceName, AcousticGraphPort port) =>
        port.AtStart
            ? $"{voiceName}_graph_in_{port.Segment.Index}_start"
            : $"{voiceName}_graph_in_{port.Segment.Index}_end";

    private static string GraphOutgoing(string voiceName, AcousticGraphPort port) =>
        port.AtStart
            ? $"{voiceName}_graph_out_{port.Segment.Index}_start"
            : $"{voiceName}_graph_out_{port.Segment.Index}_end";

    private static string GraphPortArea(string voiceName, AcousticGraphPort port) =>
        $"{voiceName}_graph_segment_area_{port.Segment.Index}";

    private static IEnumerable<string> NextStatesPlaceholder(int count) =>
        Enumerable.Range(0, count).Select(i => $"__graph_next_state_{i}__");

    private static int AcousticConnectionIndex(SynthPatch patch, AcousticConnection connection)
    {
        for (var i = 0; i < patch.AcousticConnections.Count; i++)
        {
            if (ReferenceEquals(patch.AcousticConnections[i], connection) ||
                patch.AcousticConnections[i].Name.Equals(connection.Name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static int AcousticPathIndex(SynthPatch patch, AcousticPath path)
    {
        for (var i = 0; i < patch.AcousticPaths.Count; i++)
        {
            if (ReferenceEquals(patch.AcousticPaths[i], path) ||
                patch.AcousticPaths[i].Name.Equals(path.Name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static int AcousticBranchIndex(SynthPatch patch, AcousticBranch branch)
    {
        for (var i = 0; i < patch.AcousticBranches.Count; i++)
        {
            if (ReferenceEquals(patch.AcousticBranches[i], branch) ||
                patch.AcousticBranches[i].Name.Equals(branch.Name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static int AcousticRadiationPortIndex(SynthPatch patch, AcousticRadiationPort port)
    {
        for (var i = 0; i < patch.AcousticRadiationPorts.Count; i++)
        {
            if (ReferenceEquals(patch.AcousticRadiationPorts[i], port) ||
                patch.AcousticRadiationPorts[i].Name.Equals(port.Name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static int AcousticTerminalIndex(SynthPatch patch, AcousticTerminal terminal)
    {
        for (var i = 0; i < patch.AcousticTerminals.Count; i++)
        {
            if (ReferenceEquals(patch.AcousticTerminals[i], terminal) ||
                patch.AcousticTerminals[i].Name.Equals(terminal.Name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static int AcousticSourcePortIndex(SynthPatch patch, AcousticSourcePort port)
    {
        for (var i = 0; i < patch.AcousticSourcePorts.Count; i++)
        {
            if (ReferenceEquals(patch.AcousticSourcePorts[i], port) ||
                patch.AcousticSourcePorts[i].Name.Equals(port.Name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static string SafeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '_');
        }

        return builder.Length == 0 ? "unnamed" : builder.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed record AcousticGraphSegment(
        int Index,
        AcousticPath Path,
        AcousticGraphNode Start,
        AcousticGraphNode End,
        float StartPosition,
        float EndPosition);

    private sealed record AcousticGraphNode(
        string Name,
        string Path,
        float Position,
        IReadOnlyList<AcousticTerminal> Terminals);

    private sealed record AcousticGraphPort(AcousticGraphSegment Segment, bool AtStart);

    private sealed class ParameterMap
    {
        private readonly Dictionary<string, int> _parameterIndexes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ParameterBinding> _bindings = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _boundParameterPaths = new(StringComparer.OrdinalIgnoreCase);

        public ParameterMap(SynthPatch patch, List<string> warnings)
        {
            for (var i = 0; i < patch.Parameters.Count; i++)
            {
                _parameterIndexes[patch.Parameters[i].Path] = i;
            }

            foreach (var binding in patch.ParameterBindings)
            {
                if (!_parameterIndexes.ContainsKey(binding.ParameterPath))
                {
                    warnings.Add($"parameter binding {binding.FieldPath}: unknown parameter `{binding.ParameterPath}`");
                    continue;
                }

                _bindings[binding.FieldPath] = binding;
                _boundParameterPaths.Add(binding.ParameterPath);
            }

            foreach (var parameter in patch.Parameters)
            {
                if (!_boundParameterPaths.Contains(parameter.Path))
                {
                    warnings.Add($"parameter {parameter.Path}: declared but not bound to a patch field");
                }
            }
        }

        public bool IsBound(string fieldPath) => _bindings.ContainsKey(fieldPath);

        public string Expression(string fieldPath, float fallback)
        {
            if (!_bindings.TryGetValue(fieldPath, out var binding))
            {
                return F(fallback);
            }

            var parameter = ParameterIdentifier(_parameterIndexes[binding.ParameterPath]);
            var transformed = binding.Transform switch
            {
                ParameterBindingTransform.Square => $"(({parameter}) * ({parameter}))",
                _ => parameter
            };
            return binding.Scale == 1 ? transformed : $"({F(binding.Scale)} * ({transformed}))";
        }

        public IEnumerable<string> UnboundParameterIds()
        {
            foreach (var (path, index) in _parameterIndexes)
            {
                if (!_boundParameterPaths.Contains(path))
                {
                    yield return ParameterIdentifier(index);
                }
            }
        }
    }
}


