using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Configurations;

/// <summary>Compares source-identifier lists by value, in order.</summary>
/// <remarks>
/// Without this, change tracking compares list references, so a reconciled
/// companion list would either be missed or rewritten on every save.
/// </remarks>
internal sealed class SourceIdListComparer()
    : ValueComparer<IReadOnlyList<SourceId>>(
        (left, right) => Equal(left, right),
        sourceIds => HashOf(sourceIds),
        sourceIds => CopyOf(sourceIds))
{
    private static bool Equal(IReadOnlyList<SourceId>? left, IReadOnlyList<SourceId>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SequenceEqual(right);
    }

    private static int HashOf(IReadOnlyList<SourceId>? sourceIds)
    {
        if (sourceIds is null)
        {
            return 0;
        }

        HashCode hash = default;
        foreach (SourceId sourceId in sourceIds)
        {
            hash.Add(sourceId);
        }

        return hash.ToHashCode();
    }

    private static IReadOnlyList<SourceId> CopyOf(IReadOnlyList<SourceId>? sourceIds) =>
        sourceIds is null ? [] : [.. sourceIds];
}
