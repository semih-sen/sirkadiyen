using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Schedule;
using Sirkadiyen.Domain.SchedulePublication;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// Reads a student's timetable from the event-mapping ledger joined to the canonical records it
/// references, so the result is exactly what is on their calendar.
/// </summary>
public sealed class UserScheduleReadStore(SirkadiyenDbContext dbContext) : IUserScheduleReadStore
{
    public async Task<IReadOnlyList<UserScheduleEventView>> ListUpcomingAsync(
        Guid userId,
        DateOnly fromLocalDate,
        DateOnly toLocalDate,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        // The entities are materialized and then projected, because the Departments collection is
        // mapped as JSON and is simplest to read after materialization.
        List<CanonicalScheduleRecord> records = await (
                from mapping in dbContext.UserCalendarEventMappings.AsNoTracking()
                where mapping.UserId == userId
                join record in dbContext.CanonicalScheduleRecords.AsNoTracking()
                    on mapping.CanonicalRecordId equals record.Id
                where record.LocalDate >= fromLocalDate && record.LocalDate <= toLocalDate
                orderby record.LocalDate, record.StartLocalTime
                select record)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. records.Select(record => new UserScheduleEventView
        {
            StableIdentity = record.StableIdentity,
            Title = record.DisplayTitle,
            LocalDate = record.LocalDate,
            StartLocalTime = record.StartLocalTime,
            EndLocalTime = record.EndLocalTime,
            IsAllDay = record.IsAllDay,
            TimeZoneId = record.TimeZoneId,
            Location = record.Location,
            Instructor = record.Instructor,
            EventType = record.EventType,
            Departments = record.Departments,
        })];
    }

    public async Task<IReadOnlyList<UserScheduleChangeView>> ListRecentChangesAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var rows = await (
                from mapping in dbContext.UserCalendarEventMappings.AsNoTracking()
                where mapping.UserId == userId
                join record in dbContext.CanonicalScheduleRecords.AsNoTracking()
                    on mapping.CanonicalRecordId equals record.Id
                orderby mapping.UpdatedAtUtc descending
                select new
                {
                    mapping.StableIdentity,
                    record.DisplayTitle,
                    record.LocalDate,
                    mapping.CreatedAtUtc,
                    mapping.UpdatedAtUtc,
                })
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new UserScheduleChangeView
        {
            StableIdentity = row.StableIdentity,
            Title = row.DisplayTitle,
            LocalDate = row.LocalDate,
            Kind = row.UpdatedAtUtc > row.CreatedAtUtc
                ? UserScheduleChangeKind.Updated
                : UserScheduleChangeKind.Created,
            ChangedAtUtc = row.UpdatedAtUtc,
        })];
    }
}
