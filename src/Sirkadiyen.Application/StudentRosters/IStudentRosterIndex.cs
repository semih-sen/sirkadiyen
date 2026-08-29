namespace Sirkadiyen.Application.StudentRosters;

/// <summary>
/// The current reading of every configured student list.
/// </summary>
/// <remarks>
/// Fetching four published documents on every lookup would put an onboarding
/// step behind four network calls to Google, so the implementation holds a
/// reading and refreshes it. What the lookup needs from it is only that the
/// readings are the ones the catalog describes, and that a failure to refresh
/// surfaces rather than presenting a stale list as current.
/// </remarks>
public interface IStudentRosterIndex
{
    Task<StudentRosterIndexSnapshot> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Every list as it was last read, and when.</summary>
public sealed record StudentRosterIndexSnapshot
{
    public required DateTimeOffset ReadAtUtc { get; init; }

    public IReadOnlyList<StudentRosterReading> Readings { get; init; } = [];

    /// <summary>
    /// The rosters that could not be read at all, keyed by roster ID.
    /// </summary>
    /// <remarks>
    /// A list that failed to load is not the same as a list that holds nobody. A
    /// lookup that misses must be able to say which of the two happened, because
    /// "we could not read your list" and "you are not on any list" ask the
    /// student for different things.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Failures { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
