using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// Runs a store operation that opens its own transaction as one retriable unit.
/// </summary>
/// <remarks>
/// The hosts configure the context with <c>EnableRetryOnFailure</c>, and a
/// retrying execution strategy refuses to save inside a transaction it did not
/// open: the save throws
/// <c>"The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does
/// not support user-initiated transactions."</c> The failure appears only under
/// the host configuration, so every store that begins a transaction has to go
/// through here.
/// <para>
/// A retry re-runs the whole delegate, so the change tracker is emptied first:
/// entities the failed attempt modified must not be carried into the next one.
/// </para>
/// </remarks>
internal static class RetriableTransaction
{
    public static Task<TResult> ExecuteAsync<TResult>(
        SirkadiyenDbContext dbContext,
        Func<Task<TResult>> operation)
    {
        IExecutionStrategy strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(() =>
        {
            dbContext.ChangeTracker.Clear();
            return operation();
        });
    }

    public static Task ExecuteAsync(SirkadiyenDbContext dbContext, Func<Task> operation) =>
        ExecuteAsync<object?>(dbContext, async () =>
        {
            await operation();
            return null;
        });
}
