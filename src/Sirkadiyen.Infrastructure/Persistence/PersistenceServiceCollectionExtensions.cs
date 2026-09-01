using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Observability;
using Sirkadiyen.Application.Announcements;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Access;
using Sirkadiyen.Application.Scheduling.Diffing;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Infrastructure.Persistence.Administration.Stores;
using Sirkadiyen.Infrastructure.Persistence.Observability.Stores;
using Sirkadiyen.Infrastructure.Persistence.Announcements.Stores;
using Sirkadiyen.Infrastructure.Persistence.Auditing.Stores;
using Sirkadiyen.Infrastructure.Persistence.Finance.Stores;
using Sirkadiyen.Infrastructure.Persistence.GoogleCalendar.Stores;
using Sirkadiyen.Infrastructure.Persistence.Identity.Stores;
using Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;
using Sirkadiyen.Infrastructure.Persistence.Operations.Stores;
using Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;
using Sirkadiyen.Infrastructure.Persistence.StudentProfiles.Stores;
using Sirkadiyen.Infrastructure.Persistence.StudentRosters.Stores;

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
        services.AddScoped<ICalendarDispatchReconciliationFence>(
            _ => new PostgresCalendarDispatchReconciliationFence(connectionString));

        services.AddScoped<IScheduleSourceStore, ScheduleSourceStore>();
        services.AddScoped<
            IScheduleSourceCatalogRevisionStore,
            ScheduleSourceCatalogRevisionStore>();
        services.AddScoped<
            IStudentRosterCatalogRevisionStore,
            StudentRosterCatalogRevisionStore>();
        services.AddScoped<ISourceSnapshotStore, SourceSnapshotStore>();
        services.AddScoped<ISourcePollRequestStore, SourcePollRequestStore>();
        services.AddScoped<ISnapshotRetentionStore, SnapshotRetentionStore>();
        services.AddScoped<IPipelineStallReadStore, PipelineStallReadStore>();
        services.AddScoped<ISourceDocumentUploadAuditStore, SourceDocumentUploadAuditStore>();
        services.AddScoped<IScheduleParseResultStore, ScheduleParseResultStore>();
        services.AddScoped<IGroupRotationCoverageStore, GroupRotationCoverageStore>();
        services.AddScoped<
            IScheduleSourceDateCorrectionStore,
            ScheduleSourceDateCorrectionStore>();
        services.AddScoped<IScheduleRevisionValidationStore, ScheduleRevisionValidationStore>();
        services.AddScoped<IScheduleRevisionPublicationStore, ScheduleRevisionPublicationStore>();
        services.AddScoped<IScheduleRevisionReadStore, ScheduleRevisionReadStore>();
        services.AddScoped<IScheduleDiffStore, ScheduleDiffStore>();
        services.AddScoped<IScheduleDiffReviewStore, ScheduleDiffReviewStore>();
        services.AddScoped<IOperationalFreezeStore, OperationalFreezeStore>();
        services.AddScoped<IUserStore, UserStore>();
        services.AddScoped<IUserRoleStore, UserRoleStore>();
        services.AddScoped<IAccountDeletionStore, AccountDeletionStore>();
        services.AddScoped<ILicenseStore, LicenseStore>();
        services.AddScoped<IStudentProfileStore, StudentProfileStore>();
        services.AddScoped<IProfileAcademicYearRolloverStore, ProfileAcademicYearRolloverStore>();
        // One scoped store implements both connection role interfaces (ISP); every consumer
        // depends only on the narrow role it uses. The three mappings share the one instance.
        services.AddScoped<GoogleCalendarConnectionStore>();
        services.AddScoped<IGoogleCalendarConnectionReader>(
            provider => provider.GetRequiredService<GoogleCalendarConnectionStore>());
        services.AddScoped<IUserCalendarConnectionStore>(
            provider => provider.GetRequiredService<GoogleCalendarConnectionStore>());
        services.AddScoped<ICalendarSyncConnectionStore>(
            provider => provider.GetRequiredService<GoogleCalendarConnectionStore>());
        services.AddScoped<ICalendarConnectionHealthWriter>(
            provider => provider.GetRequiredService<GoogleCalendarConnectionStore>());
        services.AddScoped<IUserCalendarEventMappingStore, UserCalendarEventMappingStore>();
        services.AddScoped<ICanonicalScheduleReadStore, CanonicalScheduleReadStore>();
        services.AddScoped<ICalendarSyncTargetReadStore, CalendarSyncTargetReadStore>();
        services.AddScoped<ICohortCalendarRepairStore, CohortCalendarRepairStore>();
        services.AddScoped<IDepartmentColorStore, DepartmentColorStore>();
        services.AddScoped<IAuditEventStore, AuditEventStore>();
        services.AddScoped<IUserScheduleReadStore, UserScheduleReadStore>();
        services.AddScoped<IAdminUserReadStore, AdminUserReadStore>();
        services.AddScoped<IAdminLicenseReadStore, AdminLicenseReadStore>();
        services.AddScoped<ISourceStatusReadStore, SourceStatusReadStore>();
        services.AddScoped<IAdminMetricsReadStore, AdminMetricsReadStore>();
        services.AddScoped<IWorkerHeartbeatStore, WorkerHeartbeatStore>();
        services.AddScoped<IFinanceLedgerStore, FinanceLedgerStore>();
        services.AddScoped<IFinanceReadStore, FinanceReadStore>();
        services.AddScoped<IFinanceAuditStore, FinanceAuditStore>();
        services.AddScoped<IFinanceObligationStore, FinanceObligationStore>();
        services.AddScoped<IFinanceSummaryReadStore, FinanceSummaryReadStore>();
        services.AddScoped<IFinanceDistributionStore, FinanceDistributionStore>();
        services.AddScoped<IAnnouncementStore, AnnouncementStore>();
        services.AddScoped<IAnnouncementAudienceReadStore, AnnouncementAudienceReadStore>();

        return services;
    }
}
