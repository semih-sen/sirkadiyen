using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Sirkadiyen.Api.Administration;
using Sirkadiyen.Api.Announcements;
using Sirkadiyen.Api.Auditing;
using Sirkadiyen.Api.Finance;
using Sirkadiyen.Api.GoogleCalendar;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Api.Licensing;
using Sirkadiyen.Api.Onboarding;
using Sirkadiyen.Api.Operations;
using Sirkadiyen.Api.Scheduling.Access;
using Sirkadiyen.Api.Scheduling.Diffing;
using Sirkadiyen.Api.Scheduling.Ingestion;
using Sirkadiyen.Api.Scheduling.Publication;
using Sirkadiyen.Api.StudentProfiles;
using Sirkadiyen.Api.StudentRosters;

namespace Sirkadiyen.Api.Composition;

/// <summary>
/// Maps the API's health probes, OpenAPI document, and every feature endpoint group.
/// </summary>
internal static class ApiEndpointRouteBuilderExtensions
{
    public static WebApplication MapSirkadiyenApiEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions { Predicate = static registration => registration.Tags.Contains("ready") });
        app.MapOpenApi();
        app.MapAuthenticationEndpoints();
        app.MapAccountEndpoints();
        app.MapLicenseEndpoints();
        app.MapStudentProfileEndpoints();
        app.MapStudentRosterEndpoints();
        app.MapCalendarAuthorizationEndpoints();
        app.MapCalendarSyncEndpoints();
        app.MapCalendarReconcileEndpoints();
        app.MapCalendarRebuildEndpoints();
        app.MapScheduleEndpoints();
        app.MapDepartmentColorEndpoints();
        app.MapOnboardingEndpoints();
        app.MapRevisionEndpoints();
        app.MapSourceDocumentEndpoints();
        app.MapDiffEndpoints();
        app.MapOperationalEndpoints();
        app.MapAdminUserEndpoints();
        app.MapAnnouncementEndpoints();
        app.MapSourceStatusEndpoints();
        app.MapSourceCatalogEndpoints()
            .MapRosterCatalogEndpoints();
        app.MapAuditEndpoints();
        app.MapFinanceEndpoints();
        app.MapFinanceObligationEndpoints();
        app.MapFinanceDistributionEndpoints();
        app.MapMetricsEndpoints();
        app.MapServiceHealthEndpoints();

        return app;
    }
}
