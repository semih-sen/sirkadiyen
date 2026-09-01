namespace Sirkadiyen.Application.Notifications;

/// <summary>
/// One thing the operator is told about, outside the journal (ADR-144).
/// </summary>
/// <remarks>
/// An alert is a message, not a record. Everything it names — a revision, a diff, a source, a
/// failure — is already persisted by the stage that produced it, and the panel is where it is
/// looked at. This carries only enough to decide whether to go and look: what happened, how bad
/// it is, and the identifiers to search for.
/// <para>
/// Nothing personal belongs in one. Alerts leave the system to a third-party messaging service,
/// so they name sources, revisions, diffs and counts; never a student, an address, a token, or
/// the contents of a snapshot.
/// </para>
/// </remarks>
public sealed record OperatorAlert
{
    /// <summary>The headline, stated as what happened rather than as a category.</summary>
    public required string Title { get; init; }

    public required OperatorAlertSeverity Severity { get; init; }

    /// <summary>
    /// What repeated sending of this alert is suppressed by.
    /// </summary>
    /// <remarks>
    /// A key naming a one-off event — a revision id, a diff id — is unique by construction and is
    /// never suppressed. A key naming a standing condition — "the pipeline is stalled", "this
    /// source cannot be read" — repeats every cycle the condition survives, and is exactly what
    /// the cooldown exists for. Choosing the key is therefore choosing whether the alert repeats.
    /// </remarks>
    public required string DedupeKey { get; init; }

    /// <summary>A sentence of context, when the title does not carry it.</summary>
    public string? Detail { get; init; }

    /// <summary>Labelled identifiers and counts, rendered in order.</summary>
    public IReadOnlyList<OperatorAlertField> Fields { get; init; } = [];
}

/// <summary>One labelled value inside an alert.</summary>
public sealed record OperatorAlertField(string Label, string Value);

public enum OperatorAlertSeverity
{
    /// <summary>Something normal happened that the operator asked to be told about.</summary>
    Info,

    /// <summary>Nothing failed, but work is waiting for a person and is reaching no calendar.</summary>
    Warning,

    /// <summary>A stage failed.</summary>
    Error,
}
