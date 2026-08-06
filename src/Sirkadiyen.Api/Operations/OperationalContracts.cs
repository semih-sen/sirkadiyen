using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Api.Administration;

public sealed record SetOperationalFreezeRequest
{
    public required bool IsFrozen { get; init; }

    public required string Reason { get; init; }
}

public sealed record SetScopedOperationalFreezeRequest
{
    public required int ClassYear { get; init; }
    public required ProgramLanguage ProgramLanguage { get; init; }
    public required bool IsFrozen { get; init; }
    public required string Reason { get; init; }
}
