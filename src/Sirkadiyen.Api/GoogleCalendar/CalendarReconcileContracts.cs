namespace Sirkadiyen.Api.GoogleCalendar;

public sealed record RequestReconciliationResponse
{
    public required bool Requested { get; init; }
}
