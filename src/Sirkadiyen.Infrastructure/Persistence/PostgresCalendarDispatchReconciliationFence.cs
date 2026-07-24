using Npgsql;
using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL session advisory lock shared by every worker instance. The dedicated connection
/// stays open for the whole dispatch/reconciliation stage; PostgreSQL releases the lock
/// automatically if the process or connection dies.
/// </summary>
public sealed class PostgresCalendarDispatchReconciliationFence(string connectionString)
    : ICalendarDispatchReconciliationFence
{
    // "SIRK" as the namespace plus version 1 of the Calendar stage.
    private const int LockNamespace = 0x5349524B;
    private const int LockKey = 1;

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = new(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@namespace, @key);";
            command.Parameters.AddWithValue("namespace", LockNamespace);
            command.Parameters.AddWithValue("key", LockKey);
            bool acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken)
                ?? false);
            if (!acquired)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new Lease(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(NpgsqlConnection connection) : IAsyncDisposable
    {
        private NpgsqlConnection? heldConnection = connection;

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection? current = Interlocked.Exchange(ref heldConnection, null);
            if (current is null)
            {
                return;
            }

            try
            {
                await using NpgsqlCommand command = current.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@namespace, @key);";
                command.Parameters.AddWithValue("namespace", LockNamespace);
                command.Parameters.AddWithValue("key", LockKey);
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
            finally
            {
                await current.DisposeAsync();
            }
        }
    }
}
