using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Worker.Calendars;
using Sirkadiyen.Worker.Composition;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Resolves the worker's stages from its real service collection.
/// </summary>
/// <remarks>
/// A missing registration is invisible to every other test in this project, because each of those
/// constructs its subject by hand. It shows up as a crash on the first cycle of a deployed worker
/// instead. Building the provider does not open a database connection, so this stays a unit test.
/// </remarks>
public sealed class WorkerCompositionTests
{
    [Fact]
    public void EveryFencedCalendarStageResolves()
    {
        using ServiceProvider provider = BuildProvider();

        // The composite stage pulls in every task it runs, so resolving it proves the whole chain.
        Assert.NotNull(provider.GetRequiredService<FencedCalendarMaintenanceTask>());
    }

    [Fact]
    public void TheAcademicYearReconcilerAndEverythingItNeedsResolve()
    {
        // ADR-117 added three registrations to the worker that only this stage uses: the profile
        // schema, the drift bound, and the audit recorder — which in turn needs an IP protector
        // the worker had never registered, because nothing in it wrote an audit entry before.
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(provider.GetRequiredService<ProfileAcademicYearDriftTask>());
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<ProfileAcademicYearRolloverService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AuditEventRecorder>());
    }

    [Fact]
    public void TheWorkerStampsProfilesWithTheSameSchemaTheApiDoes()
    {
        // Both hosts construct the schema from the same code, and the reconciler restamping a
        // profile with a different one from the API's would be the very split ADR-115 exists to
        // repair — one cohort across two years, half of them invisible to dispatch.
        using ServiceProvider provider = BuildProvider();

        SupportedProfileSchema schema = provider.GetRequiredService<SupportedProfileSchema>();

        Assert.Equal(CurrentSupportedProfileSchema.SchemaVersion, schema.SchemaVersion);
        Assert.Equal(
            CurrentSupportedProfileSchema.Create().Programs.Count,
            schema.Programs.Count);
    }

    private static ServiceProvider BuildProvider()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                ContentRootPath = AppContext.BaseDirectory,
                DisableDefaults = true,
            });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SIRKADIYEN_DATABASE:CONNECTION_STRING"] = "Host=localhost;Database=test",
            ["SIRKADIYEN_PARSER:BASE_URL"] = "http://127.0.0.1:8000",
            ["SIRKADIYEN_GOOGLE:CALENDAR_CLIENT_ID"] = "client-id",
            ["SIRKADIYEN_GOOGLE:CALENDAR_CLIENT_SECRET"] = "client-secret",
        });

        builder.Services.AddWorkerApplication(builder.Configuration, builder.Environment);
        return builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });
    }
}
