namespace AquaSynthDingsMcp;

public sealed record DingNote(double Semitones, int DelayMilliseconds, float Gain = 1f);
public sealed record DingEvent(string Id, string Meaning, IReadOnlyList<DingNote> Notes);
public sealed record DingInstrument(
    string Id,
    string Description,
    string TimbreFamily,
    string Script,
    float RootFrequency,
    float Brightness,
    float DecaySeconds,
    bool UsesNoise);

public static class DingCatalog
{
    public static readonly IReadOnlyDictionary<string, DingEvent> Events = new DingEvent[]
    {
        new("session.start", "Opening welcome", [new(0, 0), new(7, 220)]),
        new("task.acknowledge", "Quiet ongoing acknowledgement", [new(7, 0)]),
        new("task.complete", "Satisfying closure", [new(7, 0), new(12, 220)]),
        new("task.error", "Gentle failure notice", [new(7, 0), new(4, 240), new(0, 480)]),
        new("input.required", "Input requested", [new(12, 0), new(5, 260)]),
        new("resource.limit", "Resource is holding", [new(0, 0), new(0, 280), new(0, 560)]),
        new("user.spam", "Curt rate warning", [new(9, 0), new(9, 190), new(9, 380)]),
        new("session.end", "Session closure", [new(7, 0), new(0, 240)]),
        new("task.progress", "Faint ignorable progress", [new(-5, 0, .45f)])
    }.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlyDictionary<string, DingInstrument> Instruments = new DingInstrument[]
    {
        Instrument("warm-bell", "Warm cabin bell; soft fundamental and restrained upper partial", "rounded-fm-bell", "sine", .17f, .004f, .035f, 1.15f, .34f, "fm=3.01 fm_index=2.6 fm_decay=.42 lpf=.86"),
        Instrument("glass-chime", "Clear glass chime; bright and precise", "clear-fm-chime", "sine", .12f, .001f, .025f, .85f, .72f, "fm=4.12 fm_index=4.1 fm_decay=.28 hpf=.04"),
        Instrument("soft-triangle", "Rounded triangle voice; calm and distinct", "filtered-triangle", "triangle", .14f, .006f, .04f, .9f, .46f, "lpf=.72"),
        Instrument("muted-pluck", "Short muted pluck; dry and unobtrusive", "muted-pluck", "triangle", .13f, .001f, .018f, .34f, .56f, "lpf=.58 drive=.05")
    }.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ValidateCuration()
    {
        var failures = new List<string>();
        foreach (var instrument in Instruments.Values)
        {
            if (instrument.RootFrequency is < 196 or > 392) failures.Add($"{instrument.Id}: root leaves the calm mid register");
            if (instrument.Brightness is < .2f or > .78f) failures.Add($"{instrument.Id}: brightness leaves the pleasantness window");
            if (instrument.DecaySeconds is < .25f or > 1.5f) failures.Add($"{instrument.Id}: decay leaves the notification window");
            if (instrument.UsesNoise) failures.Add($"{instrument.Id}: broadband noise is forbidden");
        }
        var duplicateFamilies = Instruments.Values.GroupBy(x => x.TimbreFamily).Where(x => x.Count() > 1).Select(x => x.Key);
        failures.AddRange(duplicateFamilies.Select(x => $"timbre family '{x}' is not distinct"));
        foreach (var dingEvent in Events.Values)
        {
            var gaps = dingEvent.Notes.Zip(dingEvent.Notes.Skip(1), (left, right) => right.DelayMilliseconds - left.DelayMilliseconds);
            if (gaps.Any(gap => gap is < 180 or > 320)) failures.Add($"{dingEvent.Id}: onset gap leaves the researched routine-notification window");
        }
        return failures;
    }

    private static DingInstrument Instrument(string id, string description, string family, string wave, float gain, float attack, float sustain, float decay, float brightness, string color) =>
        new(id, description, family, $"param path=/ding/frequency default=261.6256 min=40 max=4000 step=.001; voice wave={wave} freq=@/ding/frequency gain={gain} attack={attack} sustain={sustain} decay={decay} {color}", 261.6256f, brightness, decay, false);
}
