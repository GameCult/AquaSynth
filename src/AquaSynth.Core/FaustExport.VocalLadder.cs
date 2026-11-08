using System.Text;

namespace AquaSynth.Dsl;

// The vocal_network resonator: a Kelly-Lochbaum ladder per waveguide path, one cell per sample (cell length c / sample
// rate), written as a Faust `letrec` so every wave variable is a one-sample state. Cell areas come from the morphology
// rest shape deformed by the path's area control, or from its measured vowel shapes (DeformedAreaExpression owns both). A branch port whose
// two paths are both in the network becomes a three-port parallel junction (Pink Trombone's velum: ports are the oral
// cells either side of the join and the branch path's first cell, whose area is the branch opening).
public static partial class FaustEmitter
{
    // Reflection at the closed glottal end of a path that carries a source.
    private const float LadderGlottalReflection = 0.75f;
    // Speed of sound in cm/s used only to size the lip end correction from the lip radius (the ladder's own cell length uses the path's speed).
    private const float LipEndCorrectionSoundSpeedCentimeters = 35000f;

    private sealed record LadderPath(
        WaveguidePath Path,
        AreaFunction Area,
        int AreaIndex,
        int PathIndex,
        string Prefix,
        int Cells,
        RadiationLoad? Radiation,
        int RadiationIndex)
    {
        public string A(int i) => $"{Prefix}_A{i}";
        public string R(int i) => $"{Prefix}_R{i}";
        public string L(int i) => $"{Prefix}_L{i}";
        public string Refl(int i) => $"{Prefix}_r{i}";
    }

    private sealed record LadderJunction(LadderPath Oral, LadderPath Branch, int Interface, string Velum, string Left, string Right, string Nose);

    private static int LadderCells(WaveguidePath path, TractAreaFunction shape, FaustExportOptions options) =>
        Math.Max(3, (int)MathF.Round(shape.LengthMeters * options.SampleRate / Math.Max(1f, path.PropagationSpeedMetersPerSecond)));

