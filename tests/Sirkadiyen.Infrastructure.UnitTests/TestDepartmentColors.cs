using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Infrastructure.UnitTests;

internal static class TestDepartmentColors
{
    public static DepartmentColorService Create() =>
        new(new EmptyStore(), TimeProvider.System);

    private sealed class EmptyStore : IDepartmentColorStore
    {
        private static readonly IReadOnlyDictionary<string, string> Empty =
            new Dictionary<string, string>();

        public Task<IReadOnlyDictionary<string, string>> GetAdminDefaultsAsync(
            CancellationToken cancellationToken) => Task.FromResult(Empty);

        public Task<IReadOnlyDictionary<string, string>> GetUserOverridesAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult(Empty);

        public Task<bool> SetAdminDefaultAsync(
            string departmentKey,
            string? color,
            string actor,
            string reason,
            string correlationId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> SetUserOverrideAsync(
            Guid userId,
            string departmentKey,
            string? color,
            string correlationId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
