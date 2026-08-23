using Microsoft.Data.SqlClient;

namespace SQLBulkProcessor.Internal;

internal static class SqlBulkCopyExecutor
{
    public static async Task WriteAsync<T>(
        SqlSession session,
        string destinationTable,
        IReadOnlyList<T> entities,
        IReadOnlyList<ColumnMapping> columns,
        BulkOptions options,
        bool includeRowId,
        bool keepIdentity,
        CancellationToken cancellationToken)
        where T : class
    {
        var copyOptions = SqlBulkCopyOptions.Default;
        var destinationIsTemp = destinationTable.Contains('#', StringComparison.Ordinal);
        if (options.UseTableLock && !destinationIsTemp)
            copyOptions |= SqlBulkCopyOptions.TableLock;
        if (keepIdentity)
            copyOptions |= SqlBulkCopyOptions.KeepIdentity;
        if (options.FireTriggers)
            copyOptions |= SqlBulkCopyOptions.FireTriggers;
        if (options.CheckConstraints)
            copyOptions |= SqlBulkCopyOptions.CheckConstraints;

        using var reader = new EntityDataReader<T>(entities, columns, includeRowId);
        using var bulkCopy = new SqlBulkCopy(session.Connection, copyOptions, session.Transaction)
        {
            DestinationTableName = destinationTable,
            BatchSize = options.BatchSize,
            BulkCopyTimeout = options.TimeoutSeconds,
            EnableStreaming = options.EnableStreaming
        };

        if (includeRowId)
            bulkCopy.ColumnMappings.Add("_BulkRowId", "_BulkRowId");

        foreach (var column in columns)
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);

        if (options.OnRowsCopied is not null)
        {
            bulkCopy.NotifyAfter = options.NotifyAfter > 0
                ? options.NotifyAfter
                : Math.Max(entities.Count / 10, 1);
            bulkCopy.SqlRowsCopied += (_, args) => options.OnRowsCopied(args.RowsCopied);
        }

        await bulkCopy.WriteToServerAsync(reader, cancellationToken).ConfigureAwait(false);
    }
}
