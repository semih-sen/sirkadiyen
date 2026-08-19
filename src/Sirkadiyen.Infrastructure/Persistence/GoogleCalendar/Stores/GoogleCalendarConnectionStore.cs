using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;

namespace Sirkadiyen.Infrastructure.Persistence.GoogleCalendar.Stores;

/// <summary>Transactional PostgreSQL Calendar-connection store.</summary>
public sealed class GoogleCalendarConnectionStore(SirkadiyenDbContext dbContext)
    : IUserCalendarConnectionStore, ICalendarSyncConnectionStore
{
    public async Task<GoogleCalendarConnectionView?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnection? connection = await dbContext.GoogleCalendarConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        return connection is null ? null : View(connection);
    }

    public async Task<GoogleCalendarConnectionView> UpsertAuthorizationAsync(
        Guid userId,
        string protectedRefreshToken,
        string grantedScopes,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        await UpsertAuthorizationAsync(
            userId,
            protectedRefreshToken,
            grantedScopes,
            atUtc,
            retryAfterConcurrentInsert: true,
            cancellationToken);

    public async Task<RequestInitialSyncResult> RequestInitialSyncAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnection? connection = await dbContext.GoogleCalendarConnections
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        if (connection is null)
        {
            return new RequestInitialSyncResult { Outcome = RequestInitialSyncOutcome.NotFound };
        }

        if (connection.Status is not GoogleCalendarConnectionStatus.Authorized)
        {
            return new RequestInitialSyncResult { Outcome = RequestInitialSyncOutcome.NotAuthorized };
        }

        switch (connection.InitialSyncState)
        {
            case GoogleCalendarInitialSyncState.InProgress:
                return new RequestInitialSyncResult
                {
                    Outcome = RequestInitialSyncOutcome.AlreadyInProgress,
                    Connection = View(connection),
                };
            case GoogleCalendarInitialSyncState.Completed:
                return new RequestInitialSyncResult
                {
                    Outcome = RequestInitialSyncOutcome.AlreadyCompleted,
                    Connection = View(connection),
                };
        }

        connection.RequestInitialSync(atUtc);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RequestInitialSyncResult
        {
            Outcome = RequestInitialSyncOutcome.Requested,
            Connection = View(connection),
        };
    }

    /// <remarks>
    /// A revoked student is simply absent (ADR-095). Their connection stays
    /// <see cref="GoogleCalendarInitialSyncState.InProgress"/> rather than being failed, so
    /// restoring access resumes the sync from what the ledger already holds instead of discarding
    /// the progress over an administrative action that is often reversed.
    /// </remarks>
    public async Task<IReadOnlyList<PendingCalendarSync>> ListPendingInitialSyncAsync(
        int limit,
        CancellationToken cancellationToken) =>
        await dbContext.GoogleCalendarConnections
            .AsNoTracking()
            .Where(connection =>
                connection.Status == GoogleCalendarConnectionStatus.Authorized
                && connection.InitialSyncState == GoogleCalendarInitialSyncState.InProgress
                && ActiveLicenseQuery.UserIds(dbContext).Contains(connection.UserId))
            .OrderBy(connection => connection.UpdatedAtUtc)
            .Take(limit)
            .Select(connection => new PendingCalendarSync
            {
                UserId = connection.UserId,
                ProtectedRefreshToken = connection.ProtectedRefreshToken,
                ManagedCalendarId = connection.ManagedCalendarId,
            })
            .ToListAsync(cancellationToken);

    public async Task AttachManagedCalendarAsync(
        Guid userId,
        string managedCalendarId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnection connection = await SingleForUpdateAsync(userId, cancellationToken);
        connection.AttachManagedCalendar(managedCalendarId, atUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkInitialSyncCompletedAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnection connection = await SingleForUpdateAsync(userId, cancellationToken);
        connection.CompleteInitialSync(atUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkNeedsReauthorizationAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnection connection = await SingleForUpdateAsync(userId, cancellationToken);
        connection.MarkNeedsReauthorization(atUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PendingCalendarReconciliation>> ListPendingReconciliationAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await dbContext.GoogleCalendarConnections
            .AsNoTracking()
            .Where(connection =>
                connection.Status == GoogleCalendarConnectionStatus.Authorized
                && connection.InitialSyncState == GoogleCalendarInitialSyncState.Completed
                && connection.ManagedCalendarId != null
                && connection.ManagedCalendarUnavailableAtUtc == null
                && connection.ReconciliationRequiredSinceUtc != null
                && connection.ReconciliationCursorDispatchedAtUtc != null
                && connection.ReconciliationCursorDiffId != null
                && ActiveLicenseQuery.UserIds(dbContext).Contains(connection.UserId))
            .OrderBy(connection => connection.ReconciliationRequiredSinceUtc)
            .ThenBy(connection => connection.UserId)
            .Take(limit)
            .Select(connection => new PendingCalendarReconciliation
            {
                UserId = connection.UserId,
                ProtectedRefreshToken = connection.ProtectedRefreshToken,
                ManagedCalendarId = connection.ManagedCalendarId!,
                RequiredSinceUtc = connection.ReconciliationRequiredSinceUtc!.Value,
                CursorDispatchedAtUtc =
                    connection.ReconciliationCursorDispatchedAtUtc!.Value,
                CursorDiffId = connection.ReconciliationCursorDiffId!.Value,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task AdvanceReconciliationCursorAsync(
        Guid userId,
        DateTimeOffset expectedRequiredSinceUtc,
        DateTimeOffset dispatchedAtUtc,
        Guid diffId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnection connection = await SingleForUpdateAsync(userId, cancellationToken);
        connection.AdvanceReconciliationCursor(
            expectedRequiredSinceUtc,
            dispatchedAtUtc,
            diffId,
            atUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteReconciliationAsync(
        Guid userId,
        DateTimeOffset expectedRequiredSinceUtc,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnection connection = await SingleForUpdateAsync(userId, cancellationToken);
        connection.CompleteReconciliation(expectedRequiredSinceUtc, atUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PendingProfileResync>> ListPendingProfileResyncAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        // A pending re-authorization replay is deliberately not excluded here the way inventory
        // excludes it: replay only applies diffs the user missed, and this only converges the
        // audience, so the two are about different things and both are idempotent. A connection
        // that needs re-authorization is excluded, because nothing can be written for it at all.
        return await dbContext.GoogleCalendarConnections
            .AsNoTracking()
            .Where(connection =>
                connection.Status == GoogleCalendarConnectionStatus.Authorized
                && connection.InitialSyncState == GoogleCalendarInitialSyncState.Completed
                && connection.ManagedCalendarId != null
                && connection.ManagedCalendarUnavailableAtUtc == null
                && connection.ProfileResyncRequiredSinceUtc != null
                && ActiveLicenseQuery.UserIds(dbContext).Contains(connection.UserId))
            .OrderBy(connection => connection.ProfileResyncRequiredSinceUtc)
            .ThenBy(connection => connection.UserId)
            .Take(limit)
            .Select(connection => new PendingProfileResync
            {
                UserId = connection.UserId,
                ProtectedRefreshToken = connection.ProtectedRefreshToken,
                ManagedCalendarId = connection.ManagedCalendarId!,
                RequiredSinceUtc = connection.ProfileResyncRequiredSinceUtc!.Value,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<CompleteProfileResyncOutcome> CompleteProfileResyncAsync(
        Guid userId,
        DateTimeOffset expectedRequiredSinceUtc,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnection? connection = await dbContext.GoogleCalendarConnections
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        if (connection is null)
        {
            return CompleteProfileResyncOutcome.NotFound;
        }

        if (connection.ProfileResyncRequiredSinceUtc != expectedRequiredSinceUtc)
        {
            // The profile changed again while this pass ran. Reported rather than thrown: the
            // newer request is correct and the next cycle converges it, so this is an ordinary
            // outcome of a race the design expects, not a failure.
            return CompleteProfileResyncOutcome.Superseded;
        }

        connection.CompleteProfileResync(expectedRequiredSinceUtc, atUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CompleteProfileResyncOutcome.Completed;
    }

    public async Task MarkCalendarInventoryCompletedAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnection connection = await SingleForUpdateAsync(userId, cancellationToken);
        connection.CompleteCalendarInventory(atUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkManagedCalendarUnavailableAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnection connection = await SingleForUpdateAsync(userId, cancellationToken);
        connection.MarkManagedCalendarUnavailable(atUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<RequestReconciliationOutcome> RequestReconciliationAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnection? connection = await dbContext.GoogleCalendarConnections
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        if (connection is null)
        {
            return RequestReconciliationOutcome.NotFound;
        }

        if (!connection.TryRequestReconciliation(atUtc))
        {
            return RequestReconciliationOutcome.NotEligible;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return RequestReconciliationOutcome.Requested;
    }

    public async Task<ManagedCalendarRebuildResult> RebuildManagedCalendarAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        await RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            GoogleCalendarConnection? connection = await dbContext.GoogleCalendarConnections
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

            if (connection is null)
            {
                return new ManagedCalendarRebuildResult
                {
                    Outcome = ManagedCalendarRebuildOutcome.NoConnection,
                };
            }

            // The domain owns the eligibility rule, so asking it beforehand and acting on the
            // answer keeps "may this be rebuilt" in exactly one place.
            if (connection.ManagedCalendarUnavailableAtUtc is null)
            {
                return new ManagedCalendarRebuildResult
                {
                    Outcome = ManagedCalendarRebuildOutcome.NotEligible,
                };
            }

            // Every row described an event on the calendar that is gone. Leaving them would make
            // initial sync skip the lessons they name, because it writes what the ledger does not
            // already record — an empty calendar with no state explaining it.
            int discarded = await dbContext.UserCalendarEventMappings
                .Where(mapping => mapping.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);

            connection.ResetForCalendarRebuild(atUtc);

            // One transaction for both. A connection detached from its calendar while its ledger
            // rows survive, or the reverse, is a state nothing downstream can reason about.
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ManagedCalendarRebuildResult
            {
                Outcome = ManagedCalendarRebuildOutcome.Reset,
                DiscardedMappings = discarded,
            };
        });

    private async Task<GoogleCalendarConnection> SingleForUpdateAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await dbContext.GoogleCalendarConnections
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken)
        ?? throw new InvalidOperationException(
            $"No Calendar connection exists for user '{userId}'.");

    private async Task<GoogleCalendarConnectionView> UpsertAuthorizationAsync(
        Guid userId,
        string protectedRefreshToken,
        string grantedScopes,
        DateTimeOffset atUtc,
        bool retryAfterConcurrentInsert,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RetriableTransaction.ExecuteAsync(dbContext, async () =>
            {
                await using IDbContextTransaction transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                GoogleCalendarConnection? connection = await dbContext.GoogleCalendarConnections
                    .SingleOrDefaultAsync(
                        candidate => candidate.UserId == userId,
                        cancellationToken);

                if (connection is null)
                {
                    connection = GoogleCalendarConnection.Create(
                        userId,
                        protectedRefreshToken,
                        grantedScopes,
                        atUtc);
                    dbContext.GoogleCalendarConnections.Add(connection);
                }
                else
                {
                    // Re-granting keeps the row, and with it the dedicated calendar the
                    // user's events already live in and their initial-sync progress (ADR-024).
                    connection.Reauthorize(protectedRefreshToken, grantedScopes, atUtc);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return View(connection);
            });
        }
        catch (DbUpdateException exception)
            when (retryAfterConcurrentInsert && IsUniqueViolation(exception))
        {
            // Two concurrent first-time authorizations for one user can race the unique
            // index. The loser reruns exactly once, now finding the winning row and
            // applying its own grant as a re-authorization.
            dbContext.ChangeTracker.Clear();
            return await UpsertAuthorizationAsync(
                userId,
                protectedRefreshToken,
                grantedScopes,
                atUtc,
                retryAfterConcurrentInsert: false,
                cancellationToken);
        }
    }

    private static GoogleCalendarConnectionView View(GoogleCalendarConnection connection) => new()
    {
        UserId = connection.UserId,
        GrantedScopes = connection.GrantedScopes,
        Status = connection.Status,
        InitialSyncState = connection.InitialSyncState,
        ManagedCalendarId = connection.ManagedCalendarId,
        ManagedCalendarUnavailableAtUtc = connection.ManagedCalendarUnavailableAtUtc,
        LastCalendarInventoryAtUtc = connection.LastCalendarInventoryAtUtc,
        ReconciliationRequiredSinceUtc = connection.ReconciliationRequiredSinceUtc,
        UpdatedAtUtc = connection.UpdatedAtUtc,
    };

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        };
}
