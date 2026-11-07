using System.Security.Cryptography;
using System.Text;

namespace AquaSynth.Dsl;

/// <summary>
/// The one mapping from a caller-chosen id to a single file-name component in a store.
/// An id passes through unchanged only if it is already a fixed point: ASCII [A-Za-z0-9._-],
/// all lowercase (stores live on case-insensitive filesystems), at most <see cref="MaxStem"/> long,
/// no trailing dot, not a Windows device stem (CON, NUL, COM1, ...), and not ending in a
/// "-" plus 16 hex digits. Every other id is sanitised and suffixed with "-" plus the first
/// 16 hex of SHA-256 of the original id. Passed-through names never end in the hash shape and
/// rewritten names always do, so the two sets cannot meet; distinct ids stay distinct up to a 64-bit
/// hash. The result is never a path: no separators, never "." or "..".
/// </summary>
public static class StoreFileName
{
    private const int MaxStem = 100;
    private const int HashLength = 16;

    public static string From(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.Length == 0 || id.All(c => c == '.'))
        {
            throw new ArgumentException($"Id '{id}' cannot name a store file.", nameof(id));
        }

        var builder = new StringBuilder(Math.Min(id.Length, MaxStem));
        foreach (var c in id.Take(MaxStem))
        {
            builder.Append(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.' ? c : '_');
        }

        var safe = builder.ToString();
        if (IsFixedPoint(id, safe))
        {
            return safe;
        }

        // "nul.x-<hash>" still has the device stem "nul"; the underscore moves the whole name off it.
        if (IsDeviceStem(safe))
        {
            safe = "_" + safe;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..HashLength].ToLowerInvariant();
        return $"{safe}-{hash}";
    }

    /// <summary>A child of <paramref name="root"/> named for an external id; the result never leaves the root.</summary>
    public static string Combine(string root, string id) => Path.Combine(root, From(id));

    private static bool IsFixedPoint(string id, string safe)
    {
        if (safe != id || id.EndsWith('.') || id.Any(char.IsAsciiLetterUpper) || IsDeviceStem(id) || EndsWithHashShape(id))
        {
            return false;
        }

        return true;
    }

    // Windows resolves CON, PRN, AUX, NUL, COM0-9, LPT0-9 as devices whatever the extension.
    private static bool IsDeviceStem(string id)
    {
        var dot = id.IndexOf('.');
        var stem = (dot < 0 ? id : id[..dot]).TrimEnd(' ');
        return stem.Length switch
        {
            3 => stem.Equals("con", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("prn", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("aux", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("nul", StringComparison.OrdinalIgnoreCase),
            4 => char.IsAsciiDigit(stem[3])
                && (stem.StartsWith("com", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("lpt", StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    private static bool EndsWithHashShape(string id)
    {
        if (id.Length <= HashLength || id[^(HashLength + 1)] != '-')
        {
            return false;
        }

        return id[^HashLength..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
