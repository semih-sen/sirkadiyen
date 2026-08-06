using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Infrastructure.Persistence.Administration.Stores;

/// <summary>Aggregates the admin dashboard's operational counts from existing pipeline tables.</summary>
public sealed class AdminMetricsReadStore(SirkadiyenDbContext dbContext, TimeProvider timeProvider)
    : IAdminMetricsReadStore
{
    /// <summary>A polling source unseen for this long is counted as overdue.</summary>
    private static readonly TimeSpan OverdueAfter = TimeSpan.FromHours(24);

    public async Task<AdminMetricsSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset overdueBefore = now - OverdueAfter;

        int totalUsers = await dbContext.Users.CountAsync(cancellationToken);

        int activeLicenses = await dbContext.Licenses
            .CountAsync(license => license.Status == LicenseStatus.Redeemed, cancellationToken);

        int initialSyncsInProgress = await dbContext.GoogleCalendarConnections
            .CountAsync(
                connection => connection.InitialSyncState
                    == GoogleCalendarInitialSyncState.InProgress,
                cancellationToken);

        int completedConnections = await dbContext.GoogleCalendarConnections
            .CountAsync(
                connection => connection.InitialSyncState
                    == GoogleCalendarInitialSyncState.Completed,
                cancellationToken);

        int revisionsAwaitingReview = await dbContext.ScheduleRevisions
            .CountAsync(
                revision => revision.State == RevisionState.ReviewRequired,
                cancellationToken);

        int heldDiffs = await dbContext.ScheduleDiffs
            .CountAsync(diff => diff.State == ScheduleDiffState.Held, cancellationToken);

        int pollingSourcesOverdue = await dbContext.ScheduleSources
            .CountAsync(
                source => source.IsPollingEnabled
                    && (source.LastPolledAtUtc == null
                        || source.LastPolledAtUtc < overdueBefore),
                cancellationToken);

        bool freezeActive = await dbContext.OperationalFreezeControls
            .AnyAsync(control => control.IsFrozen, cancellationToken)
            || await dbContext.ScopedOperationalFreezeControls
                .AnyAsync(control => control.IsFrozen, cancellationToken);

        return new AdminMetricsSnapshot
        {
            GeneratedAtUtc = now,
            TotalUsers = totalUsers,
            ActiveLicenses = activeLicenses,
            InitialSyncsInProgress = initialSyncsInProgress,
            CompletedConnections = completedConnections,
            RevisionsAwaitingReview = revisionsAwaitingReview,
            HeldDiffs = heldDiffs,
            PollingSourcesOverdue = pollingSourcesOverdue,
            OperationalFreezeActive = freezeActive,
        };
    }
}
