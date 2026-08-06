using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Application.Identity;

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
