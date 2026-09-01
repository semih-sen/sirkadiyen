using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Sirkadiyen.Application.Notifications;
using Sirkadiyen.Infrastructure.Notifications;
using Sirkadiyen.Worker.Configuration;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class WorkerOptionsFactoryTests
{
    [Fact]
    public void CreateOptions_UsesExistingDefaults()
    {
        HostApplicationBuilder builder = CreateBuilder();
        WorkerOptionsFactory factory = new(builder.Configuration, builder.Environment);

        WorkerOptions worker = factory.CreateWorkerOptions();
        var initialSync = factory.CreateInitialSyncOptions();
        var polling = factory.CreatePollingOptions();

        Assert.Equal(TimeSpan.FromSeconds(5), worker.CalendarCatchUpInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), worker.CalendarIdleCheckInterval);
        Assert.Equal(Path.Combine(builder.Environment.ContentRootPath,
            "config", "schedule-sources.json"), worker.SourceCatalogPath);
        Assert.Equal(5, initialSync.ConnectionBatchSize);
        Assert.Equal(100, initialSync.EventsPerConnectionPerCycle);
        Assert.Equal("Sirkadiyen", initialSync.CalendarSummary);
        Assert.Equal("Europe/Istanbul", polling.TimeZoneId);
        Assert.Equal(TimeSpan.FromMinutes(15), polling.DaytimeInterval);

        var profileResync = factory.CreateProfileResyncOptions();
        Assert.Equal(5, profileResync.ConnectionBatchSize);
        Assert.Equal(100, profileResync.CalendarOperationsPerConnectionPerCycle);

        // The reconciler's bound is separate from the resync batch it feeds: this caps how fast
        // convergence work is created, that one caps how fast calendars are written (ADR-117).
        Assert.Equal(
            25,
            factory.CreateProfileAcademicYearDriftOptions().ProfilesPerProgramPerCycle);
    }

    [Fact]
    public void CreateOptions_ParsesConfiguredValuesWithInvariantCulture()
    {
        HostApplicationBuilder builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["SIRKADIYEN_SYNC:CALENDAR_CATCH_UP_INTERVAL"] = "00:00:07",
            ["SIRKADIYEN_SYNC:CONNECTION_BATCH_SIZE"] = "8",
            ["SIRKADIYEN_POLLING:DAYTIME_START"] = "06:30",
            ["SIRKADIYEN_VALIDATION:MAXIMUM_DELETION_SHARE"] = "0.25",
            ["SIRKADIYEN_SYNC:PROFILE_RESYNC_OPERATIONS_PER_CONNECTION"] = "40",
            ["SIRKADIYEN_SYNC:ACADEMIC_YEAR_DRIFT_PROFILES_PER_PROGRAM"] = "8",
        });
        WorkerOptionsFactory factory = new(builder.Configuration, builder.Environment);

        Assert.Equal(TimeSpan.FromSeconds(7),
            factory.CreateWorkerOptions().CalendarCatchUpInterval);
        Assert.Equal(8, factory.CreateInitialSyncOptions().ConnectionBatchSize);
        Assert.Equal(new TimeOnly(6, 30), factory.CreatePollingOptions().DaytimeStart);
        Assert.Equal(0.25, factory.CreateValidationOptions().MaximumDeletionShare);
        Assert.Equal(
            40,
            factory.CreateProfileResyncOptions().CalendarOperationsPerConnectionPerCycle);
        Assert.Equal(
            8,
            factory.CreateProfileAcademicYearDriftOptions().ProfilesPerProgramPerCycle);
    }

    [Fact]
    public void AlertingIsOffUntilBothABotTokenAndAChatAreConfigured()
    {
        // A deployment that predates ADR-144 configures neither and must keep starting. A
        // messaging credential is never allowed to be a startup requirement of the pipeline.
        HostApplicationBuilder builder = CreateBuilder();

        TelegramAlertOptions options = new WorkerOptionsFactory(
            builder.Configuration,
            builder.Environment).CreateTelegramAlertOptions();

        Assert.False(options.IsConfigured);
        Assert.Empty(options.ChatIds);
        Assert.Equal(OperatorAlertSeverity.Info, options.MinimumSeverity);
        Assert.Equal(TimeSpan.FromHours(6), options.RepeatCooldown);
    }

    [Fact]
    public void ConfiguredAlertingIsReadIntoTheChannelOptions()
    {
        HostApplicationBuilder builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["SIRKADIYEN_TELEGRAM:BOT_TOKEN"] = "1234567:token",
            ["SIRKADIYEN_TELEGRAM:CHAT_IDS"] = "5027475773, 1176903009",
            ["SIRKADIYEN_TELEGRAM:MINIMUM_SEVERITY"] = "warning",
            ["SIRKADIYEN_TELEGRAM:REPEAT_COOLDOWN"] = "02:00:00",
        });

        TelegramAlertOptions options = new WorkerOptionsFactory(
            builder.Configuration,
            builder.Environment).CreateTelegramAlertOptions();

        Assert.True(options.IsConfigured);
        Assert.Equal([5027475773L, 1176903009L], options.ChatIds);

        // Case-insensitively, because nobody types an enum name from memory.
        Assert.Equal(OperatorAlertSeverity.Warning, options.MinimumSeverity);
        Assert.Equal(TimeSpan.FromHours(2), options.RepeatCooldown);
    }

    [Fact]
    public void AMisspeltSeverityIsRefusedRatherThanSilentlyTreatedAsInfo()
    {
        // Falling back would mean somebody who asked for problems only keeps receiving every
        // routine revision and has no way to tell that their setting was ignored.
        HostApplicationBuilder builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["SIRKADIYEN_TELEGRAM:MINIMUM_SEVERITY"] = "critical",
        });
        WorkerOptionsFactory factory = new(builder.Configuration, builder.Environment);

        Assert.Throws<InvalidOperationException>(() => factory.CreateTelegramAlertOptions());
    }

    private static HostApplicationBuilder CreateBuilder(
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        string contentRoot = AppContext.BaseDirectory;
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                ContentRootPath = contentRoot,
                DisableDefaults = true,
            });
        Dictionary<string, string?> values = new()
        {
            ["SIRKADIYEN_DATABASE:CONNECTION_STRING"] = "Host=localhost;Database=test",
            ["SIRKADIYEN_PARSER:BASE_URL"] = "http://127.0.0.1:8000",
            ["SIRKADIYEN_GOOGLE:CALENDAR_CLIENT_ID"] = "client-id",
            ["SIRKADIYEN_GOOGLE:CALENDAR_CLIENT_SECRET"] = "client-secret",
        };
        if (overrides is not null)
        {
            foreach ((string key, string? value) in overrides)
            {
                values[key] = value;
            }
        }

        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }
}
