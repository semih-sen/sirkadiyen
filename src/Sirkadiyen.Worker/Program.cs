using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Application.ScheduleParsing;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Infrastructure.Configuration;
using Sirkadiyen.Infrastructure.Google;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.ScheduleIngestion;
using Sirkadiyen.Infrastructure.ScheduleParsing;
using Sirkadiyen.Infrastructure.ScheduleSources;
using Sirkadiyen.Infrastructure.Security;
using Sirkadiyen.Worker;

// Before the builder, because the environment-variable provider reads the
// process environment as it is added. A deployed host injects its own variables
// and ships no file, so this does nothing there (ADR-041).
DotEnvFile.Load();

var builder = Host.CreateApplicationBuilder(args);

string connectionString = Required(
    builder.Configuration,
    "SIRKADIYEN_DATABASE:CONNECTION_STRING");
Uri parserBaseUrl = new(Required(builder.Configuration, "SIRKADIYEN_PARSER:BASE_URL"));
string configuredCatalogPath = builder.Configuration["SIRKADIYEN_SOURCES:CATALOG_PATH"]
    ?? "config/schedule-sources.json";
string catalogPath = Path.GetFullPath(configuredCatalogPath, builder.Environment.ContentRootPath);

GoogleSourceAccessOptions googleOptions = new()
{
    ClientId = builder.Configuration["SIRKADIYEN_GOOGLE:CLIENT_ID"],
    ClientSecret = builder.Configuration["SIRKADIYEN_GOOGLE:CLIENT_SECRET"],
    SourceRefreshToken = builder.Configuration["SIRKADIYEN_GOOGLE:SOURCE_REFRESH_TOKEN"],
    ServiceAccountCredentialPath =
        builder.Configuration["SIRKADIYEN_GOOGLE:SERVICE_ACCOUNT_CREDENTIAL_PATH"],
};

// Calendar synchronization refreshes each user's stored token with the confidential Calendar
// OAuth client (ADR-058). Required, like in the API: the worker cannot synchronize without it.
GoogleCalendarAuthorizationOptions calendarOptions = new()
{
    ClientId = Required(builder.Configuration, "SIRKADIYEN_GOOGLE:CALENDAR_CLIENT_ID"),
    ClientSecret = Required(builder.Configuration, "SIRKADIYEN_GOOGLE:CALENDAR_CLIENT_SECRET"),
    RedirectUri =
        builder.Configuration["SIRKADIYEN_GOOGLE:CALENDAR_REDIRECT_URI"] is { Length: > 0 } redirect
            ? redirect
            : GoogleCalendarAuthorizationOptions.PostMessageRedirectUri,
};

InitialSyncOptions initialSyncOptions = new()
{
    ConnectionBatchSize = ParseInteger(
        builder.Configuration["SIRKADIYEN_SYNC:CONNECTION_BATCH_SIZE"],
        5),
    EventsPerConnectionPerCycle = ParseInteger(
        builder.Configuration["SIRKADIYEN_SYNC:EVENTS_PER_CONNECTION"],
        100),
    CalendarSummary =
        builder.Configuration["SIRKADIYEN_SYNC:CALENDAR_SUMMARY"] is { Length: > 0 } summary
            ? summary
            : "Sirkadiyen",
    CalendarTimeZoneId =
        builder.Configuration["SIRKADIYEN_SYNC:CALENDAR_TIME_ZONE_ID"] is { Length: > 0 } zone
            ? zone
            : "Europe/Istanbul",
};
initialSyncOptions.Validate();

// How incremental sync fans diffs out onto calendars and backs off after a transient failure
// (ADR-059). Shares the SIRKADIYEN_SYNC block with initial sync.
IncrementalSyncOptions incrementalSyncOptions = new()
{
    DiffDispatchBatchSize = ParseInteger(
        builder.Configuration["SIRKADIYEN_SYNC:DIFF_DISPATCH_BATCH_SIZE"],
        10),
    MaxDispatchAttempts = ParseInteger(
        builder.Configuration["SIRKADIYEN_SYNC:MAX_DISPATCH_ATTEMPTS"],
        5),
    DispatchRetryBaseDelaySeconds = ParseInteger(
        builder.Configuration["SIRKADIYEN_SYNC:DISPATCH_RETRY_BASE_DELAY_SECONDS"],
        30),
};
incrementalSyncOptions.Validate();

