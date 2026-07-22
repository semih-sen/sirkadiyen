using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// Builds a context for the EF Core command-line tools.
/// </summary>
/// <remarks>
/// Design-time only. It reads the connection string from the environment so no
/// credential is ever written into the repository, and it falls back to a local
/// development host when the variable is absent, because generating a migration
/// does not require a reachable database.
/// </remarks>
public sealed class SirkadiyenDbContextFactory : IDesignTimeDbContextFactory<SirkadiyenDbContext>
{
    public const string ConnectionStringVariable = "SIRKADIYEN_DATABASE__CONNECTION_STRING";

    private const string DesignTimeFallback =
        "Host=localhost;Port=15432;Database=sirkadiyen;Username=sirkadiyen;Password=sirkadiyen";

    public SirkadiyenDbContext CreateDbContext(string[] args)
    {
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
