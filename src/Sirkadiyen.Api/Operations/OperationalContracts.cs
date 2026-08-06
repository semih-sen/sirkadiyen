using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Api.Operations;

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
