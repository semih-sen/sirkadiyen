using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>PostgreSQL ledger of the calendar events written for each user (ADR-058, ADR-059).</summary>
public sealed class UserCalendarEventMappingStore(SirkadiyenDbContext dbContext)
    : IUserCalendarEventMappingStore
{
    public async Task<IReadOnlySet<string>> ListStableIdentitiesForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        List<string> identities = await dbContext.UserCalendarEventMappings
            .AsNoTracking()
            .Where(mapping => mapping.UserId == userId)
            .Select(mapping => mapping.StableIdentity)
            .ToListAsync(cancellationToken);

        return identities.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.UserCalendarEventMappings
            .AsNoTracking()
            .CountAsync(mapping => mapping.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<CalendarEventMappingView>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await dbContext.UserCalendarEventMappings
            .AsNoTracking()
            .Where(mapping => mapping.UserId == userId)
            .OrderBy(mapping => mapping.StableIdentity)
            .Select(mapping => new CalendarEventMappingView
            {
                UserId = mapping.UserId,
                StableIdentity = mapping.StableIdentity,
                GoogleCalendarId = mapping.GoogleCalendarId,
                GoogleEventId = mapping.GoogleEventId,
                ContentHash = mapping.ContentHash,
                CanonicalRecordId = mapping.CanonicalRecordId,
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CalendarEventMappingView>> ListForStableIdentityAsync(
        SourceId sourceId,
        string stableIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableIdentity);

        return await dbContext.UserCalendarEventMappings
            .AsNoTracking()
            .Where(mapping =>
                mapping.SourceId == sourceId && mapping.StableIdentity == stableIdentity)
            .Select(mapping => new CalendarEventMappingView
            {
                UserId = mapping.UserId,
                StableIdentity = mapping.StableIdentity,
                GoogleCalendarId = mapping.GoogleCalendarId,
                GoogleEventId = mapping.GoogleEventId,
                ContentHash = mapping.ContentHash,
                CanonicalRecordId = mapping.CanonicalRecordId,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<CalendarEventMappingAddOutcome> AddAsync(
        UserCalendarEventMapping mapping,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        dbContext.UserCalendarEventMappings.Add(mapping);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return CalendarEventMappingAddOutcome.Added;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // The (UserId, StableIdentity) index already holds this lesson: a concurrent or
            // resumed pass wrote it first. The event exists either way, so this is a no-op.
            dbContext.ChangeTracker.Clear();
            return CalendarEventMappingAddOutcome.AlreadyPresent;
        }
    }

    public async Task UpdateContentAsync(
        Guid userId,
        string stableIdentity,
        Guid canonicalRecordId,
        string contentHash,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableIdentity);

        UserCalendarEventMapping? mapping = await dbContext.UserCalendarEventMappings
            .SingleOrDefaultAsync(
                candidate => candidate.UserId == userId
                    && candidate.StableIdentity == stableIdentity,
                cancellationToken);
        if (mapping is null)
        {
            // The row was removed between the reverse lookup and here; the patched calendar stands.
            return;
        }

        mapping.UpdateContent(canonicalRecordId, contentHash, atUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CalendarEventMappingReidentifyOutcome> ReidentifyAsync(
        Guid userId,
        SourceId sourceId,
        string previousStableIdentity,
        string currentStableIdentity,
        Guid canonicalRecordId,
        string contentHash,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previousStableIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentStableIdentity);

        string[] identities = string.Equals(
            previousStableIdentity,
            currentStableIdentity,
            StringComparison.Ordinal)
            ? [previousStableIdentity]
            : [previousStableIdentity, currentStableIdentity];

        List<UserCalendarEventMapping> mappings = await dbContext.UserCalendarEventMappings
            .Where(mapping =>
                mapping.UserId == userId && identities.Contains(mapping.StableIdentity))
            .ToListAsync(cancellationToken);

        UserCalendarEventMapping? previous = mappings.SingleOrDefault(mapping =>
            string.Equals(
                mapping.StableIdentity,
                previousStableIdentity,
                StringComparison.Ordinal));
        UserCalendarEventMapping? current = mappings.SingleOrDefault(mapping =>
            string.Equals(
                mapping.StableIdentity,
                currentStableIdentity,
                StringComparison.Ordinal));

        if (string.Equals(previousStableIdentity, currentStableIdentity, StringComparison.Ordinal))
        {
            if (previous is null)
            {
                return CalendarEventMappingReidentifyOutcome.NotFound;
            }

            if (previous.SourceId != sourceId)
            {
                return CalendarEventMappingReidentifyOutcome.Conflict;
            }

            previous.UpdateContent(canonicalRecordId, contentHash, atUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            return CalendarEventMappingReidentifyOutcome.AlreadyReidentified;
        }

        if (previous is null)
        {
            if (current is null)
            {
                return CalendarEventMappingReidentifyOutcome.NotFound;
            }

            if (current.SourceId != sourceId)
            {
                return CalendarEventMappingReidentifyOutcome.Conflict;
            }

            current.UpdateContent(canonicalRecordId, contentHash, atUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            return CalendarEventMappingReidentifyOutcome.AlreadyReidentified;
        }

        if (previous.SourceId != sourceId || current is not null)
        {
            return CalendarEventMappingReidentifyOutcome.Conflict;
        }

        previous.Reidentify(currentStableIdentity, canonicalRecordId, contentHash, atUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CalendarEventMappingReidentifyOutcome.Reidentified;
    }

    public async Task<CalendarEventMappingRemoveOutcome> RemoveAsync(
        Guid userId,
        string stableIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableIdentity);

        UserCalendarEventMapping? mapping = await dbContext.UserCalendarEventMappings
            .SingleOrDefaultAsync(
                candidate => candidate.UserId == userId
                    && candidate.StableIdentity == stableIdentity,
                cancellationToken);
        if (mapping is null)
        {
            return CalendarEventMappingRemoveOutcome.NotFound;
        }

        dbContext.UserCalendarEventMappings.Remove(mapping);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CalendarEventMappingRemoveOutcome.Removed;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        };
}
