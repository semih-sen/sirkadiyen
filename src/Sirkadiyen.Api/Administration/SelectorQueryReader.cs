namespace Sirkadiyen.Api.Administration;

/// <summary>
/// Reads the repeated <c>?selector=key:value</c> query parameter that admin endpoints use to
/// state a cohort.
/// </summary>
/// <remarks>
/// Selectors are a dictionary whose keys the schema owns rather than the route, so they cannot
/// be named parameters. This lives on its own because more than one endpoint reads them and a
/// second copy of the parser would eventually disagree with the first about a malformed pair.
/// </remarks>
internal static class SelectorQueryReader
{
    /// <summary>
    /// Parses the pairs. A malformed pair is refused rather than skipped: silently dropping one
    /// would answer a wider question than the operator asked and nothing on the screen would
    /// say so.
    /// </summary>
    public static bool TryRead(
        string[]? values,
        out Dictionary<string, string> selectors,
        out string? problem)
    {
        selectors = new Dictionary<string, string>(StringComparer.Ordinal);
        problem = null;

        foreach (string value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            int separator = value.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator == value.Length - 1)
            {
                problem = $"'selector' must be written as 'key:value'; '{value}' is not.";
                return false;
            }

            selectors[value[..separator].Trim()] = value[(separator + 1)..].Trim();
        }

        return true;
    }
}
