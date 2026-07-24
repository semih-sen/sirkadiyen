namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Produces the exact metadata marker written onto a user's dedicated calendar. The marker
/// lets initial sync recover the one non-idempotent Calendar operation after a process crash.
/// </summary>
public static class ManagedCalendarIdentity
{
    private const string Prefix = "sirkadiyen-managed-calendar:v1:";

    public static string DescriptionMarker(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A calendar owner is required.", nameof(userId));
        }

        return $"{Prefix}{userId:N}";
    }
}
