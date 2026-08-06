using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

/// <summary>Compares department lists by value, in order.</summary>
/// <remarks>
/// Without this, change tracking would compare list references and either miss an
/// edit or rewrite the column on every save.
/// </remarks>
internal sealed class DepartmentListComparer()
    : ValueComparer<IReadOnlyList<string>>(
        (left, right) => Equal(left, right),
        departments => HashOf(departments),
        departments => CopyOf(departments))
{
    private static bool Equal(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static int HashOf(IReadOnlyList<string>? departments)
    {
        if (departments is null)
        {
            return 0;
        }

        HashCode hash = default;
        foreach (string department in departments)
        {
            hash.Add(department, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static IReadOnlyList<string> CopyOf(IReadOnlyList<string>? departments) =>
        departments is null ? [] : [.. departments];
}
