using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.StudentRosters;

public sealed record StudentRosterIndexOptions
{
    /// <summary>The path of the roster catalog this deployment reads.</summary>
    public required string CatalogPath { get; init; }

    /// <summary>
    /// How long a reading is served before the lists are read again.
    /// </summary>
    /// <remarks>
    /// A roster changes when Student Affairs edits it, which is rare and never
    /// urgent: a student who onboards an hour after a correction types the one
    /// value that changed. Fetching four documents per lookup would put an
    /// onboarding step behind four Google calls instead.
    /// </remarks>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Holds the current reading of every configured student list in memory.
/// </summary>
/// <remarks>
/// Nothing here is persisted, and that is a requirement rather than an
/// optimization: ADR-085 permits a student's name and surname to be shown during
/// a lookup and forbids Sirkadiyen from retaining them. A reading lives in this
/// process, is replaced by the next one, and is gone when the process stops.
/// <para>
/// A list that fails to refresh keeps its previous reading and is reported as
/// failed at the same time. Dropping it would turn a Google outage into "you are
/// not on any list", which asks the student to do the wrong thing.
/// </para>
/// </remarks>
public sealed class StudentRosterIndex(
    IStudentRosterCatalogSerializer serializer,
    StudentRosterIndexOptions options,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<StudentRosterIndex> logger) : IStudentRosterIndex
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly StudentRosterReader reader = new();
    private StudentRosterIndexSnapshot? current;

    /// <summary>
    /// Drops the held reading. The next <see cref="GetAsync"/> reads the catalog and the lists
    /// again, which is what makes an administrative catalog edit take effect at the next lookup
    /// rather than at the next refresh (ADR-134).
    /// </summary>
    /// <remarks>
    /// A plain field write, deliberately taking no lock: a refresh already in flight finishes and
    /// stores its result, and the worst case is one more reading than strictly necessary. Blocking
    /// an editor on an in-flight Google call to avoid that would be the worse trade.
    /// </remarks>
    public void Invalidate() => current = null;

    public async Task<StudentRosterIndexSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (current is { } cached && now - cached.ReadAtUtc < options.RefreshInterval)
        {
            return cached;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Re-checked inside the lock, so a burst of first-time lookups reads the
            // lists once rather than once each.
            now = timeProvider.GetUtcNow();
            if (current is { } stillCached && now - stillCached.ReadAtUtc < options.RefreshInterval)
            {
                return stillCached;
            }

            current = await RefreshAsync(current, now, cancellationToken);
            return current;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private async Task<StudentRosterIndexSnapshot> RefreshAsync(
        StudentRosterIndexSnapshot? previous,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        StudentRosterCatalog catalog = await serializer.LoadAsync(
            options.CatalogPath,
            cancellationToken);

        List<StudentRosterReading> readings = [];
        Dictionary<string, string> failures = new(StringComparer.Ordinal);

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        foreach (StudentRosterDefinition roster in catalog.Rosters)
        {
            try
            {
                // Resolved per roster and inside the try, because building a Google
                // client is itself a way this can fail: a deployment with no source
                // credential must report an unreadable list, not throw out of the
                // whole refresh and take the other three with it.
                NormalizedSpreadsheetSnapshot snapshot = await AcquireAsync(
                    roster,
                    scope.ServiceProvider,
                    now,
                    cancellationToken);

                StudentRosterReading reading = reader.Read(roster, snapshot);
                readings.Add(reading);

                logger.LogInformation(
                    "Read student roster {RosterId}: {EntryCount} students, {RefusedCount} refused "
                    + "rows, {WarningCount} warnings.",
                    reading.RosterId,
                    reading.Entries.Count,
                    reading.RefusedRows.Count,
                    reading.Warnings.Count);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures[roster.RosterId] = exception.Message;

                // The previous reading of this list is kept. It is stale, and it is
                // better than telling a student they are on no list because Google
                // was unreachable for a minute.
                StudentRosterReading? stale = previous?.Readings.FirstOrDefault(
                    reading => string.Equals(
                        reading.RosterId,
                        roster.RosterId,
                        StringComparison.Ordinal));

                if (stale is not null)
                {
                    readings.Add(stale);
                }

                logger.LogWarning(
                    exception,
                    "Student roster {RosterId} could not be read; {Disposition}.",
                    roster.RosterId,
                    stale is null ? "no earlier reading is held" : "the earlier reading is kept");
            }
        }

        return new StudentRosterIndexSnapshot
        {
            ReadAtUtc = now,
            Readings = readings,
            Failures = failures,
        };
    }

    private static Task<NormalizedSpreadsheetSnapshot> AcquireAsync(
        StudentRosterDefinition roster,
        IServiceProvider services,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AcquireSpreadsheetSnapshotRequest request = new()
        {
            SourceId = roster.RosterId,
            SnapshotId = Guid.CreateVersion7().ToString("N"),
            SpreadsheetId = roster.ExternalId
                ?? throw new InvalidOperationException(
                    $"Roster '{roster.RosterId}' names no external document ID."),
            AcquiredAtUtc = now,
        };

        return roster.Transport switch
        {
            ScheduleSourceTransport.GoogleSheets => services
                .GetRequiredService<ISpreadsheetSnapshotAcquirer>()
                .AcquireAsync(request, cancellationToken),
            ScheduleSourceTransport.GoogleDriveFile => services
                .GetRequiredService<IDriveDocumentAcquirer>()
                .AcquireAsync(roster.DocumentFormat, request, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Roster '{roster.RosterId}' declares transport '{roster.Transport}', which has no "
                + "reader."),
        };
    }
}
