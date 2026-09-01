namespace Sirkadiyen.Application.Scheduling.Publication;

/// <summary>
/// Runs revision validation for one revision, or for every revision still
/// waiting.
/// </summary>
/// <remarks>
/// The poller validates the revision it has just created, but validation must
/// not depend on the poller having survived: <see cref="ValidatePendingAsync"/>
/// picks up anything a crashed cycle left behind.
/// <para>
/// Like the rest of the application layer this service reports outcomes through
/// its return values rather than logging; the host decides what to record.
/// </para>
/// </remarks>
public sealed class ScheduleRevisionValidationService(
    IScheduleRevisionValidationStore store,
    ScheduleRevisionValidator validator,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Validates one revision.
    /// </summary>
    /// <returns>
    /// The outcome, or <see langword="null"/> when the revision was not in a
    /// state that may be validated. A null result is normal: another cycle may
    /// have validated it first.
    /// </returns>
    public async Task<RevisionValidationResult?> ValidateAsync(
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        RevisionValidationInput? input = await store.LoadAsync(revisionId, cancellationToken);
        if (input is null)
        {
            return null;
        }

        DateTimeOffset atUtc = timeProvider.GetUtcNow();
        RevisionValidationResult result = validator.Validate(input, atUtc);
        await store.ApplyAsync(revisionId, result, atUtc, cancellationToken);
        return result;
    }

    /// <summary>
    /// Validates revisions a previous cycle left unvalidated, oldest first.
    /// </summary>
    /// <remarks>
    /// One revision that cannot be validated is reported rather than thrown, so
    /// it cannot stop the rest of the backlog. Without that, a single revision
    /// whose validation throws sits at the head of an oldest-first queue and
    /// every revision behind it stays in <see cref="RevisionState.Parsed"/>
    /// forever — the queue is re-read from the same end on every cycle, so the
    /// failure is not merely repeated, it is permanent. That is the difference
    /// between a recovery pass and a recovery pass that works, and the diff stage
    /// already draws it (ADR-097's queues are independent for the same reason).
    /// </remarks>
    public async Task<IReadOnlyList<RevisionValidationOutcome>> ValidatePendingAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IReadOnlyList<Guid> pending = await store.ListPendingValidationAsync(
            limit,
            cancellationToken);

        List<RevisionValidationOutcome> outcomes = [];
        foreach (Guid revisionId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await ValidateAsync(revisionId, cancellationToken) is { } result)
                {
                    outcomes.Add(new RevisionValidationOutcome
                    {
                        RevisionId = revisionId,
                        Result = result,
                    });
                }
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested)
            {
                // Reported, not swallowed: the revision stays in Parsed and will
                // be tried again, but the ones behind it are validated now. The
                // host decides how loudly to say so.
                outcomes.Add(new RevisionValidationOutcome
                {
                    RevisionId = revisionId,
                    Failure = exception,
                });
            }
        }

        return outcomes;
    }
}

public sealed record RevisionValidationOutcome
{
    public required Guid RevisionId { get; init; }

    /// <summary>The outcome, when validation ran.</summary>
    public RevisionValidationResult? Result { get; init; }

    /// <summary>Why validation could not run, when it could not.</summary>
    public Exception? Failure { get; init; }
}
