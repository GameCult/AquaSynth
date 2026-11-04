using System.Text.Json;
using AquaSynth.Dsl;

// Lowers .aqua patches to Faust source for the static playground (tools/aqua-playground).
// usage: AquaLower <outDir> <patchesRoot> <name>=<relative .aqua or .dsp path> ...
// A .aqua entry goes through FaustEmitter.EmitScript, the same lowering the daemon uses; a .dsp entry
// (the thruster) is already Faust and is copied verbatim. Writes <outDir>/<name>.dsp, and
// catalog.json for the browser (the one non-CultNet xeno at this boundary).
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
        var export = FaustEmitter.EmitScript(text, new FaustExportOptions(name));
        source = export.Source;
        warnings.AddRange(export.Warnings);
    }
    else source = text;
    File.WriteAllText(Path.Combine(outDir, name + ".dsp"), source);
    entries.Add(new { name, origin = rel.Replace('\\', '/'), kind = path.EndsWith(".aqua", StringComparison.Ordinal) ? "aqua" : "faust", dsp = name + ".dsp", warnings });
    Console.WriteLine($"{name}: {source.Length} chars from {rel}, {warnings.Count} warnings");
}
File.WriteAllText(Path.Combine(outDir, "catalog.json"), JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
return 0;
