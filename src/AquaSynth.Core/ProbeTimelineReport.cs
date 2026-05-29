using System.Globalization;
using System.Text;

namespace AquaSynth.Dsl;

public sealed record ProbeTimelineSample(int Block, string Primitive, string Signal, float Value);

public static class ProbeTimelineReport
{
    public static IReadOnlyList<ProbeTimelineSample> Build(SynthPatch patch, string networkName, int blocks = 4)
    {
        var network = patch.VocalNetworks.First(network => network.Name.Equals(networkName, StringComparison.OrdinalIgnoreCase));
        var areas = patch.AreaFunctions.ToDictionary(area => area.Name, StringComparer.OrdinalIgnoreCase);
        var paths = patch.WaveguidePaths.ToDictionary(path => path.Name, StringComparer.OrdinalIgnoreCase);
        var sources = patch.SourcePorts.ToDictionary(port => port.Name, StringComparer.OrdinalIgnoreCase);
        var contacts = patch.ConstrictionContacts.ToDictionary(contact => contact.Name, StringComparer.OrdinalIgnoreCase);
        var branches = patch.BranchPorts.ToDictionary(branch => branch.Name, StringComparer.OrdinalIgnoreCase);
        var radiation = patch.RadiationLoads.ToDictionary(load => load.Name, StringComparer.OrdinalIgnoreCase);
        var surfacesByField = patch.ControlSurfaces.ToDictionary(surface => surface.FieldPath, StringComparer.OrdinalIgnoreCase);
        var splinesBySurface = patch.ControlSplines
            .Where(spline => spline.Enabled)
            .GroupBy(spline => spline.SurfacePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var samples = new List<ProbeTimelineSample>();
        var blockSize = patch.ProbeTimelines.FirstOrDefault(probe => probe.Name.Equals(network.ProbeTimeline, StringComparison.OrdinalIgnoreCase))?.BlockSize ?? 64;

        for (var block = 0; block < Math.Max(1, blocks); block++)
        {
            var timeSeconds = block * blockSize / 48000f;
            var outgoingByPath = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var pathName in network.Paths)
            {
                var path = paths[pathName];
                var area = areas[path.AreaFunction];
                var pathArea = area.Shape.Areas.Count == 0 ? 0 : area.Shape.Areas.Average();
                var pathIndex = PathIndex(patch.WaveguidePaths, path);
                var speed = SurfaceValue(surfacesByField, splinesBySurface, $"/vocal/paths/{pathIndex}/speed", path.PropagationSpeedMetersPerSecond, timeSeconds);
                var loss = SurfaceValue(surfacesByField, splinesBySurface, $"/vocal/paths/{pathIndex}/loss", path.Loss, timeSeconds);
                var delay = area.Shape.LengthMeters / Math.Max(1, speed) * 48000;
                var incoming = PathIncomingFlow(patch, network, sources, contacts, branches, path.Name, surfacesByField, splinesBySurface, timeSeconds);
                var outgoing = incoming * Math.Clamp(loss, 0, 1);
                outgoingByPath[path.Name] = outgoing;
                var energyIn = pathArea * incoming * incoming;
                var energyOut = pathArea * outgoing * outgoing;
                Add(samples, block, $"path:{path.Name}", "area", pathArea);
                Add(samples, block, $"path:{path.Name}", "delay_samples", delay);
                Add(samples, block, $"path:{path.Name}", "loss", loss);
                Add(samples, block, $"path:{path.Name}", "incoming_wave", incoming);
                Add(samples, block, $"path:{path.Name}", "outgoing_wave", outgoing);
                Add(samples, block, $"path:{path.Name}", "energy_in", energyIn);
                Add(samples, block, $"path:{path.Name}", "energy_out", energyOut);
                Add(samples, block, $"path:{path.Name}", "passivity_ratio", energyIn <= 0.000001f ? 1 : energyOut / energyIn);
            }

            foreach (var sourceName in network.Sources)
            {
                var source = sources[sourceName];
                var sourceIndex = PathIndex(patch.SourcePorts, source);
                var owner = $"/vocal/sources/{sourceIndex}";
                var pressure = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/pressure", source.Pressure, timeSeconds);
                var impedance = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/impedance", source.Impedance, timeSeconds);
                var flowScale = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/flow_scale", source.FlowScale, timeSeconds);
                var flow = SourceFlow(patch, source, surfacesByField, splinesBySurface, timeSeconds);
                Add(samples, block, $"source:{source.Name}", "load_pressure", pressure * impedance);
                Add(samples, block, $"source:{source.Name}", "flow_scale", flowScale);
                Add(samples, block, $"source:{source.Name}", "flow", flow);
            }

            foreach (var contactName in network.Contacts)
            {
                var contact = contacts[contactName];
                var contactIndex = PathIndex(patch.ConstrictionContacts, contact);
                var owner = $"/vocal/contacts/{contactIndex}";
                var opening = Math.Clamp(SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/opening", contact.Opening, timeSeconds), 0, 1);
                var resistance = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/resistance", contact.Resistance, timeSeconds);
                var stored = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/stored_pressure", contact.StoredPressure, timeSeconds);
                var reservoir = stored + (1 - opening) * resistance;
                Add(samples, block, $"contact:{contact.Name}", "opening", opening);
                Add(samples, block, $"contact:{contact.Name}", "resistance", resistance);
                Add(samples, block, $"contact:{contact.Name}", "reservoir", reservoir);
                Add(samples, block, $"contact:{contact.Name}", "released_flow", reservoir * opening * (1 - Math.Min(0.95f, resistance * 0.5f)));
            }

            foreach (var branchName in network.Branches)
            {
                var branch = branches[branchName];
                var branchIndex = PathIndex(patch.BranchPorts, branch);
                var owner = $"/vocal/branches/{branchIndex}";
                var opening = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/opening", branch.Opening, timeSeconds);
                var coupling = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/coupling", branch.Coupling, timeSeconds);
                var admittance = Math.Clamp(opening, 0, 1) * Math.Clamp(coupling, 0, 1);
                Add(samples, block, $"branch:{branch.Name}", "admittance", admittance);
                Add(samples, block, $"branch:{branch.Name}", "exchanged_flow", -admittance * 0.05f);
            }

            foreach (var loadName in network.Radiation)
            {
                var load = radiation[loadName];
                var radiationIndex = PathIndex(patch.RadiationLoads, load);
                var owner = $"/vocal/radiation/{radiationIndex}";
                var aperture = Math.Clamp(SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/aperture", load.Aperture, timeSeconds), 0, 1);
                var impedance = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/impedance", load.Impedance, timeSeconds);
                var reflection = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/reflection", load.Reflection, timeSeconds);
                var pathWave = outgoingByPath.GetValueOrDefault(load.Path, 0);
                var flow = pathWave * aperture / Math.Max(0.05f, impedance);
                Add(samples, block, $"radiation:{load.Name}", "reflection", reflection);
                Add(samples, block, $"radiation:{load.Name}", "boundary_flow", flow);
                Add(samples, block, $"radiation:{load.Name}", "flow", flow);
                Add(samples, block, $"radiation:{load.Name}", "output", flow);
            }
        }

        return samples;
    }

    public static string ToCsv(IReadOnlyList<ProbeTimelineSample> samples)
    {
        var builder = new StringBuilder();
        builder.AppendLine("block,primitive,signal,value");
        foreach (var sample in samples)
        {
            builder.Append(sample.Block);
            builder.Append(',');
            builder.Append(Escape(sample.Primitive));
            builder.Append(',');
            builder.Append(Escape(sample.Signal));
            builder.Append(',');
            builder.AppendLine(sample.Value.ToString("0.######", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void Add(List<ProbeTimelineSample> samples, int block, string primitive, string signal, float value) =>
        samples.Add(new ProbeTimelineSample(block, primitive, signal, value));

    private static float PathIncomingFlow(
        SynthPatch patch,
        VocalNetwork network,
        IReadOnlyDictionary<string, SourcePort> sources,
        IReadOnlyDictionary<string, ConstrictionContact> contacts,
        IReadOnlyDictionary<string, BranchPort> branches,
        string pathName,
        IReadOnlyDictionary<string, ControlSurface> surfacesByField,
        IReadOnlyDictionary<string, ControlSpline[]> splinesBySurface,
        float timeSeconds)
    {
        var sourceFlow = network.Sources
            .Select(name => sources[name])
            .Where(source => source.Path.Equals(pathName, StringComparison.OrdinalIgnoreCase))
            .Sum(source => SourceFlow(patch, source, surfacesByField, splinesBySurface, timeSeconds));
        var contactFlow = network.Contacts
            .Select(name => contacts[name])
            .Where(contact => contact.Path.Equals(pathName, StringComparison.OrdinalIgnoreCase))
            .Sum(contact =>
            {
                var contactIndex = PathIndex(patch.ConstrictionContacts, contact);
                var owner = $"/vocal/contacts/{contactIndex}";
                var opening = Math.Clamp(SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/opening", contact.Opening, timeSeconds), 0, 1);
                var resistance = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/resistance", contact.Resistance, timeSeconds);
                var stored = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/stored_pressure", contact.StoredPressure, timeSeconds);
                var reservoir = stored + (1 - opening) * resistance;
                return reservoir * opening * (1 - Math.Min(0.95f, resistance * 0.5f));
            });
        var branchFlow = network.Branches
            .Select(name => branches[name])
            .Where(branch => branch.FromPath.Equals(pathName, StringComparison.OrdinalIgnoreCase))
            .Sum(branch =>
            {
                var branchIndex = PathIndex(patch.BranchPorts, branch);
                var owner = $"/vocal/branches/{branchIndex}";
                var opening = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/opening", branch.Opening, timeSeconds);
                var coupling = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/coupling", branch.Coupling, timeSeconds);
                return -Math.Clamp(opening, 0, 1) * Math.Clamp(coupling, 0, 1) * 0.05f;
            });
        return sourceFlow + contactFlow + branchFlow;
    }

    private static float SourceFlow(
        SynthPatch patch,
        SourcePort source,
        IReadOnlyDictionary<string, ControlSurface> surfacesByField,
        IReadOnlyDictionary<string, ControlSpline[]> splinesBySurface,
        float timeSeconds)
    {
        var sourceIndex = PathIndex(patch.SourcePorts, source);
        var owner = $"/vocal/sources/{sourceIndex}";
        var pressure = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/pressure", source.Pressure, timeSeconds);
        var opening = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/opening", source.Opening, timeSeconds);
        var tension = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/tension", source.Tension, timeSeconds);
        var impedance = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/impedance", source.Impedance, timeSeconds);
        var flowScale = SurfaceValue(surfacesByField, splinesBySurface, $"{owner}/flow_scale", source.FlowScale, timeSeconds);
        return flowScale * MathF.Tanh(pressure * Math.Max(0, opening) * (0.5f + tension) / Math.Max(0.05f, impedance));
    }

    private static float SurfaceValue(
        IReadOnlyDictionary<string, ControlSurface> surfacesByField,
        IReadOnlyDictionary<string, ControlSpline[]> splinesBySurface,
        string fieldPath,
        float fallback,
        float timeSeconds)
    {
        if (!surfacesByField.TryGetValue(fieldPath, out var surface))
        {
            return fallback;
        }

        var normalized = surface.DefaultNormalized;
        if (splinesBySurface.TryGetValue(surface.Path, out var splines))
        {
            foreach (var spline in splines)
            {
                normalized += ControlSplineTimeline.Evaluate(spline, timeSeconds, surface.DefaultNormalized) - surface.DefaultNormalized;
            }
        }

        normalized = Math.Clamp(normalized, 0, 1);
        return surface.MinValue + (surface.MaxValue - surface.MinValue) * normalized;
    }

    private static int PathIndex<T>(IReadOnlyList<T> items, T item)
        where T : class
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item) || EqualityComparer<T>.Default.Equals(items[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}
