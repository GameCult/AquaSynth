using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AquaSynth.Faust;

// Offline render of one Faust source through the daemon's native libfaust path (AquaSynthPatchCompiler, -vec -lv 1)
// to a mono float32 WAV, for diffing against aqua-feel's faustwasm render. 48 kHz, 512-frame blocks, like render.js.
// usage: FaustNativeRender <patch.dsp> <out.wav> <seconds> [controlSuffix=value ...]
const int Rate = 48000, Frames = 512;
if (args.Length < 3) { Console.Error.WriteLine("usage: FaustNativeRender <patch.dsp> <out.wav> <seconds> [suffix=value ...]"); return 2; }
var dspPath = Path.GetFullPath(args[0]);
var outPath = Path.GetFullPath(args[1]);
var total = (int)Math.Round(double.Parse(args[2]) * Rate);
var source = File.ReadAllText(dspPath);
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: Rate));
var name = Path.GetFileNameWithoutExtension(dspPath);
if (!compiler.TryCompileSource(new AquaSynthCompileIdentity(name, name, ""), source, (float)total / Rate, out var patch0, out var cerr))
{
    Console.Error.WriteLine("COMPILE FAILED: " + cerr);
    return 1;
}
using var patch = patch0!;
var ctl = new Dictionary<string, float>();
foreach (var spec in args.Skip(3))
{
    var eq = spec.IndexOf('=');
    var path = patch.ControlPaths.Single(p => p.EndsWith(spec[..eq]));
    ctl[path] = float.Parse(spec[(eq + 1)..], System.Globalization.CultureInfo.InvariantCulture);
}

using var stream = patch.CreateStreamingPatch();
var buf = GC.AllocateArray<float>(Frames, pinned: true);
var ptrs = GC.AllocateArray<IntPtr>(1, pinned: true);
unsafe { ptrs[0] = (IntPtr)Unsafe.AsPointer(ref buf[0]); }
var outP = Marshal.UnsafeAddrOfPinnedArrayElement(ptrs, 0);
var pcm = new float[total];
for (var at = 0; at < total; at += Frames)
{
    stream.ProcessBlock(Frames, IntPtr.Zero, outP, ctl);
    Array.Copy(buf, 0, pcm, at, Math.Min(Frames, total - at));
}
using (var w = new BinaryWriter(File.Create(outPath)))
{
    var db = total * 4;
    w.Write("RIFF"u8); w.Write(36 + db); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)3); w.Write((short)1);
    w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)32); w.Write("data"u8); w.Write(db);
    foreach (var s in pcm) w.Write(s);
}
Console.WriteLine($"{outPath} frames={total} controls={ctl.Count}");
return 0;