// Re-authorization catch-up replays already-dispatched semantic diffs per connection, preserving
// the deletion authority and yielding at complete-diff cursor boundaries (ADR-060).
CalendarReconciliationOptions reconciliationOptions = new()
{
    ConnectionBatchSize = ParseInteger(
        builder.Configuration["SIRKADIYEN_SYNC:RECONCILIATION_CONNECTION_BATCH_SIZE"],
        5),
    DiffsPerConnectionPerCycle = ParseInteger(
        builder.Configuration["SIRKADIYEN_SYNC:RECONCILIATION_DIFFS_PER_CONNECTION"],
        10),
};
reconciliationOptions.Validate();

CalendarInventoryReconciliationOptions inventoryOptions = new()
{
    ConnectionBatchSize = ParseInteger(
        builder.Configuration["SIRKADIYEN_SYNC:INVENTORY_CONNECTION_BATCH_SIZE"],
        5),
    Interval = ParseDuration(
        builder.Configuration["SIRKADIYEN_SYNC:INVENTORY_INTERVAL"],
        TimeSpan.FromHours(24)),
};
inventoryOptions.Validate();

// The worker decrypts the refresh token the API encrypted, so it shares the same Data
// Protection key ring (ADR-058).
string? dataProtectionKeyRingPath =
    builder.Configuration["SIRKADIYEN_DATAPROTECTION:KEY_RING_PATH"];
AdaptivePollingOptions pollingOptions = new()
{
    TimeZoneId = builder.Configuration["SIRKADIYEN_POLLING:TIME_ZONE_ID"]
        ?? "Europe/Istanbul",
    DaytimeStart = ParseTime(
        builder.Configuration["SIRKADIYEN_POLLING:DAYTIME_START"],
        new TimeOnly(7, 0)),
    LateAfternoonStart = ParseTime(
        builder.Configuration["SIRKADIYEN_POLLING:LATE_AFTERNOON_START"],
        new TimeOnly(16, 0)),
    NightStart = ParseTime(
        builder.Configuration["SIRKADIYEN_POLLING:NIGHT_START"],
        new TimeOnly(21, 0)),
    DaytimeInterval = ParseDuration(
        builder.Configuration["SIRKADIYEN_POLLING:DAYTIME_INTERVAL"],
        TimeSpan.FromMinutes(15)),
    LateAfternoonInterval = ParseDuration(
        builder.Configuration["SIRKADIYEN_POLLING:LATE_AFTERNOON_INTERVAL"],
        TimeSpan.FromMinutes(25)),
    NightInterval = ParseDuration(
        builder.Configuration["SIRKADIYEN_POLLING:NIGHT_INTERVAL"],
        TimeSpan.FromMinutes(45)),
    WeekendInterval = ParseDuration(
        builder.Configuration["SIRKADIYEN_POLLING:WEEKEND_INTERVAL"],
        TimeSpan.FromHours(1)),
};

RevisionValidationOptions validationOptions = new()
{
    MaximumDeletionShare = ParseDouble(
        builder.Configuration["SIRKADIYEN_VALIDATION:MAXIMUM_DELETION_SHARE"],
        0.20),
    MinimumDeletionCount = ParseInteger(
        builder.Configuration["SIRKADIYEN_VALIDATION:MINIMUM_DELETION_COUNT"],
        10),
    LowConfidenceThreshold = (decimal)ParseDouble(
        builder.Configuration["SIRKADIYEN_VALIDATION:LOW_CONFIDENCE_THRESHOLD"],
        0.50),
    MinimumLessonMinutes = ParseInteger(
        builder.Configuration["SIRKADIYEN_VALIDATION:MINIMUM_LESSON_MINUTES"],
        10),
    MaximumLessonMinutes = ParseInteger(
        builder.Configuration["SIRKADIYEN_VALIDATION:MAXIMUM_LESSON_MINUTES"],
        600),
    MaximumToleratedOverlaps = ParseInteger(
        builder.Configuration["SIRKADIYEN_VALIDATION:MAXIMUM_TOLERATED_OVERLAPS"],
        1),
    AcademicYearGraceDays = ParseInteger(
        builder.Configuration["SIRKADIYEN_VALIDATION:ACADEMIC_YEAR_GRACE_DAYS"],
        30),
};
validationOptions.Validate();

// Secondary-matching thresholds stay on their defaults: they were calibrated in
// ADR-035 against real sources, and loosening them from configuration would let
// an operator turn two different lessons into one update without a decision
// record. The dispatch safety gate is configurable because it is an operational
// limit rather than a matching rule.
SemanticDiffOptions diffOptions = new();
diffOptions.Validate();

