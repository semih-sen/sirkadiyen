using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Sirkadiyen.Api.Administration;
using Sirkadiyen.Api.GoogleCalendar;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Api.Licensing;
using Sirkadiyen.Api.Onboarding;
using Sirkadiyen.Api.StudentProfiles;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Configuration;
using Sirkadiyen.Infrastructure.Google;
using Sirkadiyen.Infrastructure.Licensing;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.ScheduleIngestion;
using Sirkadiyen.Infrastructure.Security;

// Before the builder, because the environment-variable provider reads the
// process environment as it is added. A deployed host injects its own variables
// and ships no file, so this does nothing there (ADR-041).
DotEnvFile.Load();

var builder = WebApplication.CreateBuilder(args);

string connectionString = Required(
    builder.Configuration,
    "SIRKADIYEN_DATABASE:CONNECTION_STRING");

string googleAuthClientId = Required(
    builder.Configuration,
    "SIRKADIYEN_GOOGLE:AUTH_CLIENT_ID");

// The Calendar grant is a confidential-client exchange, so unlike sign-in it needs a
// secret. It may reuse the browser client, but it is configured separately so the
// secret is never implied by the public sign-in client (ADR-057).
string calendarClientId = Required(
    builder.Configuration,
    "SIRKADIYEN_GOOGLE:CALENDAR_CLIENT_ID");

string calendarClientSecret = Required(
    builder.Configuration,
    "SIRKADIYEN_GOOGLE:CALENDAR_CLIENT_SECRET");

string calendarRedirectUri =
    builder.Configuration["SIRKADIYEN_GOOGLE:CALENDAR_REDIRECT_URI"] is { Length: > 0 } configured
        ? configured
        : GoogleCalendarAuthorizationOptions.PostMessageRedirectUri;

// The worker decrypts the refresh token this host encrypts, so both must share a Data
// Protection key ring (ADR-058). Optional: a default shared path is used when unset.
string? dataProtectionKeyRingPath =
    builder.Configuration["SIRKADIYEN_DATAPROTECTION:KEY_RING_PATH"];

string licenseHashKey = Required(
    builder.Configuration,
    "SIRKADIYEN_LICENSING:HASH_KEY");

builder.Services.AddProblemDetails();

// TLS is terminated at the edge (the Next dev server locally, a reverse proxy in
// production) and the request reaches Kestrel over HTTP, so Request.IsHttps is
// false unless we honour X-Forwarded-Proto. Without this the Secure cookies and
// the antiforgery SSL guard (SecurePolicy.Always) reject every request (ADR-066).
// The forwarded headers are trusted only from a known proxy: in Development the
// sole caller is the local same-host proxy, whose loopback peer can present as
// ::ffff:127.0.0.1 and not match the default known-network entries, so we trust
// the immediate peer directly. A production deployment MUST instead pin its
// reverse proxy through KnownProxies/KnownNetworks (ADR-052).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    if (builder.Environment.IsDevelopment())
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

// States and rules are read by a person deciding whether to approve a revision.
// "ReviewRequired" tells them something; "2" makes them go and count the enum.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new GoogleSignInOptions { ClientId = googleAuthClientId });
builder.Services.AddSingleton(LicenseCodeOptions.FromBase64(licenseHashKey));
builder.Services.AddSingleton<ILicenseCodeService, LicenseCodeService>();
builder.Services.AddScoped<IGoogleIdentityVerifier, GoogleIdentityVerifier>();
builder.Services.AddScoped<GoogleSignInService>();
builder.Services.AddScoped<LicenseService>();
builder.Services.AddSingleton(CurrentSupportedProfileSchema.Create());
builder.Services.AddScoped<StudentProfileService>();
builder.Services.AddSingleton(new GoogleCalendarAuthorizationOptions
{
    ClientId = calendarClientId,
    ClientSecret = calendarClientSecret,
    RedirectUri = calendarRedirectUri,
});
builder.Services.AddSingleton<
    IGoogleCalendarAuthorizationClient,
    GoogleCalendarAuthorizationClient>();
builder.Services.AddSirkadiyenDataProtection(dataProtectionKeyRingPath);
builder.Services.AddSingleton<ICalendarTokenProtector, DataProtectionCalendarTokenProtector>();
builder.Services.AddScoped<CalendarAuthorizationService>();
builder.Services.AddScoped<OnboardingStateService>();
builder.Services.AddScoped<SirkadiyenCookieAuthenticationEvents>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(AuthenticationConfiguration.ConfigureCookie);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(
        AuthorizationPolicies.SuperAdmin,
        policy => policy.RequireRole(UserRole.SuperAdmin.ToString()));
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = AuthenticationConfiguration.AntiforgeryHeaderName;
    options.Cookie.Name = AuthenticationConfiguration.AntiforgeryCookieName;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
    options.Cookie.IsEssential = true;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        RateLimitingPolicies.GoogleSignIn,
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            static _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.AddPolicy(
        RateLimitingPolicies.LicenseRedemption,
        context => RateLimitPartition.GetFixedWindowLimiter(
            $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous"}:"
                + $"{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            static _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});
builder.Services.AddScoped<ScheduleRevisionPublicationService>();
builder.Services.AddScoped<ScheduleDiffReviewService>();

// Administrative acquisition. The API stores the uploaded evidence; the worker
// still owns parsing, validation and publication (ADR-080).
builder.Services.AddSingleton<DocxSnapshotConverter>();
builder.Services.AddScoped<IUploadedDocumentConverter, UploadedDocumentConverter>();
builder.Services.AddScoped<AdministrativeDocumentUploadService>();
builder.Services.AddSirkadiyenPersistence(connectionString);

var app = builder.Build();

// Must run before anything that reads the scheme (auth, antiforgery, cookies).
app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseAntiforgery();
app.MapHealthChecks("/health");
app.MapOpenApi();
app.MapAuthenticationEndpoints();
app.MapLicenseEndpoints();
app.MapStudentProfileEndpoints();
app.MapCalendarAuthorizationEndpoints();
app.MapCalendarSyncEndpoints();
app.MapOnboardingEndpoints();
app.MapRevisionEndpoints();
app.MapSourceDocumentEndpoints();
app.MapDiffEndpoints();
app.MapOperationalEndpoints();

app.Run();

static string Required(IConfiguration configuration, string key) =>
    configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException(
            $"Required configuration '{key}' is missing. Set it in the repository's '.env' "
            + $"file as '{key.Replace(":", "__", StringComparison.Ordinal)}' or export it as "
            + "an environment variable.");

public partial class Program;
