using System.Globalization;

namespace AquaSynth.Dsl;

public static class PrimitiveTimelineFactExtractor
{
    public static IReadOnlyList<PrimitiveTimelineFact> Extract(IReadOnlyList<ProbeTimelineSample> timeline)
    {
        if (timeline.Count == 0)
        {
            return [];
        }

        var facts = new List<PrimitiveTimelineFact>
        {
            Fact(
                "timeline_blocks",
                "timeline",
                "block_count",
                timeline.Select(sample => sample.Block).Distinct().Count(),
                "blocks",
                timeline.Min(sample => sample.Block),
                timeline.Max(sample => sample.Block),
                "Observed primitive timeline block count.")
        };

        AddSignalFacts(facts, timeline, primitivePrefix: "contact:", signal: "opening", "contact_min_opening", "normalized", Min);
        AddSignalFacts(facts, timeline, primitivePrefix: "contact:", signal: "opening", "contact_mean_opening", "normalized", Mean);
        AddSignalFacts(facts, timeline, primitivePrefix: "contact:", signal: "opening", "contact_closed_blocks", "blocks", ClosedBlocks);
        AddSignalFacts(facts, timeline, primitivePrefix: "contact:", signal: "reservoir", "contact_reservoir_peak", "pressure", MaxAbs);
        AddSignalFacts(facts, timeline, primitivePrefix: "contact:", signal: "released_flow", "contact_release_peak", "flow", MaxAbs);
        AddPeakBlockFacts(facts, timeline, "contact:", "released_flow", "contact_release_peak_block");

        AddSignalFacts(facts, timeline, primitivePrefix: "source:", signal: "load_pressure", "source_load_pressure_peak", "pressure", MaxAbs);
        AddSignalFacts(facts, timeline, primitivePrefix: "source:", signal: "flow", "source_flow_peak", "flow", MaxAbs);
        AddPeakBlockFacts(facts, timeline, "source:", "flow", "source_flow_peak_block");

        AddSignalFacts(facts, timeline, primitivePrefix: "branch:", signal: "admittance", "branch_admittance_peak", "admittance", MaxAbs);
        AddSignalFacts(facts, timeline, primitivePrefix: "branch:", signal: "exchanged_flow", "branch_exchanged_flow_peak", "flow", MaxAbs);

        AddSignalFacts(facts, timeline, primitivePrefix: "radiation:", signal: "boundary_flow", "radiation_boundary_flow_peak", "flow", MaxAbs);
        AddSignalFacts(facts, timeline, primitivePrefix: "radiation:", signal: "output", "radiation_output_peak", "amplitude", MaxAbs);
        AddSignalFacts(facts, timeline, primitivePrefix: "radiation:", signal: "output", "radiation_output_mean_abs", "amplitude", MeanAbs);
        AddPeakBlockFacts(facts, timeline, "radiation:", "output", "radiation_output_peak_block");

        AddSignalFacts(facts, timeline, primitivePrefix: "path:", signal: "area", "path_mean_area", "area", Mean);
        AddSignalFacts(facts, timeline, primitivePrefix: "path:", signal: "delay_samples", "path_mean_delay_samples", "samples", Mean);
        AddSignalFacts(facts, timeline, primitivePrefix: "path:", signal: "energy_in", "path_energy_in_sum", "energy", Sum);
        AddSignalFacts(facts, timeline, primitivePrefix: "path:", signal: "energy_out", "path_energy_out_sum", "energy", Sum);
        AddSignalFacts(facts, timeline, primitivePrefix: "path:", signal: "passivity_ratio", "path_passivity_max", "ratio", Max);

        return facts
            .Where(fact => float.IsFinite(fact.Value))
            .OrderBy(fact => fact.Name, StringComparer.Ordinal)
            .ThenBy(fact => fact.Primitive, StringComparer.Ordinal)
            .ThenBy(fact => fact.BlockStart)
            .ToArray();
    }

    private static void AddSignalFacts(
        List<PrimitiveTimelineFact> facts,
        IReadOnlyList<ProbeTimelineSample> timeline,
        string primitivePrefix,
        string signal,
        string name,
        string unit,
        Func<IReadOnlyList<ProbeTimelineSample>, float> reducer)
    {
        foreach (var group in Matching(timeline, primitivePrefix, signal).GroupBy(sample => sample.Primitive, StringComparer.OrdinalIgnoreCase))
        {
            var samples = group.ToArray();
            if (samples.Length == 0)
            {
                continue;
            }

            var value = reducer(samples);
            facts.Add(Fact(
                name,
                group.Key,
                signal,
                value,
                unit,
                samples.Min(sample => sample.Block),
                samples.Max(sample => sample.Block),
                Summary(name, group.Key, signal, value, unit)));
        }
    }

    private static void AddPeakBlockFacts(
        List<PrimitiveTimelineFact> facts,
        IReadOnlyList<ProbeTimelineSample> timeline,
        string primitivePrefix,
        string signal,
        string name)
    {
        foreach (var group in Matching(timeline, primitivePrefix, signal).GroupBy(sample => sample.Primitive, StringComparer.OrdinalIgnoreCase))
        {
            var peak = group
                .OrderByDescending(sample => Math.Abs(sample.Value))
                .ThenBy(sample => sample.Block)
                .FirstOrDefault();
            if (peak is null)
            {
                continue;
            }

            facts.Add(Fact(
                name,
                peak.Primitive,
                signal,
                peak.Block,
                "block",
                peak.Block,
                peak.Block,
                Summary(name, peak.Primitive, signal, peak.Block, "block")));
        }
    }

    private static IEnumerable<ProbeTimelineSample> Matching(
        IReadOnlyList<ProbeTimelineSample> timeline,
        string primitivePrefix,
        string signal) =>
        timeline.Where(sample =>
            sample.Primitive.StartsWith(primitivePrefix, StringComparison.OrdinalIgnoreCase) &&
            sample.Signal.Equals(signal, StringComparison.OrdinalIgnoreCase) &&
            float.IsFinite(sample.Value));

    private static PrimitiveTimelineFact Fact(
        string name,
        string primitive,
        string signal,
        float value,
        string unit,
        int blockStart,
        int blockEnd,
        string summary) =>
        new(name, primitive, signal, value, unit, blockStart, blockEnd, summary);

    private static string Summary(string name, string primitive, string signal, float value, string unit) =>
        $"{name} {primitive} {signal} {value.ToString("0.######", CultureInfo.InvariantCulture)} {unit}";

    private static float Min(IReadOnlyList<ProbeTimelineSample> samples) => samples.Min(sample => sample.Value);

    private static float Max(IReadOnlyList<ProbeTimelineSample> samples) => samples.Max(sample => sample.Value);

    private static float Mean(IReadOnlyList<ProbeTimelineSample> samples) => samples.Average(sample => sample.Value);

    private static float Sum(IReadOnlyList<ProbeTimelineSample> samples) => samples.Sum(sample => sample.Value);

    private static float MaxAbs(IReadOnlyList<ProbeTimelineSample> samples) => samples.Max(sample => Math.Abs(sample.Value));

    private static float MeanAbs(IReadOnlyList<ProbeTimelineSample> samples) => samples.Average(sample => Math.Abs(sample.Value));

    private static float ClosedBlocks(IReadOnlyList<ProbeTimelineSample> samples) =>
        samples.Count(sample => sample.Value <= 0.2f);
}
