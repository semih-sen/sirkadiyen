using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Sirkadiyen.Infrastructure.Persistence.Finance.Configurations;

/// <summary>Compares changed-field lists by value, in order, for change tracking.</summary>
internal sealed class ChangedFieldsComparer()
    : ValueComparer<IReadOnlyList<string>>(
        (left, right) => Equal(left, right),
        fields => HashOf(fields),
        fields => CopyOf(fields))
{
    private static bool Equal(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static int HashOf(IReadOnlyList<string>? fields)
    {
        if (fields is null)
        {
            return 0;
        }

        HashCode hash = default;
        foreach (string field in fields)
        {
            hash.Add(field, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static IReadOnlyList<string> CopyOf(IReadOnlyList<string>? fields) =>
        fields is null ? [] : [.. fields];
}
