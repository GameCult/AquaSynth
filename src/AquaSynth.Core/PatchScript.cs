using System.Globalization;

namespace AquaSynth.Dsl;

public sealed class PatchScriptException(int line, string message) : Exception($"line {line}: {message}")
{
    public int Line { get; } = line;
}
public static class PatchScript
{
    public static SynthPatch Parse(string script)
    {
        var compiler = new Compiler();
        var line = 1;
        foreach (var statement in PatchScriptStatements.Enumerate(script))
        {
            compiler.Apply(statement.Text, statement.Line);
            line = statement.Line;
        }

        if (!compiler.HasOutput)
        {
            throw new PatchScriptException(line, "patch script produced no voices or operator graphs");
        }

        return compiler.Build();
    }

    private sealed class Compiler
    {
        private readonly Dictionary<string, Dictionary<string, string>> _templates = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ControlLane> _controls = [];
        private readonly List<OperatorGraph> _operatorGraphs = [];
        private readonly List<PatchParameter> _parameters = [];
        private readonly List<ParameterBinding> _parameterBindings = [];
        private readonly List<PatchLayer> _layers = [];
        private readonly List<HarmonicBank> _harmonicBanks = [];
        private readonly List<SpectralBank> _spectralBanks = [];
        private readonly Dictionary<string, string> _defaults = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> _layerDefaults = new(StringComparer.OrdinalIgnoreCase);
        private PendingOperatorGraph? _pendingOperatorGraph;

        public List<Voice> Voices { get; } = [];
        public List<OperatorGraph> OperatorGraphs => _operatorGraphs;
        public bool HasOutput => Voices.Count > 0 || _spectralBanks.Count > 0 || _operatorGraphs.Count > 0 || _pendingOperatorGraph is not null;
        private Repeat? Repeat { get; set; }
        private Playback Playback { get; set; } = new();
        private float Gain { get; set; } = 1;
        private bool SoftClip { get; set; } = true;

        public SynthPatch Build()
        {
            FlushPendingOperatorGraph();
            return new SynthPatch
            {
                Voices = Voices,
                Layers = _layers,
                HarmonicBanks = _harmonicBanks,
                SpectralBanks = _spectralBanks,
                OperatorGraphs = _operatorGraphs,
                Controls = _controls,
                Parameters = _parameters,
                ParameterBindings = _parameterBindings,
                Playback = Playback,
                Repeat = Repeat,
                Gain = Gain,
                SoftClip = SoftClip
            };
        }

        public void Apply(string statement, int line)
        {
            var parts = statement.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var rawCommand = parts[0];
            var command = CanonicalCommand(parts[0]);
            var fields = ParseFields(parts.Skip(1), line);
            if (SfxrParams.Named(rawCommand) is { } namedParams)
            {
                FlushPendingOperatorGraph();
                AddSfxrPatch(ApplySfxrFields(namedParams, fields, line));
                return;
            }

            switch (command)
            {
                case "patch":
                    FlushPendingOperatorGraph();
                    ApplyPatch(fields, line);
                    break;
                case "defaults":
                    FlushPendingOperatorGraph();
                    Merge(_defaults, fields);
                    break;
                case "template":
                    FlushPendingOperatorGraph();
                    var name = Required(fields, "name", line);
                    _templates[name] = Without(fields, "name");
                    break;
                case "layer":
                    FlushPendingOperatorGraph();
                    AddLayer(fields, line);
                    break;
                case "harmonics":
                    FlushPendingOperatorGraph();
                    AddHarmonicBank(fields, line);
                    break;
                case "spectrum":
                    FlushPendingOperatorGraph();
                    AddSpectralBank(fields, line);
                    break;
                case "voice":
                    FlushPendingOperatorGraph();
                    Voices.Add(ParseVoice(ExpandVoiceFields(fields, line), VoicePath(Voices.Count), line));
                    break;
                case "tract":
                    FlushPendingOperatorGraph();
                    Voices.Add(ParseTractVoice(ExpandVoiceFields(fields, line), VoicePath(Voices.Count), line));
                    break;
                case "opgraph":
                    StartOperatorGraph(fields, line);
                    break;
                case "operator":
                    AddOperator(fields, line);
                    break;
                case "route":
                    AddOperatorRoute(fields, line);
                    break;
                case "carrier":
                    AddOperatorCarrier(fields, line);
                    break;
                case "mod":
                    FlushPendingOperatorGraph();
                    AddModBus(fields, line);
                    break;
                case "control":
                    FlushPendingOperatorGraph();
                    AddControlLane(fields, line);
                    break;
                case "param":
                    FlushPendingOperatorGraph();
                    AddParameter(fields, line);
                    break;
                case "sfxr":
                    FlushPendingOperatorGraph();
                    AddSfxrPatch(ParseSfxrCommand(fields, line));
                    break;
                default:
                    throw new PatchScriptException(line, $"unknown command `{parts[0]}`");
            }
        }

        private void AddHarmonicBank(IReadOnlyDictionary<string, string> fields, int line)
        {
            var layerName = Required(fields, "layer", line);
            if (!_layerDefaults.ContainsKey(layerName))
            {
                throw new PatchScriptException(line, $"unknown layer `{layerName}`");
            }

            var rootFrequency = GetBoundFloat(fields, line, 440, $"/harmonics/{_harmonicBanks.Count}/root", "root", "base", "freq", "frequency");
            if (rootFrequency <= 0)
            {
                throw new PatchScriptException(line, "harmonic root frequency must be greater than zero");
            }

            var partials = ParseHarmonicPartials(
                GetAny(fields, ["partials", "bank", "tones", "drawbars"], ""),
                line);
            if (partials.Count == 0)
            {
                throw new PatchScriptException(line, "harmonics needs at least one partial");
            }

            _harmonicBanks.Add(new HarmonicBank(layerName, rootFrequency, partials));

            var sharedFields = Without(fields,
                "layer",
                "root",
                "base",
                "freq",
                "frequency",
                "partials",
                "bank",
                "tones",
                "drawbars",
                "gain",
                "g");
            foreach (var partial in partials)
            {
                var voiceFields = new Dictionary<string, string>(sharedFields, StringComparer.OrdinalIgnoreCase)
                {
                    ["layer"] = layerName,
                    ["freq"] = F(rootFrequency * partial.Ratio),
                    ["gain"] = F(partial.Gain)
                };
                Voices.Add(ParseVoice(ExpandVoiceFields(voiceFields, line), VoicePath(Voices.Count), line));
            }
        }

        private void AddSpectralBank(IReadOnlyDictionary<string, string> fields, int line)
        {
            var layerName = Required(fields, "layer", line);
            if (!_layerDefaults.ContainsKey(layerName))
            {
                throw new PatchScriptException(line, $"unknown layer `{layerName}`");
            }

            var rootFrequency = GetFloat(fields, line, 440, "root", "base", "basefreq", "table_freq", "table_frequency");
            if (rootFrequency <= 0)
            {
                throw new PatchScriptException(line, "spectral root frequency must be greater than zero");
            }

            var spread = GetFloat(fields, line, 0, "spread", "width", "detune");
            if (spread is < 0 or >= 1)
            {
                throw new PatchScriptException(line, "spectral spread must be at least zero and less than one");
            }
            var profile = ParsePadSpectrumProfile(fields, line);

            var partials = ParseHarmonicPartials(
                GetAny(fields, ["partials", "bank", "tones"], ""),
                line);
            if (partials.Count == 0)
            {
                throw new PatchScriptException(line, "spectrum needs at least one partial");
            }

            var treatmentFields = Without(fields,
                "layer",
                "root",
                "base",
                "basefreq",
                "table_freq",
                "table_frequency",
                "spread",
                "width",
                "detune",
                "pad_mode",
                "zyn_mode",
                "pad_bandwidth",
                "zyn_bandwidth",
                "bandwidth",
                "pad_bwscale",
                "zyn_bwscale",
                "bwscale",
                "pad_profile",
                "zyn_profile",
                "pad_position",
                "zyn_position",
                "partials",
                "bank",
                "tones");
            treatmentFields["layer"] = layerName;
            if (!HasAny(treatmentFields, "freq", "frequency", "f"))
            {
                treatmentFields["freq"] = F(rootFrequency);
            }
            var treatment = ParseVoice(
                ExpandVoiceFields(treatmentFields, line),
                SpectralPath(_spectralBanks.Count),
                line);
            _spectralBanks.Add(new SpectralBank(layerName, rootFrequency, spread, partials, treatment, profile));
        }

