using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// A migrated PostgreSQL database, shared by the integration tests.
/// </summary>
/// <remarks>
/// The connection string comes from the environment so no credential is written
/// into the repository. When the variable is absent the integration tests skip
/// themselves rather than fail: a developer without a local database still gets
/// a meaningful test run, and CI sets the variable so the tests really execute.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string ConnectionStringVariable = "SIRKADIYEN_TEST_DATABASE__CONNECTION_STRING";

    public const string SkipReason =
        "Set SIRKADIYEN_TEST_DATABASE__CONNECTION_STRING to run the database integration tests. "
        + "'docker compose up -d postgres' starts a suitable database.";

    public string? ConnectionString { get; private set; }

    public bool IsAvailable => ConnectionString is not null;

    public async ValueTask InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (ConnectionString is null)
        {
            return;
        }

        await using SirkadiyenDbContext context = CreateContext();

        // Every run starts from the migrations, so a migration that does not
        // apply cleanly fails here rather than in production.
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public SirkadiyenDbContext CreateContext()
    {
        if (ConnectionString is null)
        {
            throw new InvalidOperationException(SkipReason);
        }

        return new SirkadiyenDbContext(
            new DbContextOptionsBuilder<SirkadiyenDbContext>()
                .UseNpgsql(ConnectionString, static npgsql => npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    SirkadiyenDbContext.SchemaName))
                .Options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
