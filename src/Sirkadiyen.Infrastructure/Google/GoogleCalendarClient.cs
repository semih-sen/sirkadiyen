using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Sirkadiyen.Application.GoogleCalendar;
using GoogleCalendarData = Google.Apis.Calendar.v3.Data.Calendar;

namespace Sirkadiyen.Infrastructure.Google;

/// <summary>
/// Writes a user's dedicated Sirkadiyen calendar through the Google Calendar API (ADR-024,
/// ADR-058, ADR-059), authenticating each call with the user's stored refresh token.
/// </summary>
/// <remarks>
/// This class talks to live Google and so cannot be exercised without real credentials; the
/// synchronization use cases are tested against a fake of <see cref="IUserCalendarClient"/>.
/// The authorization flow is built once and a <see cref="CalendarService"/> is memoized per
/// refresh token so a user's many event writes reuse one HTTP client. Every call goes through
/// <see cref="ExecuteAsync{T}"/>, which retries genuinely transient failures with a short back-off
/// and classifies the rest into the synchronization exception taxonomy (ADR-059).
/// </remarks>
public sealed class GoogleCalendarClient : IUserCalendarClient, IDisposable
{
    private const string ApplicationName = "Sirkadiyen";

    /// <summary>The Google library keys its (absent) token store by user; a constant stands in.</summary>
    private const string CredentialUserKey = "sirkadiyen-calendar-user";

    /// <summary>How many times one call retries a transient failure before giving up for this cycle.</summary>
    private const int MaxTransientAttempts = 3;

    private static readonly TimeSpan TransientRetryBaseDelay = TimeSpan.FromSeconds(1);

    private readonly GoogleAuthorizationCodeFlow flow;
    private readonly ConcurrentDictionary<string, CalendarService> services =
        new(StringComparer.Ordinal);

