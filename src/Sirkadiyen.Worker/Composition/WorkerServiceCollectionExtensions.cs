using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sirkadiyen.Application.Announcements;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Scheduling.Diffing;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Infrastructure.Google;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Scheduling.Ingestion;
using Sirkadiyen.Infrastructure.Scheduling.Parsing;
using Sirkadiyen.Infrastructure.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Security;
using Sirkadiyen.Worker.Calendars;
using Sirkadiyen.Worker.Configuration;
using Sirkadiyen.Worker.Health;
using Sirkadiyen.Worker.Sources;

namespace Sirkadiyen.Worker.Composition;

internal static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerApplication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        WorkerOptionsFactory options = new(configuration, environment);

        GoogleSourceAccessOptions googleOptions = options.CreateGoogleSourceAccessOptions();
        SemanticDiffOptions diffOptions = options.CreateDiffOptions();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<WorkerHealthState>();
        services.AddSingleton(diffOptions);
        services.AddSingleton(options.CreateDiffThresholds());
        services.AddSingleton<SemanticScheduleDiffer>();
        services.AddScoped<ScheduleDiffService>();
        services.AddSingleton(options.CreatePollingOptions());
        services.AddSingleton(options.CreateValidationOptions());
        services.AddSingleton(options.CreateRetentionOptions());
        services.AddSingleton(options.CreateParseRunOptions());
        services.AddSingleton<ScheduleRevisionValidator>();
        services.AddScoped<SnapshotRetentionService>();
        services.AddScoped<ScheduleRevisionValidationService>();
        services.AddScoped<ScheduleRevisionPublicationService>();
        services.AddSingleton<AdaptivePollingIntervalPolicy>();
        services.AddSingleton(options.CreateWorkerOptions());
        services.AddSingleton<ScheduleSourceCatalogLoader>();
        services.AddSingleton(googleOptions);
        services.AddSingleton(options.CreateCalendarAuthorizationOptions());
        services.AddSingleton(options.CreateInitialSyncOptions());
        services.AddSingleton(options.CreateIncrementalSyncOptions());
        services.AddSingleton(options.CreateProfileResyncOptions());
        services.AddSingleton(options.CreateReconciliationOptions());
        services.AddSingleton(options.CreateInventoryOptions());
        services.AddSingleton(options.CreateAnnouncementDispatchOptions());
        services.AddSirkadiyenDataProtection(options.DataProtectionKeyRingPath);
        services.AddSingleton<ICalendarTokenProtector, DataProtectionCalendarTokenProtector>();
        services.AddSingleton<IUserCalendarClient, GoogleCalendarClient>();
        services.AddScoped<DepartmentColorService>();
        services.AddScoped<InitialCalendarSyncService>();
        services.AddScoped<IncrementalCalendarSyncService>();
        services.AddScoped<ProfileChangeResyncService>();

        // The automatic half of the academic-year rollover (ADR-117). The operator screen
        // in the API drives the same service; this is the same repair, unattended.
        services.AddSingleton(CurrentSupportedProfileSchema.Create());
        services.AddSingleton(options.CreateProfileAcademicYearDriftOptions());
        services.AddScoped<ProfileAcademicYearRolloverService>();

        // The worker writes audit entries for the profile changes it makes on nobody's
        // request, so it needs the recorder the API already uses. There is no client IP on
        // a background pass; the protector is still required to construct it.
        services.AddSingleton<IAuditIpProtector, DataProtectionAuditIpProtector>();
        services.AddScoped<AuditEventRecorder>();
        services.AddScoped<CalendarReconciliationService>();
        services.AddScoped<CalendarInventoryReconciliationService>();
        services.AddScoped<AnnouncementDispatchService>();
        services.AddSingleton<GoogleSheetsServiceFactory>();
        services.AddSingleton<SheetsService>(provider =>
            provider.GetRequiredService<GoogleSheetsServiceFactory>().Create(googleOptions));
        services.AddSingleton<GoogleSheetsSnapshotMapper>();
        services.AddScoped<ISpreadsheetSnapshotAcquirer, GoogleSheetsSnapshotAcquirer>();
        services.AddSirkadiyenGoogleDriveClient(googleOptions);
        services.AddSingleton<DocxSnapshotConverter>();

        // The Drive transport publishes both Office formats: the Grade 2 calendars
        // are documents and the Grade 3 programs are workbooks (ADR-083).
        services.AddSingleton<LocalXlsxSnapshotConverter>();
        services.AddScoped<IDriveDocumentAcquirer, DriveDocumentAcquirer>();
        services.AddScoped<ScheduleSourcePoller>();
        services.AddSirkadiyenPersistence(options.ConnectionString);
        services.AddSirkadiyenParserClient(options.ParserBaseUrl, options.ParserTimeout);

        // The catalog this release ships is installed through the administrative editing service,
        // so a deployment writes the running document by exactly the rules a panel edit does and
        // lands in the same history (ADR-138).
        services.AddSingleton(new ScheduleSourceCatalogFileOptions
        {
            Path = options.CreateWorkerOptions().SourceCatalogPath,
        });
        services.AddSingleton<IScheduleSourceCatalogFile, ScheduleSourceCatalogFile>();
        services.AddSingleton<IScheduleSourceCatalogSerializer, ScheduleSourceCatalogLoader>();
        services.AddScoped<ScheduleSourceCatalogEditingService>();
        services.AddSingleton<SourceCatalogInitializer>();
        services.AddSingleton<SourcePollingTask>();
        services.AddSingleton<ManualSourcePollTask>();
        services.AddSingleton<RevisionValidationTask>();
        services.AddSingleton<RevisionPublicationTask>();
        services.AddSingleton<ScheduleDiffCalculationTask>();
        services.AddSingleton<SourceProcessingPipeline>();
        services.AddSingleton<SnapshotRetentionTask>();
        services.AddSingleton<InitialCalendarSyncTask>();
        services.AddSingleton<PendingDiffDispatchTask>();
        services.AddSingleton<CalendarReconciliationTask>();
        services.AddSingleton<ProfileResyncTask>();
        services.AddSingleton<ProfileAcademicYearDriftTask>();
        services.AddSingleton<AnnouncementDispatchTask>();
        services.AddSingleton<CalendarInventoryTask>();
        services.AddSingleton<FencedCalendarMaintenanceTask>();
        services.AddSingleton<WorkerHeartbeatTask>();
        services.AddHostedService<Worker>();

        return services;
    }
}
