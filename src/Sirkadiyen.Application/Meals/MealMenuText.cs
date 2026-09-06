using System.Security.Cryptography;
using System.Text;

namespace Sirkadiyen.Application.Meals;

/// <summary>
/// Normalizes and hashes cafeteria menu text (ADR-150). Pure and deterministic so the same API
/// answer always yields the same stored text and the same hash — the property change detection
/// depends on.
/// </summary>
public static class MealMenuText
{
    /// <summary>
    /// Turns the API's dish list into one clean dish per line. The observed shape separates dishes
    /// with a comma and a CRLF (<c>"Çorba,\r\nKöfte,\r\n..."</c>), so lines are split on any newline,
    /// trimmed, stripped of a trailing separator comma, and rejoined with a single LF. Blank lines
    /// are dropped. The result is what is both stored and shown.
    /// </summary>
    public static string Normalize(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        string[] lines = raw
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        IEnumerable<string> dishes = lines
            .Select(line => line.Trim().TrimEnd(',').Trim())
            .Where(line => line.Length > 0);

        return string.Join('\n', dishes);
    }

    /// <summary>The lowercase hex SHA-256 of the normalized text; the change-detection key.</summary>
    public static string Hash(string normalizedText)
    {
        ArgumentNullException.ThrowIfNull(normalizedText);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText));
        return Convert.ToHexStringLower(digest);
    }
}
