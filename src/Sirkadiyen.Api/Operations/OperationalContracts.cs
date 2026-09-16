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

/// <summary>
/// Asks what moving a program's stored profiles onto the year its sources now state would do,
/// changing nothing (ADR-115).
/// </summary>
/// <remarks>
/// Only the year being moved <em>from</em> is named. The target is the deployed schema's, so an
/// operator cannot stamp a year that new sign-ups will not get and split the cohort in two.
/// </remarks>
public sealed record PreviewProfileRolloverRequest
{
    public required string FromAcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }
}

/// <summary>
/// Authorizes the rollover the operator was shown. <see cref="PlanHash"/> binds the confirmation
/// to that plan, and <see cref="Reason"/> is recorded with it because this rewrites stored student
/// profiles and queues calendar writes no published revision asked for.
/// </summary>
public sealed record RequestProfileRolloverRequest
{
    public required string FromAcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required string PlanHash { get; init; }

    public required string Reason { get; init; }
}

/// <summary>
/// Asks which of a cohort's stored profiles disagree with the published faculty lists, changing
/// nothing (ADR-159).
/// </summary>
/// <remarks>
/// Only the program is named. The academic year is the deployed schema's for the cohort, never the
/// caller's, so the check runs against the year new sign-ups are stamped with.
/// </remarks>
public sealed record PreviewRosterProfileAuditRequest
{
    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }
}

/// <summary>
/// Authorizes the corrections the operator was shown. <see cref="PlanHash"/> binds the confirmation
/// to that plan, and <see cref="Reason"/> is recorded with it because this rewrites stored student
/// profiles and queues calendar writes no published revision asked for.
/// </summary>
public sealed record RequestRosterProfileAuditRequest
{
    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required string PlanHash { get; init; }

    public required string Reason { get; init; }
}
