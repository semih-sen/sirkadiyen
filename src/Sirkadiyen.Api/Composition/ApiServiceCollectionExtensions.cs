using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Sirkadiyen.Api.Health;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Api.Observability;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Announcements;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Application.Scheduling.Diffing;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Google;
using Sirkadiyen.Infrastructure.Licensing;
using Sirkadiyen.Infrastructure.Observability;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Scheduling.Ingestion;
using Sirkadiyen.Infrastructure.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Security;

namespace Sirkadiyen.Api.Composition;

/// <summary>
/// Composition root for the API: reads the required configuration and registers every service,
/// option, and cross-cutting policy the host depends on.
/// </summary>
internal static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddSirkadiyenApi(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        IServiceCollection services = builder.Services;
        IConfiguration configuration = builder.Configuration;
        bool isDevelopment = builder.Environment.IsDevelopment();

        string connectionString = RequiredConfiguration.Get(
            configuration, "SIRKADIYEN_DATABASE:CONNECTION_STRING");
        string googleAuthClientId = RequiredConfiguration.Get(
            configuration, "SIRKADIYEN_GOOGLE:AUTH_CLIENT_ID");

        // The Calendar grant is a confidential-client exchange, so unlike sign-in it needs a
        // secret. It may reuse the browser client, but it is configured separately so the
        // secret is never implied by the public sign-in client (ADR-057).
        string calendarClientId = RequiredConfiguration.Get(
            configuration, "SIRKADIYEN_GOOGLE:CALENDAR_CLIENT_ID");
        string calendarClientSecret = RequiredConfiguration.Get(
            configuration, "SIRKADIYEN_GOOGLE:CALENDAR_CLIENT_SECRET");
        string calendarRedirectUri =
            configuration["SIRKADIYEN_GOOGLE:CALENDAR_REDIRECT_URI"] is { Length: > 0 } configured
                ? configured
                : GoogleCalendarAuthorizationOptions.PostMessageRedirectUri;

        // The worker decrypts the refresh token this host encrypts, so both must share a Data
        // Protection key ring (ADR-058). Optional: a default shared path is used when unset.
        string? dataProtectionKeyRingPath =
            configuration["SIRKADIYEN_DATAPROTECTION:KEY_RING_PATH"];

        string licenseHashKey = RequiredConfiguration.Get(
            configuration, "SIRKADIYEN_LICENSING:HASH_KEY");

        Uri parserBaseUrl = new(RequiredConfiguration.Get(
            configuration, "SIRKADIYEN_PARSER:BASE_URL"), UriKind.Absolute);
        Uri workerBaseUrl = new(
            configuration["SIRKADIYEN_WORKER:BASE_URL"] ?? "http://127.0.0.1:5081",
            UriKind.Absolute);

        services.AddProblemDetails();
        services.Configure<ForwardedHeadersOptions>(
            options => ForwardedHeadersConfiguration.Configure(options, isDevelopment));

        // States and rules are read by a person deciding whether to approve a revision.
        // "ReviewRequired" tells them something; "2" makes them go and count the enum.
        services.ConfigureHttpJsonOptions(
            options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddScoped<DatabaseHealthCheck>();
        services.AddSingleton(new AdminServiceHealthProbeOptions
        {
            WorkerBaseUrl = workerBaseUrl,
            ParserBaseUrl = parserBaseUrl,
        });
        services.AddHttpClient<IAdminServiceHealthProbe, AdminServiceHealthProbe>(client =>
            client.Timeout = TimeSpan.FromSeconds(5));
        services
            .AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
        services.AddOpenApi();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new GoogleSignInOptions { ClientId = googleAuthClientId });
        services.AddSingleton(LicenseCodeOptions.FromBase64(licenseHashKey));
        services.AddSingleton<ILicenseCodeService, LicenseCodeService>();
        services.AddScoped<IGoogleIdentityVerifier, GoogleIdentityVerifier>();
        services.AddScoped<GoogleSignInService>();
        services.AddScoped<LicenseService>();
        services.AddScoped<FinanceLedgerService>();
        services.AddScoped<FinancePeriodResolver>();
        services.AddScoped<FinanceObligationService>();
        services.AddScoped<FinanceSummaryService>();
        services.AddScoped<FinanceDistributionService>();
        services.AddSingleton(CurrentSupportedProfileSchema.Create());
        services.AddScoped<StudentProfileService>();
        services.AddSingleton(new GoogleCalendarAuthorizationOptions
        {
            ClientId = calendarClientId,
            ClientSecret = calendarClientSecret,
            RedirectUri = calendarRedirectUri,
        });
        services.AddSingleton<
            IGoogleCalendarAuthorizationClient,
            GoogleCalendarAuthorizationClient>();
        services.AddSirkadiyenDataProtection(dataProtectionKeyRingPath);
        services.AddSingleton<ICalendarTokenProtector, DataProtectionCalendarTokenProtector>();
        services.AddSingleton<IAuditIpProtector, DataProtectionAuditIpProtector>();
        services.AddScoped<AuditEventRecorder>();
        services.AddScoped<CalendarAuthorizationService>();
        services.AddScoped<OnboardingStateService>();
        services.AddScoped<SirkadiyenCookieAuthenticationEvents>();
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(AuthenticationConfiguration.ConfigureCookie);
        services.AddAuthorizationBuilder()
            .AddPolicy(
                AuthorizationPolicies.SuperAdmin,
                policy => policy.RequireRole(UserRole.SuperAdmin.ToString()));
        services.AddAntiforgery(AntiforgeryConfiguration.Configure);
        services.AddRateLimiter(RateLimiterConfiguration.Configure);
        services.AddScoped<ScheduleRevisionPublicationService>();
        services.AddScoped<ScheduleDiffReviewService>();
        services.AddScoped<DepartmentColorService>();

        // Announcement composition and confirmation are the API's; delivery is the worker's
        // (ADR-107), so no dispatch service is registered here.
        services.AddScoped<AnnouncementService>();
        services.AddScoped<CohortCalendarRepairService>();

        // Operator-triggered snapshot payload pruning from the source dashboard (ADR-120).
        services.AddScoped<SnapshotPayloadPruneService>();

        // On-demand read-only verification of one user's calendar against Google (ADR-121).
        services.AddScoped<CalendarVerificationService>();

        // The one way out of a deleted managed calendar, shared by the student's own
        // endpoint and the operator's (ADR-116).
        services.AddScoped<ManagedCalendarRebuildService>();

        // Account deletion, shared by the student's own "Hesabımı sil" and the operator's delete
        // (ADR-118). It reaches Google to remove the managed calendar and revoke the grant, so the
        // API host needs the Calendar client the worker also uses; it needs no worker config.
        services.AddSingleton<IUserCalendarClient, GoogleCalendarClient>();
        services.AddScoped<IExternalAccountCleanup, ExternalAccountCleanupService>();
        services.AddScoped<AccountDeletionService>();

        // Administrative role change: promote a user to operator, or remove operator rights
        // (ADR-119). SuperAdmin-only and audited, guarded against self-change and demoting bootstrap.
        services.AddScoped<UserRoleService>();

        // The rollover corrects stored profiles; every calendar write it implies is still
        // performed by the worker's convergence pass (ADR-115).
        services.AddSingleton(new ProfileAcademicYearDriftOptions());
        services.AddScoped<ProfileAcademicYearRolloverService>();

        // Administrative acquisition. The API stores the uploaded evidence; the worker
        // still owns parsing, validation and publication (ADR-080).
        services.AddSingleton<DocxSnapshotConverter>();
        services.AddScoped<IUploadedDocumentConverter, UploadedDocumentConverter>();
        services.AddScoped<AdministrativeDocumentUploadService>();

        // The editable schedule source catalog (ADR-114). The path is shared with the worker,
        // which reads the same file at startup, so it must be configured to a location outside
        // either host's release directory - a catalog inside a deployed artifact is replaced by
        // the next deployment and every administrative edit would silently revert.
        services.AddSingleton(new ScheduleSourceCatalogFileOptions
        {
            Path = Path.GetFullPath(
                configuration["SIRKADIYEN_SOURCES:CATALOG_PATH"] ?? "config/schedule-sources.json",
                builder.Environment.ContentRootPath),
        });
        services.AddSingleton<IScheduleSourceCatalogFile, ScheduleSourceCatalogFile>();
        services.AddSingleton<IScheduleSourceCatalogSerializer, ScheduleSourceCatalogLoader>();
        services.AddScoped<ScheduleSourceCatalogEditingService>();
        services.AddSirkadiyenPersistence(connectionString);

        return services;
    }
}