        private static string SpectralPath(int spectralIndex) => $"/spectral/{spectralIndex}";

        private Dictionary<string, string> ExpandVoiceFields(Dictionary<string, string> fields, int line)
        {
            var expanded = new Dictionary<string, string>(_defaults, StringComparer.OrdinalIgnoreCase);
            if (TryGetAny(fields, ["layer"], out var layerName))
            {
                if (!_layerDefaults.TryGetValue(layerName, out var layer))
                {
                    throw new PatchScriptException(line, $"unknown layer `{layerName}`");
                }
                Merge(expanded, layer);
            }
            if (TryGetAny(fields, ["use", "u"], out var templateName))
            {
                foreach (var name in templateName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!_templates.TryGetValue(name, out var template))
                    {
                        throw new PatchScriptException(line, $"unknown template `{name}`");
                    }
                    Merge(expanded, template);
                }
            }
            Merge(expanded, Without(fields, "use", "u"));
            return expanded;
        }

        private void AddLayer(IReadOnlyDictionary<string, string> fields, int line)
        {
            var name = Required(fields, "name", line);
            if (_layers.Any(layer => layer.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new PatchScriptException(line, $"duplicate layer `{name}`");
            }

            var layer = new PatchLayer(
                name,
                GetAny(fields, ["engine", "e"], ""),
                TryGetAny(fields, ["min_key", "key_min", "lo"], out var minKey) ? ParseInt(minKey, line) : null,
                TryGetAny(fields, ["max_key", "key_max", "hi"], out var maxKey) ? ParseInt(maxKey, line) : null,
                GetBoundFloat(fields, line, 1, $"/layers/{_layers.Count}/gain", "gain", "g"),
                GetAny(fields, ["send", "effect", "fx"], ""));
            if (layer is { MinKey: { } min, MaxKey: { } max } && min > max)
            {
                throw new PatchScriptException(line, "layer min_key must be less than or equal to max_key");
            }

            _layers.Add(layer);
            _layerDefaults[name] = LayerVoiceFields(layer, Without(fields, "name", "engine", "e", "min_key", "key_min", "lo", "max_key", "key_max", "hi", "send", "effect", "fx"));
        }

        private static Dictionary<string, string> LayerVoiceFields(PatchLayer layer, IReadOnlyDictionary<string, string> fields)
        {
            var result = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase)
            {
                ["layer"] = layer.Name
            };
            if (!result.ContainsKey("gain"))
            {
                result["gain"] = F(layer.Gain);
            }
            return result;
        }

        private void ApplyPatch(IReadOnlyDictionary<string, string> fields, int line)
        {
            if (TryGetAny(fields, ["gain", "g"], out var gain)) Gain = ParseBoundFloat(gain, line, Gain, "/patch/gain");
            if (TryGetAny(fields, ["soft_clip", "clip"], out var softClip)) SoftClip = ParseBool(softClip, line);
            if (TryGetAny(fields, ["mode", "playback"], out var mode))
            {
                var playbackMode = ParsePlaybackMode(mode, line);
                Playback = Playback with
                {
                    Mode = playbackMode,
                    Midi = playbackMode == PlaybackMode.Poly || Playback.Midi
                };
            }
            if (TryGetAny(fields, ["polyphony", "voices", "nvoices"], out var voices))
            {
                var count = ParseInt(voices, line);
                if (count < 1) throw new PatchScriptException(line, "polyphony must be at least 1");
                Playback = Playback with
                {
                    Voices = count,
                    Mode = count > 1 ? PlaybackMode.Poly : Playback.Mode,
                    Midi = count > 1 || Playback.Midi
                };
            }
            if (TryGetAny(fields, ["midi"], out var midi))
            {
                var enabled = ParseBool(midi, line);
                Playback = Playback with
                {
                    Midi = enabled,
                    Mode = enabled && Playback.Mode == PlaybackMode.OneShot ? PlaybackMode.Mono : Playback.Mode
                };
            }
            if (TryGetAny(fields, ["note_freq", "note_frequency"], out var noteFreq)) Playback = Playback with { FrequencyHz = ParseFloat(noteFreq, line) };
            if (TryGetAny(fields, ["note_gain", "velocity"], out var noteGain)) Playback = Playback with { Gain = ParseFloat(noteGain, line) };
            if (TryGetAny(fields, ["repeat", "r", "rp"], out var repeat))
            {
                var interval = ParseBoundFloat(repeat, line, 0.1f, "/patch/repeat");
                Repeat = interval > 0 ? new Repeat(interval) : null;
            }
        }

