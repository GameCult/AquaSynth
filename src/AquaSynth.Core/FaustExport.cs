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
        var tongueIndex = parameters.Expression(OwnerField(tractPath, "tongue_index"), tract.TongueIndex);
        var tongueDiameter = parameters.Expression(OwnerField(tractPath, "tongue_diameter"), tract.TongueDiameter);
        var velum = $"clip01(({parameters.Expression(OwnerField(tractPath, "velum"), tract.Velum)} - 0.01) / 0.39)";
        var constrictionIndex = parameters.Expression(OwnerField(tractPath, "constriction_index"), tract.ConstrictionIndex);
        var constrictionDiameter = parameters.Expression(OwnerField(tractPath, "constriction_diameter"), tract.ConstrictionDiameter);
        var turbulence = $"clip01({parameters.Expression(OwnerField(tractPath, "turbulence"), tract.Turbulence)})";
        var lipOpening = parameters.Expression(OwnerField(tractPath, "lip_opening"), tract.LipOpening);
        var glottalReflection = parameters.Expression(OwnerField(tractPath, "glottal_reflection"), tract.GlottalReflection);
        var lipReflection = parameters.Expression(OwnerField(tractPath, "lip_reflection"), tract.LipReflection);

        source.AppendLine($"{name}_tract_phase = os.phasor(1.0, {frequency});");
        source.AppendLine($"{name}_tract_tongue_pos = clip01({tongueIndex} / {F(sections)});");
        source.AppendLine($"{name}_tract_constriction_pos = clip01({constrictionIndex} / {F(sections)});");
        source.AppendLine($"{name}_tract_tongue_close = clip01((3.5 - {tongueDiameter}) / 3.5);");
        source.AppendLine($"{name}_tract_constriction_close = clip01((1.15 - {constrictionDiameter}) / 1.15);");
        source.AppendLine($"{name}_tract_lip = clip01({lipOpening} / 2.5);");
        source.AppendLine($"{name}_tract_q = 2.0 + {tenseness} * 10.0 + {name}_tract_constriction_close * 8.0;");
        source.AppendLine($"{name}_tract_f1 = max(90.0, 260.0 + {name}_tract_lip * 720.0 - {name}_tract_tongue_close * 260.0 + {velum} * 120.0);");
        source.AppendLine($"{name}_tract_f2 = max(180.0, 820.0 + {name}_tract_tongue_pos * 1850.0 - {name}_tract_tongue_close * 640.0 - (1.0 - {name}_tract_lip) * 260.0);");
        source.AppendLine($"{name}_tract_f3 = max(500.0, 1900.0 + {name}_tract_constriction_pos * 2600.0 + {name}_tract_tongue_close * 700.0);");
        source.AppendLine($"{name}_tract_lf = (sin(2.0 * ma.PI * {name}_tract_phase) - (0.15 + {tenseness} * 0.45) * sin(4.0 * ma.PI * {name}_tract_phase)) * {intensity} * (0.45 + 0.55 * pow(max(0.0, {tenseness}), 0.25));");
        source.AppendLine($"{name}_tract_aspiration = no.noise * {intensity} * (1.0 - sqrt(max(0.0, {tenseness}))) * (0.02 + 0.16 * {name}_tract_constriction_close);");
        source.AppendLine($"{name}_tract_frication = no.noise * {turbulence} * {name}_tract_constriction_close * {intensity} * (0.2 + 0.8 * {tenseness});");
        source.AppendLine($"{name}_tract_burst = no.noise * {name}_tract_constriction_close * exp(-age * (45.0 + {name}_tract_constriction_close * 120.0)) * 0.18;");
        source.AppendLine($"{name}_tract_raw = ({name}_tract_lf + {name}_tract_aspiration + {name}_tract_frication + {name}_tract_burst) * (0.55 + 0.45 * abs({glottalReflection}));");
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

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

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


