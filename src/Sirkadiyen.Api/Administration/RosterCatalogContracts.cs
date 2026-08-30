namespace Sirkadiyen.Api.Administration;

/// <summary>A proposed roster catalog document, and the on-disk document it was edited from.</summary>
public sealed record PreviewRosterCatalogRequest
{
    public required string Content { get; init; }

    /// <summary>
    /// The hash of the document the editor was opened on. It is required so an operator editing a
    /// stale copy is told at preview time rather than at confirmation.
    /// </summary>
    public required string BaseContentHash { get; init; }
}

/// <summary>The confirmation of a previewed roster catalog change.</summary>
public sealed record ApplyRosterCatalogRequest
{
    public required string Content { get; init; }

    public required string BaseContentHash { get; init; }

    /// <summary>The hash of the plan that was shown; binds this confirmation to that plan.</summary>
    public required string PlanHash { get; init; }

    /// <summary>Why the catalog is being changed. Recorded with the revision and the audit event.</summary>
    public required string Reason { get; init; }
}
