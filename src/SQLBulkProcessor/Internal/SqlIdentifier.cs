using System.Text;

namespace SQLBulkProcessor.Internal;

internal static class SqlIdentifier
{
    public static string Quote(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    public static string Qualify(string? schema, string table)
    {
        return string.IsNullOrEmpty(schema)
            ? Quote(table)
            : Quote(schema) + "." + Quote(table);
    }

    public static string TempTable()
        => Quote("#bulk_" + Guid.NewGuid().ToString("N"));

    public static string JoinEquals(string leftAlias, string rightAlias, IReadOnlyList<ColumnMapping> keys)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0)
                sb.Append(" AND ");

            var quoted = Quote(keys[i].ColumnName);
            sb.Append(leftAlias).Append('.').Append(quoted)
                .Append(" = ")
                .Append(rightAlias).Append('.').Append(quoted);
        }

        return sb.ToString();
    }
}
