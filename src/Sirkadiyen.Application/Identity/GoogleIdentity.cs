using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Application.Identity;

/// <summary>Identity fields verified from a Google ID token by infrastructure.</summary>
public sealed record GoogleIdentity
{
    public required string Subject { get; init; }

    public required string Email { get; init; }

    public required bool EmailVerified { get; init; }

    public string? DisplayName { get; init; }
}

public sealed record UserSession
{
    public required Guid UserId { get; init; }

    public required string Email { get; init; }

    public string? DisplayName { get; init; }

    public required UserRole Role { get; init; }

    public required DateTimeOffset LastSignedInAtUtc { get; init; }
}

public interface IGoogleIdentityVerifier
{
    Task<GoogleIdentity> VerifyAsync(
        string credential,
        CancellationToken cancellationToken);
}

public interface IUserStore
{
    Task<UserSession> SignInWithGoogleAsync(
        GoogleIdentity identity,
        UserRole bootstrapRole,
        DateTimeOffset signedInAtUtc,
        CancellationToken cancellationToken);

    Task<UserSession?> FindSessionAsync(
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed class InvalidGoogleCredentialException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class GoogleIdentityConflictException(string message) : Exception(message);
