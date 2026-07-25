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
using Google.Apis.Util;
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
    private readonly ConcurrentDictionary<
        CalendarService,
        ConcurrentDictionary<string, CalendarLabelRegistry>> labelRegistries = [];

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
        string descriptionMarker,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        CalendarService service = ServiceFor(access);
        GoogleCalendarData calendar = new()
        {
            Summary = calendarSummary,
            TimeZone = timeZoneId,
            Description = descriptionMarker,
        };

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

    public async Task<IReadOnlyList<string>> FindManagedCalendarIdsAsync(
        CalendarAccess access,
        string descriptionMarker,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptionMarker);

        CalendarService service = ServiceFor(access);
        return await ExecuteAsync(
            async () =>
            {
                List<string> candidateIds = [];
                string? pageToken = null;
                do
                {
                    CalendarListResource.ListRequest request = service.CalendarList.List();
                    request.MaxResults = 250;
                    request.ShowHidden = true;
                    request.PageToken = pageToken;
                    CalendarList response = await request.ExecuteAsync(cancellationToken);
                    foreach (CalendarListEntry entry in response.Items ?? [])
                    {
                        if (string.Equals(
                                entry.Description,
                                descriptionMarker,
                                StringComparison.Ordinal)
                            && string.Equals(entry.AccessRole, "owner", StringComparison.Ordinal)
                            && await CanReadManagedEventsAsync(
                                service,
                                entry.Id,
                                cancellationToken))
                        {
                            candidateIds.Add(entry.Id);
                        }
                    }

                    pageToken = response.NextPageToken;
                }
                while (!string.IsNullOrWhiteSpace(pageToken));

                return (IReadOnlyList<string>)[.. candidateIds.Order(StringComparer.Ordinal)];
            },
            "Finding an orphaned managed calendar",
            cancellationToken);
    }

    public async Task<IReadOnlyList<ManagedCalendarEventSnapshot>> ListManagedEventsAsync(
        CalendarAccess access,
        string calendarId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);

        CalendarService service = ServiceFor(access);
        return await ExecuteAsync(
            async () =>
            {
                List<ManagedCalendarEventSnapshot> snapshots = [];
                string? pageToken = null;
                do
                {
                    EventsResource.ListRequest request = service.Events.List(calendarId);
                    request.PrivateExtendedProperty =
                        new Repeatable<string>([$"{ManagedCalendarEventFactory.ManagedMarkerKey}=1"]);
                    request.ShowDeleted = false;
                    request.MaxResults = 2500;
                    request.PageToken = pageToken;

                    Events response = await request.ExecuteAsync(cancellationToken);
                    snapshots.AddRange((response.Items ?? []).Select(ToSnapshot));
                    pageToken = response.NextPageToken;
                }
                while (!string.IsNullOrWhiteSpace(pageToken));

                return (IReadOnlyList<ManagedCalendarEventSnapshot>)snapshots;
            },
            "Listing managed calendar events",
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
        await EnsureEventLabelAsync(service, calendarId, calendarEvent.Label, cancellationToken);

        return await ExecuteAsync(
            async () =>
            {
                try
                {
                    EventsResource.InsertRequest request =
                        service.Events.Insert(ToGoogleEvent(calendarEvent), calendarId);
                    request.EventLabelVersion = 1;
                    await request.ExecuteAsync(cancellationToken);
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
        await EnsureEventLabelAsync(service, calendarId, calendarEvent.Label, cancellationToken);

        return await ExecuteAsync(
            async () =>
            {
                try
                {
                    EventsResource.PatchRequest request = service.Events.Patch(
                        ToGoogleEvent(calendarEvent),
                        calendarId,
                        calendarEvent.EventId);
                    request.EventLabelVersion = 1;
                    await request.ExecuteAsync(cancellationToken);
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
        foreach (ConcurrentDictionary<string, CalendarLabelRegistry> calendars
            in labelRegistries.Values)
        {
            foreach (CalendarLabelRegistry registry in calendars.Values)
            {
                registry.Gate.Dispose();
            }
        }

        foreach (CalendarService service in services.Values)
        {
            service.Dispose();
        }

        flow.Dispose();
    }

    private async Task EnsureEventLabelAsync(
        CalendarService service,
        string calendarId,
        ManagedCalendarEventLabel desired,
        CancellationToken cancellationToken)
    {
        ConcurrentDictionary<string, CalendarLabelRegistry> calendars =
            labelRegistries.GetOrAdd(
                service,
                static _ => new ConcurrentDictionary<string, CalendarLabelRegistry>(
                    StringComparer.Ordinal));
        CalendarLabelRegistry registry = calendars.GetOrAdd(
            calendarId,
            static _ => new CalendarLabelRegistry());

        await registry.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!registry.Initialized)
            {
                GoogleCalendarData calendar = await ExecuteAsync(
                    () => service.Calendars.Get(calendarId).ExecuteAsync(cancellationToken),
                    "Reading managed-calendar event labels",
                    cancellationToken);
                registry.Labels = (calendar.LabelProperties?.EventLabels ?? [])
                    .Where(label => !string.IsNullOrWhiteSpace(label.Id))
                    .ToDictionary(label => label.Id, StringComparer.Ordinal);
                registry.Initialized = true;
            }

            if (registry.Labels.TryGetValue(desired.Id, out EventLabel? existing)
                && string.Equals(existing.Name, desired.Name, StringComparison.Ordinal)
                && string.Equals(
                    existing.BackgroundColor,
                    desired.BackgroundColor,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Dictionary<string, EventLabel> merged =
                new(registry.Labels, StringComparer.Ordinal)
                {
                    [desired.Id] = new EventLabel
                    {
                        Id = desired.Id,
                        Name = desired.Name,
                        BackgroundColor = desired.BackgroundColor,
                    },
                };

            GoogleCalendarData patch = new()
            {
                LabelProperties = new LabelProperties
                {
                    EventLabels =
                    [
                        .. merged.Values.OrderBy(label => label.Id, StringComparer.Ordinal),
                    ],
                },
            };
            await ExecuteAsync(
                () => service.Calendars.Patch(patch, calendarId).ExecuteAsync(cancellationToken),
                "Defining a managed-calendar event label",
                cancellationToken);
            registry.Labels = merged;
        }
        finally
        {
            registry.Gate.Release();
        }
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
            catch (GoogleApiException exception)
                when (IsGone(exception.HttpStatusCode))
            {
                throw new GoogleManagedCalendarUnavailableException(
                    $"{operation} failed because the managed calendar is unavailable.",
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

    private static async Task<bool> CanReadManagedEventsAsync(
        CalendarService service,
        string calendarId,
        CancellationToken cancellationToken)
    {
        try
        {
            EventsResource.ListRequest request = service.Events.List(calendarId);
            request.PrivateExtendedProperty =
                new Repeatable<string>([$"{ManagedCalendarEventFactory.ManagedMarkerKey}=1"]);
            request.MaxResults = 1;
            await request.ExecuteAsync(cancellationToken);
            return true;
        }
        catch (GoogleApiException exception)
            when (exception.HttpStatusCode is HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound
                or HttpStatusCode.Gone)
        {
            // CalendarList can see calendars this app did not create. The app-created scope
            // cannot read their events, which makes this an authorization-independent proof
            // that the description marker alone is not enough to attach them.
            return false;
        }
    }

    private static ManagedCalendarEventSnapshot ToSnapshot(Event googleEvent)
    {
        IReadOnlyDictionary<string, string> privateProperties =
            googleEvent.ExtendedProperties?.Private__ is { } properties
                ? new Dictionary<string, string>(properties, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);

        bool isAllDay = !string.IsNullOrWhiteSpace(googleEvent.Start?.Date);
        return new ManagedCalendarEventSnapshot
        {
            EventId = googleEvent.Id,
            Summary = googleEvent.Summary,
            Description = googleEvent.Description,
            Location = googleEvent.Location,
            EventLabelId = googleEvent.EventLabelId,
            IsAllDay = isAllDay,
            StartDate = isAllDay ? ParseDate(googleEvent.Start?.Date) : null,
            EndDateExclusive = isAllDay ? ParseDate(googleEvent.End?.Date) : null,
            StartAt = isAllDay ? null : googleEvent.Start?.DateTimeDateTimeOffset,
            EndAt = isAllDay ? null : googleEvent.End?.DateTimeDateTimeOffset,
            PrivateProperties = privateProperties,
        };
    }

    private static Event ToGoogleEvent(ManagedCalendarEvent calendarEvent)
    {
        Event googleEvent = new()
        {
            Id = calendarEvent.EventId,
            Summary = calendarEvent.Summary,
            Description = calendarEvent.Description,
            Location = calendarEvent.Location,
            EventLabelId = calendarEvent.Label.Id,
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

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly parsed)
            ? parsed
            : null;

    private sealed class CalendarLabelRegistry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public bool Initialized { get; set; }

        public Dictionary<string, EventLabel> Labels { get; set; } =
            new(StringComparer.Ordinal);
    }
}
