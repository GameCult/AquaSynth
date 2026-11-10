namespace AquaSynth.Dsl;

/// <summary>
/// The owner of the measured vowel tract shapes and of the IPA vowel -> Story vowel mapping. Data: Story BH, 2008, "Comparison of
/// magnetic resonance imaging-based vocal tract area functions obtained from the same speaker in 1994 and 2002", JASA 123(1):327-335,
/// doi 10.1121/1.2805683, PMC2377017, Table I: eleven vowels, 44 sections glottis -> lips, areas in cm^2. The cited copy is
/// docs/research/data/story2008-area-functions.csv; a test compares every cell of this table to that file (transcription guard).
/// The table is a one-speaker measurement and is unheard here: nothing in this file says how any vowel sounds.
///
/// Story's vocal tract lengths run 16.1-19.6 cm (the CSV carries each). The ladder has one length per patch, so a patch that uses
/// these shapes sets its oral morphology to <see cref="MeanLengthCentimeters"/> and every vowel is read at that length; the sections are
/// resampled onto the ladder's cells by <see cref="AreaAt"/>.
///
/// Control surface ids (ascii, used in /vocal/areas/&lt;i&gt;/area/vowel_&lt;id&gt;): i ih e eh ae uh ah aw o oo_short u for
/// i ɪ e ɛ æ ʌ ɑ ɔ o ʊ u. The schwa has no Story shape: it is the all-weights-zero state, the geometric mean of the eleven shapes.
/// </summary>
public static class StoryVowelAreas
{
    public const string Source = "story2008";
    public const float MeanLengthCentimeters = 17.52f;
    public const int Sections = 44;

    public sealed record Vowel(string Ipa, string Id, string Descriptor, IReadOnlyList<float> Areas);

    public static string SurfaceField(string id) => $"vowel_{id}";

    /// <summary>
    /// Vowels other than the eleven shapes that the DSL's IPA table accepts, and the shape each selects. ɑ and a share a shape; the schwa
    /// maps to null (no weight raised: the geometric-mean shape).
    /// </summary>
    public static bool TryVowelForIpa(string ipa, out string descriptor, out string? id)
    {
        if (ipa == "a") ipa = "ɑ";
        if (ipa == "ə")
        {
            descriptor = SchwaDescriptor;
            id = null;
            return true;
        }

        var vowel = All.FirstOrDefault(candidate => candidate.Ipa == ipa);
        descriptor = vowel?.Descriptor ?? "";
        id = vowel?.Id;
        return vowel is not null;
    }

    public const string SchwaDescriptor = "voiced_mid_central_unrounded_vowel";

    /// <summary>
    /// The explicit descriptor -> Story vowel id lookup (there is no feature-distance guessing). A vowel descriptor with no entry,
    /// and the schwa's, return null: the geometric-mean shape.
    /// </summary>
    public static string? IdForDescriptor(string descriptor) =>
        All.FirstOrDefault(vowel => vowel.Descriptor.Equals(descriptor.Replace('-', '_').ToLowerInvariant(), StringComparison.Ordinal))?.Id;

    /// <summary>
    /// Area in cm^2 of <paramref name="areas"/> at a normalized glottis -> lips position: linear interpolation between section centres
    /// (section j is centred at (j + 0.5) / n), held at the first and last section beyond them.
    /// </summary>
    public static float AreaAt(IReadOnlyList<float> areas, float position)
    {
        var x = Math.Clamp(position * areas.Count - 0.5f, 0, areas.Count - 1);
        var left = (int)MathF.Floor(x);
        var right = Math.Min(areas.Count - 1, left + 1);
        return areas[left] + (areas[right] - areas[left]) * (x - left);
    }

