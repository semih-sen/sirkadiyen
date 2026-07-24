using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>PostgreSQL ledger of the calendar events written for each user (ADR-058).</summary>
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

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        };
}
