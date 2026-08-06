using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Compares declared selector maps by value.
/// </summary>
/// <remarks>
/// Without this, change tracking would compare dictionary references and either
/// miss an edit or rewrite the column on every save.
/// </remarks>
internal sealed class AudienceSelectorMapComparer()
    : ValueComparer<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(
        (left, right) => Equal(left, right),
        map => HashOf(map),
        map => CopyOf(map))
{
    private static bool Equal(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? left,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Count == right.Count
            && left.All(entry =>
                right.TryGetValue(entry.Key, out IReadOnlyList<string>? values)
                && entry.Value.SequenceEqual(values, StringComparer.Ordinal));
    }

    private static int HashOf(IReadOnlyDictionary<string, IReadOnlyList<string>>? map)
    {
        if (map is null)
        {
            return 0;
        }

        HashCode hash = default;
        foreach ((string dimension, IReadOnlyList<string> values) in map.OrderBy(
            entry => entry.Key,
            StringComparer.Ordinal))
        {
            hash.Add(dimension, StringComparer.Ordinal);
            foreach (string value in values)
            {
                hash.Add(value, StringComparer.Ordinal);
            }
        }

        return hash.ToHashCode();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? CopyOf(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? map) =>
        map is null
            ? null
            : map.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)entry.Value.ToList(),
                StringComparer.Ordinal);
}
