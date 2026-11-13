using System.Text.Json;
using AquaSynth.Dings;
using AquaSynth.Dsl;

// Lowers .aqua patches to Faust source for the static playground (tools/aqua-playground).
// usage: AquaLower <outDir> <patchesRoot> <name>=<relative .aqua or .dsp path> ...
// A .aqua entry goes through FaustEmitter.EmitScript, the same lowering the daemon uses; a .dsp entry
// (the thruster) is already Faust and is copied verbatim. Writes <outDir>/<name>.dsp, and
// catalog.json for the browser (the one non-CultNet xeno at this boundary).
// usage: AquaLower --dings <outDir>: lowers each DingCatalog instrument to <outDir>/<id>.dsp and writes dings.json
// (instruments with their root frequency, events with their notes) so aqua-feel can mix an event the way DingsPlaybackHost does.
if (args.Length == 2 && args[0] == "--dings")
{
    var dingDir = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(dingDir);
    var instruments = new List<object>();
    foreach (var ins in DingCatalog.Instruments.Values.OrderBy(i => i.Id, StringComparer.Ordinal))
    {
        // 48000 (the FaustExportOptions default) is the Ding catalog contract: DingCatalogTests pins the sources at that rate.
        var export = FaustEmitter.EmitScript(ins.Script, new FaustExportOptions(DingCatalog.Identity(ins).FaustName));
        File.WriteAllText(Path.Combine(dingDir, ins.Id + ".dsp"), export.Source);
        instruments.Add(new { id = ins.Id, rootFrequency = ins.RootFrequency, dsp = ins.Id + ".dsp", controlPath = "/ding/frequency", warnings = export.Warnings });
        Console.WriteLine($"{ins.Id}: {export.Source.Length} chars, {export.Warnings.Count} warnings");
    }
    var events = DingCatalog.Events.Values.OrderBy(e => e.Id, StringComparer.Ordinal)
        .Select(e => new { id = e.Id, meaning = e.Meaning, notes = e.Notes.Select(n => new { semitones = n.Semitones, delayMilliseconds = n.DelayMilliseconds, gain = n.Gain }) });
    File.WriteAllText(Path.Combine(dingDir, "dings.json"), JsonSerializer.Serialize(new { instruments, events, noteSeconds = 1.5 }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
if (args.Length < 3) { Console.Error.WriteLine("usage: AquaLower <outDir> <patchesRoot> <name>=<path> ..."); return 2; }
var outDir = Path.GetFullPath(args[0]);
var root = Path.GetFullPath(args[1]);
Directory.CreateDirectory(outDir);
var entries = new List<object>();
foreach (var spec in args.Skip(2))
{
    var eq = spec.IndexOf('=');
    var name = spec[..eq];
    var rel = spec[(eq + 1)..];
    var path = Path.Combine(root, rel);
    var text = File.ReadAllText(path);
    string source;
    var warnings = new List<string>();
    if (path.EndsWith(".aqua", StringComparison.Ordinal))
    {
        // 48000 (the FaustExportOptions default): AquaLower lowers for the 48 kHz hosts.
        var export = FaustEmitter.EmitScript(text, new FaustExportOptions(name));
        source = export.Source;
        warnings.AddRange(export.Warnings);
    }
    else source = text;
    var dspFile = StoreFileName.From(name) + ".dsp";
    File.WriteAllText(Path.Combine(outDir, dspFile), source);
    entries.Add(new { name, origin = rel.Replace('\\', '/'), kind = path.EndsWith(".aqua", StringComparison.Ordinal) ? "aqua" : "faust", dsp = dspFile, warnings });
    Console.WriteLine($"{name}: {source.Length} chars from {rel}, {warnings.Count} warnings");
}
File.WriteAllText(Path.Combine(outDir, "catalog.json"), JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
return 0;