ScheduleDiffSafetyThresholds diffThresholds = new()
{
    MaximumDeletionShare = ParseDouble(
        builder.Configuration["SIRKADIYEN_DIFF:MAXIMUM_DELETION_SHARE"],
        0.20),
    MinimumDeletionCount = ParseInteger(
        builder.Configuration["SIRKADIYEN_DIFF:MINIMUM_DELETION_COUNT"],
        10),
};
diffThresholds.Validate();

ParseRunOptions parseRunOptions = new()
{
    StaleRunTimeout = ParseDuration(
        builder.Configuration["SIRKADIYEN_PARSER:STALE_RUN_TIMEOUT"],
        TimeSpan.FromMinutes(30)),
};
parseRunOptions.Validate();

SnapshotRetentionOptions retentionOptions = new()
{
    RecentWindow = TimeSpan.FromDays(ParseDouble(
        builder.Configuration["SIRKADIYEN_RETENTION:SNAPSHOT_RECENT_DAYS"],
        10)),
    BatchSize = ParseInteger(
        builder.Configuration["SIRKADIYEN_RETENTION:SNAPSHOT_BATCH_SIZE"],
        50),
};
retentionOptions.Validate();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(diffOptions);
builder.Services.AddSingleton(diffThresholds);
builder.Services.AddSingleton<SemanticScheduleDiffer>();
builder.Services.AddScoped<ScheduleDiffService>();
builder.Services.AddSingleton(pollingOptions);
builder.Services.AddSingleton(validationOptions);
builder.Services.AddSingleton(retentionOptions);
builder.Services.AddSingleton(parseRunOptions);
builder.Services.AddSingleton<ScheduleRevisionValidator>();
builder.Services.AddScoped<SnapshotRetentionService>();
builder.Services.AddScoped<ScheduleRevisionValidationService>();
builder.Services.AddScoped<ScheduleRevisionPublicationService>();
builder.Services.AddSingleton<AdaptivePollingIntervalPolicy>();
builder.Services.AddSingleton(new WorkerOptions { SourceCatalogPath = catalogPath });
builder.Services.AddSingleton<ScheduleSourceCatalogLoader>();
builder.Services.AddSingleton(googleOptions);
builder.Services.AddSingleton(calendarOptions);
builder.Services.AddSingleton(initialSyncOptions);
builder.Services.AddSingleton(incrementalSyncOptions);
builder.Services.AddSingleton(reconciliationOptions);
builder.Services.AddSingleton(inventoryOptions);
builder.Services.AddSirkadiyenDataProtection(dataProtectionKeyRingPath);
builder.Services.AddSingleton<ICalendarTokenProtector, DataProtectionCalendarTokenProtector>();
builder.Services.AddSingleton<IUserCalendarClient, GoogleCalendarClient>();
builder.Services.AddScoped<InitialCalendarSyncService>();
builder.Services.AddScoped<IncrementalCalendarSyncService>();
builder.Services.AddScoped<CalendarReconciliationService>();
builder.Services.AddScoped<CalendarInventoryReconciliationService>();
builder.Services.AddSingleton<GoogleSheetsServiceFactory>();
builder.Services.AddSingleton<SheetsService>(services =>
    services.GetRequiredService<GoogleSheetsServiceFactory>().Create(googleOptions));
builder.Services.AddSingleton<GoogleSheetsSnapshotMapper>();
builder.Services.AddScoped<ISpreadsheetSnapshotAcquirer, GoogleSheetsSnapshotAcquirer>();
builder.Services.AddScoped<ScheduleSourcePoller>();
builder.Services.AddSirkadiyenPersistence(connectionString);
builder.Services.AddSirkadiyenParserClient(
    parserBaseUrl,
    ParseDuration(
        builder.Configuration["SIRKADIYEN_PARSER:TIMEOUT"],
        TimeSpan.FromMinutes(2)));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();

static string Required(IConfiguration configuration, string key) =>
    configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException(
            $"Required configuration '{key}' is missing. Set it in the repository's '.env' "
            + $"file as '{key.Replace(":", "__", StringComparison.Ordinal)}' or export it as "
            + "an environment variable.");

static TimeOnly ParseTime(string? value, TimeOnly fallback) =>
    string.IsNullOrWhiteSpace(value)
        ? fallback
        : TimeOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

static double ParseDouble(string? value, double fallback) =>
    string.IsNullOrWhiteSpace(value)
        ? fallback
        : double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

static int ParseInteger(string? value, int fallback) =>
    string.IsNullOrWhiteSpace(value)
        ? fallback
        : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

static TimeSpan ParseDuration(string? value, TimeSpan fallback) =>
    string.IsNullOrWhiteSpace(value)
        ? fallback
        : TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
