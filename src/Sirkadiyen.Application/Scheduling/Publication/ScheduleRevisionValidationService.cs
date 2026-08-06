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
            if (await ValidateAsync(revisionId, cancellationToken) is { } result)
            {
                outcomes.Add(new RevisionValidationOutcome
                {
                    RevisionId = revisionId,
                    Result = result,
                });
            }
        }

        return outcomes;
    }
}

public sealed record RevisionValidationOutcome
{
    public required Guid RevisionId { get; init; }

    public required RevisionValidationResult Result { get; init; }
}
