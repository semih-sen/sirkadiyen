using Sirkadiyen.Application.Scheduling.Publication;

namespace Sirkadiyen.Api.Scheduling.Publication;

/// <summary>
/// Why the authenticated SuperAdmin is rejecting a quarantined revision.
/// </summary>
/// <remarks>
/// The actor is derived from the backend-authenticated Google identity and is
/// never accepted from this payload.
/// </remarks>
public sealed record RejectRevisionRequest
{
    /// <example>Checked the source: the workbook was mid-edit and half the rooms are blank.</example>
    public required string? RejectionReason { get; init; }
}

public sealed record RejectRevisionResponse
{
    public required Guid RevisionId { get; init; }

    public required bool Rejected { get; init; }
}

/// <summary>
/// Why the authenticated SuperAdmin is approving a quarantined revision.
/// </summary>
/// <remarks>
/// The actor is derived from the backend-authenticated Google identity and is
/// never accepted from this payload.
/// </remarks>
public sealed record ApproveRevisionRequest
{
    /// <example>Checked the source: the 40% drop is the exam period, not a parse fault.</example>
    public required string? ApprovalReason { get; init; }
}

public sealed record ApproveRevisionResponse
{
    public required Guid RevisionId { get; init; }

    public required bool Approved { get; init; }

    public required RevisionPublicationOutcome PublicationOutcome { get; init; }

    public Guid? SupersededRevisionId { get; init; }
}
