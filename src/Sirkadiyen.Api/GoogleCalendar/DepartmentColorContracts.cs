namespace Sirkadiyen.Api.GoogleCalendar;

public sealed record SetDepartmentColorRequest
{
    public required string Color { get; init; }
}

public sealed record SetAdminDepartmentColorRequest
{
    public required string Color { get; init; }
    public required string Reason { get; init; }
}

public sealed record ResetAdminDepartmentColorRequest
{
    public required string Reason { get; init; }
}

public sealed record DepartmentColorMutationResponse
{
    public required bool Changed { get; init; }
    public required bool CalendarRefreshQueued { get; init; }
}
