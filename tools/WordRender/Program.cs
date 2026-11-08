using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AquaSynth.Dsl;
using AquaSynth.Faust;

// Offline probe: one word as a run of IPA gestures on the IpaGestureExperiment scaffold, rendered through the native
// libfaust path to a mono 16-bit WAV plus script.aqua, source.dsp and manifest.json. It measures peak, rms and NaN;
// it cannot say how the word sounds.
// usage: WordRender <outDir> <label> <freq-hz> <ipa>:<ms>:<intensity> ...      e.g. WordRender out nibu 140 n:95:1 i:180:1.2 b:95:1 u:180:.9
const int Rate = 48000, Frames = 512;
if (args.Length < 4) { Console.Error.WriteLine("usage: WordRender <outDir> <label> <freq-hz> <ipa>:<ms>:<intensity> ..."); return 2; }
var outDir = Path.GetFullPath(args[0]);
var label = args[1];
var freq = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
var phones = args.Skip(3).Select((spec, i) =>
{
    var p = spec.Split(':');
    var ipa = p[0];
    var ms = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
    var intensity = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);
    return (new IpaGestureExperimentTarget($"{label}_{i}_{ipa}", ipa, "", ms / 1000f), intensity);
}).ToArray();
Directory.CreateDirectory(outDir);

var script = IpaGestureExperiment.BuildWordScript(phones, freq);
File.WriteAllText(Path.Combine(outDir, "script.aqua"), script + "\n");
var seconds = phones.Sum(p => p.Item1.DurationSeconds) + 0.12f;
var home = AquaSynthFaustToolchain.Find();
Console.WriteLine($"faust home: {home ?? "(not found)"}");
using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: Rate));
// The exporter bakes splines in as offsets summed per surface; the engine's ControlSplineTimeline merges them per surface
// into one absolute curve. Back-to-back gestures only sequence under the timeline, so the DSP is emitted without splines
// (surface sliders only) and the timeline drives the sliders block by block, as NativeFaustRuntime.ControlValuesAt does.
var scripted = PatchScript.Parse(script);
var timeline = ControlSurfaceCatalog.FromPatch(scripted).CreateTimeline();
var source = FaustEmitter.Emit(scripted with { ControlSplines = [] }, new FaustExportOptions(label)).Source;
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
    var ctl = patch.ControlValuesAt(timeline, blk * Frames / (float)Rate);
    stream.ProcessBlock(Frames, IntPtr.Zero, outP, ctl);
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
    label, freq, phones = args.Skip(3), seconds, sampleRate = Rate, frames = pcm.Length, peak, rms, nonFinite = nan,
    wavSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(wav))).ToLowerInvariant(),
    scriptSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(outDir, "script.aqua")))).ToLowerInvariant(),
    os = RuntimeInformation.OSDescription, faustHome = home
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{wav} {seconds:F2}s peak={peak:F3} rms={rms:F4} nonFinite={nan}");
return nan ? 1 : 0;
