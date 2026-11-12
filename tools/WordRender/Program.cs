using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AquaSynth.Dsl;
using AquaSynth.Faust;

// Offline probe: one word, segmented by Weksa (the phones line of `weksa lower --phones`), as a run of IPA gestures on the
// IpaGestureExperiment scaffold, rendered through the native libfaust path to a mono 16-bit WAV plus script.aqua, source.dsp
// and manifest.json. It measures peak, rms and NaN; it cannot say how the word sounds.
// usage: WordRender <outDir> <label> <freq-hz> <phones.json> [--no-burst]
// --no-burst drops the plosive release_flow splines (the same edit the burst test makes), so a render pair isolates the burst.
// exit: 0 rendered, 1 compile or non-finite output, 2 usage, 3 phones file refused, 4 phones the tract cannot make (listed, nothing rendered)
const int Rate = 48000, Frames = 512;
var noBurst = args.Length == 5 && args[4] == "--no-burst";
if (args.Length != 4 && !noBurst) { Console.Error.WriteLine("usage: WordRender <outDir> <label> <freq-hz> <phones.json> [--no-burst]"); return 2; }
var outDir = Path.GetFullPath(args[0]);
var label = args[1];
var freq = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
WeksaPhones word;
try { word = WeksaPhonesReader.Parse(File.ReadAllText(args[3])); }
catch (Exception ex) when (ex is WeksaPhonesException or IOException)
{
    Console.Error.WriteLine($"REFUSED {args[3]}: {ex.Message}");
    return 3;
}

var plan = WeksaWordPlan.From(word, label);
if (!plan.Renderable)
{
    Console.Error.WriteLine("UNSUPPORTED PHONES (the tract has no gesture for them; nothing rendered): " + string.Join(" ", plan.Unsupported.Select(p => p.Ipa)));
    return 4;
}

var phones = plan.Phones.ToArray();
Directory.CreateDirectory(outDir);

var script = IpaGestureExperiment.BuildWordScript(phones, freq);
File.WriteAllText(Path.Combine(outDir, "script.aqua"), script + "\n");
var seconds = phones.Sum(p => p.Item1.DurationSeconds) + 0.12f;
var home = AquaSynthFaustToolchain.Find();
Console.WriteLine($"faust home: {home ?? "(not found)"}");
using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: Rate));
// The exporter emits one merged spline per surface (the ControlSplineTimeline curve), so the DSP carries the word's motion
// itself and no slider is driven from outside.
var parsed = PatchScript.Parse(script);
if (noBurst) parsed = parsed with { ControlSplines = parsed.ControlSplines.Where(s => !s.SurfacePath.EndsWith("/release_flow", StringComparison.Ordinal)).ToArray() };
var source = FaustEmitter.Emit(parsed, new FaustExportOptions(label)).Source;
File.WriteAllText(Path.Combine(outDir, "source.dsp"), source);
if (!compiler.TryCompileSource(new AquaSynthCompileIdentity(label, label, ""), source, seconds, out var patch0, out var cerr))
{
    Console.Error.WriteLine("COMPILE FAILED: " + cerr);
    return 1;
}

using var patch = patch0!;
using var stream = patch.CreateStreamingPatch();
var buf = GC.AllocateArray<float>(Frames, pinned: true);
var ptrs = GC.AllocateArray<IntPtr>(1, pinned: true);
unsafe { ptrs[0] = (IntPtr)Unsafe.AsPointer(ref buf[0]); }
var outP = Marshal.UnsafeAddrOfPinnedArrayElement(ptrs, 0);
var blocks = (int)Math.Ceiling(seconds * Rate / Frames);
var pcm = new short[blocks * Frames];
double sum = 0; float peak = 0; var nan = false;
for (var blk = 0; blk < blocks; blk++)
{
    stream.ProcessBlock(Frames, IntPtr.Zero, outP);
    for (var i = 0; i < Frames; i++)
    {
        var v = buf[i];
        nan |= float.IsNaN(v) || float.IsInfinity(v);
        peak = Math.Max(peak, Math.Abs(v));
        sum += (double)v * v;
        pcm[blk * Frames + i] = (short)(Math.Clamp(v, -1f, 1f) * 32767);
    }
}

var wav = Path.Combine(outDir, $"{label}.wav");
using (var w = new BinaryWriter(File.Create(wav)))
{
    var db = pcm.Length * 2;
    w.Write("RIFF"u8); w.Write(36 + db); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
    w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(db);
    foreach (var s in pcm) w.Write(s);
}

var rms = Math.Sqrt(sum / pcm.Length);
File.WriteAllText(Path.Combine(outDir, "manifest.json"), JsonSerializer.Serialize(new
{
    label, freq, noBurst, weksa = new { word.Language, word.Source, Phones = word.Phones.Select(p => new { p.Ipa, Stress = p.Stress.ToString() }) }, seconds, sampleRate = Rate, frames = pcm.Length, peak, rms, nonFinite = nan,
    wavSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(wav))).ToLowerInvariant(),
    scriptSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(outDir, "script.aqua")))).ToLowerInvariant(),
    os = RuntimeInformation.OSDescription, faustHome = home
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{wav} {seconds:F2}s peak={peak:F3} rms={rms:F4} nonFinite={nan}");
return nan ? 1 : 0;
