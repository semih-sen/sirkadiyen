using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Infrastructure.Google;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.ScheduleIngestion;
using Sirkadiyen.Infrastructure.ScheduleParsing;
using Sirkadiyen.Infrastructure.ScheduleSources;
using Sirkadiyen.Worker;

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

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(diffOptions);
builder.Services.AddSingleton(diffThresholds);
builder.Services.AddSingleton<SemanticScheduleDiffer>();
builder.Services.AddScoped<ScheduleDiffService>();
builder.Services.AddSingleton(pollingOptions);
builder.Services.AddSingleton(validationOptions);
builder.Services.AddSingleton<ScheduleRevisionValidator>();
builder.Services.AddScoped<ScheduleRevisionValidationService>();
builder.Services.AddScoped<ScheduleRevisionPublicationService>();
builder.Services.AddSingleton<AdaptivePollingIntervalPolicy>();
builder.Services.AddSingleton(new WorkerOptions { SourceCatalogPath = catalogPath });
builder.Services.AddSingleton<ScheduleSourceCatalogLoader>();
builder.Services.AddSingleton(googleOptions);
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
        : throw new InvalidOperationException($"Required configuration '{key}' is missing.");

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
