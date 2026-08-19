using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Worker.Calendars;

/// <summary>
/// Moves stored student profiles onto the academic year the deployed schema states for their
/// program, and queues the convergence that writes that year's lessons (ADR-117).
/// </summary>
/// <remarks>
/// It runs immediately before the profile-resync stage so the requests it creates are picked up in
/// the same cycle rather than a cycle later, and inside the shared Calendar fence so two workers
/// cannot restamp the same batch at once.
/// <para>
/// In steady state it is a bounded query per program that returns nothing and logs nothing. It
/// only has anything to do in the window between deploying a schema that names a new year and
/// every profile in that program having caught up.
/// </para>
/// </remarks>
internal sealed class ProfileAcademicYearDriftTask(
    IServiceScopeFactory scopeFactory,
    ILogger<ProfileAcademicYearDriftTask> logger)
{
    private static readonly JsonSerializerOptions AuditMetadataOptions =
        ContractJson.CreateOptions();

    /// <returns>Whether work remains, so the scheduler shortens the next cycle.</returns>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ProfileAcademicYearRolloverService rollovers = scope.ServiceProvider
                .GetRequiredService<ProfileAcademicYearRolloverService>();
            AuditEventRecorder audit = scope.ServiceProvider
                .GetRequiredService<AuditEventRecorder>();

            ProfileDriftReconcileRunResult result = await rollovers.ReconcileDriftAsync(
                (reconciliation, token) => audit.RecordAsync(
                    new AuditEventDraft
                    {
                        Category = AuditEventCategory.ProfileAcademicYearRolled,
                        // No actor: nobody asked for this pass. The reason states what did.
                        SubjectType = "ProfileAcademicYearRollover",
                        SubjectId = reconciliation.ToString(),
                        Reason = "Automatic reconciliation: the deployed profile schema states "
                            + $"{reconciliation.ToAcademicYear} for this program.",
                        Metadata = JsonSerializer.Serialize(
                            new
                            {
                                requestedBy = "worker",
                                toAcademicYear = reconciliation.ToAcademicYear,
                                toSchemaVersion = reconciliation.ToSchemaVersion,
                                drifted = reconciliation.DriftedProfiles,
                                moving = reconciliation.ProfilesMoved,
                                blocked = reconciliation.BlockedByInvalidSelectors.Count,
                            },
                            AuditMetadataOptions),
                    },
                    token),
                cancellationToken);

            if (result.Frozen)
            {
                logger.LogInformation(
                    "Academic-year reconciliation skipped because the global operational freeze "
                    + "is active.");
                return false;
            }

            foreach (ProfileDriftReconciliation program in result.Programs)
            {
                LogResult(program);
            }

            // A batch that filled its bound almost certainly has more behind it. The other
            // outcomes are all waiting on something a shorter cycle would not change.
            return result.Programs.Any(static program =>
                program.Outcome is ProfileDriftOutcome.Moved && program.ProfilesMoved > 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Reconciling stored profile academic years failed.");
            return false;
        }
    }

    private void LogResult(ProfileDriftReconciliation program)
    {
        switch (program.Outcome)
        {
            case ProfileDriftOutcome.Moved:
                logger.LogInformation(
                    "Moved {Moved} of {Drifted} drifted profiles in {Program} onto "
                    + "{AcademicYear}; {Requested} calendars queued for convergence, "
                    + "{Blocked} left for a person.",
                    program.ProfilesMoved, program.DriftedProfiles, program.ToString(),
                    program.ToAcademicYear, program.ConvergenceRequested,
                    program.BlockedByInvalidSelectors.Count);
                break;

            case ProfileDriftOutcome.NothingPublishedYet:
                logger.LogWarning(
                    "{Drifted} profiles in {Program} still state an earlier academic year, but "
                    + "nothing is published for {AcademicYear} yet. Moving them now would "
                    + "guarantee an empty calendar, so they wait for the first revision.",
                    program.DriftedProfiles, program.ToString(), program.ToAcademicYear);
                break;

            case ProfileDriftOutcome.Frozen:
                logger.LogInformation(
                    "{Drifted} profiles in {Program} are due to move onto {AcademicYear}, but "
                    + "that pipeline is frozen.",
                    program.DriftedProfiles, program.ToString(), program.ToAcademicYear);
                break;

            case ProfileDriftOutcome.AllBlocked:
                // Not transient and not self-correcting: another cycle will find the same rows.
                logger.LogError(
                    "All {Drifted} drifted profiles in {Program} have selectors the "
                    + "{AcademicYear} program refuses, so none could be moved. They need a "
                    + "person: re-onboarding, or a schema that still declares their dimension.",
                    program.DriftedProfiles, program.ToString(), program.ToAcademicYear);
                break;

            case ProfileDriftOutcome.NoDrift:
            default:
                break;
        }
    }
}
