using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Application.Identity;

/// <summary>
/// Changes another account's authorization role on a SuperAdmin's request (ADR-119): promoting a
/// student to operator, or removing an operator's rights.
/// </summary>
/// <remarks>
/// The guards live here, in one place, because they are what keep role management from being a way
/// to lock the system out of itself:
/// <list type="bullet">
/// <item>An operator cannot change their <em>own</em> role — no self-demotion that strands the panel
/// mid-action, and no self-promotion path that makes the check meaningless.</item>
/// <item>The bootstrap operator cannot be demoted. Their role is re-granted from a backend-owned
/// e-mail on every sign-in (ADR-045), so a demotion would silently reverse itself and the audit
/// record would claim a change that did not last.</item>
/// </list>
/// A change is recorded before it is applied, via the same callback shape the other operator flows
/// use, so "who made me an admin / took it away, and why" is answerable from the trail alone
/// (AI_GUIDELINE §19).
/// </remarks>
public sealed class UserRoleService(
    IUserStore userStore,
    IUserRoleStore roleStore,
    TimeProvider timeProvider)
{
    public async Task<ChangeUserRoleServiceResult> ChangeRoleAsync(
        ChangeUserRoleCommand command,
        Func<RoleChangeRecord, CancellationToken, Task> recordAuthorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(recordAuthorization);

        if (command.TargetUserId == command.ActorUserId)
        {
            return Result(ChangeUserRoleServiceOutcome.CannotChangeOwnRole);
        }

        UserSession? target =
            await userStore.FindSessionAsync(command.TargetUserId, cancellationToken);
        if (target is null)
        {
            return Result(ChangeUserRoleServiceOutcome.UserNotFound);
        }

        if (command.NewRole is UserRole.User && IsBootstrapOperator(target.Email))
        {
            return Result(ChangeUserRoleServiceOutcome.CannotDemoteBootstrapOperator);
        }

        if (target.Role == command.NewRole)
        {
            return new ChangeUserRoleServiceResult
            {
                Outcome = ChangeUserRoleServiceOutcome.Unchanged,
                PreviousRole = target.Role,
                NewRole = command.NewRole,
            };
        }

        // Record the authorization before applying it, so a throw from the audit write abandons the
        // change rather than leaving a role altered with no record of who did it or why.
        await recordAuthorization(
            new RoleChangeRecord
            {
                TargetUserId = command.TargetUserId,
                PreviousRole = target.Role,
                NewRole = command.NewRole,
            },
            cancellationToken);

        ChangeUserRoleResult stored = await roleStore.ChangeRoleAsync(
            command.TargetUserId,
            command.NewRole,
            timeProvider.GetUtcNow(),
            cancellationToken);

        // A concurrent deletion between the lookup and the write is the only way to reach NotFound
        // here; report it faithfully rather than claiming a change.
        ChangeUserRoleServiceOutcome outcome = stored.Outcome switch
        {
            ChangeUserRoleOutcome.Changed => ChangeUserRoleServiceOutcome.Changed,
            ChangeUserRoleOutcome.Unchanged => ChangeUserRoleServiceOutcome.Unchanged,
            _ => ChangeUserRoleServiceOutcome.UserNotFound,
        };

        return new ChangeUserRoleServiceResult
        {
            Outcome = outcome,
            PreviousRole = stored.PreviousRole,
            NewRole = command.NewRole,
        };
    }

    private static bool IsBootstrapOperator(string email) => string.Equals(
        User.NormalizeEmailValue(email),
        User.NormalizeEmailValue(GoogleSignInService.SuperAdminEmail),
        StringComparison.Ordinal);

    private static ChangeUserRoleServiceResult Result(ChangeUserRoleServiceOutcome outcome) =>
        new() { Outcome = outcome };
}

/// <summary>An operator's request to change one account's role (ADR-119).</summary>
public sealed record ChangeUserRoleCommand
{
    public required Guid TargetUserId { get; init; }

    public required Guid ActorUserId { get; init; }

    public required UserRole NewRole { get; init; }
}

/// <summary>What the caller records in the audit trail, just before the change is applied.</summary>
public sealed record RoleChangeRecord
{
    public required Guid TargetUserId { get; init; }

    public required UserRole PreviousRole { get; init; }

    public required UserRole NewRole { get; init; }
}

public sealed record ChangeUserRoleServiceResult
{
    public required ChangeUserRoleServiceOutcome Outcome { get; init; }

    public UserRole PreviousRole { get; init; }

    public UserRole NewRole { get; init; }
}

public enum ChangeUserRoleServiceOutcome
{
    /// <summary>The role was changed.</summary>
    Changed,

    /// <summary>The account already had that role; nothing was written.</summary>
    Unchanged,

    /// <summary>No account with the id exists.</summary>
    UserNotFound,

    /// <summary>An operator may not change their own role.</summary>
    CannotChangeOwnRole,

    /// <summary>The bootstrap operator's role cannot be removed (it is re-granted at sign-in).</summary>
    CannotDemoteBootstrapOperator,
}
