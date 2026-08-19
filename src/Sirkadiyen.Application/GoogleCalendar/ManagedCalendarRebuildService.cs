using Sirkadiyen.Application.Operations;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Rebuilds the dedicated calendar a student deleted, by returning their connection to the state
/// initial synchronization starts from (ADR-116).
/// </summary>
/// <remarks>
/// ADR-062 recorded that "automatic recreation of a deleted whole calendar is not part of this
/// decision" and left the repair to a later, explicit flow. That flow was never written, and its
/// absence was not a missing convenience but a dead end: a deleted calendar marks the connection
/// unavailable, which drops the student out of every writer and makes onboarding report
/// <c>ActionRequired</c>, which routes them to the consent screen — and re-consenting does not
/// clear the flag, so it routes them there again, forever.
/// <para>
/// One service for both callers on purpose. A student repairs their own account from the screen
/// they are stuck on, and an operator repairs it for them when they do not; the eligibility rule,
/// the freeze, the ledger discard and the audit record must not be able to differ between those
/// two doors.
/// </para>
/// <para>
/// It writes no calendar. It clears state so that the existing initial-sync path — which already
/// knows how to find a marker-matched orphan calendar before creating a new one, and writes with
/// deterministic event ids — can run again when the user starts it.
/// </para>
/// </remarks>
public sealed class ManagedCalendarRebuildService(
    IUserCalendarConnectionStore connectionStore,
    IOperationalFreezeStore freezeStore,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Whether this connection can be rebuilt, and how much the rebuild would rewrite. Changes
    /// nothing, so the caller can show a student or an operator what they are about to start.
    /// </summary>
    public async Task<ManagedCalendarRebuildAssessment> AssessAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnectionView? connection =
            await connectionStore.GetByUserIdAsync(userId, cancellationToken);

        if (connection is null)
        {
            return new ManagedCalendarRebuildAssessment
            {
                Outcome = ManagedCalendarRebuildOutcome.NoConnection,
            };
        }

        return new ManagedCalendarRebuildAssessment
        {
            Outcome = connection.ManagedCalendarUnavailableAtUtc is null
                ? ManagedCalendarRebuildOutcome.NotEligible
                : ManagedCalendarRebuildOutcome.Reset,
            UnavailableSinceUtc = connection.ManagedCalendarUnavailableAtUtc,
        };
    }

    /// <summary>
    /// Performs the rebuild reset, recording it first.
    /// </summary>
    /// <param name="recordAuthorization">
    /// Writes the audit entry. It is called immediately before the state is changed, and a throw
    /// from it abandons the rebuild: this discards a student's whole event ledger, and "why did my
    /// calendar start over" has to be answerable from the trail alone (AI_GUIDELINE §19). It is
    /// required for the student's own request as much as an operator's — the account is theirs,
    /// but the record is what lets anyone reconstruct the sequence afterwards.
    /// </param>
    public async Task<ManagedCalendarRebuildResult> RequestAsync(
        Guid userId,
        Func<ManagedCalendarRebuildAssessment, CancellationToken, Task> recordAuthorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordAuthorization);

        ManagedCalendarRebuildAssessment assessment =
            await AssessAsync(userId, cancellationToken);
        if (assessment.Outcome is not ManagedCalendarRebuildOutcome.Reset)
        {
            return new ManagedCalendarRebuildResult { Outcome = assessment.Outcome };
        }

        // A rebuild queues no calendar write by itself — the user still has to start the
        // synchronization, and that path is freeze-gated too. It is refused during a freeze
        // anyway, because it discards durable ledger state, and a freeze exists precisely to stop
        // the pipeline mutating anything until someone has looked (ADR-034/043).
        OperationalFreezeSnapshot freeze = await freezeStore.GetAsync(cancellationToken);
        if (freeze.IsFrozen)
        {
            return new ManagedCalendarRebuildResult
            {
                Outcome = ManagedCalendarRebuildOutcome.Frozen,
            };
        }

        await recordAuthorization(assessment, cancellationToken);

        return await connectionStore.RebuildManagedCalendarAsync(
            userId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}

/// <summary>What a rebuild would do, computed without changing anything (ADR-116).</summary>
public sealed record ManagedCalendarRebuildAssessment
{
    public required ManagedCalendarRebuildOutcome Outcome { get; init; }

    /// <summary>
    /// When the calendar was first proven unreachable. Shown rather than inferred, because "since
    /// when" is the first thing both a student and an operator ask.
    /// </summary>
    public DateTimeOffset? UnavailableSinceUtc { get; init; }
}
