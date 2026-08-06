using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Scheduling.Diffing;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Application.Scheduling.Publication;
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
        services.AddSirkadiyenDataProtection(options.DataProtectionKeyRingPath);
        services.AddSingleton<ICalendarTokenProtector, DataProtectionCalendarTokenProtector>();
        services.AddSingleton<IUserCalendarClient, GoogleCalendarClient>();
        services.AddScoped<DepartmentColorService>();
        services.AddScoped<InitialCalendarSyncService>();
        services.AddScoped<IncrementalCalendarSyncService>();
        services.AddScoped<ProfileChangeResyncService>();
        services.AddScoped<CalendarReconciliationService>();
        services.AddScoped<CalendarInventoryReconciliationService>();
        services.AddSingleton<GoogleSheetsServiceFactory>();
        services.AddSingleton<SheetsService>(provider =>
            provider.GetRequiredService<GoogleSheetsServiceFactory>().Create(googleOptions));
        services.AddSingleton<GoogleSheetsSnapshotMapper>();
        services.AddScoped<ISpreadsheetSnapshotAcquirer, GoogleSheetsSnapshotAcquirer>();
        services.AddSirkadiyenGoogleDriveClient(googleOptions);
        services.AddSingleton<DocxSnapshotConverter>();
        services.AddScoped<IDriveDocumentAcquirer, DriveDocumentAcquirer>();
        services.AddScoped<ScheduleSourcePoller>();
        services.AddSirkadiyenPersistence(options.ConnectionString);
        services.AddSirkadiyenParserClient(options.ParserBaseUrl, options.ParserTimeout);

        services.AddSingleton<SourceCatalogInitializer>();
        services.AddSingleton<SourcePollingTask>();
        services.AddSingleton<RevisionPublicationTask>();
        services.AddSingleton<ScheduleDiffCalculationTask>();
        services.AddSingleton<SourceProcessingPipeline>();
        services.AddSingleton<SnapshotRetentionTask>();
        services.AddSingleton<InitialCalendarSyncTask>();
        services.AddSingleton<PendingDiffDispatchTask>();
        services.AddSingleton<CalendarReconciliationTask>();
        services.AddSingleton<ProfileResyncTask>();
        services.AddSingleton<CalendarInventoryTask>();
        services.AddSingleton<FencedCalendarMaintenanceTask>();
        services.AddHostedService<Worker>();

        return services;
    }
}
