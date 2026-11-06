using System.Security.Cryptography;
using System.Text;

namespace AquaSynth.Dsl;

/// <summary>
/// The one mapping from a caller-chosen id to a single file-name component in a store.
/// Ids already within [A-Za-z0-9._-] pass through unchanged. Anything else is sanitised and
/// suffixed with the first 8 hex of SHA-256 of the original id, so distinct ids stay distinct.
/// The result is never a path: no separators, never "." or "..".
/// </summary>
public static class StoreFileName
{
    public static string From(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.Length == 0 || id.All(c => c == '.'))
        {
            throw new ArgumentException($"Id '{id}' cannot name a store file.", nameof(id));
        }

        var builder = new StringBuilder(id.Length);
        foreach (var c in id)
        {
            builder.Append(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.' ? c : '_');
        }

        var safe = builder.ToString();
        // A trailing dot is dropped by Windows, so "a." would alias "a"; hash it like any other rewrite.
        if (safe == id && !id.EndsWith('.'))
        {
            return safe;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..8].ToLowerInvariant();
        return $"{safe}-{hash}";
    }
}
