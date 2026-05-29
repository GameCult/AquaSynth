using System.Globalization;
using System.Text;

namespace AquaSynth.Dsl;

public sealed record PrimitiveReferenceSample(
    string Reference,
    string Primitive,
    string Signal,
    float Candidate,
    float Expected,
    float Error);

public static class PrimitiveReferenceReport
{
    public const string PinkTromboneReference = "pt-sndkit";

    public static IReadOnlyList<PrimitiveReferenceSample> ComparePinkTrombone(
        SynthPatch patch,
        string networkName,
        PinkTromboneFixtureControls controls)
    {
        var network = patch.VocalNetworks.First(network => network.Name.Equals(networkName, StringComparison.OrdinalIgnoreCase));
        var areas = patch.AreaFunctions.ToDictionary(area => area.Name, StringComparer.OrdinalIgnoreCase);
        var paths = patch.WaveguidePaths.ToDictionary(path => path.Name, StringComparer.OrdinalIgnoreCase);
        var contacts = patch.ConstrictionContacts.ToDictionary(contact => contact.Name, StringComparer.OrdinalIgnoreCase);
        var branches = patch.BranchPorts.ToDictionary(branch => branch.Name, StringComparer.OrdinalIgnoreCase);
        var radiation = patch.RadiationLoads.ToDictionary(load => load.Name, StringComparer.OrdinalIgnoreCase);
        var rows = new List<PrimitiveReferenceSample>();

        foreach (var pathName in network.Paths)
        {
            var path = paths[pathName];
            var area = areas[path.AreaFunction];
            var isNasal = path.Name.Contains("nasal", StringComparison.OrdinalIgnoreCase) ||
                area.Name.Contains("nasal", StringComparison.OrdinalIgnoreCase);
            Add(rows, $"area:{area.Name}", "sections", area.Shape.Sections, isNasal ? 28 : 44);
            Add(rows, $"area:{area.Name}", "length_cm", area.Shape.LengthCentimeters, isNasal ? 12 : 17);
            Add(rows, $"area:{area.Name}", "reflection_count", area.Shape.ReflectionCoefficients.Count, Math.Max(0, area.Shape.Sections - 1));
            Add(rows, $"path:{path.Name}", "delay_samples_48k", area.Shape.LengthMeters / Math.Max(1, path.PropagationSpeedMetersPerSecond) * 48000, area.Shape.LengthMeters / 343f * 48000);
        }

        foreach (var branchName in network.Branches)
        {
            var branch = branches[branchName];
            Add(rows, $"branch:{branch.Name}", "admittance", Math.Clamp(branch.Opening, 0, 1) * Math.Clamp(branch.Coupling, 0, 1), Math.Clamp(controls.Velum, 0, 1));
            Add(rows, $"branch:{branch.Name}", "junction_position", branch.FromPosition, 17f / 43f);
        }

        foreach (var contactName in network.Contacts)
        {
            var contact = contacts[contactName];
            Add(rows, $"contact:{contact.Name}", "position", contact.Position, controls.ConstrictionIndex / 43f);
            Add(rows, $"contact:{contact.Name}", "opening", contact.Opening, Math.Clamp(controls.ConstrictionDiameter / 2.5f, 0, 1));
            Add(rows, $"contact:{contact.Name}", "stored_pressure", contact.StoredPressure, controls.Burst);
        }

        foreach (var loadName in network.Radiation)
        {
            var load = radiation[loadName];
            Add(rows, $"radiation:{load.Name}", "aperture", load.Aperture, Math.Clamp(controls.LipOpening, 0, 1));
            Add(rows, $"radiation:{load.Name}", "reflection", load.Reflection, controls.LipReflection);
        }

        return rows;
    }

    public static IReadOnlyList<PrimitiveReferenceSample> ComparePinkTromboneTimeline(
        IReadOnlyList<ProbeTimelineSample> candidate,
        IReadOnlyList<PinkTromboneReferenceTimelineSample> reference)
    {
        var referenceByKey = reference
            .GroupBy(sample => (sample.Block, PrimitiveRole(sample.Primitive), sample.Signal))
            .ToDictionary(group => group.Key, group => group.First().Value);
        var rows = new List<PrimitiveReferenceSample>();

        foreach (var sample in candidate)
        {
            var role = PrimitiveRole(sample.Primitive);
            if (role.Length == 0)
            {
                continue;
            }

            if (!referenceByKey.TryGetValue((sample.Block, role, sample.Signal), out var expected))
            {
                continue;
            }

            Add(rows, role, sample.Signal, sample.Value, expected);
        }

        return rows;
    }

    public static string ToCsv(IReadOnlyList<PrimitiveReferenceSample> samples)
    {
        var builder = new StringBuilder();
        builder.AppendLine("reference,primitive,signal,candidate,expected,error");
        foreach (var sample in samples)
        {
            builder.Append(Escape(sample.Reference));
            builder.Append(',');
            builder.Append(Escape(sample.Primitive));
            builder.Append(',');
            builder.Append(Escape(sample.Signal));
            builder.Append(',');
            builder.Append(sample.Candidate.ToString("0.######", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(sample.Expected.ToString("0.######", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.AppendLine(sample.Error.ToString("0.######", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void Add(List<PrimitiveReferenceSample> rows, string primitive, string signal, float candidate, float expected) =>
        rows.Add(new PrimitiveReferenceSample(PinkTromboneReference, primitive, signal, candidate, expected, candidate - expected));

    private static string PrimitiveRole(string primitive)
    {
        if (primitive.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
        {
            return primitive.Contains("nasal", StringComparison.OrdinalIgnoreCase) || primitive.Contains("nose", StringComparison.OrdinalIgnoreCase)
                ? "path:nasal"
                : "path:oral";
        }

        if (primitive.StartsWith("branch:", StringComparison.OrdinalIgnoreCase))
        {
            return "branch:velopharynx";
        }

        if (primitive.StartsWith("contact:", StringComparison.OrdinalIgnoreCase))
        {
            return "contact:obstruction";
        }

        if (primitive.StartsWith("radiation:", StringComparison.OrdinalIgnoreCase))
        {
            return "radiation:lip";
        }

        return "";
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}
