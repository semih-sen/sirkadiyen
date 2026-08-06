using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Sirkadiyen.Infrastructure.Persistence.StudentProfiles.Configurations;

/// <summary>Compares selector documents by key/value so change tracking is exact.</summary>
/// <remarks>
/// Without a value comparer, EF compares dictionary references and would either
/// miss an edit or rewrite the column on every save.
/// </remarks>
internal sealed class ProfileSelectorsComparer()
    : ValueComparer<IReadOnlyDictionary<string, string>>(
        (left, right) => Equal(left, right),
        selectors => HashOf(selectors),
        selectors => CopyOf(selectors))
{
    private static bool Equal(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach ((string key, string value) in left)
        {
            if (!right.TryGetValue(key, out string? other)
                || !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int HashOf(IReadOnlyDictionary<string, string>? selectors)
    {
        if (selectors is null)
        {
            return 0;
        }

        // Order-independent so two equal documents hash the same regardless of
        // enumeration order.
        int hash = 0;
        foreach ((string key, string value) in selectors)
        {
            hash ^= HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(key),
                StringComparer.Ordinal.GetHashCode(value));
        }

        return hash;
    }

    private static IReadOnlyDictionary<string, string> CopyOf(
        IReadOnlyDictionary<string, string>? selectors) =>
        selectors is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(selectors, StringComparer.Ordinal);
}
