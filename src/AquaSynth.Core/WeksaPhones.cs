using System.Text.Json;
using System.Text.Json.Serialization;

namespace AquaSynth.Dsl;

public enum WeksaStress { None, Primary, Secondary }

public sealed record WeksaPhone(string Ipa, WeksaStress Stress);

public sealed record WeksaPhones(string Language, string Source, IReadOnlyList<WeksaPhone> Phones);

public sealed class WeksaPhonesException(string message) : Exception(message);

/// <summary>
/// Reader for the Weksa phones line (`weksa lower --phones`): {"language","source","phones":[{"ipa","stress"}]}. It is
/// strict: a missing, extra, null or mistyped key, an unknown stress value, an empty phone list or malformed JSON is an
/// error, never a guess. Weksa owns the segmentation; durations, pitch and gesture stay with <see cref="IpaGestureExperiment"/>.
/// </summary>
public static class WeksaPhonesReader
{
    private static readonly JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    private sealed class PhoneDto
    {
        [JsonPropertyName("ipa")] public required string? Ipa { get; init; }
        [JsonPropertyName("stress")] public required string? Stress { get; init; }
    }

    private sealed class LineDto
    {
        [JsonPropertyName("language")] public required string? Language { get; init; }
        [JsonPropertyName("source")] public required string? Source { get; init; }
        [JsonPropertyName("phones")] public required List<PhoneDto?>? Phones { get; init; }
    }

    public static WeksaPhones Parse(string json)
    {
        LineDto? dto;
        try { dto = JsonSerializer.Deserialize<LineDto>(json, Strict); }
        catch (JsonException ex) { throw new WeksaPhonesException($"phones line is not the expected JSON: {ex.Message}"); }
        if (dto is null) throw new WeksaPhonesException("phones line is null");
        if (string.IsNullOrWhiteSpace(dto.Language)) throw new WeksaPhonesException("`language` must be a non-empty string");
        if (string.IsNullOrWhiteSpace(dto.Source)) throw new WeksaPhonesException("`source` must be a non-empty string");
        if (dto.Phones is null || dto.Phones.Count == 0) throw new WeksaPhonesException("`phones` must be a non-empty list");
        var phones = new List<WeksaPhone>();
        for (var i = 0; i < dto.Phones.Count; i++)
        {
            var p = dto.Phones[i] ?? throw new WeksaPhonesException($"phones[{i}] is null");
            if (string.IsNullOrWhiteSpace(p.Ipa)) throw new WeksaPhonesException($"phones[{i}].ipa must be a non-empty string");
            var stress = p.Stress switch
            {
                "none" => WeksaStress.None,
                "primary" => WeksaStress.Primary,
                "secondary" => WeksaStress.Secondary,
                null => throw new WeksaPhonesException($"phones[{i}].stress must be a string"),
                var other => throw new WeksaPhonesException($"phones[{i}].stress `{other}` is not none, primary or secondary")
            };
            phones.Add(new WeksaPhone(p.Ipa, stress));
        }

        return new WeksaPhones(dto.Language, dto.Source, phones);
    }
}

/// <summary>
/// A Weksa phones line as a word the tract can render. Every phone must be in the DSL's IPA table
/// (<see cref="PatchScript.TryIpaDescriptor"/>); a phone outside it is listed in <see cref="Unsupported"/> and the plan
/// carries no targets, so nothing is approximated. Base durations are the vowel/consonant lengths AquaSynth owns; stress
/// carries its three acoustic cues (<see cref="StressCue"/>): the marked phone is longer, louder and higher in f0.
/// </summary>
public sealed record WeksaWordPlan(IReadOnlyList<(IpaGestureExperimentTarget Target, float Intensity)> Phones, IReadOnlyList<WeksaPhone> Unsupported)
{
    public const float VowelSeconds = 0.18f, ConsonantSeconds = 0.095f;
    public bool Renderable => Unsupported.Count == 0;

    /// <summary>Duration factor, amplitude factor on the source flow and f0 ratio of a stressed phone against an unstressed one.</summary>
    public readonly record struct StressCue(float Duration, float Amplitude, float PitchRatio);

    /// <summary>
    /// Primary stress: duration 220/160 = 1.375 (stressed against unstressed syllable in initial-stress pairs, Sluijter and van
    /// Heuven 1996), amplitude 2 dB = 1.26 (the 1-3 dB overall-intensity difference they report), f0 +6 percent (their unaccented
    /// f0 difference). Secondary stress is placed halfway, in dB and in percent: a DESIGN VALUE, no secondary-stress measurement was
    /// retrieved. Unheard.
    /// </summary>
    public static StressCue CueFor(WeksaStress stress) => stress switch
    {
        WeksaStress.Primary => new(220f / 160f, 1.26f, 1.06f),
        WeksaStress.Secondary => new(1.19f, 1.12f, 1.03f),
        _ => new(1f, 1f, 1f)
    };

    public static WeksaWordPlan From(WeksaPhones word, string label)
    {
        var unsupported = word.Phones.Where(p => !PatchScript.TryIpaDescriptor(p.Ipa, out _)).ToArray();
        if (unsupported.Length > 0) return new WeksaWordPlan([], unsupported);
        var targets = word.Phones.Select((p, i) =>
        {
            PatchScript.TryIpaDescriptor(p.Ipa, out var descriptor);
            var cue = CueFor(p.Stress);
            var seconds = (descriptor.EndsWith("_vowel", StringComparison.Ordinal) ? VowelSeconds : ConsonantSeconds) * cue.Duration;
            return (new IpaGestureExperimentTarget($"{label}_{i}_{p.Ipa}", p.Ipa, "", seconds, PitchRatio: cue.PitchRatio), cue.Amplitude);
        }).ToArray();
        return new WeksaWordPlan(targets, []);
    }
}
