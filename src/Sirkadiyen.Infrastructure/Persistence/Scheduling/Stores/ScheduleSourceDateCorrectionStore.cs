using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;

/// <summary>
/// Stores the dates an operator has decided a source states wrongly, in
/// PostgreSQL (ADR-139).
/// </summary>
public sealed class ScheduleSourceDateCorrectionStore(SirkadiyenDbContext dbContext)
    : IScheduleSourceDateCorrectionStore
{
    public async Task<IReadOnlyList<ScheduleSourceDateCorrection>> ListForSourceAsync(
        SourceId sourceId,
        CancellationToken cancellationToken) =>
        await dbContext.ScheduleSourceDateCorrections
            .AsNoTracking()
            .Where(correction => correction.SourceId == sourceId)
            .OrderBy(correction => correction.Original)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScheduleSourceDateCorrection>> ListAllAsync(
        CancellationToken cancellationToken) =>
        await dbContext.ScheduleSourceDateCorrections
            .AsNoTracking()
            .OrderByDescending(correction => correction.DecidedAtUtc)
            .ThenBy(correction => correction.Original)
            .ToListAsync(cancellationToken);

    public async Task<ScheduleSourceDateCorrection> AcceptAsync(
        ScheduleSourceDateCorrection correction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(correction);

        // One source states one wrong date once, which the unique index also
        // enforces. An operator who picks the other candidate after reading the
        // suggestion again is correcting their own decision, so the earlier row
        // gives way rather than the write being refused.
        ScheduleSourceDateCorrection? existing = await dbContext.ScheduleSourceDateCorrections
            .SingleOrDefaultAsync(
                stored => stored.SourceId == correction.SourceId
                    && stored.Original == correction.Original,
                cancellationToken);

        if (existing is not null)
        {
            dbContext.ScheduleSourceDateCorrections.Remove(existing);
        }

        dbContext.ScheduleSourceDateCorrections.Add(correction);
        await dbContext.SaveChangesAsync(cancellationToken);
        return correction;
    }

    public async Task<bool> RetireAsync(
        SourceId sourceId,
        Guid correctionId,
        CancellationToken cancellationToken)
    {
        ScheduleSourceDateCorrection? stored = await dbContext.ScheduleSourceDateCorrections
            .SingleOrDefaultAsync(
                correction => correction.Id == correctionId && correction.SourceId == sourceId,
                cancellationToken);

        if (stored is null)
        {
            return false;
        }

        dbContext.ScheduleSourceDateCorrections.Remove(stored);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
