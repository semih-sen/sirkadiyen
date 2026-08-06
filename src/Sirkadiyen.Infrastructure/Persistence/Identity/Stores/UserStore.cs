using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Infrastructure.Persistence.Identity.Stores;

/// <summary>PostgreSQL-backed Google identity and session user store.</summary>
public sealed class UserStore(SirkadiyenDbContext dbContext) : IUserStore
{
    public async Task<UserSession> SignInWithGoogleAsync(
        GoogleIdentity identity,
        UserRole bootstrapRole,
        DateTimeOffset signedInAtUtc,
        CancellationToken cancellationToken) =>
        await SignInWithGoogleAsync(
            identity,
            bootstrapRole,
            signedInAtUtc,
            retryAfterConcurrentInsert: true,
            cancellationToken);

    private async Task<UserSession> SignInWithGoogleAsync(
        GoogleIdentity identity,
        UserRole bootstrapRole,
        DateTimeOffset signedInAtUtc,
        bool retryAfterConcurrentInsert,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        try
        {
            return await RetriableTransaction.ExecuteAsync(dbContext, async () =>
            {
                await using IDbContextTransaction transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                User? user = await dbContext.Users.SingleOrDefaultAsync(
                    candidate => candidate.GoogleSubject == identity.Subject,
                    cancellationToken);

                if (user is null)
                {
                    string normalizedEmail = User.NormalizeEmailValue(identity.Email);
                    bool emailBelongsToAnotherIdentity = await dbContext.Users
                        .AnyAsync(
                            candidate => candidate.NormalizedEmail == normalizedEmail,
                            cancellationToken);

                    if (emailBelongsToAnotherIdentity)
                    {
                        throw Conflict();
                    }

                    user = User.CreateFromGoogle(
                        identity.Subject,
                        identity.Email,
                        identity.EmailVerified,
                        identity.DisplayName,
                        bootstrapRole,
                        signedInAtUtc);
                    dbContext.Users.Add(user);
                }
                else
                {
                    string normalizedEmail = User.NormalizeEmailValue(identity.Email);
                    bool emailBelongsToAnotherIdentity = await dbContext.Users
                        .AnyAsync(
                            candidate => candidate.Id != user.Id
                                && candidate.NormalizedEmail == normalizedEmail,
                            cancellationToken);

                    if (emailBelongsToAnotherIdentity)
                    {
                        throw Conflict();
                    }

                    user.RegisterVerifiedGoogleSignIn(
                        identity.Email,
                        identity.EmailVerified,
                        identity.DisplayName,
                        signedInAtUtc);
                    user.GrantRole(bootstrapRole, signedInAtUtc);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Snapshot(user);
            });
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
            })
        {
            // Two browser callbacks for the same first sign-in can race. The
            // unique subject is the database arbiter; once the winner commits,
            // rerun exactly once as an update instead of reporting a false
            // identity conflict. A collision on only the email remains a real
            // conflict and is never auto-linked.
            dbContext.ChangeTracker.Clear();
            bool subjectNowExists = await dbContext.Users
                .AsNoTracking()
                .AnyAsync(
                    candidate => candidate.GoogleSubject == identity.Subject,
                    cancellationToken);
            if (retryAfterConcurrentInsert && subjectNowExists)
            {
                return await SignInWithGoogleAsync(
                    identity,
                    bootstrapRole,
                    signedInAtUtc,
                    retryAfterConcurrentInsert: false,
                    cancellationToken);
            }

            throw Conflict();
        }
    }

    public async Task<UserSession?> FindSessionAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        User? user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        return user is null ? null : Snapshot(user);
    }

    private static UserSession Snapshot(User user) => new()
    {
        UserId = user.Id,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Role = user.Role,
        LastSignedInAtUtc = user.LastSignedInAtUtc,
    };

    private static GoogleIdentityConflictException Conflict() => new(
        "This verified email is already linked to a different Google identity.");
}
