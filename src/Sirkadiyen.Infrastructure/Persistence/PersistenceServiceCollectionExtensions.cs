using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Application.ScheduleParsing;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Application.ScheduleSources;
using Sirkadiyen.Application.StudentProfiles;

namespace Sirkadiyen.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL context and the stores that depend on it.
    /// </summary>
    public static IServiceCollection AddSirkadiyenPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<SirkadiyenDbContext>(options => options
            .UseNpgsql(connectionString, static npgsql =>
            {
                npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    SirkadiyenDbContext.SchemaName);

                // Polling and calendar synchronization run against external
                // services, so a transient database blip should not fail a job
                // that is otherwise safe to retry.
                npgsql.EnableRetryOnFailure();
            }));

        services.AddScoped<IScheduleSourceStore, ScheduleSourceStore>();
        services.AddScoped<ISourceSnapshotStore, SourceSnapshotStore>();
        services.AddScoped<ISnapshotRetentionStore, SnapshotRetentionStore>();
        services.AddScoped<IScheduleParseResultStore, ScheduleParseResultStore>();
        services.AddScoped<IScheduleRevisionValidationStore, ScheduleRevisionValidationStore>();
        services.AddScoped<IScheduleRevisionPublicationStore, ScheduleRevisionPublicationStore>();
        services.AddScoped<IScheduleRevisionReadStore, ScheduleRevisionReadStore>();
        services.AddScoped<IScheduleDiffStore, ScheduleDiffStore>();
        services.AddScoped<IScheduleDiffReviewStore, ScheduleDiffReviewStore>();
        services.AddScoped<IOperationalFreezeStore, OperationalFreezeStore>();
        services.AddScoped<IUserStore, UserStore>();
        services.AddScoped<ILicenseStore, LicenseStore>();
        services.AddScoped<IStudentProfileStore, StudentProfileStore>();

        return services;
    }
}