    /// <summary>
    /// Emits the area, reflection and junction definitions and the recursive ladder for <paramref name="members"/>, and returns,
    /// per path, the identifier of its lip output (the right-going wave leaving its last cell).
    /// </summary>
    private static IReadOnlyDictionary<string, string> EmitVocalLadder(
        StringBuilder source,
        SynthPatch patch,
        VocalNetwork network,
        string name,
        IReadOnlyList<LadderPath> members,
        IReadOnlyDictionary<string, string> inputs,
        IReadOnlyDictionary<string, string> admittances,
        ParameterMap parameters,
        List<string> warnings)
    {
        var junctions = new List<LadderJunction>();
        foreach (var branchName in network.Branches)
        {
            var branch = patch.BranchPorts.FirstOrDefault(candidate => candidate.Name.Equals(branchName, StringComparison.OrdinalIgnoreCase));
            var oral = branch is null ? null : members.FirstOrDefault(member => member.Path.Name.Equals(branch.FromPath, StringComparison.OrdinalIgnoreCase));
            var nasal = branch is null ? null : members.FirstOrDefault(member => member.Path.Name.Equals(branch.ToPath, StringComparison.OrdinalIgnoreCase));
            if (branch is null || oral is null || nasal is null || oral == nasal)
            {
                continue;
            }

            var cell = Math.Clamp((int)MathF.Round(Math.Clamp(branch.FromPosition, 0, 1) * oral.Cells), 1, oral.Cells - 2);
            if (junctions.Any(existing => existing.Oral == oral && existing.Interface == cell) || junctions.Any(existing => existing.Branch == nasal))
            {
                warnings.Add($"{name}: branch `{branch.Name}` shares a junction cell or target path with another branch; ignored by the ladder");
                continue;
            }

            // The velopharyngeal port area, cm^2: opening * coupling * the design maximum (NasalTractAreas); it is the nasal ladder's first cell.
            var velum = $"{name}_primitive_branch_{SafeIdentifier(branch.Name)}_velum_area";
            source.AppendLine($"    {velum} = max(0.000001, {admittances[branch.Name]} * {F(NasalTractAreas.VelopharyngealPortMaximumAreaSquareCentimeters)});");
            var sum = $"({oral.A(cell)} + {oral.A(cell + 1)} + {velum})";
            var key = $"{name}_primitive_branch_{SafeIdentifier(branch.Name)}";
            source.AppendLine($"    {key}_refl_left = (2.0 * {oral.A(cell)} - {sum}) / {sum};");
            source.AppendLine($"    {key}_refl_right = (2.0 * {oral.A(cell + 1)} - {sum}) / {sum};");
            source.AppendLine($"    {key}_refl_nose = (2.0 * {velum} - {sum}) / {sum};");
            junctions.Add(new LadderJunction(oral, nasal, cell, velum, $"{key}_refl_left", $"{key}_refl_right", $"{key}_refl_nose"));
        }

        var targets = junctions.ToDictionary(junction => junction.Branch);
        var joinedAt = junctions.ToDictionary(junction => (junction.Oral, junction.Interface));
        var equations = new StringBuilder();

        foreach (var member in members)
        {
            var targetJunction = targets.GetValueOrDefault(member);
            for (var i = 0; i < member.Cells; i++)
            {
                if (i == 0 && targetJunction is not null)
                {
                    source.AppendLine($"    {member.A(0)} = {targetJunction.Velum};");
                    continue;
                }

                var area = DeformedAreaExpression(parameters, member.Area.Shape, member.Area.Deformation, $"/vocal/areas/{member.AreaIndex}/area", (i + 0.5f) / member.Cells, member.Area.Vowels);
                source.AppendLine($"    {member.A(i)} = max(0.000001, {area});");
            }

            for (var i = 1; i < member.Cells; i++)
            {
                source.AppendLine($"    {member.Refl(i)} = ({member.A(i - 1)} - {member.A(i)}) / ({member.A(i - 1)} + {member.A(i)});");
            }

            var loss = $"clip01({parameters.Expression(OwnerField($"/vocal/paths/{member.PathIndex}", "loss"), member.Path.Loss)})";
            var lipReflection = member.Radiation is { } load
                ? parameters.Expression(OwnerField($"/vocal/radiation/{member.RadiationIndex}", "reflection"), load.Reflection)
                : F(new RadiationLoad("", "").Reflection);
            var last = member.Cells - 1;

            equations.AppendLine(targetJunction is { } entering
                ? $"        '{member.R(0)} = ({entering.Oral.L(entering.Interface)} + {entering.Oral.R(entering.Interface - 1)}) * (1.0 + {entering.Nose}) * {loss} + {entering.Nose} * {member.L(0)} * {loss};"
                : $"        '{member.R(0)} = ({member.L(0)} * {F(LadderGlottalReflection)} + {(inputs.TryGetValue(member.Path.Name, out var input) ? input : "0.0")}) * {loss};");
            for (var i = 1; i < member.Cells; i++)
            {
                equations.AppendLine(joinedAt.TryGetValue((member, i), out var join)
                    ? $"        '{member.R(i)} = ({join.Right} * {member.L(i)} + (1.0 + {join.Right}) * ({member.R(i - 1)} + {join.Branch.L(0)})) * {loss};"
                    : $"        '{member.R(i)} = ({member.R(i - 1)} - {member.Refl(i)} * ({member.R(i - 1)} + {member.L(i)})) * {loss};");
            }

            for (var i = 0; i < last; i++)
            {
                equations.AppendLine(joinedAt.TryGetValue((member, i + 1), out var join)
                    ? $"        '{member.L(i)} = ({join.Left} * {member.R(i)} + (1.0 + {join.Left}) * ({join.Branch.L(0)} + {member.L(i + 1)})) * {loss};"
                    : $"        '{member.L(i)} = ({member.L(i + 1)} + {member.Refl(i + 1)} * ({member.R(i)} + {member.L(i + 1)})) * {loss};");
            }

            // Open-end reflectance R(z) = -g (1-b)/(1-b z^-1): g is the radiation load's reflection (DC gain, negative), b follows the
            // lip cell's area so that the low-frequency loss 1-|R|^2 ~ (ka)^2 and the DC group delay b/(1-b) is the end correction
            // (Levine & Schwinger; Smith PASP; Valimaki & Karjalainen 1994). With a = sqrt(A/pi) in cm, q = (a fs / c)^2, c = 35000 cm/s,
            // b/(1-b)^2 = q gives b = ((2q+1) - sqrt(4q+1)) / (2q). The pole is one-sample state in the same slot the constant used.
            var q = $"{member.Prefix}_lip_q";
            var b = $"{member.Prefix}_lip_b";
            source.AppendLine($"    {q} = max(0.000001, pow(sqrt({member.A(last)} / ma.PI) * ma.SR / {F(LipEndCorrectionSoundSpeedCentimeters)}, 2.0));");
            source.AppendLine($"    {b} = min(0.9, max(0.0, ((2.0 * {q} + 1.0) - sqrt(4.0 * {q} + 1.0)) / (2.0 * {q})));");
            equations.AppendLine($"        '{member.L(last)} = ({b} * {member.L(last)} + (1.0 - {b}) * ({lipReflection}) * {member.R(last)}) * {loss};");
        }

        var outputs = string.Join(", ", members.Select(member => member.R(member.Cells - 1)));
        var ladder = $"{name}_primitive_ladder";
        source.AppendLine($"    {ladder} = ({outputs}) letrec {{");
        source.Append(equations);
        source.AppendLine("    };");

        var lips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var m = 0; m < members.Count; m++)
        {
            var lip = $"{members[m].Prefix}_lip";
            source.AppendLine(members.Count == 1
                ? $"    {lip} = {ladder};"
                : $"    {lip} = {ladder} : ba.selector({m}, {members.Count});");
            lips[members[m].Path.Name] = lip;
        }

        return lips;
    }
}
