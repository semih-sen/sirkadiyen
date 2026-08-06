namespace Sirkadiyen.Application.GoogleCalendar;

public interface IDepartmentColorStore
{
    Task<IReadOnlyDictionary<string, string>> GetAdminDefaultsAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>> GetUserOverridesAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<bool> SetAdminDefaultAsync(
        string departmentKey,
        string? color,
        string actor,
        string reason,
        string correlationId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    Task<bool> SetUserOverrideAsync(
        Guid userId,
        string departmentKey,
        string? color,
        string correlationId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}