    public static readonly IReadOnlyList<Vowel> All =
    [
        new("i", "i", "voiced_close_front_unrounded_vowel", [
            0.51f, 0.59f, 0.62f, 0.72f, 1.24f, 2.30f, 3.30f, 3.59f, 3.22f, 2.86f, 3.00f, 3.61f, 4.39f, 4.95f, 5.17f, 5.16f, 5.18f, 5.26f, 5.20f, 5.02f, 4.71f, 4.13f, 3.43f, 2.83f, 2.32f, 1.83f, 1.46f, 1.23f, 1.08f, 0.94f, 0.80f, 0.67f, 0.55f, 0.46f, 0.40f, 0.36f, 0.35f, 0.35f, 0.38f, 0.51f, 0.74f, 0.92f, 0.96f, 0.91f]),
        new("ɪ", "ih", "voiced_near_close_near_front_unrounded_vowel", [
            0.28f, 0.21f, 0.21f, 0.30f, 0.47f, 0.71f, 1.12f, 1.48f, 1.35f, 1.05f, 0.92f, 0.92f, 1.19f, 1.94f, 2.83f, 3.31f, 3.48f, 3.60f, 3.64f, 3.49f, 3.20f, 2.90f, 2.59f, 2.21f, 1.87f, 1.54f, 1.20f, 0.92f, 0.74f, 0.59f, 0.52f, 0.54f, 0.59f, 0.65f, 0.71f, 0.67f, 0.61f, 0.57f, 0.50f, 0.48f, 0.54f, 0.73f, 0.93f, 0.82f]),
        new("e", "e", "voiced_close_mid_front_unrounded_vowel", [
            0.29f, 0.26f, 0.30f, 0.40f, 0.55f, 0.74f, 0.99f, 1.09f, 0.90f, 0.69f, 0.77f, 1.31f, 2.13f, 2.74f, 3.03f, 3.23f, 3.33f, 3.27f, 3.09f, 2.84f, 2.66f, 2.46f, 2.14f, 1.79f, 1.44f, 1.17f, 1.00f, 0.88f, 0.80f, 0.81f, 0.85f, 0.84f, 0.86f, 0.96f, 1.18f, 1.35f, 1.48f, 1.62f, 1.49f, 1.29f, 1.24f, 1.17f, 1.04f, 0.95f]),
        new("ɛ", "eh", "voiced_open_mid_front_unrounded_vowel", [
            0.37f, 0.30f, 0.24f, 0.23f, 0.29f, 0.41f, 0.58f, 0.82f, 0.97f, 0.82f, 0.62f, 0.54f, 0.54f, 0.68f, 1.09f, 1.62f, 2.03f, 2.35f, 2.51f, 2.39f, 2.22f, 2.13f, 2.00f, 1.78f, 1.58f, 1.43f, 1.31f, 1.23f, 1.24f, 1.38f, 1.61f, 1.82f, 1.96f, 2.01f, 2.00f, 1.95f, 1.77f, 1.48f, 1.30f, 1.21f, 1.10f, 0.99f, 0.91f, 0.78f]),
        new("æ", "ae", "voiced_near_open_front_unrounded_vowel", [
            0.31f, 0.21f, 0.18f, 0.23f, 0.33f, 0.50f, 0.78f, 0.96f, 0.85f, 0.63f, 0.46f, 0.36f, 0.33f, 0.46f, 0.73f, 1.00f, 1.30f, 1.66f, 1.97f, 2.06f, 2.03f, 2.01f, 1.89f, 1.66f, 1.49f, 1.42f, 1.37f, 1.34f, 1.41f, 1.58f, 1.82f, 2.19f, 2.63f, 2.97f, 3.17f, 3.40f, 3.56f, 3.57f, 3.58f, 3.44f, 3.15f, 3.38f, 3.99f, 4.17f]),
        new("ʌ", "uh", "voiced_open_mid_back_unrounded_vowel", [
            0.23f, 0.34f, 0.47f, 0.60f, 0.77f, 1.06f, 1.26f, 1.09f, 0.80f, 0.65f, 0.56f, 0.47f, 0.37f, 0.24f, 0.17f, 0.18f, 0.23f, 0.27f, 0.28f, 0.29f, 0.33f, 0.52f, 0.97f, 1.50f, 1.91f, 2.23f, 2.65f, 3.29f, 4.13f, 5.00f, 5.77f, 6.33f, 6.61f, 6.63f, 6.45f, 6.04f, 5.39f, 4.42f, 3.29f, 2.37f, 1.74f, 1.36f, 1.17f, 0.99f]),
        new("ɑ", "ah", "voiced_open_back_unrounded_vowel", [
            0.56f, 0.62f, 0.66f, 0.78f, 0.97f, 1.16f, 1.12f, 0.82f, 0.55f, 0.45f, 0.37f, 0.29f, 0.21f, 0.15f, 0.16f, 0.25f, 0.34f, 0.43f, 0.54f, 0.61f, 0.67f, 0.98f, 1.76f, 2.75f, 3.52f, 4.08f, 4.74f, 5.61f, 6.60f, 7.61f, 8.48f, 9.06f, 9.29f, 9.26f, 9.06f, 8.64f, 7.91f, 6.98f, 6.02f, 5.13f, 4.55f, 4.52f, 4.71f, 4.72f]),
        new("ɔ", "aw", "voiced_open_mid_back_rounded_vowel", [
            0.27f, 0.43f, 0.54f, 0.67f, 0.83f, 0.92f, 0.89f, 0.73f, 0.55f, 0.44f, 0.37f, 0.28f, 0.18f, 0.14f, 0.15f, 0.14f, 0.13f, 0.14f, 0.18f, 0.20f, 0.23f, 0.49f, 1.03f, 1.58f, 2.06f, 2.61f, 3.35f, 4.34f, 5.51f, 6.70f, 7.75f, 8.63f, 9.29f, 9.59f, 9.42f, 8.78f, 7.82f, 6.50f, 4.95f, 3.47f, 2.15f, 1.38f, 1.11f, 0.90f]),
        new("o", "o", "voiced_close_mid_back_rounded_vowel", [
            0.38f, 0.45f, 0.57f, 0.77f, 1.31f, 1.92f, 1.74f, 1.11f, 0.75f, 0.59f, 0.57f, 0.68f, 0.73f, 0.67f, 0.58f, 0.49f, 0.44f, 0.42f, 0.49f, 0.53f, 0.38f, 0.30f, 0.45f, 0.61f, 0.71f, 0.79f, 0.86f, 1.01f, 1.41f, 2.09f, 3.00f, 4.10f, 5.16f, 6.22f, 7.34f, 8.15f, 8.61f, 8.37f, 6.76f, 4.37f, 2.30f, 1.06f, 0.58f, 0.47f]),
        new("ʊ", "oo_short", "voiced_near_close_near_back_rounded_vowel", [
            0.37f, 0.38f, 0.49f, 0.62f, 0.85f, 1.28f, 1.62f, 1.47f, 1.04f, 0.81f, 1.03f, 1.44f, 1.49f, 1.28f, 1.06f, 0.85f, 0.69f, 0.56f, 0.42f, 0.27f, 0.27f, 0.41f, 0.51f, 0.49f, 0.47f, 0.50f, 0.58f, 0.80f, 1.19f, 1.62f, 2.27f, 3.24f, 4.16f, 5.00f, 5.70f, 6.11f, 6.21f, 6.29f, 6.24f, 4.91f, 2.61f, 1.09f, 0.63f, 0.59f]),
        new("u", "u", "voiced_close_back_rounded_vowel", [
            0.54f, 0.61f, 0.66f, 0.75f, 1.13f, 1.99f, 2.83f, 2.90f, 2.52f, 2.40f, 2.83f, 3.56f, 3.99f, 3.89f, 3.50f, 3.04f, 2.64f, 2.44f, 2.31f, 2.07f, 1.80f, 1.52f, 1.14f, 0.74f, 0.42f, 0.22f, 0.14f, 0.20f, 0.47f, 0.89f, 1.15f, 1.42f, 2.17f, 3.04f, 3.69f, 4.70f, 5.74f, 5.41f, 3.82f, 2.34f, 1.35f, 0.65f, 0.29f, 0.16f]),
    ];
}
