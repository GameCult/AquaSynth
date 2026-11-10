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
/// carries no targets, so nothing is approximated. Duration is the vowel/consonant length AquaSynth owns; stress moves
/// only the gesture intensity, the one thing the word script uses stress for.
/// </summary>
public sealed record WeksaWordPlan(IReadOnlyList<(IpaGestureExperimentTarget Target, float Intensity)> Phones, IReadOnlyList<WeksaPhone> Unsupported)
{
    public const float VowelSeconds = 0.18f, ConsonantSeconds = 0.095f;
    public bool Renderable => Unsupported.Count == 0;

    public static float StressIntensity(WeksaStress stress) => stress switch
    {
        WeksaStress.Primary => 1.2f,
        WeksaStress.Secondary => 1.1f,
        _ => 1f
    };

    public static WeksaWordPlan From(WeksaPhones word, string label)
    {
        var unsupported = word.Phones.Where(p => !PatchScript.TryIpaDescriptor(p.Ipa, out _)).ToArray();
        if (unsupported.Length > 0) return new WeksaWordPlan([], unsupported);
        var targets = word.Phones.Select((p, i) =>
        {
            PatchScript.TryIpaDescriptor(p.Ipa, out var descriptor);
            var seconds = descriptor.EndsWith("_vowel", StringComparison.Ordinal) ? VowelSeconds : ConsonantSeconds;
            return (new IpaGestureExperimentTarget($"{label}_{i}_{p.Ipa}", p.Ipa, "", seconds), StressIntensity(p.Stress));
        }).ToArray();
        return new WeksaWordPlan(targets, []);
    }
}
