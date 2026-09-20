using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Infrastructure.UnitTests;

internal static class TestDepartmentColors
{
    /// <summary>
    /// A colour service with no stored colours, so every key resolves to its catalog default.
    /// Pass <paramref name="adminDefaults"/> to stand in for colours an operator has set.
    /// </summary>
    public static DepartmentColorService Create(
        IReadOnlyDictionary<string, string>? adminDefaults = null) =>
        new(new EmptyStore(adminDefaults), TimeProvider.System);

    private sealed class EmptyStore(IReadOnlyDictionary<string, string>? adminDefaults)
        : IDepartmentColorStore
    {
        private static readonly IReadOnlyDictionary<string, string> Empty =
            new Dictionary<string, string>();

        public Task<IReadOnlyDictionary<string, string>> GetAdminDefaultsAsync(
            CancellationToken cancellationToken) => Task.FromResult(adminDefaults ?? Empty);

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
