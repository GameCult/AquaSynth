namespace AquaSynth.Dsl;

/// <summary>
/// The owner of the nasal tract area function and of the velopharyngeal port's maximum area. Data: Birkholz P, 2005, "3D-Artikulatorische
/// Sprachsynthese", dissertation Universitaet Rostock, Table 2.3 (the nasal cavity of one subject, after Dang J, Honda K, 1994, "Morphological
/// and acoustical analysis of the nasal and the paranasal cavities", JASA 96(4):2088-2100): 19 sections of 0.6 cm (11.4 cm), areas in cm^2,
/// velopharyngeal end first, nostrils last. A test compares <see cref="Areas"/> to the cited values.
/// Not modelled: the paranasal sinus Helmholtz resonators (zeros at 552-1682 Hz in the same source) and the piriform fossae. The table is a
/// one-subject measurement and is unheard here: nothing in this file says how any nasal sounds.
/// </summary>
public static class NasalTractAreas
{
    public const string Source = "birkholz2005-table2.3";
    public const float SectionLengthCentimeters = 0.6f;
    public const float LengthCentimeters = 11.4f;

    public static IReadOnlyList<float> Areas { get; } =
    [
        1.63f, 2.07f, 2.72f, 3.59f, 4.24f, 3.26f, 3.04f, 3.04f, 2.72f, 2.5f,
        2.39f, 2.39f, 1.85f, 0.76f, 1.41f, 1.74f, 1.30f, 1.74f, 0.76f,
    ];

    /// <summary>
    /// Maximum cross-section area of the velopharyngeal port in cm^2: the ladder's port area is opening * coupling * this. A DESIGN VALUE:
    /// no source for the range of the port area was retrieved (Birkholz's velum port is a controllable area; its range is not in the
    /// material read). 0.8 cm^2 is a wide-open port, below the first nasal section (1.63 cm^2) so the port, not the table, is the narrowest
    /// first cell. Pink Trombone's earlier constant, a 0.4 cm diameter (0.16 cm^2 area at full opening and coupling), was likewise unsourced.
    /// </summary>
    public const float VelopharyngealPortMaximumAreaSquareCentimeters = 0.8f;

    /// <summary>The scaffold's morphology line value for areas=, invariant-culture comma separated.</summary>
    public static string AreasList() => string.Join(",", Areas.Select(area => area.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
}
