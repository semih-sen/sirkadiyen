using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Infrastructure.Configuration;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// A migrated PostgreSQL database, shared by the integration tests.
/// </summary>
/// <remarks>
/// The connection string comes from the environment, or from the repository's
/// <c>.env</c> file when the environment does not set it, so no credential is
/// written into the repository. When neither supplies one the integration tests
/// skip themselves rather than fail: a developer without a local database still
/// gets a meaningful test run, and CI sets the variable so the tests really
/// execute.
/// <para>
/// This fixture drops and re-migrates whatever database it is pointed at, so the
/// variable must name a dedicated test database and never a working one.
/// </para>
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
        DotEnvFile.Load();

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

    public SirkadiyenDbContext CreateContext() => CreateContext(retryOnFailure: false);

    /// <summary>
    /// A context configured the way the worker and the API configure theirs.
    /// </summary>
    /// <remarks>
    /// The hosts enable retry on transient failures, and a retrying execution
    /// strategy refuses to take part in a transaction it did not open. A store
    /// that begins its own transaction therefore behaves differently here than
    /// under the plain test context, and only this configuration proves it works
    /// in production.
    /// </remarks>
    public SirkadiyenDbContext CreateProductionLikeContext() =>
        CreateContext(retryOnFailure: true);

    private SirkadiyenDbContext CreateContext(bool retryOnFailure)
    {
        if (ConnectionString is null)
        {
            throw new InvalidOperationException(SkipReason);
        }

        return new SirkadiyenDbContext(
            new DbContextOptionsBuilder<SirkadiyenDbContext>()
                .UseNpgsql(ConnectionString, npgsql =>
                {
                    npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        SirkadiyenDbContext.SchemaName);

                    if (retryOnFailure)
                    {
                        npgsql.EnableRetryOnFailure();
                    }
                })
                .Options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
