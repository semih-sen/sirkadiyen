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
/// ADR-058), authenticating each call with the user's stored refresh token.
/// </summary>
/// <remarks>
/// This class talks to live Google and so cannot be exercised without real credentials; the
/// synchronization use cases are tested against a fake of <see cref="IUserCalendarClient"/>.
/// The authorization flow is built once and a <see cref="CalendarService"/> is memoized per
/// refresh token so a user's many event inserts reuse one HTTP client.
/// </remarks>
public sealed class GoogleCalendarClient : IUserCalendarClient, IDisposable
{
    private const string ApplicationName = "Sirkadiyen";

    /// <summary>The Google library keys its (absent) token store by user; a constant stands in.</summary>
    private const string CredentialUserKey = "sirkadiyen-calendar-user";

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

        try
        {
            GoogleCalendarData created = await service.Calendars.Insert(calendar)
                .ExecuteAsync(cancellationToken);
            return created.Id;
        }
        catch (Exception exception)
            when (exception is GoogleApiException or HttpRequestException or TokenResponseException)
        {
            throw new GoogleCalendarSyncException(
                "Creating the dedicated Google calendar failed.",
                exception);
        }
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

        try
        {
            await service.Events.Insert(ToGoogleEvent(calendarEvent), calendarId)
                .ExecuteAsync(cancellationToken);
            return CalendarEventInsertOutcome.Inserted;
        }
        catch (GoogleApiException exception)
            when (exception.HttpStatusCode == HttpStatusCode.Conflict)
        {
            // The client-chosen id already exists, so a previous run wrote this event. Reporting
            // it as already-present is the idempotency the initial sync depends on (ADR-058).
            return CalendarEventInsertOutcome.AlreadyExists;
        }
        catch (Exception exception)
            when (exception is GoogleApiException or HttpRequestException or TokenResponseException)
        {
            throw new GoogleCalendarSyncException("Inserting a calendar event failed.", exception);
        }
    }

    public void Dispose()
    {
        foreach (CalendarService service in services.Values)
        {
            service.Dispose();
        }

        flow.Dispose();
    }

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
