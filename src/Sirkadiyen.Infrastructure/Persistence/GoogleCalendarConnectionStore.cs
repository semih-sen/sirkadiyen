using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>Transactional PostgreSQL Calendar-connection store.</summary>
public sealed class GoogleCalendarConnectionStore(SirkadiyenDbContext dbContext)
    : IGoogleCalendarConnectionStore
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

    public async Task<IReadOnlyList<PendingCalendarSync>> ListPendingInitialSyncAsync(
        int limit,
        CancellationToken cancellationToken) =>
        await dbContext.GoogleCalendarConnections
            .AsNoTracking()
            .Where(connection =>
                connection.Status == GoogleCalendarConnectionStatus.Authorized
                && connection.InitialSyncState == GoogleCalendarInitialSyncState.InProgress)
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
                && connection.ReconciliationCursorDiffId != null)
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
