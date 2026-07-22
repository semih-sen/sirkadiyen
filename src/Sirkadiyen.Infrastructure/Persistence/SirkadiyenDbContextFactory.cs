using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sirkadiyen.Infrastructure.Configuration;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// Builds a context for the EF Core command-line tools.
/// </summary>
/// <remarks>
/// Design-time only. It reads the connection string from the environment, and
/// from the repository's <c>.env</c> file when the environment does not set it,
/// so no credential is ever written into the repository. It falls back to a
/// local development host when neither supplies one, because generating a
/// migration does not require a reachable database.
/// </remarks>
public sealed class SirkadiyenDbContextFactory : IDesignTimeDbContextFactory<SirkadiyenDbContext>
{
    public const string ConnectionStringVariable = "SIRKADIYEN_DATABASE__CONNECTION_STRING";

    private const string DesignTimeFallback =
        "Host=localhost;Port=15432;Database=sirkadiyen;Username=sirkadiyen;Password=sirkadiyen";

    public SirkadiyenDbContext CreateDbContext(string[] args)
    {
        DotEnvFile.Load();

        string connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? DesignTimeFallback;

        DbContextOptions<SirkadiyenDbContext> options =
            new DbContextOptionsBuilder<SirkadiyenDbContext>()
                .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    SirkadiyenDbContext.SchemaName))
                .Options;

        return new SirkadiyenDbContext(options);
    }
}
