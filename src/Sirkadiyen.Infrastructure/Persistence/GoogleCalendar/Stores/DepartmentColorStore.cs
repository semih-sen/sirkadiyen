using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Infrastructure.Persistence.GoogleCalendar.Stores;

public sealed class DepartmentColorStore(SirkadiyenDbContext dbContext)
    : IDepartmentColorStore
{
    public async Task<IReadOnlyDictionary<string, string>> GetAdminDefaultsAsync(
        CancellationToken cancellationToken) =>
        await dbContext.DepartmentColorSettings
            .AsNoTracking()
            .ToDictionaryAsync(
                item => item.DepartmentKey,
                item => item.BackgroundColor,
                StringComparer.Ordinal,
                cancellationToken);

    public async Task<IReadOnlyDictionary<string, string>> GetUserOverridesAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await dbContext.UserDepartmentColorPreferences
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .ToDictionaryAsync(
                item => item.DepartmentKey,
                item => item.BackgroundColor,
                StringComparer.Ordinal,
                cancellationToken);

    public async Task<bool> SetAdminDefaultAsync(
        string departmentKey,
        string? color,
        string actor,
        string reason,
        string correlationId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        return await RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);
            DepartmentColorSetting? existing = await dbContext.DepartmentColorSettings
                .SingleOrDefaultAsync(item => item.DepartmentKey == departmentKey, cancellationToken);
            string? previous = existing?.BackgroundColor;
            if (StringComparer.Ordinal.Equals(previous, color))
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            if (color is null)
            {
                dbContext.DepartmentColorSettings.Remove(existing!);
            }
            else if (existing is null)
            {
                dbContext.DepartmentColorSettings.Add(
                    DepartmentColorSetting.Create(departmentKey, color, actor, atUtc));
            }
            else
            {
                existing.Change(color, actor, atUtc);
            }

            dbContext.DepartmentColorAudits.Add(DepartmentColorAudit.Create(
                DepartmentColorScope.AdminDefault,
                null,
                departmentKey,
                previous,
                color,
                actor,
                reason,
                correlationId,
                atUtc));

            List<GoogleCalendarConnection> connections = await dbContext.GoogleCalendarConnections
                .Where(item => item.InitialSyncState == GoogleCalendarInitialSyncState.Completed)
                .ToListAsync(cancellationToken);
            foreach (GoogleCalendarConnection connection in connections)
            {
                connection.RequestCalendarPresentationRefresh(atUtc);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task<bool> SetUserOverrideAsync(
        Guid userId,
        string departmentKey,
        string? color,
        string correlationId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        return await RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);
            UserDepartmentColorPreference? existing =
                await dbContext.UserDepartmentColorPreferences.SingleOrDefaultAsync(
                    item => item.UserId == userId && item.DepartmentKey == departmentKey,
                    cancellationToken);
            string? previous = existing?.BackgroundColor;
            if (StringComparer.Ordinal.Equals(previous, color))
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            if (color is null)
            {
                dbContext.UserDepartmentColorPreferences.Remove(existing!);
            }
            else if (existing is null)
            {
                dbContext.UserDepartmentColorPreferences.Add(
                    UserDepartmentColorPreference.Create(userId, departmentKey, color, atUtc));
            }
            else
            {
                existing.Change(color, atUtc);
            }

            dbContext.DepartmentColorAudits.Add(DepartmentColorAudit.Create(
                DepartmentColorScope.UserOverride,
                userId,
                departmentKey,
                previous,
                color,
                userId.ToString(),
                null,
                correlationId,
                atUtc));

            GoogleCalendarConnection? connection = await dbContext.GoogleCalendarConnections
                .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
            connection?.RequestCalendarPresentationRefresh(atUtc);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }
}
