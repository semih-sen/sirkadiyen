using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sirkadiyen.Application.ScheduleIngestion;
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

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(pollingOptions);
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

static TimeSpan ParseDuration(string? value, TimeSpan fallback) =>
    string.IsNullOrWhiteSpace(value)
        ? fallback
        : TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
