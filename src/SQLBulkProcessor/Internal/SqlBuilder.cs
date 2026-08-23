using System.Text;

namespace SQLBulkProcessor.Internal;

internal static class SqlBuilder
{
    public static string CreateTempTable(string tempTable, IReadOnlyList<ColumnMapping> columns, bool includeRowId)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE ").Append(tempTable).Append(" (");
        if (includeRowId)
            sb.Append("[_BulkRowId] INT NOT NULL, ");

        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");

            var column = columns[i];
            sb.Append(SqlIdentifier.Quote(column.ColumnName))
                .Append(' ')
                .Append(column.StoreType)
                .Append(" NULL");
        }

        sb.Append(");");
        return sb.ToString();
    }

    public static string DropTempTable(string tempTable)
        => $"DROP TABLE IF EXISTS {tempTable};";

    public static string UpdateFromTemp(
        string targetTable,
        string tempTable,
        IReadOnlyList<ColumnMapping> keys,
        IReadOnlyList<ColumnMapping> updateColumns)
    {
        var sb = new StringBuilder();
        sb.Append("UPDATE T SET ");
        for (var i = 0; i < updateColumns.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");

            var quoted = SqlIdentifier.Quote(updateColumns[i].ColumnName);
            sb.Append("T.").Append(quoted).Append(" = S.").Append(quoted);
        }

        sb.Append(" FROM ").Append(targetTable).Append(" AS T INNER JOIN ")
            .Append(tempTable).Append(" AS S ON ")
            .Append(SqlIdentifier.JoinEquals("T", "S", keys))
            .Append(" OPTION (RECOMPILE);");

        return sb.ToString();
    }

    public static string DeleteFromTemp(
        string targetTable,
        string tempTable,
        IReadOnlyList<ColumnMapping> keys)
    {
        return "DELETE T FROM " + targetTable + " AS T WITH (TABLOCK) INNER JOIN " + tempTable
               + " AS S ON " + SqlIdentifier.JoinEquals("T", "S", keys) + " OPTION (RECOMPILE, MAXDOP 1);";
    }

    public static string CreateTempIndex(string tempTable, IReadOnlyList<ColumnMapping> keys)
    {
        if (keys.Count == 0)
            throw new ArgumentException("At least one key column is required to index the staging table.", nameof(keys));

        var sb = new StringBuilder();
        sb.Append("CREATE CLUSTERED INDEX [IX_bulk_keys] ON ").Append(tempTable).Append(" (");
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(SqlIdentifier.Quote(keys[i].ColumnName));
        }

        sb.Append(");");
        return sb.ToString();
    }

    public static string Merge(
        string targetTable,
        string tempTable,
        IReadOnlyList<ColumnMapping> keys,
        IReadOnlyList<ColumnMapping> insertColumns,
        IReadOnlyList<ColumnMapping> updateColumns,
        BulkMergeOptions options,
        ColumnMapping? outputIdentity,
        string? outputTable)
    {
        var sb = new StringBuilder();
        sb.Append("MERGE ").Append(targetTable);
        if (options.UseHoldLock)
            sb.Append(" WITH (HOLDLOCK)");

        sb.Append(" AS T USING ").Append(tempTable).Append(" AS S ON (")
            .Append(SqlIdentifier.JoinEquals("T", "S", keys))
            .Append(')');

        if (options.UpdateWhenMatched && updateColumns.Count > 0)
        {
            sb.Append(" WHEN MATCHED THEN UPDATE SET ");
            for (var i = 0; i < updateColumns.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");

                var quoted = SqlIdentifier.Quote(updateColumns[i].ColumnName);
                sb.Append("T.").Append(quoted).Append(" = S.").Append(quoted);
            }
        }

        if (options.InsertWhenNotMatched && insertColumns.Count > 0)
        {
            sb.Append(" WHEN NOT MATCHED BY TARGET THEN INSERT (");
            AppendQuotedNames(sb, insertColumns);
            sb.Append(") VALUES (");
            for (var i = 0; i < insertColumns.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append("S.").Append(SqlIdentifier.Quote(insertColumns[i].ColumnName));
            }

            sb.Append(')');
        }

        if (options.DeleteWhenNotMatchedBySource)
            sb.Append(" WHEN NOT MATCHED BY SOURCE THEN DELETE");

        if (outputIdentity is not null && outputTable is not null)
        {
            var idCol = SqlIdentifier.Quote(outputIdentity.ColumnName);
            sb.Append(" OUTPUT $action, INSERTED.").Append(idCol)
                .Append(", S.[_BulkRowId] INTO ").Append(outputTable)
                .Append(" ([Action], ").Append(idCol).Append(", [_BulkRowId])");
        }

        sb.Append(" OPTION (RECOMPILE);");
        return sb.ToString();
    }

    public static string InsertViaMergeOutput(
        string targetTable,
        string tempTable,
        string outputTable,
        IReadOnlyList<ColumnMapping> insertColumns,
        ColumnMapping identity)
    {
        var idCol = SqlIdentifier.Quote(identity.ColumnName);
        var sb = new StringBuilder();
        sb.Append("MERGE ").Append(targetTable).Append(" AS T USING ").Append(tempTable)
            .Append(" AS S ON 1 = 0 WHEN NOT MATCHED THEN INSERT (");
        AppendQuotedNames(sb, insertColumns);
        sb.Append(") VALUES (");
        for (var i = 0; i < insertColumns.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append("S.").Append(SqlIdentifier.Quote(insertColumns[i].ColumnName));
        }

        sb.Append(") OUTPUT INSERTED.").Append(idCol)
            .Append(", S.[_BulkRowId] INTO ").Append(outputTable)
            .Append(" (").Append(idCol).Append(", [_BulkRowId]);");

        return sb.ToString();
    }

    public static string CreateIdentityOutputTable(string table, ColumnMapping identity, bool includeAction)
    {
        var idCol = SqlIdentifier.Quote(identity.ColumnName);
        var action = includeAction ? "[Action] NVARCHAR(10) NOT NULL, " : string.Empty;
        return $"CREATE TABLE {table} ({action}[_BulkRowId] INT NOT NULL, {idCol} {identity.StoreType} NULL);";
    }

    public static string WrapIdentityInsert(string targetTable, string sql)
        => $"SET IDENTITY_INSERT {targetTable} ON; {sql} SET IDENTITY_INSERT {targetTable} OFF;";

    public static string SelectIdentityOutput(string outputTable, ColumnMapping identity, bool includeAction)
    {
        var idCol = SqlIdentifier.Quote(identity.ColumnName);
        return includeAction
            ? $"SELECT [_BulkRowId], {idCol}, [Action] FROM {outputTable};"
            : $"SELECT [_BulkRowId], {idCol} FROM {outputTable};";
    }

    private static void AppendQuotedNames(StringBuilder sb, IReadOnlyList<ColumnMapping> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(SqlIdentifier.Quote(columns[i].ColumnName));
        }
    }
}
