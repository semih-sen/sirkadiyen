using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Domain.Licensing;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>Transactional PostgreSQL license store.</summary>
public sealed class LicenseStore(SirkadiyenDbContext dbContext) : ILicenseStore
{
    public async Task SaveCreatedAsync(
        License license,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(license);

        try
        {
            await RetriableTransaction.ExecuteAsync(dbContext, async () =>
            {
                await using IDbContextTransaction transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                dbContext.Licenses.Add(license);
                dbContext.LicenseAudits.Add(license.CreationAudit());
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new LicenseHashCollisionException(
                "The generated license code hash already exists.");
        }
    }

    public async Task<LicenseRedemptionResult> RedeemAsync(
        byte[] codeHash,
        Guid userId,
        string userEmail,
        DateTimeOffset redeemedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codeHash);

        try
        {
            return await RetriableTransaction.ExecuteAsync(dbContext, async () =>
            {
                await using IDbContextTransaction transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                License? license = await dbContext.Licenses
                    .FromSql($"""
                        SELECT *, xmin FROM sirkadiyen.licenses
                        WHERE "CodeHash" = {codeHash}
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(cancellationToken);

                if (license is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return Result(LicenseRedemptionOutcome.Invalid);
                }

                if (license.Status == LicenseStatus.Redeemed)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return license.RedeemedByUserId == userId
                        ? Result(
                            LicenseRedemptionOutcome.AlreadyRedeemedByCurrentUser,
                            license.Id)
                        : Result(LicenseRedemptionOutcome.RedeemedByAnotherUser);
                }

                if (license.Status == LicenseStatus.Revoked)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return Result(LicenseRedemptionOutcome.Revoked);
                }

                if (license.Status == LicenseStatus.Expired)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return Result(LicenseRedemptionOutcome.Expired);
                }

                if (license.IsExpiredAt(redeemedAtUtc))
                {
                    dbContext.LicenseAudits.Add(
                        license.MarkExpired(userId, userEmail, redeemedAtUtc));
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return Result(LicenseRedemptionOutcome.Expired);
                }

                bool alreadyActivated = await dbContext.Licenses.AnyAsync(
                    candidate => candidate.RedeemedByUserId == userId
                        && candidate.Status == LicenseStatus.Redeemed,
                    cancellationToken);
                if (alreadyActivated)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return Result(LicenseRedemptionOutcome.UserAlreadyActivated);
                }

                dbContext.LicenseAudits.Add(
                    license.Redeem(userId, userEmail, redeemedAtUtc));
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Result(LicenseRedemptionOutcome.Redeemed, license.Id);
            });
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Two different codes can be submitted concurrently for the same
            // user. The partial unique index on the active redemption chooses
            // one winner.
            return Result(LicenseRedemptionOutcome.UserAlreadyActivated);
        }
    }

    public Task<LicenseRevocationResult> RevokeAsync(
        Guid licenseId,
        Guid actorUserId,
        string actorEmail,
        string reason,
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            License? license = await dbContext.Licenses
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.licenses
                    WHERE "Id" = {licenseId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            if (license is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new LicenseRevocationResult
                {
                    Outcome = LicenseRevocationOutcome.NotFound,
                };
            }

            if (license.Status == LicenseStatus.Revoked)
            {
                await transaction.CommitAsync(cancellationToken);
                return new LicenseRevocationResult
                {
                    Outcome = LicenseRevocationOutcome.AlreadyRevoked,
                    AffectedUserId = license.RedeemedByUserId,
                };
            }

            Guid? affectedUserId = license.RedeemedByUserId;
            dbContext.LicenseAudits.Add(
                license.Revoke(
                    actorUserId,
                    actorEmail,
                    reason,
                    revokedAtUtc));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new LicenseRevocationResult
            {
                Outcome = LicenseRevocationOutcome.Revoked,
                AffectedUserId = affectedUserId,
            };
        });

    public async Task<ManualLicenseActivationResult> ActivateManuallyAsync(
        Guid userId,
        Guid actorUserId,
        string actorEmail,
        string reason,
        DateTimeOffset activatedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RetriableTransaction.ExecuteAsync(dbContext, async () =>
            {
                await using IDbContextTransaction transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                bool userExists = await dbContext.Users.AnyAsync(
                    user => user.Id == userId,
                    cancellationToken);
                if (!userExists)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ManualResult(ManualLicenseActivationOutcome.UserNotFound, userId);
                }

                bool alreadyActivated = await dbContext.Licenses.AnyAsync(
                    license => license.RedeemedByUserId == userId
                        && license.Status == LicenseStatus.Redeemed,
                    cancellationToken);
                if (alreadyActivated)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ManualResult(
                        ManualLicenseActivationOutcome.AlreadyActivated,
                        userId);
                }

                License license = License.CreateManualActivation(
                    userId,
                    actorUserId,
                    actorEmail,
                    reason,
                    activatedAtUtc);
                dbContext.Licenses.Add(license);
                dbContext.LicenseAudits.Add(license.ManualActivationAudit(reason));
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return ManualResult(
                    ManualLicenseActivationOutcome.Activated,
                    userId,
                    license.Id);
            });
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A code redemption or another manual activation won the same-user
            // race through the partial unique index.
            return ManualResult(ManualLicenseActivationOutcome.AlreadyActivated, userId);
        }
    }

    public async Task<UserLicenseState> GetUserLicenseStateAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        bool active = await dbContext.Licenses
            .AsNoTracking()
            .AnyAsync(
                license => license.RedeemedByUserId == userId
                    && license.Status == LicenseStatus.Redeemed,
                cancellationToken);
        if (active)
        {
            return UserLicenseState.Active;
        }

        bool revoked = await dbContext.Licenses
            .AsNoTracking()
            .AnyAsync(
                license => license.RedeemedByUserId == userId
                    && license.Status == LicenseStatus.Revoked,
                cancellationToken);
        return revoked ? UserLicenseState.Suspended : UserLicenseState.None;
    }

    public async Task<UserLicenseSummary?> GetUserLicenseSummaryAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        // The redeemed license is the one that grants access, so it wins over any older revoked
        // one. Only fall back to a revoked license so a suspended user can be told why.
        License? license = await dbContext.Licenses
            .AsNoTracking()
            .Where(candidate => candidate.RedeemedByUserId == userId
                && candidate.Status == LicenseStatus.Redeemed)
            .FirstOrDefaultAsync(cancellationToken);

        license ??= await dbContext.Licenses
            .AsNoTracking()
            .Where(candidate => candidate.RedeemedByUserId == userId
                && candidate.Status == LicenseStatus.Revoked)
            .OrderByDescending(candidate => candidate.RevokedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (license is null)
        {
            return null;
        }

        return new UserLicenseSummary
        {
            Status = license.Status,
            Kind = license.Kind,
            RedeemedAtUtc = license.RedeemedAtUtc,
            RevokedAtUtc = license.RevokedAtUtc,
        };
    }

    private static LicenseRedemptionResult Result(
        LicenseRedemptionOutcome outcome,
        Guid? licenseId = null) => new()
        {
            Outcome = outcome,
            LicenseId = licenseId,
        };

    private static ManualLicenseActivationResult ManualResult(
        ManualLicenseActivationOutcome outcome,
        Guid userId,
        Guid? licenseId = null) => new()
        {
            Outcome = outcome,
            UserId = userId,
            LicenseId = licenseId,
        };

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        };
}
