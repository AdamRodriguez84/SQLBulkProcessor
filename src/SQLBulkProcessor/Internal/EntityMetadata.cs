namespace SQLBulkProcessor.Internal;

internal sealed class EntityMetadata
{
    public EntityMetadata(
        IEntityType entityType,
        string tableName,
        string? schema,
        IReadOnlyList<ColumnMapping> columns)
    {
        EntityType = entityType;
        TableName = tableName;
        Schema = schema;
        QuotedFullName = SqlIdentifier.Qualify(schema, tableName);
        Columns = columns;
        IdentityColumn = columns.FirstOrDefault(c => c.IsIdentity);
        DefaultKeyColumns = columns.Where(c => c.IsKey).ToArray();
    }

    public IEntityType EntityType { get; }
    public string TableName { get; }
    public string? Schema { get; }
    public string QuotedFullName { get; }
    public IReadOnlyList<ColumnMapping> Columns { get; }
    public IReadOnlyList<ColumnMapping> DefaultKeyColumns { get; }
    public ColumnMapping? IdentityColumn { get; }

    public IReadOnlyList<ColumnMapping> ResolveKeys(BulkOptions options)
    {
        IReadOnlyList<ColumnMapping> keys;
        if (options.KeyColumns is { Length: > 0 })
        {
            keys = FilterByNames(Columns, options.KeyColumns, required: true);
        }
        else
        {
            keys = DefaultKeyColumns;
        }

        if (keys.Count == 0)
        {
            throw new InvalidOperationException(
                $"Entity '{EntityType.DisplayName()}' has no key columns. Set BulkOptions.KeyColumns explicitly.");
        }

        return keys;
    }

    public IReadOnlyList<ColumnMapping> ResolveInsertColumns(BulkOptions options)
    {
        var columns = Columns.Where(c => !c.IsComputed && !c.IsRowVersion && !c.IsPeriod);
        if (!options.KeepIdentity)
            columns = columns.Where(c => !c.IsIdentity);

        return ApplyIncludeExclude(columns, options, requireNonEmpty: true, role: "insert");
    }

    public IReadOnlyList<ColumnMapping> ResolveUpdateColumns(BulkOptions options, IReadOnlyList<ColumnMapping> keys)
    {
        var keyNames = keys.Select(k => k.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columns = Columns.Where(c =>
            !keyNames.Contains(c.ColumnName)
            && !c.IsIdentity
            && !c.IsComputed
            && !c.IsRowVersion
            && !c.IsPeriod);

        return ApplyIncludeExclude(columns, options, requireNonEmpty: true, role: "update");
    }

    public IReadOnlyList<ColumnMapping> ResolveStagingColumns(params IReadOnlyList<ColumnMapping>[] sets)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ColumnMapping>();
        foreach (var set in sets)
        {
            foreach (var column in set)
            {
                if (seen.Add(column.ColumnName))
                    result.Add(column);
            }
        }

        if (result.Count == 0)
            throw new InvalidOperationException($"No columns available to stage for '{EntityType.DisplayName()}'.");

        return result;
    }

    private IReadOnlyList<ColumnMapping> ApplyIncludeExclude(
        IEnumerable<ColumnMapping> columns,
        BulkOptions options,
        bool requireNonEmpty,
        string role)
    {
        if (options.IncludeColumns is { Length: > 0 })
        {
            var include = options.IncludeColumns;
            columns = columns.Where(c => MatchesAny(c, include));
        }

        if (options.ExcludeColumns is { Length: > 0 })
        {
            var exclude = options.ExcludeColumns;
            columns = columns.Where(c => !MatchesAny(c, exclude));
        }

        var list = columns.ToList();
        if (requireNonEmpty && list.Count == 0)
        {
            throw new InvalidOperationException(
                $"No {role} columns remain for '{EntityType.DisplayName()}' after applying IncludeColumns/ExcludeColumns.");
        }

        return list;
    }

    private static IReadOnlyList<ColumnMapping> FilterByNames(
        IReadOnlyList<ColumnMapping> columns,
        string[] names,
        bool required)
    {
        var result = new List<ColumnMapping>(names.Length);
        foreach (var name in names)
        {
            var match = columns.FirstOrDefault(c => c.Matches(name));
            if (match is null)
            {
                if (required)
                    throw new InvalidOperationException($"Column or property '{name}' was not found on the mapped entity.");
                continue;
            }

            result.Add(match);
        }

        return result;
    }

    private static bool MatchesAny(ColumnMapping column, string[] names)
    {
        foreach (var name in names)
        {
            if (column.Matches(name))
                return true;
        }

        return false;
    }
}
