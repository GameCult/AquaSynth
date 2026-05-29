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
        var samples = new List<ProbeTimelineSample>();

        for (var block = 0; block < Math.Max(1, blocks); block++)
        {
            var outgoingByPath = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var pathName in network.Paths)
            {
                var path = paths[pathName];
                var area = areas[path.AreaFunction];
                var pathArea = area.Shape.Areas.Count == 0 ? 0 : area.Shape.Areas.Average();
                var delay = area.Shape.LengthMeters / Math.Max(1, path.PropagationSpeedMetersPerSecond) * 48000;
                var incoming = PathIncomingFlow(network, sources, contacts, branches, path.Name);
                var outgoing = incoming * Math.Clamp(path.Loss, 0, 1);
                outgoingByPath[path.Name] = outgoing;
                var energyIn = pathArea * incoming * incoming;
                var energyOut = pathArea * outgoing * outgoing;
                Add(samples, block, $"path:{path.Name}", "area", pathArea);
                Add(samples, block, $"path:{path.Name}", "delay_samples", delay);
                Add(samples, block, $"path:{path.Name}", "loss", path.Loss);
                Add(samples, block, $"path:{path.Name}", "incoming_wave", incoming);
                Add(samples, block, $"path:{path.Name}", "outgoing_wave", outgoing);
                Add(samples, block, $"path:{path.Name}", "energy_in", energyIn);
                Add(samples, block, $"path:{path.Name}", "energy_out", energyOut);
                Add(samples, block, $"path:{path.Name}", "passivity_ratio", energyIn <= 0.000001f ? 1 : energyOut / energyIn);
            }

            foreach (var sourceName in network.Sources)
            {
                var source = sources[sourceName];
                var flow = SourceFlow(source);
                Add(samples, block, $"source:{source.Name}", "load_pressure", source.Pressure * source.Impedance);
                Add(samples, block, $"source:{source.Name}", "flow_scale", source.FlowScale);
                Add(samples, block, $"source:{source.Name}", "flow", flow);
            }

            foreach (var contactName in network.Contacts)
            {
                var contact = contacts[contactName];
                var opening = Math.Clamp(contact.Opening, 0, 1);
                var reservoir = contact.StoredPressure + (1 - opening) * contact.Resistance;
                Add(samples, block, $"contact:{contact.Name}", "opening", opening);
                Add(samples, block, $"contact:{contact.Name}", "resistance", contact.Resistance);
                Add(samples, block, $"contact:{contact.Name}", "reservoir", reservoir);
                Add(samples, block, $"contact:{contact.Name}", "released_flow", reservoir * opening * (1 - Math.Min(0.95f, contact.Resistance * 0.5f)));
            }

            foreach (var branchName in network.Branches)
            {
                var branch = branches[branchName];
                var admittance = Math.Clamp(branch.Opening, 0, 1) * Math.Clamp(branch.Coupling, 0, 1);
                Add(samples, block, $"branch:{branch.Name}", "admittance", admittance);
                Add(samples, block, $"branch:{branch.Name}", "exchanged_flow", -admittance * 0.05f);
            }

            foreach (var loadName in network.Radiation)
            {
                var load = radiation[loadName];
                var aperture = Math.Clamp(load.Aperture, 0, 1);
                var pathWave = outgoingByPath.GetValueOrDefault(load.Path, 0);
                var flow = pathWave * aperture / Math.Max(0.05f, load.Impedance);
                Add(samples, block, $"radiation:{load.Name}", "reflection", load.Reflection);
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
        VocalNetwork network,
        IReadOnlyDictionary<string, SourcePort> sources,
        IReadOnlyDictionary<string, ConstrictionContact> contacts,
        IReadOnlyDictionary<string, BranchPort> branches,
        string pathName)
    {
        var sourceFlow = network.Sources
            .Select(name => sources[name])
            .Where(source => source.Path.Equals(pathName, StringComparison.OrdinalIgnoreCase))
            .Sum(SourceFlow);
        var contactFlow = network.Contacts
            .Select(name => contacts[name])
            .Where(contact => contact.Path.Equals(pathName, StringComparison.OrdinalIgnoreCase))
            .Sum(contact =>
            {
                var opening = Math.Clamp(contact.Opening, 0, 1);
                var reservoir = contact.StoredPressure + (1 - opening) * contact.Resistance;
                return reservoir * opening * (1 - Math.Min(0.95f, contact.Resistance * 0.5f));
            });
        var branchFlow = network.Branches
            .Select(name => branches[name])
            .Where(branch => branch.FromPath.Equals(pathName, StringComparison.OrdinalIgnoreCase))
            .Sum(branch => -Math.Clamp(branch.Opening, 0, 1) * Math.Clamp(branch.Coupling, 0, 1) * 0.05f);
        return sourceFlow + contactFlow + branchFlow;
    }

    private static float SourceFlow(SourcePort source) =>
        source.FlowScale * MathF.Tanh(source.Pressure * Math.Max(0, source.Opening) * (0.5f + source.Tension) / Math.Max(0.05f, source.Impedance));

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}
