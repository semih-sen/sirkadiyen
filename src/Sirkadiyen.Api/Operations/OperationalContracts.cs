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

/// <summary>Asks what a cohort calendar repair would converge, changing nothing (ADR-111).</summary>
public sealed record PreviewCalendarRepairRequest
{
    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }
}

/// <summary>
/// Authorizes the repair the operator was shown. <see cref="PlanHash"/> binds the confirmation to
/// that plan, and <see cref="Reason"/> is recorded with it because this queues calendar deletions
/// no published revision asked for.
/// </summary>
public sealed record RequestCalendarRepairRequest
{
    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required string PlanHash { get; init; }

    public required string Reason { get; init; }
}
