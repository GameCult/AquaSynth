using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AquaSynth.Faust;

// Offline probe: render one Faust source with a swept `rpm` control to mono 16-bit WAVs through the
// native libfaust path, and write manifest.json (hashes, levels, toolchain). It measures peak, rms and
// NaN only; it cannot say how anything sounds.
// usage: FaustRender <patch.dsp> <outDir> <seconds> <label>=<rpmA>[:<rpmB>] ...
const int Rate = 48000, Frames = 512;
if (args.Length < 4) { Console.Error.WriteLine("usage: FaustRender <patch.dsp> <outDir> <seconds> <label>=<rpmA>[:<rpmB>] ..."); return 2; }
var dspPath = Path.GetFullPath(args[0]);
var outDir = Path.GetFullPath(args[1]);
var seconds = double.Parse(args[2]);
var source = File.ReadAllText(dspPath);
Directory.CreateDirectory(outDir);

var home = AquaSynthFaustToolchain.Find();
Console.WriteLine($"faust home: {home ?? "(not found)"}");
using var compiler = new AquaSynthPatchCompiler(new AquaSynthNativeOptions(SampleRate: Rate));
var name = Path.GetFileNameWithoutExtension(dspPath);
if (!compiler.TryCompileSource(new AquaSynthCompileIdentity(name, name, ""), source, (float)seconds, out var patch0, out var cerr))
{
    Console.Error.WriteLine("COMPILE FAILED: " + cerr);
    return 1;
}

using var patch = patch0!;
var rpmPath = patch.ControlPaths.First(p => p.EndsWith("rpm"));
var cases = new List<object>();
foreach (var spec in args.Skip(3))
{
    var eq = spec.IndexOf('=');
    var label = spec[..eq];
    var range = spec[(eq + 1)..].Split(':');
    var a = float.Parse(range[0]);
    var b = range.Length > 1 ? float.Parse(range[1]) : a;

    using var stream = patch.CreateStreamingPatch();
    var buf = GC.AllocateArray<float>(Frames, pinned: true);
    var ptrs = GC.AllocateArray<IntPtr>(1, pinned: true);
    unsafe { ptrs[0] = (IntPtr)Unsafe.AsPointer(ref buf[0]); }
    var outP = Marshal.UnsafeAddrOfPinnedArrayElement(ptrs, 0);
    var blocks = (int)(seconds * Rate) / Frames;
    var ctl = new Dictionary<string, float>();
    var pcm = new short[blocks * Frames];
    double sum = 0; float peak = 0; var nan = false;
    for (var blk = 0; blk < blocks; blk++)
    {
        ctl[rpmPath] = a + (b - a) * blk / Math.Max(1, blocks - 1);
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

    var wav = Path.Combine(outDir, $"{name}-{label}.wav");
    using (var w = new BinaryWriter(File.Create(wav)))
    {
        var db = pcm.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + db); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(db);
        foreach (var s in pcm) w.Write(s);
    }

    cases.Add(new
    {
        label, file = Path.GetFileName(wav), rpmStart = a, rpmEnd = b, frames = pcm.Length,
        peak, rms = Math.Sqrt(sum / pcm.Length), nonFinite = nan,
        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(wav))).ToLowerInvariant()
    });
    Console.WriteLine($"{wav} rpm={a}->{b} peak={peak:F3} rms={Math.Sqrt(sum / pcm.Length):F4} nonFinite={nan}");
}

var manifest = new
{
    patch = name, dspSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dspPath))).ToLowerInvariant(),
    sampleRate = Rate, blockFrames = Frames, seconds, rpmControl = rpmPath,
    os = RuntimeInformation.OSDescription, faustHome = home,
    faustLibrary = home is null ? null : AquaSynthFaustToolchain.FindLibrary(home),
    cases
};
File.WriteAllText(Path.Combine(outDir, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
return 0;
