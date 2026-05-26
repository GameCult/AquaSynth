using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AquaSynth.Dsl;

public sealed record FaustExportOptions(string Name = "aquasynth_patch", bool Stereo = false);

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
            EmitVoice(source, patch, patch.Voices[i], VoicePath(i), name, parameters, warnings);
            voices.Add(name);
        }
        for (var i = 0; i < patch.SpectralBanks.Count; i++)
        {
            var name = $"spectral_{i}";
            EmitSpectralBank(source, patch, patch.SpectralBanks[i], i, name, parameters, warnings);
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
            source.AppendLine($"{ParameterIdentifier(i)} = hslider(\"{Escape(parameter.Path)}\", {F(parameter.Default)}, {F(parameter.Min)}, {F(parameter.Max)}, {F(parameter.Step)}) : si.smoo;");
        }
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
                         (voice.Tract is { } tract
                             ? TractExpression(source, tract, ownerPath, name, frequency, noteGate, parameters)
                             : voice.AcousticNetwork is { } acousticNetwork
                             ? AcousticNetworkExpression(source, patch, acousticNetwork, ownerPath, name, frequency, parameters, warnings)
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
        List<string> warnings)
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
        EmitVoice(source, patch, bank.Treatment, SpectralPath(bankIndex), name, parameters, warnings, $"{name}_wavetable");
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
        List<string> warnings)
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
            var graph = AcousticGraphExpression(source, patch, network, name, frequency, parameters, warnings, paths, sources, radiation, terminals, connections, waveClocks);
            if (graph.Length > 0)
            {
                return graph;
            }
        }

        var sourceExpressions = new List<string>();
        foreach (var sourceName in network.SourcePorts)
        {
            if (!sources.TryGetValue(sourceName, out var port))
            {
                warnings.Add($"{name}: acoustic network `{network.Name}` has unknown source port `{sourceName}`");
                continue;
            }
            sourceExpressions.Add(AcousticSourceExpression(patch, port, frequency, parameters));
        }
        if (sourceExpressions.Count == 0)
        {
            sourceExpressions.Add("0.0");
            warnings.Add($"{name}: acoustic network `{network.Name}` has no valid source ports");
        }

        var baseInput = $"({string.Join(" + ", sourceExpressions)})";
        var lengthMeters = Math.Max(0.001f, primaryPath.AreaFunction.LengthMeters);
        var quarterWave = primaryPath.PropagationSpeedMetersPerSecond / (4 * lengthMeters);
        var back = primaryPath.AreaFunction.AverageDiameter(0.1f, 0.35f);
        var middle = primaryPath.AreaFunction.AverageDiameter(0.35f, 0.7f);
        var front = primaryPath.AreaFunction.AverageDiameter(0.7f, 1f);
        var reflectionEnergy = primaryPath.AreaFunction.ReflectionCoefficients.Count == 0
            ? 0.12f
            : MathF.Min(1, primaryPath.AreaFunction.ReflectionCoefficients.Sum(item => MathF.Abs(item)) / primaryPath.AreaFunction.ReflectionCoefficients.Count);
        var loss = F(Math.Clamp(primaryPath.Loss, 0, 1));
        var q = F(2.5f + reflectionEnergy * 8f + (1 - Math.Clamp(primaryPath.Loss, 0, 1)) * 18f);
        source.AppendLine($"{name}_acoustic_source = {baseInput};");
        source.AppendLine($"{name}_acoustic_f1 = max(40.0, {F(quarterWave)} * (1.0 + {F(back - middle)} * 0.18));");
        source.AppendLine($"{name}_acoustic_f2 = max(80.0, {F(quarterWave * 3)} * (1.0 + {F(middle - front)} * 0.14));");
        source.AppendLine($"{name}_acoustic_f3 = max(120.0, {F(quarterWave * 5)} * (1.0 + {F(front - back)} * 0.10));");
        source.AppendLine($"{name}_acoustic_body = ({name}_acoustic_source * 0.12 + ({name}_acoustic_source : fi.resonbp({name}_acoustic_f1, {q}, 1.0)) * 0.9 + ({name}_acoustic_source : fi.resonbp({name}_acoustic_f2, {q}, 1.0)) * 0.65 + ({name}_acoustic_source : fi.resonbp({name}_acoustic_f3, {q}, 1.0)) * 0.35) * {loss};");

        var branchExpressions = new List<string>();
        foreach (var branchName in network.Branches)
        {
            if (!branches.TryGetValue(branchName, out var branch) ||
                !paths.TryGetValue(branch.ToPath, out var branchPath))
            {
                warnings.Add($"{name}: acoustic network `{network.Name}` has unknown branch `{branchName}`");
                continue;
            }

            var branchLength = Math.Max(0.001f, branchPath.AreaFunction.LengthMeters);
            var branchQuarterWave = branchPath.PropagationSpeedMetersPerSecond / (4 * branchLength);
            var branchIndex = AcousticBranchIndex(patch, branch);
            var branchPathName = $"/acoustic/branches/{branchIndex}";
            var branchOpening = $"clip01({parameters.Expression(OwnerField(branchPathName, "opening"), branch.Opening)})";
            var branchCoupling = $"max(0.0, {parameters.Expression(OwnerField(branchPathName, "coupling"), branch.Coupling)})";
            var branchQ = F(2f + Math.Clamp(branch.Coupling, 0, 1) * 8f);
            var branchKindGain = branch.Kind == AcousticBranchKind.Nasal ? "0.75" : branch.Kind == AcousticBranchKind.Bronchial ? "0.55" : "0.45";
            source.AppendLine($"{name}_branch_{SafeIdentifier(branch.Name)} = ({name}_acoustic_source : fi.resonbp({F(branchQuarterWave)}, {branchQ}, 1.0) : fi.lowpass(2, {F(branchQuarterWave * 8)})) * {branchOpening} * {branchCoupling} * {branchKindGain};");
            branchExpressions.Add($"{name}_branch_{SafeIdentifier(branch.Name)}");
        }

        var radiationExpressions = new List<string>();
        foreach (var radiationName in network.RadiationPorts)
        {
            if (!radiation.TryGetValue(radiationName, out var port))
            {
                warnings.Add($"{name}: acoustic network `{network.Name}` has unknown radiation port `{radiationName}`");
                continue;
            }

            var radiationIndex = AcousticRadiationPortIndex(patch, port);
            var radiationPath = $"/acoustic/radiation/{radiationIndex}";
            var opening = $"max(0.0, {parameters.Expression(OwnerField(radiationPath, "opening"), port.Opening)})";
            var reflection = $"min(1.0, abs({parameters.Expression(OwnerField(radiationPath, "reflection"), port.Reflection)}))";
            var portLoss = $"clip01({parameters.Expression(OwnerField(radiationPath, "loss"), port.Loss)})";
            var highpass = port.Kind is AcousticRadiationKind.Lip or AcousticRadiationKind.Beak
                ? "80.0"
                : port.Kind == AcousticRadiationKind.Nostril
                ? "40.0"
                : "20.0";
            radiationExpressions.Add($"(({name}_acoustic_body : fi.highpass(1, {highpass})) * {opening} * (0.55 + 0.45 * {reflection}) * {portLoss})");
        }
        if (radiationExpressions.Count == 0)
        {
            radiationExpressions.Add($"{name}_acoustic_body");
        }

        var branchMix = branchExpressions.Count == 0 ? "0.0" : string.Join(" + ", branchExpressions);
        source.AppendLine($"{name}_acoustic_branches = {branchMix};");
        source.AppendLine($"{name}_acoustic_radiated = ({string.Join(" + ", radiationExpressions)}) + {name}_acoustic_branches;");
        return $"{name}_acoustic_radiated";
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
        IReadOnlyDictionary<string, WaveClockPolicy> waveClocks)
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
            warnings.Add($"{name}: acoustic graph `{network.Name}` needs at least two valid terminals; using response proxy");
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

            var ordered = group
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
            warnings.Add($"{name}: acoustic graph `{network.Name}` produced no path segments; using response proxy");
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
            source.AppendLine($"{name}_graph_terminal_area_{SafeIdentifier(terminal.Name)} = max(0.000001, {F(TerminalArea(paths, terminal))} * max(0.0, {parameters.Expression(OwnerField(terminalPath, "area_scale"), terminal.AreaScale)}));");
            source.AppendLine($"{name}_graph_terminal_reflection_{SafeIdentifier(terminal.Name)} = {parameters.Expression(OwnerField(terminalPath, "reflection"), terminal.Reflection)};");
        }
        foreach (var node in graphNodes)
        {
            source.AppendLine($"{name}_graph_node_area_{node.Name} = max(0.000001, {string.Join(" + ", node.Terminals.Select(terminal => $"{name}_graph_terminal_area_{SafeIdentifier(terminal.Name)}"))});");
            source.AppendLine($"{name}_graph_node_reflection_{node.Name} = ({string.Join(" + ", node.Terminals.Select(terminal => $"{name}_graph_terminal_reflection_{SafeIdentifier(terminal.Name)}"))}) / {F(Math.Max(1, node.Terminals.Count))};");
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

        foreach (var sourceName in network.SourcePorts)
        {
            if (sources.TryGetValue(sourceName, out var port))
            {
                source.AppendLine($"{name}_graph_source_{SafeIdentifier(sourceName)} = {AcousticSourceExpression(patch, port, frequency, parameters)};");
            }
        }

        var stateCount = segments.Count * 2;
        var stateInputs = segments.Select(segment => $"r{segment.Index}")
            .Concat(segments.Select(segment => $"l{segment.Index}"))
            .ToList();
        var nextStates = new List<string>(stateCount);
        source.AppendLine($"{name}_graph(x) = {name}_graph_loop ~ si.bus({stateCount}) : (si.block({stateCount}), _) with {{");
        source.AppendLine($"  {name}_graph_loop({string.Join(", ", stateInputs)}) = {string.Join(", ", NextStatesPlaceholder(stateCount))}, {name}_graph_out with {{");

        foreach (var segment in segments)
        {
            source.AppendLine($"    {name}_graph_in_{segment.Index}_start = l{segment.Index};");
            source.AppendLine($"    {name}_graph_in_{segment.Index}_end = r{segment.Index};");
        }

        var connectionGroups = network.Connections
            .Select(connectionName => connections.TryGetValue(connectionName, out var connection) ? connection : null)
            .Where(connection => connection is not null)
            .Cast<AcousticConnection>()
            .ToList();
        foreach (var connection in connectionGroups)
        {
            var connectionNodes = connection.Terminals
                .Where(terminalNodes.ContainsKey)
                .Select(terminalName => terminalNodes[terminalName])
                .DistinctBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var ports = connectionNodes
                .SelectMany(node => incident[node.Name].Select(port => (node, port)))
                .ToList();
            if (ports.Count < 2)
            {
                continue;
            }

            var safeConnection = SafeIdentifier(connection.Name);
            var connectionPressure = ConnectionPressureExpression(name, connection, ports);
            source.AppendLine($"    {name}_graph_connection_pressure_{safeConnection} = {connectionPressure};");
            foreach (var (node, port) in ports)
            {
                var incoming = GraphIncoming(name, port);
                var outgoing = GraphOutgoing(name, port);
                var sourceInjection = NodeSourceExpression(name, node, sources);
                var bypassInput = ports.Count == 1
                    ? incoming
                    : $"(({string.Join(" + ", ports.Where(item => item.port != port).Select(item => GraphIncoming(name, item.port)))}) / {F(ports.Count - 1)})";
                var scattered = connection.Law == AcousticConnectionLaw.Bypass
                    ? bypassInput
                    : $"({name}_graph_connection_pressure_{safeConnection} - {incoming})";
                source.AppendLine($"    {outgoing} = (({scattered}) * {name}_graph_connection_coupling_{safeConnection} + {incoming} * (1.0 - {name}_graph_connection_coupling_{safeConnection}) + ({sourceInjection}) / {F(Math.Max(1, incident[node.Name].Count))}) * {name}_graph_connection_loss_{safeConnection};");
            }
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

            var sourceInjection = NodeSourceExpression(name, node, sources);
            var reflection = $"{name}_graph_node_reflection_{node.Name}";
            foreach (var port in ports)
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
                var opening = $"max(0.0, {parameters.Expression(OwnerField(radiationPath, "opening"), radiationPort.Opening)})";
                var loss = $"clip01({parameters.Expression(OwnerField(radiationPath, "loss"), radiationPort.Loss)})";
                radiated.Add($"(({string.Join(" + ", ports.Select(port => GraphIncoming(name, port)))}) * {opening} * {loss})");
            }
        }

        foreach (var segment in segments)
        {
            var loss = F(Math.Clamp(segment.Path.Loss, 0, 1));
            var delayExpression = SegmentDelayExpression(segment);
            var maxDelay = SegmentMaxDelay(segment, waveClock);
            source.AppendLine($"    {name}_graph_delay_{segment.Index} = {delayExpression};");
            source.AppendLine($"    {name}_graph_next_r{segment.Index} = {GraphOutgoing(name, new AcousticGraphPort(segment, true))} : {WaveClockDelayExpression(waveClock, maxDelay, $"{name}_graph_delay_{segment.Index}")} * {loss};");
            source.AppendLine($"    {name}_graph_next_l{segment.Index} = {GraphOutgoing(name, new AcousticGraphPort(segment, false))} : {WaveClockDelayExpression(waveClock, maxDelay, $"{name}_graph_delay_{segment.Index}")} * {loss};");
            nextStates.Add($"{name}_graph_next_r{segment.Index}");
        }
        nextStates.AddRange(segments.Select(segment => $"{name}_graph_next_l{segment.Index}"));
        source.Replace(string.Join(", ", NextStatesPlaceholder(stateCount)), string.Join(", ", nextStates));
        source.AppendLine($"    {name}_graph_out = {(radiated.Count == 0 ? "0.0" : string.Join(" + ", radiated))};");
        source.AppendLine("  };");
        source.AppendLine("};");
        source.AppendLine($"{name}_acoustic_graph_radiated = {name}_graph({name}_graph_drive);");
        return $"{name}_acoustic_graph_radiated";
    }

    private static string AcousticSourceExpression(
        SynthPatch patch,
        AcousticSourcePort port,
        string frequency,
        ParameterMap parameters)
    {
        var index = AcousticSourcePortIndex(patch, port);
        var path = $"/acoustic/sources/{index}";
        var pressure = $"clip01({parameters.Expression(OwnerField(path, "pressure"), port.Pressure)})";
        var tension = $"clip01({parameters.Expression(OwnerField(path, "tension"), port.Tension)})";
        var opening = $"max(0.0, {parameters.Expression(OwnerField(path, "opening"), port.Opening)})";
        var noise = $"clip01({parameters.Expression(OwnerField(path, "noise"), port.Noise)})";
        var balance = parameters.Expression(OwnerField(path, "balance"), port.Balance);
        var detune = port.Kind == AcousticSourceKind.Labial ? $" * (1.0 + ({balance} - 0.5) * 0.018)" : "";
        var phase = $"os.phasor(1.0, {frequency}{detune})";
        return port.Kind switch
        {
            AcousticSourceKind.Glottal or AcousticSourceKind.Labial =>
                $"((sin(2.0 * ma.PI * {phase}) - {tension} * 0.35 * sin(4.0 * ma.PI * {phase})) * {pressure} * {opening} + no.noise * {noise} * {pressure} * (1.0 - {tension})) * {balance}",
            AcousticSourceKind.Reed =>
                $"(ma.tanh(sin(2.0 * ma.PI * {phase}) * (1.0 + {pressure} * 8.0)) * {opening} + no.noise * {noise} * 0.2) * {balance}",
            AcousticSourceKind.TurbulenceJet =>
                $"no.noise * {noise} * {pressure} * {opening} * {balance}",
            AcousticSourceKind.Click =>
                $"no.noise * {pressure} * {opening} * exp(0.0 - age * 120.0) * {balance}",
            AcousticSourceKind.Synthetic =>
                $"sin(2.0 * ma.PI * {phase}) * {pressure} * {opening} * {balance}",
            _ => "0.0"
        };
    }

    private static string TractExpression(
        StringBuilder source,
        VocalTract tract,
        string ownerPath,
        string name,
        string frequency,
        string gate,
        ParameterMap parameters)
    {
        var tractPath = OwnerField(ownerPath, "tract");
        var sections = Math.Max(4, tract.Sections);
        var hasNose = tract.NoseSections > 0;
        var intensity = $"clip01({parameters.Expression(OwnerField(tractPath, "intensity"), tract.Intensity)})";
        var tenseness = $"clip01({parameters.Expression(OwnerField(tractPath, "tenseness"), tract.Tenseness)})";
        var motion = tract.Motion;
        var diameterSlew = parameters.Expression(OwnerField(tractPath, "motion/diameter_slew"), motion?.DiameterSlewPerSecond ?? 18);
        var shapeReturn = parameters.Expression(OwnerField(tractPath, "motion/shape_return"), motion?.ShapeReturnPerSecond ?? 8);
        var constrictionSlew = parameters.Expression(OwnerField(tractPath, "motion/constriction_slew"), motion?.ConstrictionSlewPerSecond ?? 24);
        var velumSlew = parameters.Expression(OwnerField(tractPath, "motion/velum_slew"), motion?.VelumSlewPerSecond ?? 16);
        var obstructionThreshold = parameters.Expression(OwnerField(tractPath, "motion/obstruction_threshold"), motion?.ObstructionThreshold ?? 0.05f);
        var tongueIndexPath = OwnerField(tractPath, "tongue_index");
        var tongueDiameterPath = OwnerField(tractPath, "tongue_diameter");
        var velumPath = OwnerField(tractPath, "velum");
        var constrictionIndexPath = OwnerField(tractPath, "constriction_index");
        var constrictionDiameterPath = OwnerField(tractPath, "constriction_diameter");
        var lipOpeningPath = OwnerField(tractPath, "lip_opening");
        var indexScale = F(tract.IndexScale);
        var tongueIndexRaw = ScaleIndex(parameters.Expression(tongueIndexPath, tract.TongueIndex));
        var tongueDiameterRaw = parameters.Expression(tongueDiameterPath, tract.TongueDiameter);
        var velumRaw = $"clip01(({parameters.Expression(velumPath, tract.Velum)} - 0.01) / 0.39)";
        var constrictionIndexRaw = ScaleIndex(parameters.Expression(constrictionIndexPath, tract.ConstrictionIndex));
        var constrictionDiameterRaw = parameters.Expression(constrictionDiameterPath, tract.ConstrictionDiameter);
        var tongueIndexValue = SmoothControl(motion, parameters, tongueIndexPath, diameterSlew, tongueIndexRaw);
        var tongueDiameterValue = SmoothControl(motion, parameters, tongueDiameterPath, diameterSlew, tongueDiameterRaw);
        var velumValue = SmoothControl(motion, parameters, velumPath, velumSlew, velumRaw);
        var constrictionIndexValue = SmoothControl(motion, parameters, constrictionIndexPath, constrictionSlew, constrictionIndexRaw);
        var constrictionDiameterValue = SmoothControl(motion, parameters, constrictionDiameterPath, constrictionSlew, constrictionDiameterRaw);
        var turbulence = $"clip01({parameters.Expression(OwnerField(tractPath, "turbulence"), tract.Turbulence)})";
        var lipOpening = parameters.Expression(lipOpeningPath, tract.LipOpening);
        var glottalReflection = parameters.Expression(OwnerField(tractPath, "glottal_reflection"), tract.GlottalReflection);
        var lipReflection = parameters.Expression(OwnerField(tractPath, "lip_reflection"), tract.LipReflection);
        var areaFunction = tract.AreaFunction;
        var glottis = tract.Glottis;
        var injection = tract.Injection;
        var shapeBack = F(areaFunction?.AverageDiameter(0.18f, 0.38f) ?? 1.35f);
        var shapeMiddle = F(areaFunction?.AverageDiameter(0.38f, 0.68f) ?? 1.5f);
        var shapeFront = F(areaFunction?.AverageDiameter(0.68f, 0.96f) ?? 1.5f);
        var shapeMinimum = F(areaFunction?.MinimumDiameter ?? 0.6f);
        var reflectionEnergy = F(areaFunction is null ? 0.12f : MathF.Min(1, areaFunction.ReflectionCoefficients.Sum(reflection => MathF.Abs(reflection)) / Math.Max(1, areaFunction.ReflectionCoefficients.Count)));
        var aspiration = $"clip01({parameters.Expression(OwnerField(tractPath, "glottis/aspiration"), glottis?.Aspiration ?? 0.08f)})";
        var glottalSkew = $"clip01({parameters.Expression(OwnerField(tractPath, "glottis/skew"), glottis?.Skew ?? 0.42f)})";
        var injectionPositionPath = OwnerField(tractPath, "injection/position");
        var injectionDiameterPath = OwnerField(tractPath, "injection/diameter");
        var injectionPositionRaw = ScaleIndex(parameters.Expression(injectionPositionPath, injection?.Position ?? tract.ConstrictionIndex));
        var injectionDiameterRaw = parameters.Expression(injectionDiameterPath, injection?.Diameter ?? tract.ConstrictionDiameter);
        var injectionPositionValue = SmoothControl(motion, parameters, injectionPositionPath, constrictionSlew, injectionPositionRaw);
        var injectionDiameterValue = SmoothControl(motion, parameters, injectionDiameterPath, constrictionSlew, injectionDiameterRaw);
        var injectionTurbulence = $"clip01({parameters.Expression(OwnerField(tractPath, "injection/turbulence"), injection?.Turbulence ?? tract.Turbulence)})";
        var injectionBurst = $"clip01({parameters.Expression(OwnerField(tractPath, "injection/burst"), injection?.Burst ?? 0)})";
        var injectionWidth = $"max(1.0, {ScaleIndex(parameters.Expression(OwnerField(tractPath, "injection/width"), injection?.Width ?? 1))})";

        source.AppendLine($"{name}_tract_phase = os.phasor(1.0, {frequency});");
        source.AppendLine($"{name}_tract_tongue_index = {tongueIndexValue};");
        source.AppendLine($"{name}_tract_tongue_diameter = {tongueDiameterValue};");
        source.AppendLine($"{name}_tract_velum = {velumValue};");
        source.AppendLine($"{name}_tract_constriction_index = {constrictionIndexValue};");
        source.AppendLine($"{name}_tract_constriction_diameter = {constrictionDiameterValue};");
        source.AppendLine($"{name}_tract_injection_index = {injectionPositionValue};");
        source.AppendLine($"{name}_tract_injection_diameter = {injectionDiameterValue};");
        var tongueIndex = $"{name}_tract_tongue_index";
        var tongueDiameter = $"{name}_tract_tongue_diameter";
        var velum = $"{name}_tract_velum";
        var constrictionIndex = $"{name}_tract_constriction_index";
        var constrictionDiameter = $"{name}_tract_constriction_diameter";
        var injectionPosition = $"{name}_tract_injection_index";
        var injectionDiameter = $"{name}_tract_injection_diameter";
        source.AppendLine($"{name}_tract_tongue_pos = clip01({tongueIndex} / {F(sections)});");
        source.AppendLine($"{name}_tract_constriction_pos = clip01({constrictionIndex} / {F(sections)});");
        source.AppendLine($"{name}_tract_injection_pos = clip01({injectionPosition} / {F(sections)});");
        source.AppendLine($"{name}_tract_tongue_close = clip01((3.5 - {tongueDiameter}) / 3.5);");
        source.AppendLine($"{name}_tract_constriction_close = clip01((1.15 - {constrictionDiameter}) / 1.15);");
        source.AppendLine($"{name}_tract_injection_close = clip01((1.15 - {injectionDiameter}) / max(0.05, 1.15 * {injectionWidth}));");
        source.AppendLine($"{name}_tract_lip = clip01({lipOpening} / 2.5);");
        source.AppendLine($"{name}_tract_shape_back = {shapeBack};");
        source.AppendLine($"{name}_tract_shape_middle = {shapeMiddle};");
        source.AppendLine($"{name}_tract_shape_front = {shapeFront};");
        source.AppendLine($"{name}_tract_shape_min = {shapeMinimum};");
        source.AppendLine($"{name}_tract_reflection_energy = {reflectionEnergy};");
        source.AppendLine($"{name}_tract_q = 2.0 + {tenseness} * 10.0 + {name}_tract_constriction_close * 8.0 + {name}_tract_reflection_energy * 4.0;");
        source.AppendLine($"{name}_tract_f1 = max(90.0, 260.0 + {name}_tract_lip * 720.0 - {name}_tract_tongue_close * 260.0 + ({name}_tract_shape_back - 1.35) * 210.0 - (1.0 - {name}_tract_shape_min) * 120.0 + {velum} * 120.0);");
        source.AppendLine($"{name}_tract_f2 = max(180.0, 820.0 + {name}_tract_tongue_pos * 1850.0 - {name}_tract_tongue_close * 640.0 + ({name}_tract_shape_middle - 1.5) * 360.0 - (1.0 - {name}_tract_lip) * 260.0);");
        source.AppendLine($"{name}_tract_f3 = max(500.0, 1900.0 + {name}_tract_constriction_pos * 2600.0 + {name}_tract_tongue_close * 700.0 + ({name}_tract_shape_front - 1.5) * 520.0);");
        source.AppendLine($"{name}_tract_lf_open = select2({name}_tract_phase < (0.42 + {glottalSkew} * 0.36), -0.28 * sin(ma.PI * ({name}_tract_phase - (0.42 + {glottalSkew} * 0.36)) / max(0.001, 1.0 - (0.42 + {glottalSkew} * 0.36))), sin(ma.PI * {name}_tract_phase / max(0.001, 0.42 + {glottalSkew} * 0.36)));");
        source.AppendLine($"{name}_tract_lf = ({name}_tract_lf_open - (0.12 + {tenseness} * 0.62) * sin(4.0 * ma.PI * {name}_tract_phase)) * {intensity} * (0.45 + 0.75 * pow(max(0.0, {tenseness}), 0.35));");
        source.AppendLine($"{name}_tract_aspiration = no.noise * {intensity} * {aspiration} * (1.0 - sqrt(max(0.0, {tenseness}))) * (0.08 + 0.22 * {name}_tract_constriction_close);");
        source.AppendLine($"{name}_tract_frication = no.noise * max({turbulence} * {name}_tract_constriction_close, {injectionTurbulence} * {name}_tract_injection_close) * {intensity} * (0.25 + 0.75 * {name}_tract_injection_pos) * (0.2 + 0.8 * {tenseness});");
        source.AppendLine($"{name}_tract_prev_injection_close = {name}_tract_injection_close : mem;");
        source.AppendLine($"{name}_tract_obstructed = select2({injectionDiameter} < {obstructionThreshold}, 0.0, 1.0);");
        source.AppendLine($"{name}_tract_prev_obstructed = {name}_tract_obstructed : mem;");
        source.AppendLine($"{name}_tract_release_opening = max(0.0, {name}_tract_prev_injection_close - {name}_tract_injection_close) * {name}_tract_prev_obstructed;");
        source.AppendLine($"{name}_tract_release_memory = {name}_tract_release_opening : + ~ *(0.935);");
        source.AppendLine($"{name}_tract_burst = no.noise * {name}_tract_release_memory * {injectionBurst} * {intensity};");
        source.AppendLine($"{name}_tract_excitation = ({name}_tract_lf + {name}_tract_aspiration + {name}_tract_burst) * (0.55 + 0.45 * abs({glottalReflection}));");
        source.AppendLine($"{name}_tract_injection_pressure = {name}_tract_frication * (0.55 + 0.45 * abs({glottalReflection}));");
        source.AppendLine($"{name}_tract_raw = {name}_tract_excitation + {name}_tract_injection_pressure;");
        if (tract.Propagation == TractPropagationMode.Waveguide && areaFunction is not null)
        {
            return TractWaveguideExpression(
                source,
                tract,
                areaFunction,
                name,
                glottalReflection,
                lipReflection,
                velum,
                injectionWidth,
                tongueIndex,
                tongueDiameter,
                constrictionIndex,
                constrictionDiameter,
                lipOpening,
                shapeReturn,
                motion is not null && (
                    parameters.IsBound(tongueIndexPath) ||
                    parameters.IsBound(tongueDiameterPath) ||
                    parameters.IsBound(constrictionIndexPath) ||
                    parameters.IsBound(constrictionDiameterPath) ||
                    parameters.IsBound(lipOpeningPath)));
        }

        source.AppendLine($"{name}_tract_oral = {name}_tract_raw * 0.18 + ({name}_tract_raw : fi.resonbp({name}_tract_f1, {name}_tract_q, 1.0)) * (0.75 + {name}_tract_lip * 0.65) + ({name}_tract_raw : fi.resonbp({name}_tract_f2, {name}_tract_q, 1.0)) * (0.85 + {name}_tract_tongue_close) + ({name}_tract_raw : fi.resonbp({name}_tract_f3, {name}_tract_q, 1.0)) * (0.35 + {name}_tract_constriction_close);");
        if (hasNose)
        {
            source.AppendLine($"{name}_tract_nose = ({name}_tract_raw : fi.resonbp(260.0 + {velum} * 560.0, 3.0 + {velum} * 9.0, 1.0) : fi.lowpass(2, 2400.0 + {velum} * 1200.0)) * {velum};");
        }
        else
        {
            source.AppendLine($"{name}_tract_nose = 0.0;");
        }
        source.AppendLine($"{name}_tract_radiated = ({name}_tract_oral * (0.65 + 0.35 * abs({lipReflection})) + {name}_tract_nose);");
        return $"{name}_tract_radiated";

        string ScaleIndex(string expression) =>
            tract.IndexScale == 1 ? expression : $"(({expression}) * {indexScale})";
    }

    private static string SmoothControl(TractMotion? motion, ParameterMap parameters, string fieldPath, string rate, string expression) =>
        motion is not null && parameters.IsBound(fieldPath) ? $"slew({rate}, {expression})" : expression;

    private static string TractWaveguideExpression(
        StringBuilder source,
        VocalTract tract,
        TractAreaFunction areaFunction,
        string name,
        string glottalReflection,
        string lipReflection,
        string velum,
        string injectionWidth,
        string tongueIndex,
        string tongueDiameter,
        string constrictionIndex,
        string constrictionDiameter,
        string lipOpening,
        string shapeReturn,
        bool smoothShape)
    {
        var sections = areaFunction.Sections;
        var loss = F(Math.Clamp(tract.WaveguideLoss, 0, 1));
        var substeps = Math.Max(1, tract.Substeps);
        source.AppendLine($"{name}_wg_loss = {loss};");
        source.AppendLine($"{name}_wg_substeps = {substeps};");
        source.AppendLine($"{name}_wg_substep_drive = 1.0 / {name}_wg_substeps;");
        source.AppendLine($"{name}_wg_substep_loss = pow({name}_wg_loss, {name}_wg_substeps);");
        source.AppendLine($"{name}_wg_injection_cell = {name}_tract_injection_pos * {F(sections)};");
        for (var i = 0; i < sections; i++)
        {
            var rest = F(areaFunction.Diameters[i]);
            var tongueWidth = F(Math.Max(1, sections * 0.18));
            var constrictionWidth = F(Math.Max(1, sections * 0.09));
            if (smoothShape)
            {
                source.AppendLine($"{name}_wg_tongue_weight_{i} = exp(0.0 - pow(({F(i)} - {tongueIndex}) / {tongueWidth}, 2.0));");
                source.AppendLine($"{name}_wg_constriction_weight_{i} = exp(0.0 - pow(({F(i)} - {constrictionIndex}) / {constrictionWidth}, 2.0));");
                source.AppendLine(i == sections - 1
                    ? $"{name}_wg_diameter_base_{i} = {lipOpening};"
                    : $"{name}_wg_diameter_base_{i} = {rest} + ({tongueDiameter} - {rest}) * {name}_wg_tongue_weight_{i};");
                source.AppendLine($"{name}_wg_diameter_target_{i} = max(0.0, min({name}_wg_diameter_base_{i}, {constrictionDiameter} + max(0.0, {name}_wg_diameter_base_{i} - {constrictionDiameter}) * (1.0 - {name}_wg_constriction_weight_{i})));");
            }
            else
            {
                source.AppendLine($"{name}_wg_diameter_target_{i} = {F(StaticWaveguideDiameter(tract, areaFunction, i, sections))};");
            }
            source.AppendLine(smoothShape
                ? $"{name}_wg_diameter_{i} = slew({shapeReturn}, {name}_wg_diameter_target_{i});"
                : $"{name}_wg_diameter_{i} = {name}_wg_diameter_target_{i};");
            source.AppendLine($"{name}_wg_area_{i} = max(0.000001, {name}_wg_diameter_{i} * {name}_wg_diameter_{i});");
            source.AppendLine($"{name}_wg_inject_{i} = {name}_tract_injection_pressure * {name}_wg_substep_drive * clip01(1.0 - abs({name}_wg_injection_cell - {F(i)}) / {injectionWidth});");
        }
        for (var i = 1; i < sections; i++)
        {
            source.AppendLine($"{name}_wg_k_{i} = ({name}_wg_area_{i - 1} - {name}_wg_area_{i}) / ({name}_wg_area_{i - 1} + {name}_wg_area_{i});");
        }

        var nasal = tract.Nasal is { AreaFunction: { } } ? tract.Nasal : null;
        var noseSections = nasal?.AreaFunction is { } nasalFunction ? nasalFunction.Sections : 0;
        var nasalJunction = nasal is null ? -1 : Math.Clamp(nasal.JunctionIndex, 1, sections - 2);
        var stateCount = sections * 2 + noseSections * 2;
        var stateInputs = new List<string>(stateCount);
        stateInputs.AddRange(Enumerable.Range(0, sections).Select(i => $"r{i}"));
        stateInputs.AddRange(Enumerable.Range(0, sections).Select(i => $"l{i}"));
        stateInputs.AddRange(Enumerable.Range(0, noseSections).Select(i => $"nr{i}"));
        stateInputs.AddRange(Enumerable.Range(0, noseSections).Select(i => $"nl{i}"));
        var nextStates = new List<string>(stateCount);

        source.AppendLine($"{name}_wg(x) = {name}_wg_loop ~ si.bus({stateCount}) : (si.block({stateCount}), _) with {{");
        source.AppendLine($"  {name}_wg_loop({string.Join(", ", stateInputs)}) = {string.Join(", ", nextStatesPlaceholder(stateCount))}, {name}_wg_out with {{");
        source.AppendLine($"    {name}_wg_jr0 = (x * {name}_wg_substep_drive + {name}_wg_inject_0 * 0.5 + l0 * {glottalReflection});");
        for (var i = 1; i < sections; i++)
        {
            if (i == nasalJunction)
            {
                continue;
            }

            source.AppendLine($"    {name}_wg_scatter_{i} = {name}_wg_k_{i} * (r{i - 1} + l{i});");
            source.AppendLine($"    {name}_wg_jr{i} = r{i - 1} - {name}_wg_scatter_{i} + {name}_wg_inject_{i} * 0.5;");
            source.AppendLine($"    {name}_wg_jl{i - 1} = l{i} + {name}_wg_scatter_{i} + {name}_wg_inject_{i - 1} * 0.5;");
        }

        source.AppendLine($"    {name}_wg_jl{sections - 1} = r{sections - 1} * {lipReflection};");
        var noseOutput = "0.0";
        if (nasal?.AreaFunction is { } noseShape)
        {
            var junction = nasalJunction;
            var noseReflections = noseShape.ReflectionCoefficients;
            var noseLoss = F(Math.Clamp(nasal.Loss, 0, 1));
            var noseReflection = F(nasal.Reflection);
            source.AppendLine($"    {name}_nose_loss = {noseLoss};");
            for (var i = 0; i < Math.Min(noseReflections.Count, noseSections - 1); i++)
            {
                source.AppendLine($"    {name}_nose_k_{i + 1} = {F(noseReflections[i])};");
            }
            source.AppendLine($"    {name}_nose_area = max(0.000001, (0.01 + {velum} * 0.39) * (0.01 + {velum} * 0.39));");
            source.AppendLine($"    {name}_nose_sum = {name}_wg_area_{junction - 1} + {name}_wg_area_{junction} + {name}_nose_area;");
            source.AppendLine($"    {name}_nose_reflect_left = (2.0 * {name}_wg_area_{junction - 1} - {name}_nose_sum) / {name}_nose_sum;");
            source.AppendLine($"    {name}_nose_reflect_right = (2.0 * {name}_wg_area_{junction} - {name}_nose_sum) / {name}_nose_sum;");
            source.AppendLine($"    {name}_nose_reflect_nose = (2.0 * {name}_nose_area - {name}_nose_sum) / {name}_nose_sum;");
            source.AppendLine($"    {name}_wg_jl{junction - 1} = ({name}_nose_reflect_left * r{junction - 1}) + (1.0 + {name}_nose_reflect_left) * (l{junction} + nl0) + {name}_wg_inject_{junction - 1} * 0.5;");
            source.AppendLine($"    {name}_wg_jr{junction} = ({name}_nose_reflect_right * l{junction}) + (1.0 + {name}_nose_reflect_right) * (r{junction - 1} + nl0) + {name}_wg_inject_{junction} * 0.5;");
            source.AppendLine($"    {name}_nose_jr0 = ({name}_nose_reflect_nose * nl0) + (1.0 + {name}_nose_reflect_nose) * (l{junction} + r{junction - 1});");
            for (var i = 1; i < noseSections; i++)
            {
                source.AppendLine($"    {name}_nose_scatter_{i} = {name}_nose_k_{i} * (nr{i - 1} + nl{i});");
                source.AppendLine($"    {name}_nose_jr{i} = nr{i - 1} - {name}_nose_scatter_{i};");
                source.AppendLine($"    {name}_nose_jl{i - 1} = nl{i} + {name}_nose_scatter_{i};");
            }
            source.AppendLine($"    {name}_nose_jl{noseSections - 1} = nr{noseSections - 1} * {noseReflection};");
            noseOutput = $"{name}_nose_next_r{noseSections - 1}";
        }

        for (var i = 0; i < sections; i++)
        {
            source.AppendLine($"    {name}_next_r{i} = {name}_wg_jr{i} * {name}_wg_substep_loss;");
            source.AppendLine($"    {name}_next_l{i} = {name}_wg_jl{i} * {name}_wg_substep_loss;");
            nextStates.Add($"{name}_next_r{i}");
        }
        nextStates.AddRange(Enumerable.Range(0, sections).Select(i => $"{name}_next_l{i}"));
        if (noseSections > 0)
        {
            for (var i = 0; i < noseSections; i++)
            {
                source.AppendLine($"    {name}_nose_next_r{i} = {name}_nose_jr{i} * {name}_nose_loss;");
                source.AppendLine($"    {name}_nose_next_l{i} = {name}_nose_jl{i} * {name}_nose_loss;");
                nextStates.Add($"{name}_nose_next_r{i}");
            }
            nextStates.AddRange(Enumerable.Range(0, noseSections).Select(i => $"{name}_nose_next_l{i}"));
        }
        source.Replace(string.Join(", ", nextStatesPlaceholder(stateCount)), string.Join(", ", nextStates));
        source.AppendLine($"    {name}_tract_oral_waveguide = {name}_next_r{sections - 1} + {name}_next_l{sections - 1} * 0.05;");
        source.AppendLine($"    {name}_tract_nose_waveguide = {noseOutput};");
        source.AppendLine($"    {name}_wg_out = {name}_tract_oral_waveguide * (0.65 + 0.35 * abs({lipReflection})) + {name}_tract_nose_waveguide;");
        source.AppendLine("  };");
        source.AppendLine("};");
        source.AppendLine($"{name}_tract_radiated = {name}_wg({name}_tract_excitation);");
        return $"{name}_tract_radiated";

        static IEnumerable<string> nextStatesPlaceholder(int count) =>
            Enumerable.Range(0, count).Select(i => $"__next_state_{i}__");
    }

    private static float StaticWaveguideDiameter(VocalTract tract, TractAreaFunction areaFunction, int index, int emittedSections)
    {
        var restIndex = Math.Min(index, areaFunction.Diameters.Count - 1);
        var diameter = areaFunction.Diameters[restIndex];
        if (index == emittedSections - 1)
        {
            diameter = tract.LipOpening;
        }
        else
        {
            var tongueWidth = Math.Max(1, emittedSections * 0.18f);
            var tongueWeight = MathF.Exp(0 - MathF.Pow((index - tract.TongueIndex) / tongueWidth, 2));
            diameter = diameter + (tract.TongueDiameter - diameter) * tongueWeight;
        }

        var constrictionWidth = Math.Max(1, emittedSections * 0.09f);
        var constrictionWeight = MathF.Exp(0 - MathF.Pow((index - tract.ConstrictionIndex) / constrictionWidth, 2));
        diameter = Math.Min(diameter, tract.ConstrictionDiameter + Math.Max(0, diameter - tract.ConstrictionDiameter) * (1 - constrictionWeight));
        return Math.Max(0, diameter);
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

    private static float TerminalArea(IReadOnlyDictionary<string, AcousticPath> paths, AcousticTerminal terminal)
    {
        if (!paths.TryGetValue(terminal.Path, out var path))
        {
            return 1;
        }

        var diameter = path.AreaFunction.DiameterAt(Math.Clamp(terminal.Position, 0, 1));
        return Math.Max(0.000001f, diameter * diameter);
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
        return $"max(1.0, {F(segmentLengthMeters / segment.Path.PropagationSpeedMetersPerSecond)} * ma.SR)";
    }

    private static int SegmentMaxDelay(AcousticGraphSegment segment, WaveClockPolicy waveClock)
    {
        var maxAt44k = (int)MathF.Ceiling(Math.Max(1, (segment.EndPosition - segment.StartPosition) * segment.Path.AreaFunction.LengthMeters / segment.Path.PropagationSpeedMetersPerSecond * 44100) + 4);
        return Math.Max(maxAt44k, Math.Max(1, waveClock.MaxDelaySamples));
    }

    private static string WaveClockDelayExpression(WaveClockPolicy waveClock, int maxDelay, string delayExpression)
    {
        var delay = $"min({F(maxDelay - 1)}, max(1.0, {delayExpression}))";
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

    private static string ConnectionPressureExpression(
        string voiceName,
        AcousticConnection connection,
        IReadOnlyList<(AcousticGraphNode node, AcousticGraphPort port)> ports)
    {
        if (connection.Law == AcousticConnectionLaw.PressureContinuity)
        {
            return $"2.0 * ({string.Join(" + ", ports.Select(item => GraphIncoming(voiceName, item.port)))}) / {F(Math.Max(1, ports.Count))}";
        }

        var weightedIncoming = ports
            .Select(item => $"{voiceName}_graph_node_area_{item.node.Name} * {GraphIncoming(voiceName, item.port)}");
        var areaSum = ports.Select(item => $"{voiceName}_graph_node_area_{item.node.Name}");
        return $"2.0 * ({string.Join(" + ", weightedIncoming)}) / max(0.000001, {string.Join(" + ", areaSum)})";
    }

    private static string NodeSourceExpression(
        string voiceName,
        AcousticGraphNode node,
        IReadOnlyDictionary<string, AcousticSourcePort> sources)
    {
        var sourceTerms = node.Terminals
            .Where(terminal => terminal.Kind == AcousticTerminalKind.Source && sources.ContainsKey(terminal.Port.Length == 0 ? terminal.Name : terminal.Port))
            .Select(terminal => $"{voiceName}_graph_source_{SafeIdentifier(terminal.Port.Length == 0 ? terminal.Name : terminal.Port)}")
            .ToList();
        return sourceTerms.Count == 0 ? "0.0" : string.Join(" + ", sourceTerms);
    }

    private static string GraphIncoming(string voiceName, AcousticGraphPort port) =>
        port.AtStart
            ? $"{voiceName}_graph_in_{port.Segment.Index}_start"
            : $"{voiceName}_graph_in_{port.Segment.Index}_end";

    private static string GraphOutgoing(string voiceName, AcousticGraphPort port) =>
        port.AtStart
            ? $"{voiceName}_graph_out_{port.Segment.Index}_start"
            : $"{voiceName}_graph_out_{port.Segment.Index}_end";

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
        private readonly Dictionary<string, string> _bindings = new(StringComparer.OrdinalIgnoreCase);
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

                _bindings[binding.FieldPath] = binding.ParameterPath;
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
            if (!_bindings.TryGetValue(fieldPath, out var parameterPath))
            {
                return F(fallback);
            }

            return ParameterIdentifier(_parameterIndexes[parameterPath]);
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


