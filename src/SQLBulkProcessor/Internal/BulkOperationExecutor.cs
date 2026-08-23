namespace SQLBulkProcessor.Internal;

internal sealed class BulkOperationExecutor
{
    private readonly DbContext _context;
    private readonly BulkOptions _options;

    public BulkOperationExecutor(DbContext context, BulkOptions options)
    {
        _context = context;
        _options = options;
    }

    public async Task<int> InsertAsync<T>(IReadOnlyList<T> entities, CancellationToken cancellationToken)
        where T : class
    {
        var metadata = EntityMetadataFactory.Get(_context, typeof(T));
        var insertColumns = metadata.ResolveInsertColumns(_options);
        var outputIdentity = ShouldOutputIdentity(metadata);

        if (!outputIdentity)
        {
            await using var session = await SqlSession.OpenAsync(_context, _options.TimeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
            await SqlBulkCopyExecutor.WriteAsync(
                    session,
                    metadata.QuotedFullName,
                    entities,
                    insertColumns,
                    _options,
                    includeRowId: false,
                    keepIdentity: _options.KeepIdentity,
                    cancellationToken)
                .ConfigureAwait(false);
            return entities.Count;
        }

        return await StageAndExecuteAsync(
                entities,
                metadata,
                insertColumns,
                includeRowId: true,
                keepIdentityOnCopy: false,
                sqlFactory: (temp, output) =>
                {
                    var sql = SqlBuilder.InsertViaMergeOutput(
                        metadata.QuotedFullName,
                        temp,
                        output!,
                        insertColumns,
                        metadata.IdentityColumn!);
                    return MaybeIdentityInsert(metadata, insertColumns, sql);
                },
                outputIdentity: true,
                includeAction: false,
                indexKeys: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<int> UpdateAsync<T>(IReadOnlyList<T> entities, CancellationToken cancellationToken)
        where T : class
    {
        var metadata = EntityMetadataFactory.Get(_context, typeof(T));
        var keys = metadata.ResolveKeys(_options);
        var updateColumns = metadata.ResolveUpdateColumns(_options, keys);
        var staging = metadata.ResolveStagingColumns(keys, updateColumns);

        return StageAndExecuteAsync(
            entities,
            metadata,
            staging,
            includeRowId: false,
            keepIdentityOnCopy: false,
            sqlFactory: (temp, _) => SqlBuilder.UpdateFromTemp(metadata.QuotedFullName, temp, keys, updateColumns),
            outputIdentity: false,
            includeAction: false,
            indexKeys: keys,
            cancellationToken);
    }

    public Task<int> DeleteAsync<T>(IReadOnlyList<T> entities, CancellationToken cancellationToken)
        where T : class
    {
        var metadata = EntityMetadataFactory.Get(_context, typeof(T));
        var keys = metadata.ResolveKeys(_options);

        return StageAndExecuteAsync(
            entities,
            metadata,
            keys,
            includeRowId: false,
            keepIdentityOnCopy: false,
            sqlFactory: (temp, _) => SqlBuilder.DeleteFromTemp(metadata.QuotedFullName, temp, keys),
            outputIdentity: false,
            includeAction: false,
            indexKeys: keys,
            cancellationToken);
    }

    public Task<int> MergeAsync<T>(IReadOnlyList<T> entities, CancellationToken cancellationToken)
        where T : class
    {
        if (_options is not BulkMergeOptions mergeOptions)
            throw new InvalidOperationException("Merge operations require BulkMergeOptions.");

        var metadata = EntityMetadataFactory.Get(_context, typeof(T));
        var keys = metadata.ResolveKeys(mergeOptions);
        var insertColumns = mergeOptions.InsertWhenNotMatched
            ? metadata.ResolveInsertColumns(mergeOptions)
            : Array.Empty<ColumnMapping>();
        var updateColumns = mergeOptions.UpdateWhenMatched
            ? metadata.ResolveUpdateColumns(mergeOptions, keys)
            : Array.Empty<ColumnMapping>();

        if (!mergeOptions.InsertWhenNotMatched && !mergeOptions.UpdateWhenMatched && !mergeOptions.DeleteWhenNotMatchedBySource)
        {
            throw new InvalidOperationException(
                "Bulk merge/upsert must enable at least one of InsertWhenNotMatched, UpdateWhenMatched, or DeleteWhenNotMatchedBySource.");
        }

        var staging = metadata.ResolveStagingColumns(keys, insertColumns, updateColumns);
        var outputIdentity = ShouldOutputIdentity(metadata) && mergeOptions.InsertWhenNotMatched;

        return StageAndExecuteAsync(
            entities,
            metadata,
            staging,
            includeRowId: outputIdentity,
            keepIdentityOnCopy: false,
            sqlFactory: (temp, output) =>
            {
                var sql = SqlBuilder.Merge(
                    metadata.QuotedFullName,
                    temp,
                    keys,
                    insertColumns,
                    updateColumns,
                    mergeOptions,
                    outputIdentity ? metadata.IdentityColumn : null,
                    output);
                return MaybeIdentityInsert(metadata, insertColumns, sql);
            },
            outputIdentity: outputIdentity,
            includeAction: outputIdentity,
            indexKeys: keys,
            cancellationToken);
    }

    private async Task<int> StageAndExecuteAsync<T>(
        IReadOnlyList<T> entities,
        EntityMetadata metadata,
        IReadOnlyList<ColumnMapping> stagingColumns,
        bool includeRowId,
        bool keepIdentityOnCopy,
        Func<string, string?, string> sqlFactory,
        bool outputIdentity,
        bool includeAction,
        IReadOnlyList<ColumnMapping>? indexKeys,
        CancellationToken cancellationToken)
        where T : class
    {
        var tempTable = SqlIdentifier.TempTable();
        string? outputTable = outputIdentity ? SqlIdentifier.TempTable() : null;
        await using var session = await SqlSession.OpenAsync(_context, _options.TimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await session.ExecuteAsync(SqlBuilder.CreateTempTable(tempTable, stagingColumns, includeRowId), cancellationToken)
                .ConfigureAwait(false);

            await SqlBulkCopyExecutor.WriteAsync(
                    session,
                    tempTable,
                    entities,
                    stagingColumns,
                    _options,
                    includeRowId,
                    keepIdentityOnCopy,
                    cancellationToken)
                .ConfigureAwait(false);

            if (indexKeys is { Count: > 0 })
            {
                await session.ExecuteAsync(SqlBuilder.CreateTempIndex(tempTable, indexKeys), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (outputIdentity)
            {
                await session.ExecuteAsync(
                        SqlBuilder.CreateIdentityOutputTable(outputTable!, metadata.IdentityColumn!, includeAction),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var sql = sqlFactory(tempTable, outputTable);
            var affected = await session.ExecuteAsync(sql, cancellationToken).ConfigureAwait(false);

            if (outputIdentity)
                await ApplyIdentityOutputAsync(session, entities, metadata.IdentityColumn!, outputTable!, includeAction, cancellationToken)
                    .ConfigureAwait(false);

            return affected;
        }
        finally
        {
            await session.TryDropTempAsync(tempTable, cancellationToken).ConfigureAwait(false);
            await session.TryDropTempAsync(outputTable, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ApplyIdentityOutputAsync<T>(
        SqlSession session,
        IReadOnlyList<T> entities,
        ColumnMapping identity,
        string outputTable,
        bool includeAction,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!identity.CanSet)
        {
            throw new InvalidOperationException(
                $"OutputIdentity is set but identity property '{identity.PropertyPath}' is not settable.");
        }

        await using var command = session.CreateCommand(SqlBuilder.SelectIdentityOutput(outputTable, identity, includeAction));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (includeAction)
            {
                var action = reader.GetString(2);
                if (!string.Equals(action, "INSERT", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var rowId = reader.GetInt32(0);
            if (rowId < 0 || rowId >= entities.Count)
                continue;

            var value = reader.IsDBNull(1) ? null : reader.GetValue(1);
            if (value is not null)
                identity.SetValue(entities[rowId], value);
        }
    }

    private bool ShouldOutputIdentity(EntityMetadata metadata)
        => _options.OutputIdentity
           && !_options.KeepIdentity
           && metadata.IdentityColumn is not null
           && metadata.IdentityColumn.CanSet;

    private string MaybeIdentityInsert(EntityMetadata metadata, IReadOnlyList<ColumnMapping> insertColumns, string sql)
    {
        if (_options.KeepIdentity && insertColumns.Any(c => c.IsIdentity))
            return SqlBuilder.WrapIdentityInsert(metadata.QuotedFullName, sql);

        return sql;
    }
}
