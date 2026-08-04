using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
        });
        WorkerOptionsFactory factory = new(builder.Configuration, builder.Environment);

        Assert.Equal(TimeSpan.FromSeconds(7),
            factory.CreateWorkerOptions().CalendarCatchUpInterval);
        Assert.Equal(8, factory.CreateInitialSyncOptions().ConnectionBatchSize);
        Assert.Equal(new TimeOnly(6, 30), factory.CreatePollingOptions().DaytimeStart);
        Assert.Equal(0.25, factory.CreateValidationOptions().MaximumDeletionShare);
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
