namespace Sirkadiyen.Application.Common;

/// <summary>A single page of a larger, ordered result set.</summary>
public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>The 1-based page number this result represents.</summary>
    public required int Page { get; init; }

    public required int PageSize { get; init; }

    /// <summary>The total number of matching items across every page.</summary>
    public required int TotalCount { get; init; }

    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
