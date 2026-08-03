using Sirkadiyen.Application.GoogleCalendar;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class DepartmentColorServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EffectiveColorUsesUserThenAdminThenSystemPrecedence()
    {
        MemoryStore store = new()
        {
            Admin = new Dictionary<string, string> { ["anatomi"] = "#111111" },
            User = new Dictionary<string, string> { ["anatomi"] = "#222222" },
        };
        DepartmentColorService service = new(store, new FixedTimeProvider(Now));

        IReadOnlyDictionary<string, string> colors =
            await service.GetEffectiveColorsAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal("#222222", colors["anatomi"]);
        Assert.Equal(DepartmentCatalog.DefaultColor("fizyoloji"), colors["fizyoloji"]);
    }

    [Fact]
    public async Task InvalidColorAndUnknownDepartmentAreRefusedBeforePersistence()
    {
        MemoryStore store = new();
        DepartmentColorService service = new(store, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetUserColorAsync(
            Guid.CreateVersion7(),
            "anatomi",
            "red",
            "trace",
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetUserColorAsync(
            Guid.CreateVersion7(),
            "uydurma",
            "#123456",
            "trace",
            CancellationToken.None));
        Assert.Empty(store.Mutations);
    }

    [Fact]
    public async Task AdminChangeRequiresAReasonAndNormalizesColor()
    {
        MemoryStore store = new();
        DepartmentColorService service = new(store, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAdminColorAsync(
            "anatomi",
            "#abcdef",
            "admin@example.com",
            " ",
            "trace",
            CancellationToken.None));
        await service.SetAdminColorAsync(
            "anatomi",
            "#abcdef",
            "admin@example.com",
            "Yeni palet",
            "trace",
            CancellationToken.None);

        Assert.Single(store.Mutations);
        Assert.Equal("#ABCDEF", store.Mutations[0].Color);
    }

    private sealed class MemoryStore : IDepartmentColorStore
    {
        public IReadOnlyDictionary<string, string> Admin { get; init; } =
            new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> User { get; init; } =
            new Dictionary<string, string>();
        public List<(string Key, string? Color)> Mutations { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> GetAdminDefaultsAsync(
            CancellationToken cancellationToken) => Task.FromResult(Admin);

        public Task<IReadOnlyDictionary<string, string>> GetUserOverridesAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult(User);

        public Task<bool> SetAdminDefaultAsync(
            string departmentKey,
            string? color,
            string actor,
            string reason,
            string correlationId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Mutations.Add((departmentKey, color));
            return Task.FromResult(true);
        }

        public Task<bool> SetUserOverrideAsync(
            Guid userId,
            string departmentKey,
            string? color,
            string correlationId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Mutations.Add((departmentKey, color));
            return Task.FromResult(true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
