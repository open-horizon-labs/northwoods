using Npgsql;

namespace Northwoods.Tenancy;

/// <summary>
/// A tenant-scoped database session wrapping a connection and transaction.
/// SET LOCAL only takes effect inside a transaction, so this pairs them.
/// </summary>
public sealed class DbSession : IAsyncDisposable
{
    public NpgsqlConnection Connection { get; }
    public NpgsqlTransaction Transaction { get; }

    internal DbSession(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        Connection = conn;
        Transaction = tx;
    }

    public Task CommitAsync() => Transaction.CommitAsync();

    public async ValueTask DisposeAsync()
    {
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}

/// <summary>
/// Creates tenant-scoped database sessions using row-level security.
/// </summary>
public sealed class DbConnectionFactory(string connectionString)
{
    /// <summary>
    /// Creates a new tenant-scoped session: opens a connection, begins a transaction,
    /// sets the RLS tenant context, and switches to the restricted app_user role.
    /// Caller must commit and dispose.
    /// </summary>
    public async Task<DbSession> OpenSessionAsync(string tenantId)
    {
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync();

        // set_config with local=true scopes to the current transaction
        await using (var cmd = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tid, true)", conn, tx))
        {
            cmd.Parameters.AddWithValue("@tid", tenantId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand("SET LOCAL ROLE app_user", conn, tx))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        return new DbSession(conn, tx);
    }
}