    public GoogleCalendarClient(GoogleCalendarAuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret,
            },
            Scopes = [GoogleCalendarAuthorizationOptions.CalendarScope],
        });
    }

    public async Task<string> CreateManagedCalendarAsync(
        CalendarAccess access,
        string calendarSummary,
        string timeZoneId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        CalendarService service = ServiceFor(access);
        GoogleCalendarData calendar = new() { Summary = calendarSummary, TimeZone = timeZoneId };

        return await ExecuteAsync(
            async () =>
            {
                GoogleCalendarData created = await service.Calendars.Insert(calendar)
                    .ExecuteAsync(cancellationToken);
                return created.Id;
            },
            "Creating the dedicated Google calendar",
            cancellationToken);
    }

    public async Task<CalendarEventInsertOutcome> InsertEventAsync(
        CalendarAccess access,
        string calendarId,
        ManagedCalendarEvent calendarEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(calendarEvent);

        CalendarService service = ServiceFor(access);

        return await ExecuteAsync(
            async () =>
            {
                try
                {
                    await service.Events.Insert(ToGoogleEvent(calendarEvent), calendarId)
                        .ExecuteAsync(cancellationToken);
                    return CalendarEventInsertOutcome.Inserted;
                }
                catch (GoogleApiException exception)
                    when (exception.HttpStatusCode == HttpStatusCode.Conflict)
                {
                    // The client-chosen id already exists, so a previous run wrote this event.
                    // Reporting it as already-present is the idempotency sync depends on (ADR-058).
                    return CalendarEventInsertOutcome.AlreadyExists;
                }
            },
            "Inserting a calendar event",
            cancellationToken);
    }

    public async Task<CalendarEventPatchOutcome> PatchEventAsync(
        CalendarAccess access,
        string calendarId,
        ManagedCalendarEvent calendarEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(calendarEvent);

        CalendarService service = ServiceFor(access);

        return await ExecuteAsync(
            async () =>
            {
                try
                {
                    await service.Events
                        .Patch(ToGoogleEvent(calendarEvent), calendarId, calendarEvent.EventId)
                        .ExecuteAsync(cancellationToken);
                    return CalendarEventPatchOutcome.Patched;
                }
                catch (GoogleApiException exception)
                    when (IsGone(exception.HttpStatusCode))
                {
                    // Nothing to update: the event was already removed. A no-op lets the caller
                    // decide whether to re-create it (ADR-059).
                    return CalendarEventPatchOutcome.NotFound;
                }
            },
            "Patching a calendar event",
            cancellationToken);
    }

    public async Task<CalendarEventDeleteOutcome> DeleteEventAsync(
        CalendarAccess access,
        string calendarId,
        string eventId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);

        CalendarService service = ServiceFor(access);

        return await ExecuteAsync(
            async () =>
            {
                try
                {
                    await service.Events.Delete(calendarId, eventId).ExecuteAsync(cancellationToken);
                    return CalendarEventDeleteOutcome.Deleted;
                }
                catch (GoogleApiException exception)
                    when (IsGone(exception.HttpStatusCode))
                {
                    // Already gone; a resumed dispatch converges rather than failing (ADR-059).
                    return CalendarEventDeleteOutcome.NotFound;
                }
            },
            "Deleting a calendar event",
            cancellationToken);
    }

    public void Dispose()
    {
        foreach (CalendarService service in services.Values)
        {
            service.Dispose();
        }

        flow.Dispose();
    }

    private async Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        string operation,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                return await action();
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                if (attempt >= MaxTransientAttempts)
                {
                    throw new GoogleCalendarTransientException(
                        $"{operation} failed after {attempt} transient attempts.",
                        exception);
                }

                await Task.Delay(BackoffFor(attempt), cancellationToken);
            }
            catch (Exception exception) when (IsCredentialRejected(exception))
            {
                throw new GoogleCalendarCredentialException(
                    $"{operation} was rejected: the credential needs re-authorization.",
                    exception);
            }
            catch (Exception exception)
                when (exception is GoogleApiException or HttpRequestException or TokenResponseException)
            {
                throw new GoogleCalendarSyncException($"{operation} failed.", exception);
            }
        }
    }

    private static TimeSpan BackoffFor(int attempt) =>
        TransientRetryBaseDelay * Math.Pow(2, attempt - 1);

    private static bool IsTransient(Exception exception) => exception switch
    {
        HttpRequestException => true,
        GoogleApiException api =>
            api.HttpStatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
            || IsRateLimit(api),
        _ => false,
    };

    private static bool IsCredentialRejected(Exception exception) => exception switch
    {
        // A dead or revoked refresh token surfaces from the token endpoint as invalid_grant.
        TokenResponseException token =>
            token.Error?.Error is "invalid_grant" or "unauthorized_client" or "invalid_client",
        // 401 is always an auth failure; 403 is auth unless it is a rate/quota rejection.
        GoogleApiException api =>
            api.HttpStatusCode == HttpStatusCode.Unauthorized
            || (api.HttpStatusCode == HttpStatusCode.Forbidden && !IsRateLimit(api)),
        _ => false,
    };

    private static bool IsRateLimit(GoogleApiException exception) =>
        exception.HttpStatusCode == HttpStatusCode.TooManyRequests
        || exception.Error?.Errors?.Any(single =>
            single.Reason is "rateLimitExceeded" or "userRateLimitExceeded" or "quotaExceeded")
            == true;

    private static bool IsGone(HttpStatusCode? status) =>
        status is HttpStatusCode.NotFound or HttpStatusCode.Gone;

    private CalendarService ServiceFor(CalendarAccess access)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(access.RefreshToken);

        return services.GetOrAdd(access.RefreshToken, refreshToken =>
        {
            UserCredential credential = new(
                flow,
                CredentialUserKey,
                new TokenResponse { RefreshToken = refreshToken });
            return new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
        });
    }

    private static Event ToGoogleEvent(ManagedCalendarEvent calendarEvent)
    {
        Event googleEvent = new()
        {
            Id = calendarEvent.EventId,
            Summary = calendarEvent.Summary,
            Description = calendarEvent.Description,
            Location = calendarEvent.Location,
            ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Private__ = new Dictionary<string, string>(calendarEvent.PrivateProperties),
            },
        };

        if (calendarEvent.IsAllDay)
        {
            googleEvent.Start = new EventDateTime { Date = FormatDate(calendarEvent.StartDate!.Value) };
            googleEvent.End = new EventDateTime { Date = FormatDate(calendarEvent.EndDateExclusive!.Value) };
        }
        else
        {
            // The local wall-clock time is sent with its IANA zone, so Google resolves the
            // offset (and any daylight-saving shift) rather than Sirkadiyen computing it.
            googleEvent.Start = new EventDateTime
            {
                DateTimeRaw = FormatLocal(calendarEvent.LocalStart!.Value),
                TimeZone = calendarEvent.TimeZoneId,
            };
            googleEvent.End = new EventDateTime
            {
                DateTimeRaw = FormatLocal(calendarEvent.LocalEnd!.Value),
                TimeZone = calendarEvent.TimeZoneId,
            };
        }

        return googleEvent;
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatLocal(DateTime local) =>
        local.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
