using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;

namespace SQLBulkProcessor.Internal;

internal sealed class SqlSession : IAsyncDisposable
{
    private readonly bool _ownsConnection;

    private SqlSession(SqlConnection connection, SqlTransaction? transaction, bool ownsConnection, int timeoutSeconds)
    {
        Connection = connection;
        Transaction = transaction;
        TimeoutSeconds = timeoutSeconds;
        _ownsConnection = ownsConnection;
    }

    public SqlConnection Connection { get; }
    public SqlTransaction? Transaction { get; }
    public int TimeoutSeconds { get; }

    public static async Task<SqlSession> OpenAsync(DbContext context, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (context.Database.GetDbConnection() is not SqlConnection connection)
        {
            throw new InvalidOperationException(
                "SQLBulkProcessor requires a SQL Server connection (Microsoft.Data.SqlClient.SqlConnection).");
        }

        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            opened = true;
        }

        SqlTransaction? transaction = null;
        var current = context.Database.CurrentTransaction;
        if (current is not null)
            transaction = current.GetDbTransaction() as SqlTransaction;

        return new SqlSession(connection, transaction, opened, timeoutSeconds);
    }

    public SqlCommand CreateCommand(string sql)
    {
        var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = TimeoutSeconds;
        command.Transaction = Transaction;
        return command;
    }

    public async Task<int> ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(sql);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task TryDropTempAsync(string? tempTable, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tempTable))
            return;

        try
        {
            await ExecuteAsync(SqlBuilder.DropTempTable(tempTable), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort cleanup; the original operation error (if any) is more useful.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsConnection && Connection.State != ConnectionState.Closed)
            await Connection.CloseAsync().ConfigureAwait(false);
    }
}
