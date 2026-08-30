using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Sirkadiyen.Application.Announcements;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Application.Scheduling.Diffing;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Infrastructure.Google;
using Sirkadiyen.Infrastructure.Scheduling.Ingestion;

namespace Sirkadiyen.Worker.Configuration;

internal sealed class WorkerOptionsFactory(
    IConfiguration configuration,
    IHostEnvironment environment)
{
    public string ConnectionString => ConfigurationValueParser.Required(
        configuration,
        "SIRKADIYEN_DATABASE:CONNECTION_STRING");

    public Uri ParserBaseUrl => new(ConfigurationValueParser.Required(
        configuration,
        "SIRKADIYEN_PARSER:BASE_URL"));

    public TimeSpan ParserTimeout => ConfigurationValueParser.Duration(
        configuration["SIRKADIYEN_PARSER:TIMEOUT"],
        TimeSpan.FromMinutes(2));

    public string? DataProtectionKeyRingPath =>
        configuration["SIRKADIYEN_DATAPROTECTION:KEY_RING_PATH"];

    public GoogleSourceAccessOptions CreateGoogleSourceAccessOptions() => new()
    {
        ClientId = configuration["SIRKADIYEN_GOOGLE:CLIENT_ID"],
        ClientSecret = configuration["SIRKADIYEN_GOOGLE:CLIENT_SECRET"],
        SourceRefreshToken = configuration["SIRKADIYEN_GOOGLE:SOURCE_REFRESH_TOKEN"],
        ServiceAccountCredentialPath =
            configuration["SIRKADIYEN_GOOGLE:SERVICE_ACCOUNT_CREDENTIAL_PATH"],
    };

    public GoogleCalendarAuthorizationOptions CreateCalendarAuthorizationOptions() => new()
    {
        ClientId = ConfigurationValueParser.Required(
            configuration,
            "SIRKADIYEN_GOOGLE:CALENDAR_CLIENT_ID"),
        ClientSecret = ConfigurationValueParser.Required(
            configuration,
            "SIRKADIYEN_GOOGLE:CALENDAR_CLIENT_SECRET"),
        RedirectUri = configuration["SIRKADIYEN_GOOGLE:CALENDAR_REDIRECT_URI"]
            is { Length: > 0 } redirect
                ? redirect
                : GoogleCalendarAuthorizationOptions.PostMessageRedirectUri,
    };

    public InitialSyncOptions CreateInitialSyncOptions() => Validate(new InitialSyncOptions
    {
        ConnectionBatchSize = ConfigurationValueParser.Integer(
            configuration["SIRKADIYEN_SYNC:CONNECTION_BATCH_SIZE"], 5),
        EventsPerConnectionPerCycle = ConfigurationValueParser.Integer(
            configuration["SIRKADIYEN_SYNC:EVENTS_PER_CONNECTION"], 100),
        CalendarSummary = configuration["SIRKADIYEN_SYNC:CALENDAR_SUMMARY"]
            is { Length: > 0 } summary ? summary : "Sirkadiyen",
        CalendarTimeZoneId = configuration["SIRKADIYEN_SYNC:CALENDAR_TIME_ZONE_ID"]
            is { Length: > 0 } zone ? zone : "Europe/Istanbul",
    }, static options => options.Validate());

    public IncrementalSyncOptions CreateIncrementalSyncOptions() =>
        Validate(new IncrementalSyncOptions
        {
            DiffDispatchBatchSize = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:DIFF_DISPATCH_BATCH_SIZE"], 10),
            CalendarOperationsPerDiffBatch = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:CALENDAR_OPERATIONS_PER_DIFF_BATCH"], 100),
            MaxDispatchAttempts = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:MAX_DISPATCH_ATTEMPTS"], 5),
            DispatchRetryBaseDelaySeconds = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:DISPATCH_RETRY_BASE_DELAY_SECONDS"], 30),
        }, static options => options.Validate());

    public ProfileResyncOptions CreateProfileResyncOptions() =>
        Validate(new ProfileResyncOptions
        {
            ConnectionBatchSize = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:PROFILE_RESYNC_CONNECTION_BATCH_SIZE"], 5),
            CalendarOperationsPerConnectionPerCycle = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:PROFILE_RESYNC_OPERATIONS_PER_CONNECTION"], 100),
        }, static options => options.Validate());

    /// <summary>
    /// Bounds the automatic academic-year reconciler (ADR-117). It is separate from the resync
    /// batch it feeds: this caps how fast convergence work is created, that one caps how fast
    /// calendars are written.
    /// </summary>
    public ProfileAcademicYearDriftOptions CreateProfileAcademicYearDriftOptions() =>
        Validate(new ProfileAcademicYearDriftOptions
        {
            ProfilesPerProgramPerCycle = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:ACADEMIC_YEAR_DRIFT_PROFILES_PER_PROGRAM"], 25),
        }, static options => options.Validate());

    public CalendarReconciliationOptions CreateReconciliationOptions() =>
        Validate(new CalendarReconciliationOptions
        {
            ConnectionBatchSize = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:RECONCILIATION_CONNECTION_BATCH_SIZE"], 5),
            DiffsPerConnectionPerCycle = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:RECONCILIATION_DIFFS_PER_CONNECTION"], 10),
        }, static options => options.Validate());

    public AnnouncementDispatchOptions CreateAnnouncementDispatchOptions() =>
        Validate(new AnnouncementDispatchOptions
        {
            AnnouncementBatchSize = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:ANNOUNCEMENT_BATCH_SIZE"], 3),
            CalendarOperationsPerAnnouncementPerCycle = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:ANNOUNCEMENT_OPERATIONS_PER_CYCLE"], 200),
            MaximumDeliveryAttempts = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:ANNOUNCEMENT_MAX_ATTEMPTS"], 8),
            InitialBackoff = ConfigurationValueParser.Duration(
                configuration["SIRKADIYEN_SYNC:ANNOUNCEMENT_RETRY_INITIAL_BACKOFF"],
                TimeSpan.FromMinutes(2)),
            MaximumBackoff = ConfigurationValueParser.Duration(
                configuration["SIRKADIYEN_SYNC:ANNOUNCEMENT_RETRY_MAX_BACKOFF"],
                TimeSpan.FromHours(2)),
        }, static options => options.Validate());

    public CalendarInventoryReconciliationOptions CreateInventoryOptions() =>
        Validate(new CalendarInventoryReconciliationOptions
        {
            ConnectionBatchSize = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_SYNC:INVENTORY_CONNECTION_BATCH_SIZE"], 5),
            Interval = ConfigurationValueParser.Duration(
                configuration["SIRKADIYEN_SYNC:INVENTORY_INTERVAL"], TimeSpan.FromHours(24)),
        }, static options => options.Validate());

    public AdaptivePollingOptions CreatePollingOptions() => new()
    {
        TimeZoneId = configuration["SIRKADIYEN_POLLING:TIME_ZONE_ID"] ?? "Europe/Istanbul",
        DaytimeStart = ConfigurationValueParser.Time(
            configuration["SIRKADIYEN_POLLING:DAYTIME_START"], new TimeOnly(7, 0)),
        LateAfternoonStart = ConfigurationValueParser.Time(
            configuration["SIRKADIYEN_POLLING:LATE_AFTERNOON_START"], new TimeOnly(16, 0)),
        NightStart = ConfigurationValueParser.Time(
            configuration["SIRKADIYEN_POLLING:NIGHT_START"], new TimeOnly(21, 0)),
        DaytimeInterval = ConfigurationValueParser.Duration(
            configuration["SIRKADIYEN_POLLING:DAYTIME_INTERVAL"], TimeSpan.FromMinutes(15)),
        LateAfternoonInterval = ConfigurationValueParser.Duration(
            configuration["SIRKADIYEN_POLLING:LATE_AFTERNOON_INTERVAL"],
            TimeSpan.FromMinutes(25)),
        NightInterval = ConfigurationValueParser.Duration(
            configuration["SIRKADIYEN_POLLING:NIGHT_INTERVAL"], TimeSpan.FromMinutes(45)),
        WeekendInterval = ConfigurationValueParser.Duration(
            configuration["SIRKADIYEN_POLLING:WEEKEND_INTERVAL"], TimeSpan.FromHours(1)),
    };

    public RevisionValidationOptions CreateValidationOptions() =>
        Validate(new RevisionValidationOptions
        {
            MaximumDeletionShare = ConfigurationValueParser.Double(
                configuration["SIRKADIYEN_VALIDATION:MAXIMUM_DELETION_SHARE"], 0.20),
            MinimumDeletionCount = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_VALIDATION:MINIMUM_DELETION_COUNT"], 10),
            LowConfidenceThreshold = (decimal)ConfigurationValueParser.Double(
                configuration["SIRKADIYEN_VALIDATION:LOW_CONFIDENCE_THRESHOLD"], 0.50),
            MinimumLessonMinutes = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_VALIDATION:MINIMUM_LESSON_MINUTES"], 10),
            MaximumLessonMinutes = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_VALIDATION:MAXIMUM_LESSON_MINUTES"], 600),
            MaximumToleratedOverlaps = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_VALIDATION:MAXIMUM_TOLERATED_OVERLAPS"], 1),
            AcademicYearGraceDays = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_VALIDATION:ACADEMIC_YEAR_GRACE_DAYS"], 30),
        }, static options => options.Validate());

    public SemanticDiffOptions CreateDiffOptions() => Validate(
        new SemanticDiffOptions(),
        static options => options.Validate());

    public ScheduleDiffSafetyThresholds CreateDiffThresholds() =>
        Validate(new ScheduleDiffSafetyThresholds
        {
            MaximumDeletionShare = ConfigurationValueParser.Double(
                configuration["SIRKADIYEN_DIFF:MAXIMUM_DELETION_SHARE"], 0.20),
            MinimumDeletionCount = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_DIFF:MINIMUM_DELETION_COUNT"], 10),
        }, static options => options.Validate());

    public ParseRunOptions CreateParseRunOptions() => Validate(new ParseRunOptions
    {
        StaleRunTimeout = ConfigurationValueParser.Duration(
            configuration["SIRKADIYEN_PARSER:STALE_RUN_TIMEOUT"], TimeSpan.FromMinutes(30)),
    }, static options => options.Validate());

    public SnapshotRetentionOptions CreateRetentionOptions() =>
        Validate(new SnapshotRetentionOptions
        {
            RecentWindow = TimeSpan.FromDays(ConfigurationValueParser.Double(
                configuration["SIRKADIYEN_RETENTION:SNAPSHOT_RECENT_DAYS"], 10)),
            BatchSize = ConfigurationValueParser.Integer(
                configuration["SIRKADIYEN_RETENTION:SNAPSHOT_BATCH_SIZE"], 50),
        }, static options => options.Validate());

    public WorkerOptions CreateWorkerOptions()
    {
        string configuredCatalogPath = configuration["SIRKADIYEN_SOURCES:CATALOG_PATH"]
            ?? "config/schedule-sources.json";
        return Validate(new WorkerOptions
        {
            SourceCatalogPath = Path.GetFullPath(
                configuredCatalogPath,
                environment.ContentRootPath),

            // Always the copy inside the artifact, never the configured path: the point is to
            // compare what this release ships with what the server is running (ADR-138).
            ShippedSourceCatalogPath = Path.GetFullPath(
                "config/schedule-sources.json",
                environment.ContentRootPath),
            Release = ReadRelease(),
            CalendarCatchUpInterval = ConfigurationValueParser.Duration(
                configuration["SIRKADIYEN_SYNC:CALENDAR_CATCH_UP_INTERVAL"],
                TimeSpan.FromSeconds(5)),
            CalendarIdleCheckInterval = ConfigurationValueParser.Duration(
                configuration["SIRKADIYEN_SYNC:CALENDAR_IDLE_CHECK_INTERVAL"],
                TimeSpan.FromSeconds(5)),
        }, static options => options.Validate());
    }

    private static T Validate<T>(T options, Action<T> validation) where T : class
    {
        validation(options);
        return options;
    }

    /// <summary>
    /// Which release this worker is, for the catalog revision it installs (ADR-138).
    /// </summary>
    /// <remarks>
    /// Read from a file inside the artifact rather than from configuration, because the unit file
    /// that would otherwise carry it is installed by hand on the server and would then have to be
    /// edited on every deployment. The build writes the commit into it.
    /// </remarks>
    private string ReadRelease()
    {
        if (configuration["SIRKADIYEN_DEPLOY:RELEASE"] is { } configured
            && !string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        string path = Path.Combine(environment.ContentRootPath, "RELEASE");
        try
        {
            return File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } release
                ? release
                : "unknown release";
        }
        catch (IOException)
        {
            // A release name is a label on a history row, never a reason to fail startup.
            return "unknown release";
        }
    }
}