        private Voice ParseVoice(IReadOnlyDictionary<string, string> fields, string ownerPath, int line)
        {
            var waveform = TryGetAny(fields, ["wave", "w"], out var wave) ? ParseWaveform(wave, line) : Waveform.Sine;
            var frequency = GetBoundFloat(fields, line, 440, OwnerField(ownerPath, "note/frequency"), "freq", "frequency", "f");
            var envelopeSpec = TryGetAny(fields, ["env", "envelope"], out var envSpec)
                ? ParseEnvelopeSpec(envSpec, fields, line, ownerPath)
                : null;
            var gateSeconds = envelopeSpec?.GateSeconds ??
                              GetBoundFloat(fields, line, 0.1f, OwnerField(ownerPath, "note/gate"), "gate", "hold", "duration", "sustain", "s");
            var sustainLevel = envelopeSpec?.Envelope.SustainLevel ??
                               GetBoundFloat(fields, line, 1, OwnerField(ownerPath, "env/sustain_level"), "sustain_level", "sl");
            var gainScale = 1f;
            if (TryGetAny(fields, ["punch", "pu"], out var punch))
            {
                gainScale = PunchGain(ParseBoundFloat(punch, line, 0, OwnerField(ownerPath, "env/sustain_level")));
                sustainLevel = 1 / gainScale;
            }
            var noteSource = ParseNoteSource(GetAny(fields, ["note_source", "source"], "oneshot"), line);
            if (TryGetAny(fields, ["midi"], out var midi) && ParseBool(midi, line))
            {
                noteSource = NoteSource.Host;
                Playback = Playback with
                {
                    Midi = true,
                    Mode = Playback.Mode == PlaybackMode.OneShot ? PlaybackMode.Mono : Playback.Mode,
                    FrequencyHz = frequency
                };
            }
            var formants = TryGetAny(fields, ["formants", "fs"], out var formantSpec)
                ? ParseFormants(formantSpec, line)
                : [];
            var formantFrames = TryGetAny(fields, ["vowels", "vowel_frames", "formant_frames"], out var vowelSpec)
                ? ParseFormantFrames(vowelSpec, line)
                : [];

            var modulators = TryGetAny(fields, ["mods", "m"], out var mods)
                ? ParseVoiceModulators(mods, line)
                : [];

            Arpeggio? arpeggio = null;
            var hasArpDelay = TryGetAny(fields, ["arp_delay", "ad"], out var arpDelay);
            var hasArpMult = TryGetAny(fields, ["arp_mult", "am"], out var arpMult);
            if (hasArpDelay || hasArpMult)
            {
                arpeggio = new Arpeggio(
                    ParseBoundFloat(arpDelay ?? throw new PatchScriptException(line, "arpeggio needs arp_delay"), line, 0, OwnerField(ownerPath, "arpeggio/delay")),
                    ParseBoundFloat(arpMult ?? throw new PatchScriptException(line, "arpeggio needs arp_mult"), line, 1, OwnerField(ownerPath, "arpeggio/multiplier")));
            }

            return new Voice
            {
                Layer = TryGetAny(fields, ["layer"], out var layerName)
                    ? _layers.First(layer => layer.Name.Equals(layerName, StringComparison.OrdinalIgnoreCase))
                    : null,
                Oscillator = new Oscillator(
                    waveform,
                    frequency,
                    GetBoundFloat(fields, line, 0.5f, OwnerField(ownerPath, "osc/duty"), "duty", "du"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "osc/phase"), "phase", "pa")),
                Note = new Note(frequency, gateSeconds, noteSource),
                Envelope = envelopeSpec?.Envelope ?? new Envelope(
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "env/attack"), "attack", "a"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "env/decay"), "env_decay", "ed"),
                    sustainLevel,
                    GetBoundFloat(fields, line, 0.1f, OwnerField(ownerPath, "env/release"), "release", "rel", "decay", "d")),
                RateLevelEnvelope = envelopeSpec?.RateLevelEnvelope,
                Pitch = new PitchMotion(
                    GetBoundFloat(fields, line, 20, OwnerField(ownerPath, "pitch/min_freq"), "min_freq", "min"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "pitch/ramp"), "pitch_ramp", "pr"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "pitch/delta"), "pitch_delta", "pd", "pitch_dramp", "pdr"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "pitch/vibrato"), "vibrato", "vi"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "pitch/vibrato_hz"), "vibrato_hz", "vh"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "pitch/vibrato_delay"), "vibrato_delay", "vd")),
                Duty = new DutyMotion(GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "duty/ramp"), "duty_ramp", "dur")),
                Filter = new Filter(
                    GetBoundFloat(fields, line, 1, OwnerField(ownerPath, "filter/lpf"), "lpf", "l"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "filter/lpf_ramp"), "lpf_ramp", "lr"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "filter/resonance"), "resonance", "res"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "filter/lpf_q"), "lpf_q", "lpq"),
                    GetBoundInt(fields, line, 1, OwnerField(ownerPath, "filter/lpf_order"), "lpf_order", "lpfo"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "filter/hpf"), "hpf", "h"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "filter/hpf_ramp"), "hpf_ramp", "hr"),
                    GetBoundInt(fields, line, 1, OwnerField(ownerPath, "filter/hpf_order"), "hpf_order", "hpfo"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "filter/bpf"), "bpf", "bp"),
                    GetBoundFloat(fields, line, 1, OwnerField(ownerPath, "filter/bpf_q"), "bpf_q", "bpq"),
                    GetBoundInt(fields, line, 1, OwnerField(ownerPath, "filter/bpf_order"), "bpf_order", "bpfo"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "filter/notch"), "notch", "nt"),
                    GetBoundFloat(fields, line, 1, OwnerField(ownerPath, "filter/notch_q"), "notch_q", "ntq"),
                    GetBoundInt(fields, line, 1, OwnerField(ownerPath, "filter/notch_order"), "notch_order", "nto"),
                    ParseFilterRateLevelEnvelope(fields, line, ownerPath, "lpf"),
                    ParseFilterRateLevelEnvelope(fields, line, ownerPath, "hpf")),
                Phaser = new Phaser(
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "phaser/offset"), "phaser", "ph"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "phaser/ramp"), "phaser_ramp", "phr")),
                Arpeggio = arpeggio,
                Fm = new FrequencyModulation(
                    GetBoundFloat(fields, line, 1, OwnerField(ownerPath, "fm/ratio"), "fm", "fmr", "fm_ratio"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "fm/index"), "fm_index", "fmi"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "fm/decay"), "fm_decay", "fmd")),
                Color = new VoiceColor(
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "color/noise"), "noise", "nz"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "color/drive"), "drive", "drv"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "color/fold"), "fold", "fl"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "color/tremolo"), "tremolo", "tr"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "color/tremolo_hz"), "tremolo_hz", "th"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "color/formant_mix"), "formant_mix", "fmix")),
                Formants = formants,
                FormantFrames = formantFrames,
                FormantFrameRateHz = GetBoundFloat(fields, line, 0.5f, OwnerField(ownerPath, "color/vowel_rate"), "vowel_hz", "vowel_rate", "vowels_hz"),
                Modulators = modulators,
                Gain = GetBoundFloat(fields, line, 0.2f, OwnerField(ownerPath, "gain"), "gain", "g") * gainScale
            };
        }

        private Voice ParseTractVoice(IReadOnlyDictionary<string, string> fields, string ownerPath, int line)
        {
            var voiceFields = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase);
            if (!HasAny(voiceFields, "wave", "w"))
            {
                voiceFields["wave"] = "saw";
            }
            if (!HasAny(voiceFields, "gain", "g"))
            {
                voiceFields["gain"] = "0.6";
            }
            if (!HasAny(voiceFields, "sustain", "s", "gate", "hold", "duration"))
            {
                voiceFields["sustain"] = "0.35";
            }
            if (!HasAny(voiceFields, "decay", "d", "release", "rel"))
            {
                voiceFields["decay"] = "0.12";
            }

            var voice = ParseVoice(voiceFields, ownerPath, line);
            var sections = GetBoundInt(fields, line, 44, OwnerField(ownerPath, "tract/sections"), "sections", "cells");
            if (sections < 4)
            {
                throw new PatchScriptException(line, "tract sections must be at least 4");
            }
            var noseSections = GetBoundInt(fields, line, 28, OwnerField(ownerPath, "tract/nose_sections"), "nose_sections", "nose_cells");
            if (noseSections < 0)
            {
                throw new PatchScriptException(line, "tract nose_sections must be non-negative");
            }

            return voice with
            {
                Tract = new VocalTract(
                    sections,
                    noseSections,
                    GetBoundFloat(fields, line, 0.72f, OwnerField(ownerPath, "tract/intensity"), "intensity", "pressure"),
                    GetBoundFloat(fields, line, 0.6f, OwnerField(ownerPath, "tract/tenseness"), "tenseness", "tense"),
                    GetBoundFloat(fields, line, 12.9f, OwnerField(ownerPath, "tract/tongue_index"), "tongue_index", "tongue", "ti"),
                    GetBoundFloat(fields, line, 2.43f, OwnerField(ownerPath, "tract/tongue_diameter"), "tongue_diameter", "td"),
                    GetBoundFloat(fields, line, 0.01f, OwnerField(ownerPath, "tract/velum"), "velum", "nose", "nasal"),
                    GetBoundFloat(fields, line, 32, OwnerField(ownerPath, "tract/constriction_index"), "constriction_index", "ci"),
                    GetBoundFloat(fields, line, 1, OwnerField(ownerPath, "tract/constriction_diameter"), "constriction_diameter", "cd"),
                    GetBoundFloat(fields, line, 0, OwnerField(ownerPath, "tract/turbulence"), "turbulence", "frication"),
                    GetBoundFloat(fields, line, 1.5f, OwnerField(ownerPath, "tract/lip_opening"), "lip", "lip_opening", "mouth"),
                    GetBoundFloat(fields, line, 0.75f, OwnerField(ownerPath, "tract/glottal_reflection"), "glottal_reflection", "gr"),
                    GetBoundFloat(fields, line, -0.85f, OwnerField(ownerPath, "tract/lip_reflection"), "lip_reflection", "lr"))
            };
        }

        private void AddModBus(IReadOnlyDictionary<string, string> fields, int line)
        {
            var name = GetAny(fields, ["name", "n"], "mod");
            var wave = TryGetAny(fields, ["wave", "w"], out var waveform)
                ? ParseModWaveform(waveform, line)
                : ModWaveform.Sine;
            var hz = GetFloat(fields, line, 1, "hz", "rate");
            var phase = GetFloat(fields, line, 0, "phase");

            if (TryGetAny(fields, ["to", "targets"], out var routeSpec))
            {
                foreach (var route in ParseRoutes(routeSpec, line))
                {
                    _controls.Add(new ControlLane(
                        $"{name}_{TargetSuffix(route.Target)}",
                        new Modulator(route.Target, wave, hz, route.Depth, phase)));
                }
            }

            foreach (var (key, target) in ModTargets)
            {
                if (!TryGetAny(fields, key, out var depthText)) continue;
                var depth = ParseFloat(depthText, line);
                _controls.Add(new ControlLane($"{name}_{key[0]}", new Modulator(target, wave, hz, depth, phase)));
            }
        }

        private void AddControlLane(IReadOnlyDictionary<string, string> fields, int line)
        {
            var name = GetAny(fields, ["name", "n"], "control");
            if (!TryGetAny(fields, ["target", "t"], out var targetText))
            {
                throw new PatchScriptException(line, "control lane needs target");
            }

            var wave = TryGetAny(fields, ["wave", "w"], out var waveform)
                ? ParseModWaveform(waveform, line)
                : ModWaveform.Sine;

            _controls.Add(new ControlLane(
                name,
                new Modulator(
                    ParseModTarget(targetText, line),
                    wave,
                    GetFloat(fields, line, 1, "hz", "rate"),
                    GetFloat(fields, line, 0, "depth", "d", "decay"),
                    GetFloat(fields, line, 0, "phase", "ph"),
                    GetFloat(fields, line, 0, "bias", "b"))));
        }

        private void AddOperatorGraph(IReadOnlyDictionary<string, string> fields, int line)
        {
            var graphIndex = _operatorGraphs.Count;
            var graphPath = $"/opgraphs/{graphIndex}";
            var operators = ParseOperatorNodes(Required(fields, "ops", line), line);
            var edges = TryGetAny(fields, ["edges", "e"], out var edgeSpec)
                ? ParseOperatorEdges(edgeSpec, line)
                : [];
            var carriers = TryGetAny(fields, ["carriers", "c"], out var carrierSpec)
                ? ParseOperatorIds(carrierSpec, line)
                : operators.Select(op => op.Id).ToList();

            AddValidatedOperatorGraph(line, new OperatorGraph(
                Name: GetAny(fields, ["name", "n"], $"opgraph{graphIndex}"),
                FrequencyHz: GetBoundFloat(fields, line, 440, $"{graphPath}/freq", "freq", "frequency", "f"),
                Operators: operators,
                Edges: edges,
                Carriers: carriers,
                Note: new Note(
                    GetBoundFloat(fields, line, 440, $"{graphPath}/note/frequency", "freq", "frequency", "f"),
                    GetBoundFloat(fields, line, 0.1f, $"{graphPath}/note/gate", "gate", "hold", "duration"),
                    ParseNoteSource(GetAny(fields, ["note_source", "source"], "oneshot"), line)),
                Gain: GetBoundFloat(fields, line, 0.2f, $"{graphPath}/gain", "gain", "g"),
                VibratoDepth: GetBoundFloat(fields, line, 0, $"{graphPath}/pitch/vibrato", "vibrato", "vib"),
                VibratoHz: GetBoundFloat(fields, line, 0, $"{graphPath}/pitch/vibrato_hz", "vibrato_hz", "vib_hz"),
                VibratoDelaySeconds: GetBoundFloat(fields, line, 0, $"{graphPath}/pitch/vibrato_delay", "vibrato_delay", "vib_delay")));
        }

        private void StartOperatorGraph(IReadOnlyDictionary<string, string> fields, int line)
        {
            FlushPendingOperatorGraph();
            if (fields.ContainsKey("ops"))
            {
                AddOperatorGraph(fields, line);
                return;
            }

            var graphIndex = _operatorGraphs.Count;
            var graphPath = $"/opgraphs/{graphIndex}";
            _pendingOperatorGraph = new PendingOperatorGraph(
                line,
                graphPath,
                GetAny(fields, ["name", "n"], $"opgraph{graphIndex}"),
                GetBoundFloat(fields, line, 440, $"{graphPath}/freq", "freq", "frequency", "f"),
                new Note(
                    GetBoundFloat(fields, line, 440, $"{graphPath}/note/frequency", "freq", "frequency", "f"),
                    GetBoundFloat(fields, line, 0.1f, $"{graphPath}/note/gate", "gate", "hold", "duration"),
                    ParseNoteSource(GetAny(fields, ["note_source", "source"], "oneshot"), line)),
                GetBoundFloat(fields, line, 0.2f, $"{graphPath}/gain", "gain", "g"),
                GetBoundFloat(fields, line, 0, $"{graphPath}/pitch/vibrato", "vibrato", "vib"),
                GetBoundFloat(fields, line, 0, $"{graphPath}/pitch/vibrato_hz", "vibrato_hz", "vib_hz"),
                GetBoundFloat(fields, line, 0, $"{graphPath}/pitch/vibrato_delay", "vibrato_delay", "vib_delay"));
        }

        private void AddOperator(IReadOnlyDictionary<string, string> fields, int line)
        {
            var graph = RequiredPendingOperatorGraph(line);
            var id = ParseOperatorId(Required(fields, "name", line), line);
            var operatorPath = $"{graph.Path}/operators/{id}";
            var envelopeSpec = TryGetAny(fields, ["env", "envelope"], out var envSpec)
                ? ParseEnvelopeSpec(envSpec, fields, line, operatorPath)
                : new ParsedEnvelope(
                    new Envelope(
                        GetBoundFloat(fields, line, 0, $"{operatorPath}/env/attack", "attack", "a"),
                        GetBoundFloat(fields, line, 0, $"{operatorPath}/env/decay", "env_decay", "ed"),
                        GetBoundFloat(fields, line, 1, $"{operatorPath}/env/sustain_level", "sustain_level", "sl"),
                        GetBoundFloat(fields, line, 0.1f, $"{operatorPath}/env/release", "release", "rel", "decay", "d")),
                    GetBoundFloat(fields, line, graph.Note.GateSeconds, $"{operatorPath}/note/gate", "gate", "hold", "duration"));

            graph.Operators.Add(new OperatorNode(
                Id: id,
                Ratio: GetBoundFloat(fields, line, 1, $"{operatorPath}/ratio", "ratio", "r"),
                Level: GetBoundFloat(fields, line, 1, $"{operatorPath}/level", "level", "l"),
                Feedback: GetBoundFloat(fields, line, 0, $"{operatorPath}/feedback", "feedback", "fb"),
                Note: graph.Note with { GateSeconds = envelopeSpec.GateSeconds },
                Envelope: envelopeSpec.Envelope,
                RateLevelEnvelope: envelopeSpec.RateLevelEnvelope));
        }

        private void AddOperatorRoute(IReadOnlyDictionary<string, string> fields, int line)
        {
            var graph = RequiredPendingOperatorGraph(line);
            var source = ParseOperatorId(Required(fields, "from", line), line);
            var target = ParseOperatorId(Required(fields, "to", line), line);
            graph.Edges.Add(new OperatorEdge(source, target, GetBoundFloat(fields, line, 1, $"{graph.Path}/routes/{source}>{target}/index", "index", "amount", "depth")));
        }

        private void AddOperatorCarrier(IReadOnlyDictionary<string, string> fields, int line)
        {
            var graph = RequiredPendingOperatorGraph(line);
            graph.Carriers.Add(ParseOperatorId(Required(fields, "name", line), line));
        }

        private PendingOperatorGraph RequiredPendingOperatorGraph(int line) =>
            _pendingOperatorGraph ?? throw new PatchScriptException(line, "operator graph command needs an active opgraph");

        private void FlushPendingOperatorGraph()
        {
            if (_pendingOperatorGraph is null) return;
            var graph = _pendingOperatorGraph;
            _pendingOperatorGraph = null;
            AddValidatedOperatorGraph(graph.Line, new OperatorGraph(
                graph.Name,
                graph.FrequencyHz,
                graph.Operators,
                graph.Edges,
                graph.Carriers.Count > 0 ? graph.Carriers : graph.Operators.Select(op => op.Id).ToList(),
                graph.Note,
                graph.Gain,
                graph.VibratoDepth,
                graph.VibratoHz,
                graph.VibratoDelaySeconds));
        }

        private void AddValidatedOperatorGraph(int line, OperatorGraph graph)
        {
            if (graph.Operators.Count == 0)
            {
                throw new PatchScriptException(line, "operator graph needs at least one operator");
            }

            var operatorIds = graph.Operators.Select(op => op.Id).ToHashSet();
            foreach (var edge in graph.Edges)
            {
                if (!operatorIds.Contains(edge.SourceId) || !operatorIds.Contains(edge.TargetId))
                {
                    throw new PatchScriptException(line, $"operator edge `{edge.SourceId}>{edge.TargetId}` references an unknown operator");
                }
            }
            foreach (var carrier in graph.Carriers)
            {
                if (!operatorIds.Contains(carrier))
                {
                    throw new PatchScriptException(line, $"carrier `{carrier}` references an unknown operator");
                }
            }

            _operatorGraphs.Add(graph);
        }

        private void AddParameter(IReadOnlyDictionary<string, string> fields, int line)
        {
            var path = Required(fields, "path", line);
            if (!path.StartsWith('/'))
            {
                throw new PatchScriptException(line, "parameter path must start with `/`");
            }
            if (_parameters.Any(parameter => parameter.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                throw new PatchScriptException(line, $"duplicate parameter path `{path}`");
            }

            var label = TryGetAny(fields, ["label", "name", "n"], out var labelText)
                ? labelText
                : path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;
            var min = GetFloat(fields, line, 0, "min");
            var max = GetFloat(fields, line, 1, "max");
            var step = GetFloat(fields, line, 0.001f, "step");
            var fallbackDefault = Math.Clamp(0.5f, Math.Min(min, max), Math.Max(min, max));
            var defaultValue = GetFloat(fields, line, fallbackDefault, "default", "value", "v");
            if (max < min)
            {
                throw new PatchScriptException(line, "parameter max must be greater than or equal to min");
            }
            if (step < 0)
            {
                throw new PatchScriptException(line, "parameter step must be non-negative");
            }
            if (defaultValue < min || defaultValue > max)
            {
                throw new PatchScriptException(line, "parameter default must be inside min/max");
            }

            _parameters.Add(new PatchParameter(
                path,
                label,
                defaultValue,
                min,
                max,
                step,
                GetAny(fields, ["unit"], ""),
                GetAny(fields, ["rate", "automation", "automation_rate"], "control"),
                GetAny(fields, ["notes", "note"], "")));
        }

        private float GetBoundFloat(
            IReadOnlyDictionary<string, string> fields,
            int line,
            float fallback,
            string fieldPath,
            params string[] keys) =>
            TryGetAny(fields, keys, out var value) ? ParseBoundFloat(value, line, fallback, fieldPath) : fallback;

        private int GetBoundInt(
            IReadOnlyDictionary<string, string> fields,
            int line,
            int fallback,
            string fieldPath,
            params string[] keys)
        {
            if (!TryGetAny(fields, keys, out var value))
            {
                return fallback;
            }
            if (value.StartsWith('@'))
            {
                throw new PatchScriptException(line, $"parameter binding is not supported for integer field `{fieldPath}`");
            }
            return ParseInt(value, line);
        }

        private float ParseBoundFloat(string value, int line, float fallback, string fieldPath)
        {
            if (!value.StartsWith('@'))
            {
                return ParseFloat(value, line);
            }

            var parameterPath = value[1..];
            if (!parameterPath.StartsWith('/'))
            {
                throw new PatchScriptException(line, "parameter reference must use `@/path`");
            }
            var parameter = _parameters.FirstOrDefault(candidate => candidate.Path.Equals(parameterPath, StringComparison.OrdinalIgnoreCase));
            if (parameter is null)
            {
                throw new PatchScriptException(line, $"unknown parameter `{parameterPath}`");
            }
            if (_parameterBindings.Any(binding => binding.FieldPath.Equals(fieldPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new PatchScriptException(line, $"duplicate parameter binding for `{fieldPath}`");
            }

            _parameterBindings.Add(new ParameterBinding(fieldPath, parameter.Path));
            return parameter.Default;
        }

        private static string VoicePath(int voiceIndex) => $"/voices/{voiceIndex}";

        private static string OwnerField(string ownerPath, string field) => $"{ownerPath}/{field}";

        private static List<OperatorNode> ParseOperatorNodes(string value, int line) =>
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part =>
                {
                    var pieces = part.Split(':');
                    if (pieces.Length is < 3 or > 6)
                    {
                        throw new PatchScriptException(line, $"bad operator `{part}`");
                    }

                    return new OperatorNode(
                        Id: ParseInt(pieces[0], line),
                        Ratio: ParseFloat(pieces[1], line),
                        Level: ParseFloat(pieces[2], line),
                        Feedback: pieces.Length >= 4 ? ParseFloat(pieces[3], line) : 0,
                        Note: pieces.Length >= 6 ? new Note(GateSeconds: ParseFloat(pieces[4], line)) : new Note(),
                        Envelope: pieces.Length >= 6 ? new Envelope(ReleaseSeconds: ParseFloat(pieces[5], line)) : new Envelope());
                })
                .ToList();

        private static List<OperatorEdge> ParseOperatorEdges(string value, int line) =>
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part =>
                {
                    var pieces = part.Split(':');
                    if (pieces.Length is < 1 or > 2)
                    {
                        throw new PatchScriptException(line, $"bad operator edge `{part}`");
                    }

                    var nodes = pieces[0].Split('>');
                    if (nodes.Length != 2)
                    {
                        throw new PatchScriptException(line, $"bad operator edge `{part}`");
                    }

                    return new OperatorEdge(
                        ParseInt(nodes[0], line),
                        ParseInt(nodes[1], line),
                        pieces.Length == 2 ? ParseFloat(pieces[1], line) : 1);
                })
                .ToList();

        private static List<int> ParseOperatorIds(string value, int line) =>
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => ParseInt(part, line))
                .ToList();

        private ParsedEnvelope ParseEnvelopeSpec(string value, IReadOnlyDictionary<string, string> fields, int line, string fieldPath)
        {
            var pieces = value.Split(':');
            return pieces[0].ToLowerInvariant() switch
            {
                "ad" when pieces.Length == 3 => AdEnvelope(
                    ParseBoundFloat(pieces[1], line, 0, $"{fieldPath}/env/attack"),
                    ParseBoundFloat(pieces[2], line, 0.1f, $"{fieldPath}/env/decay")),
                "adsr" when pieces.Length == 5 => new ParsedEnvelope(
                    new Envelope(
                        ParseBoundFloat(pieces[1], line, 0, $"{fieldPath}/env/attack"),
                        ParseBoundFloat(pieces[2], line, 0, $"{fieldPath}/env/decay"),
                        ParseBoundFloat(pieces[3], line, 1, $"{fieldPath}/env/sustain_level"),
                        ParseBoundFloat(pieces[4], line, 0.1f, $"{fieldPath}/env/release")),
                    GateSeconds(fields, line, 0.1f, fieldPath)),
                "rl" or "ratelevel" when pieces.Length == 1 => ParseRateLevelEnvelope(fields, line, fieldPath),
                "rl" or "ratelevel" when pieces.Length == 9 => ParseRateLevelEnvelope(pieces, fields, line, fieldPath),
                _ => throw new PatchScriptException(line, $"bad envelope `{value}`")
            };
        }

        private static ParsedEnvelope AdEnvelope(float attackSeconds, float decaySeconds) =>
            new(new Envelope(attackSeconds, decaySeconds, 0, 0), attackSeconds + decaySeconds);

        private static IReadOnlyList<HarmonicPartial> ParseHarmonicPartials(string value, int line)
        {
            if (string.IsNullOrWhiteSpace(value)) return [];

            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part =>
                {
                    var pieces = part.Split(':', 2);
                    if (pieces.Length != 2) throw new PatchScriptException(line, $"bad harmonic partial `{part}`");
                    var ratio = ParseFloat(pieces[0], line);
                    var gain = ParseFloat(pieces[1], line);
                    if (ratio <= 0) throw new PatchScriptException(line, "harmonic partial ratio must be greater than zero");
                    if (gain < 0) throw new PatchScriptException(line, "harmonic partial gain must be zero or greater");
                    return new HarmonicPartial(ratio, gain);
                })
                .ToArray();
        }

        private static PadSpectrumProfile ParsePadSpectrumProfile(IReadOnlyDictionary<string, string> fields, int line)
        {
            var hasPadProfileFields =
                HasAny(fields,
                    "pad_mode", "zyn_mode",
                    "pad_bandwidth", "zyn_bandwidth", "bandwidth",
                    "pad_bwscale", "zyn_bwscale", "bwscale",
                    "pad_profile", "zyn_profile",
                    "pad_position", "zyn_position");
            if (!hasPadProfileFields)
            {
                return PadSpectrumProfile.Generic;
            }

            var mode = TryGetAny(fields, ["pad_mode", "zyn_mode"], out var modeText)
                ? ParsePadSpectrumMode(modeText, line)
                : PadSpectrumMode.Bandwidth;
            var bandwidth = GetInt(fields, line, 500, "pad_bandwidth", "zyn_bandwidth", "bandwidth");
            var bandwidthScale = GetInt(fields, line, 0, "pad_bwscale", "zyn_bwscale", "bwscale");
            if (bandwidth < 0) throw new PatchScriptException(line, "PAD bandwidth must be zero or greater");
            return new PadSpectrumProfile(
                mode,
                Math.Clamp(bandwidth, 0, 1000),
                Math.Clamp(bandwidthScale, 0, 7),
                TryGetAny(fields, ["pad_profile", "zyn_profile"], out var profileText)
                    ? ParsePadHarmonicProfile(profileText, line)
                    : new PadHarmonicProfile(),
                TryGetAny(fields, ["pad_position", "zyn_position"], out var positionText)
                    ? ParsePadHarmonicPosition(positionText, line)
                    : new PadHarmonicPosition());
        }

        private static PadSpectrumMode ParsePadSpectrumMode(string value, int line) => value.ToLowerInvariant() switch
        {
            "generic" => PadSpectrumMode.Generic,
            "bandwidth" or "zyn" or "zyn_bandwidth" => PadSpectrumMode.Bandwidth,
            "discrete" or "other" => PadSpectrumMode.Discrete,
            "continuous" => PadSpectrumMode.Continuous,
            _ => throw new PatchScriptException(line, $"unknown PAD spectrum mode `{value}`")
        };

        private static PadHarmonicProfile ParsePadHarmonicProfile(string value, int line)
        {
            var pieces = value.Split(':', StringSplitOptions.TrimEntries);
            if (pieces.Length != 12)
            {
                throw new PatchScriptException(line, "PAD harmonic profile needs 12 colon-separated fields");
            }

            return new PadHarmonicProfile(
                ParseProfileBaseType(pieces[0], line),
                ParseByteish(pieces[1], line),
                ParseByteish(pieces[2], line),
                ParseByteish(pieces[3], line),
                ParseByteish(pieces[4], line),
                ParseByteish(pieces[5], line),
                ParseProfileAmplitudeType(pieces[6], line),
                ParseProfileAmplitudeMode(pieces[7], line),
                ParseByteish(pieces[8], line),
                ParseByteish(pieces[9], line),
                ParseBool(pieces[10], line),
                ParseProfileHalf(pieces[11], line));
        }

        private static PadHarmonicPosition ParsePadHarmonicPosition(string value, int line)
        {
            var pieces = value.Split(':', StringSplitOptions.TrimEntries);
            if (pieces.Length != 4)
            {
                throw new PatchScriptException(line, "PAD harmonic position needs 4 colon-separated fields");
            }

            return new PadHarmonicPosition(
                ParseHarmonicPositionType(pieces[0], line),
                ParseByteish(pieces[1], line, 255),
                ParseByteish(pieces[2], line, 255),
                ParseByteish(pieces[3], line, 255));
        }

        private static int ParseByteish(string value, int line, int max = 127)
        {
            var parsed = ParseInt(value, line);
            if (parsed < 0 || parsed > max)
            {
                throw new PatchScriptException(line, $"PAD profile parameter `{value}` is outside 0..{max}");
            }
            return parsed;
        }

        private static PadProfileBaseType ParseProfileBaseType(string value, int line) => value.ToLowerInvariant() switch
        {
            "0" or "gauss" or "gaussian" => PadProfileBaseType.Gaussian,
            "1" or "square" or "rect" or "rectangular" => PadProfileBaseType.Square,
            "2" or "double" or "double_exp" or "doubleexponential" => PadProfileBaseType.DoubleExponential,
            _ => throw new PatchScriptException(line, $"unknown PAD profile base type `{value}`")
        };

        private static PadProfileAmplitudeType ParseProfileAmplitudeType(string value, int line) => value.ToLowerInvariant() switch
        {
            "0" or "off" or "none" => PadProfileAmplitudeType.Off,
            "1" or "gauss" or "gaussian" => PadProfileAmplitudeType.Gaussian,
            "2" or "sine" => PadProfileAmplitudeType.Sine,
            "3" or "flat" => PadProfileAmplitudeType.Flat,
            _ => throw new PatchScriptException(line, $"unknown PAD profile amplitude type `{value}`")
        };

        private static PadProfileAmplitudeMode ParseProfileAmplitudeMode(string value, int line) => value.ToLowerInvariant() switch
        {
            "0" or "sum" => PadProfileAmplitudeMode.Sum,
            "1" or "mult" or "multiply" => PadProfileAmplitudeMode.Mult,
            "2" or "div1" => PadProfileAmplitudeMode.Div1,
            "3" or "div2" => PadProfileAmplitudeMode.Div2,
            _ => throw new PatchScriptException(line, $"unknown PAD profile amplitude mode `{value}`")
        };

        private static PadProfileHalf ParseProfileHalf(string value, int line) => value.ToLowerInvariant() switch
        {
            "0" or "full" => PadProfileHalf.Full,
            "1" or "upper" => PadProfileHalf.Upper,
            "2" or "lower" => PadProfileHalf.Lower,
            _ => throw new PatchScriptException(line, $"unknown PAD profile half `{value}`")
        };

        private static PadHarmonicPositionType ParseHarmonicPositionType(string value, int line) => value.ToLowerInvariant() switch
        {
            "0" or "harmonic" => PadHarmonicPositionType.Harmonic,
            "1" or "shiftu" or "shift_up" => PadHarmonicPositionType.ShiftUp,
            "2" or "shiftl" or "shift_down" => PadHarmonicPositionType.ShiftDown,
            "3" or "poweru" or "power_up" => PadHarmonicPositionType.PowerUp,
            "4" or "powerl" or "power_down" => PadHarmonicPositionType.PowerDown,
            "5" or "sine" => PadHarmonicPositionType.Sine,
            "6" or "power" => PadHarmonicPositionType.Power,
            "7" or "shift" => PadHarmonicPositionType.Shift,
            _ => throw new PatchScriptException(line, $"unknown PAD harmonic position type `{value}`")
        };

        private ParsedEnvelope ParseRateLevelEnvelope(IReadOnlyDictionary<string, string> fields, int line, string fieldPath)
        {
            var rates = ParseFloatList(Required(fields, "rates", line), line, "rates");
            var levels = ParseFloatList(Required(fields, "levels", line), line, "levels");
            if (rates.Count != 4 || levels.Count != 4)
            {
                throw new PatchScriptException(line, "rate/level envelope needs four rates and four levels");
            }

            return RateLevelParsedEnvelope(rates, levels, fields, line, fieldPath);
        }

        private ParsedEnvelope ParseRateLevelEnvelope(string[] pieces, IReadOnlyDictionary<string, string> fields, int line, string fieldPath)
        {
            var rates = new[] { pieces[1], pieces[3], pieces[5], pieces[7] }
                .Select(part => ParseFloat(part, line))
                .ToArray();
            var levels = new[] { pieces[2], pieces[4], pieces[6], pieces[8] }
                .Select(part => ParseFloat(part, line))
                .ToArray();
            return RateLevelParsedEnvelope(rates, levels, fields, line, fieldPath);
        }

        private ParsedEnvelope RateLevelParsedEnvelope(IReadOnlyList<float> rates, IReadOnlyList<float> levels, IReadOnlyDictionary<string, string> fields, int line, string fieldPath)
        {
            var curves = TryGetAny(fields, ["curves", "curve"], out var curveText)
                ? ParseCurveList(curveText, line)
                : [RateLevelCurve.Linear, RateLevelCurve.Linear, RateLevelCurve.Linear, RateLevelCurve.Linear];
            if (curves.Count != 4)
            {
                throw new PatchScriptException(line, "rate/level envelope needs four curves");
            }

            var envelope = new RateLevelEnvelope(
                Math.Max(0, rates[0]), Math.Clamp(levels[0], 0, 4f),
                Math.Max(0, rates[1]), Math.Clamp(levels[1], 0, 4f),
                Math.Max(0, rates[2]), Math.Clamp(levels[2], 0, 4f),
                Math.Max(0, rates[3]), Math.Clamp(levels[3], 0, 4f),
                curves[0], curves[1], curves[2], curves[3],
                TryGetAny(fields, ["start_level", "start"], out var startText)
                    ? Math.Clamp(ParseFloat(startText, line), 0, 4f)
                    : 0);
            var defaultGate = Math.Max(envelope.Rate1Seconds + envelope.Rate2Seconds + envelope.Rate3Seconds, 0.02f);
            return new ParsedEnvelope(
                new Envelope(envelope.Rate1Seconds, envelope.Rate2Seconds + envelope.Rate3Seconds, envelope.Level3, envelope.Rate4Seconds),
                GateSeconds(fields, line, defaultGate, fieldPath),
                envelope);
        }

        private RateLevelEnvelope? ParseFilterRateLevelEnvelope(IReadOnlyDictionary<string, string> fields, int line, string ownerPath, string prefix)
        {
            var envKeys = prefix == "lpf" ? new[] { "lpf_env", "filter_env" } : new[] { "hpf_env" };
            if (!TryGetAny(fields, envKeys, out var env) || env is not ("rl" or "ratelevel"))
            {
                return null;
            }

            var ratesName = $"{prefix}_rates";
            var levelsName = $"{prefix}_levels";
            var curvesName = $"{prefix}_curves";
            var curveName = $"{prefix}_curve";
            var startName = $"{prefix}_start";
            var rates = ParseFloatList(Required(fields, ratesName, line), line, ratesName);
            var levels = ParseFloatList(Required(fields, levelsName, line), line, levelsName);
            if (rates.Count != 4 || levels.Count != 4)
            {
                throw new PatchScriptException(line, "filter rate/level envelope needs four rates and four levels");
            }

            var curves = TryGetAny(fields, [curvesName, curveName], out var curveText)
                ? ParseCurveList(curveText, line)
                : [RateLevelCurve.Linear, RateLevelCurve.Linear, RateLevelCurve.Linear, RateLevelCurve.Linear];
            if (curves.Count != 4)
            {
                throw new PatchScriptException(line, "filter rate/level envelope needs four curves");
            }

            return new RateLevelEnvelope(
                Math.Max(0, rates[0]), Math.Clamp(levels[0], -1, 1),
                Math.Max(0, rates[1]), Math.Clamp(levels[1], -1, 1),
                Math.Max(0, rates[2]), Math.Clamp(levels[2], -1, 1),
                Math.Max(0, rates[3]), Math.Clamp(levels[3], -1, 1),
                curves[0], curves[1], curves[2], curves[3],
                TryGetAny(fields, prefix == "lpf" ? [startName, "filter_start"] : [startName], out var startText)
                    ? Math.Clamp(ParseFloat(startText, line), -1, 1)
                    : 0);
        }

        private static IReadOnlyList<RateLevelCurve> ParseCurveList(string value, int line)
        {
            var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                throw new PatchScriptException(line, "curves list cannot be empty");
            }

            return parts.Select(part => part.ToLowerInvariant() switch
            {
                "lin" or "linear" => RateLevelCurve.Linear,
                "exp" or "exponential" => RateLevelCurve.Exponential,
                _ => throw new PatchScriptException(line, $"unknown rate/level curve `{part}`")
            }).ToArray();
        }

        private float GateSeconds(IReadOnlyDictionary<string, string> fields, int line, float defaultValue, string fieldPath) =>
            GetBoundFloat(fields, line, defaultValue, $"{fieldPath}/note/gate", "gate", "hold", "duration");

        private static IReadOnlyList<float> ParseFloatList(string value, int line, string fieldName)
        {
            var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                throw new PatchScriptException(line, $"{fieldName} list cannot be empty");
            }

            return parts.Select(part => ParseFloat(part, line)).ToArray();
        }

        private static int ParseOperatorId(string value, int line)
        {
            var normalized = value.StartsWith("op", StringComparison.OrdinalIgnoreCase)
                ? value[2..]
                : value;
            return ParseInt(normalized, line);
        }

        private void AddSfxrPatch(SfxrParams parameters)
        {
            var mapped = parameters.ToPatch();
            Voices.AddRange(mapped.Voices);
            Repeat = mapped.Repeat;
            Gain *= mapped.Gain;
        }

        private static SfxrParams ParseSfxrCommand(IReadOnlyDictionary<string, string> fields, int line)
        {
            var parameters = TryGetAny(fields, ["preset", "p"], out var preset)
                ? SfxrParams.Named(preset) ?? throw new PatchScriptException(line, $"unknown sfxr preset `{preset}`")
                : new SfxrParams();
            return ApplySfxrFields(parameters, fields, line);
        }

        private static SfxrParams ApplySfxrFields(SfxrParams parameters, IReadOnlyDictionary<string, string> fields, int line)
        {
            if (TryGetAny(fields, ["mutate_seed", "ms"], out var seedText))
            {
                if (!ulong.TryParse(seedText, out var seed))
                {
                    throw new PatchScriptException(line, "mutate_seed must be an integer");
                }

                var amount = GetFloat(fields, line, 0.05f, "mutate", "m");
                parameters = parameters.Mutate(seed, amount);
            }

            if (TryGetAny(fields, ["wave", "w"], out var wave)) parameters = parameters with { WaveType = ParseWaveform(wave, line) };
            if (TryGetAny(fields, ["base", "b"], out var baseFreq)) parameters = parameters with { BaseFreq = Math.Clamp(ParseFloat(baseFreq, line), 0, 1) };
            if (TryGetAny(fields, ["limit", "lim"], out var limit)) parameters = parameters with { FreqLimit = Math.Clamp(ParseFloat(limit, line), 0, 1) };
            if (TryGetAny(fields, ["ramp", "r"], out var ramp)) parameters = parameters with { FreqRamp = Math.Clamp(ParseFloat(ramp, line), -1, 1) };
            if (TryGetAny(fields, ["dramp", "dr"], out var dramp)) parameters = parameters with { FreqDramp = Math.Clamp(ParseFloat(dramp, line), -1, 1) };
            if (TryGetAny(fields, ["duty", "du"], out var duty)) parameters = parameters with { Duty = Math.Clamp(ParseFloat(duty, line), 0, 1) };
            if (TryGetAny(fields, ["duty_ramp", "dur"], out var dutyRamp)) parameters = parameters with { DutyRamp = Math.Clamp(ParseFloat(dutyRamp, line), -1, 1) };
            if (TryGetAny(fields, ["vib", "vi"], out var vib)) parameters = parameters with { VibStrength = Math.Clamp(ParseFloat(vib, line), 0, 1) };
            if (TryGetAny(fields, ["vib_speed", "vs"], out var vibSpeed)) parameters = parameters with { VibSpeed = Math.Clamp(ParseFloat(vibSpeed, line), 0, 1) };
            if (TryGetAny(fields, ["vib_delay", "vd"], out var vibDelay)) parameters = parameters with { VibDelay = Math.Clamp(ParseFloat(vibDelay, line), 0, 1) };
            if (TryGetAny(fields, ["attack", "a"], out var attack)) parameters = parameters with { EnvAttack = Math.Clamp(ParseFloat(attack, line), 0, 1) };
            if (TryGetAny(fields, ["sustain", "s"], out var sustain)) parameters = parameters with { EnvSustain = Math.Clamp(ParseFloat(sustain, line), 0, 1) };
            if (TryGetAny(fields, ["decay", "d"], out var decay)) parameters = parameters with { EnvDecay = Math.Clamp(ParseFloat(decay, line), 0, 1) };
            if (TryGetAny(fields, ["punch", "pu"], out var punch)) parameters = parameters with { EnvPunch = Math.Clamp(ParseFloat(punch, line), -1, 1) };
            if (TryGetAny(fields, ["resonance", "res"], out var resonance)) parameters = parameters with { LpfResonance = Math.Clamp(ParseFloat(resonance, line), 0, 1) };
            if (TryGetAny(fields, ["lpf"], out var lpf)) parameters = parameters with { LpfFreq = Math.Clamp(ParseFloat(lpf, line), 0, 1) };
            if (TryGetAny(fields, ["lpf_ramp", "lpfr"], out var lpfRamp)) parameters = parameters with { LpfRamp = Math.Clamp(ParseFloat(lpfRamp, line), -1, 1) };
            if (TryGetAny(fields, ["hpf"], out var hpf)) parameters = parameters with { HpfFreq = Math.Clamp(ParseFloat(hpf, line), 0, 1) };
            if (TryGetAny(fields, ["hpf_ramp", "hpfr"], out var hpfRamp)) parameters = parameters with { HpfRamp = Math.Clamp(ParseFloat(hpfRamp, line), -1, 1) };
            if (TryGetAny(fields, ["phaser", "ph"], out var phaser)) parameters = parameters with { PhaOffset = Math.Clamp(ParseFloat(phaser, line), -1, 1) };
            if (TryGetAny(fields, ["phaser_ramp", "phr"], out var phaserRamp)) parameters = parameters with { PhaRamp = Math.Clamp(ParseFloat(phaserRamp, line), -1, 1) };
            if (TryGetAny(fields, ["repeat", "rep"], out var repeat)) parameters = parameters with { RepeatSpeed = Math.Clamp(ParseFloat(repeat, line), 0, 1) };
            if (TryGetAny(fields, ["arp"], out var arp)) parameters = parameters with { ArpSpeed = Math.Clamp(ParseFloat(arp, line), 0, 1) };
            if (TryGetAny(fields, ["arp_mod", "am"], out var arpMod)) parameters = parameters with { ArpMod = Math.Clamp(ParseFloat(arpMod, line), -1, 1) };
            return parameters;
        }

        private sealed record PendingOperatorGraph(
            int Line,
            string Path,
            string Name,
            float FrequencyHz,
            Note Note,
            float Gain,
            float VibratoDepth,
            float VibratoHz,
            float VibratoDelaySeconds)
        {
            public List<OperatorNode> Operators { get; } = [];
            public List<OperatorEdge> Edges { get; } = [];
            public List<int> Carriers { get; } = [];
        }

        private sealed record ParsedEnvelope(Envelope Envelope, float GateSeconds, RateLevelEnvelope? RateLevelEnvelope = null);
    }

    private static readonly (string[] Keys, ModTarget Target)[] ModTargets =
    [
        (["gain", "g"], ModTarget.Gain),
        (["pitch", "p"], ModTarget.Pitch),
        (["duty", "du"], ModTarget.Duty),
        (["lpf", "l"], ModTarget.LowPass),
        (["hpf", "h"], ModTarget.HighPass),
        (["noise", "nz"], ModTarget.Noise),
        (["drive", "drv"], ModTarget.Drive),
        (["fold", "fl"], ModTarget.Fold),
        (["formant_mix", "fmix", "formant"], ModTarget.FormantMix),
        (["fm_index", "fmi"], ModTarget.FmIndex)
    ];

    private static Dictionary<string, string> ParseFields(IEnumerable<string> tokens, int line)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var split = token.Split('=', 2);
            if (split.Length != 2 || split[0].Length == 0)
            {
                throw new PatchScriptException(line, $"bad field `{token}`");
            }
            fields[CanonicalField(split[0])] = split[1];
        }
        return fields;
    }

    private static List<Formant> ParseFormants(string value, int line) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var pieces = part.Split(':');
                if (pieces.Length != 3) throw new PatchScriptException(line, $"bad formant `{part}`");
                return new Formant(ParseFloat(pieces[0], line), ParseFloat(pieces[1], line), ParseFloat(pieces[2], line));
            })
            .ToList();

    private static List<FormantFrame> ParseFormantFrames(string value, int line) =>
        value.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(frame => new FormantFrame(ParseFormants(frame, line)))
            .ToList();

    private static List<Modulator> ParseVoiceModulators(string value, int line) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var pieces = part.Split(':');
                if (pieces.Length != 4) throw new PatchScriptException(line, $"bad modulator `{part}`");
                return new Modulator(
                    ParseModTarget(pieces[0], line),
                    ParseModWaveform(pieces[1], line),
                    ParseFloat(pieces[2], line),
                    ParseFloat(pieces[3], line));
            })
            .ToList();

    private static IEnumerable<(ModTarget Target, float Depth)> ParseRoutes(string value, int line) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var pieces = part.Split(':');
                if (pieces.Length != 2) throw new PatchScriptException(line, $"bad route `{part}`");
                return (ParseModTarget(pieces[0], line), ParseFloat(pieces[1], line));
            });

        private static string CanonicalCommand(string command) => command.ToLowerInvariant() switch
    {
        "p" or "patch" or "instrument" => "patch",
        "d" or "default" or "defaults" => "defaults",
        "def" or "t" or "template" => "template",
        "layer" or "kit" => "layer",
        "harmonics" or "partials" or "drawbars" => "harmonics",
        "spectrum" or "spectral" or "padsource" or "pad_source" => "spectrum",
        "v" or "voice" => "voice",
        "tract" or "vt" or "tractvoice" or "tract_voice" => "tract",
        "opgraph" or "ops" or "operators" => "opgraph",
        "operator" or "op" => "operator",
        "route" or "edge" => "route",
        "carrier" or "out" => "carrier",
        "mod" or "wob" or "wobble" or "bus" => "mod",
        "lfo" or "control" => "control",
        "param" or "parameter" => "param",
        "s" or "sfxr" => "sfxr",
        _ => command
    };

    private static string CanonicalField(string field) => field.ToLowerInvariant() switch
    {
        "n" => "name",
        "u" => "use",
        "w" => "wave",
        "f" => "freq",
        "g" => "gain",
        "a" => "attack",
        "s" => "sustain",
        "d" => "decay",
        "from" or "src" => "from",
        "to" or "dst" => "to",
        _ => field
    };

    private static Waveform ParseWaveform(string value, int line) => value.ToLowerInvariant() switch
    {
        "sin" or "sine" => Waveform.Sine,
        "sq" or "square" => Waveform.Square,
        "saw" or "sawtooth" => Waveform.Sawtooth,
        "tri" or "triangle" => Waveform.Triangle,
        "n" or "noise" => Waveform.Noise,
        _ => throw new PatchScriptException(line, $"unknown waveform `{value}`")
    };

    private static ModWaveform ParseModWaveform(string value, int line) => value.ToLowerInvariant() switch
    {
        "sin" or "sine" => ModWaveform.Sine,
        "tri" or "triangle" => ModWaveform.Triangle,
        "sq" or "square" => ModWaveform.Square,
        "hold" or "sample_hold" => ModWaveform.SampleHold,
        _ => throw new PatchScriptException(line, $"unknown mod waveform `{value}`")
    };

    private static NoteSource ParseNoteSource(string value, int line) => value.ToLowerInvariant() switch
    {
        "oneshot" or "one_shot" or "trigger" or "fixed" => NoteSource.OneShot,
        "host" or "midi" or "gate" => NoteSource.Host,
        _ => throw new PatchScriptException(line, $"unknown note source `{value}`")
    };

    private static PlaybackMode ParsePlaybackMode(string value, int line) => value.ToLowerInvariant() switch
    {
        "oneshot" or "one_shot" or "trigger" or "fixed" => PlaybackMode.OneShot,
        "mono" or "monophonic" or "host" => PlaybackMode.Mono,
        "poly" or "polyphonic" or "midi" => PlaybackMode.Poly,
        _ => throw new PatchScriptException(line, $"unknown playback mode `{value}`")
    };

    private static ModTarget ParseModTarget(string value, int line)
    {
        foreach (var (keys, target) in ModTargets)
        {
            if (keys.Contains(value, StringComparer.OrdinalIgnoreCase) ||
                keys.Select(CanonicalField).Contains(CanonicalField(value), StringComparer.OrdinalIgnoreCase))
            {
                return target;
            }
        }
        if (value.Equals("formant", StringComparison.OrdinalIgnoreCase)) return ModTarget.FormantMix;
        throw new PatchScriptException(line, $"unknown mod target `{value}`");
    }

    private static string TargetSuffix(ModTarget target) => target switch
    {
        ModTarget.Gain => "gain",
        ModTarget.Pitch => "pitch",
        ModTarget.Duty => "duty",
        ModTarget.LowPass => "lpf",
        ModTarget.HighPass => "hpf",
        ModTarget.Noise => "noise",
        ModTarget.Drive => "drive",
        ModTarget.Fold => "fold",
        ModTarget.FormantMix => "formant_mix",
        ModTarget.FmIndex => "fm_index",
        _ => target.ToString().ToLowerInvariant()
    };

    private static string Required(IReadOnlyDictionary<string, string> fields, string key, int line) =>
        fields.TryGetValue(key, out var value) ? value : throw new PatchScriptException(line, $"missing `{key}`");

    private static string GetAny(IReadOnlyDictionary<string, string> fields, string[] keys, string fallback) =>
        TryGetAny(fields, keys, out var value) ? value : fallback;

    private static bool TryGetAny(IReadOnlyDictionary<string, string> fields, string[] keys, out string value)
    {
        foreach (var key in keys.Select(CanonicalField))
        {
            if (fields.TryGetValue(key, out value!)) return true;
        }
        value = "";
        return false;
    }

    private static bool HasAny(IReadOnlyDictionary<string, string> fields, params string[] keys) =>
        keys.Select(CanonicalField).Any(fields.ContainsKey);

    private static float GetFloat(IReadOnlyDictionary<string, string> fields, int line, float fallback, params string[] keys) =>
        TryGetAny(fields, keys, out var value) ? ParseFloat(value, line) : fallback;

    private static int GetInt(IReadOnlyDictionary<string, string> fields, int line, int fallback, params string[] keys) =>
        TryGetAny(fields, keys, out var value) ? ParseInt(value, line) : fallback;

    private static float ParseFloat(string value, int line) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new PatchScriptException(line, $"bad number `{value}`");

    private static string F(float value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static float PunchGain(float value) => 1 + Math.Max(0, Math.Clamp(value, -1, 1));

    private static int ParseInt(string value, int line) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new PatchScriptException(line, $"bad integer `{value}`");

    private static bool ParseBool(string value, int line) => value.ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "on" => true,
        "false" or "0" or "no" or "off" => false,
        _ => throw new PatchScriptException(line, $"bad bool `{value}`")
    };

    private static void Merge(IDictionary<string, string> target, IReadOnlyDictionary<string, string> source)
    {
        foreach (var (key, value) in source) target[key] = value;
    }

    private static Dictionary<string, string> Without(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        var excluded = keys.Select(CanonicalField).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return fields.Where(pair => !excluded.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }
}
