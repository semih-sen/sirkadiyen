using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Application.Identity;

/// <summary>
/// The single mutating operation on a user's authorization role (ADR-119): an audited
/// administrative change, distinct from the bootstrap grant that happens at sign-in.
/// </summary>
public interface IUserRoleStore
{
    /// <summary>
    /// Sets one user's role transactionally, returning what it was and whether it changed. A missing
    /// user is reported rather than thrown.
    /// </summary>
    Task<ChangeUserRoleResult> ChangeRoleAsync(
        Guid userId,
        UserRole newRole,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}

/// <summary>The outcome of a role change, with the roles for the audit record and the response.</summary>
public sealed record ChangeUserRoleResult
{
    public required ChangeUserRoleOutcome Outcome { get; init; }

    /// <summary>The role before the change; meaningful for <see cref="ChangeUserRoleOutcome.Changed"/>
    /// and <see cref="ChangeUserRoleOutcome.Unchanged"/>.</summary>
    public UserRole PreviousRole { get; init; }

    public UserRole NewRole { get; init; }
}

public enum ChangeUserRoleOutcome
{
    /// <summary>The role was set to a new value.</summary>
    Changed,

    /// <summary>The user already had that role; nothing was written.</summary>
    Unchanged,

    /// <summary>No user with the id exists.</summary>
    UserNotFound,
}
