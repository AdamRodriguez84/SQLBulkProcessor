using SQLBulkProcessor.Internal;

namespace SQLBulkProcessor;

/// <summary>
/// High-performance bulk Insert, Update, Upsert, Delete, and Merge extensions for EF Core
/// <see cref="DbContext"/> instances backed by SQL Server.
/// </summary>
public static class DbContextBulkExtensions
{
    /// <summary>
    /// Bulk-inserts entities using <c>SqlBulkCopy</c>. Identity, computed, rowversion, and
    /// temporal period columns are omitted unless <see cref="BulkOptions.KeepIdentity"/> is set.
    /// </summary>
    public static int BulkInsert<T>(
        this DbContext context,
        IEnumerable<T> entities,
        Action<BulkOptions>? configure = null)
        where T : class
        => BulkInsertAsync(context, entities, configure).GetAwaiter().GetResult();

    /// <inheritdoc cref="BulkInsert{T}(DbContext, IEnumerable{T}, Action{BulkOptions}?)"/>
    public static Task<int> BulkInsertAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default)
        where T : class
        => BulkInsertAsync(context, entities, configure: null, cancellationToken);

    /// <inheritdoc cref="BulkInsert{T}(DbContext, IEnumerable{T}, Action{BulkOptions}?)"/>
    public static Task<int> BulkInsertAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        Action<BulkOptions>? configure,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var (list, options) = Prepare(context, entities, configure);
        if (list.Count == 0)
            return Task.FromResult(0);

        return new BulkOperationExecutor(context, options).InsertAsync(list, cancellationToken);
    }

    /// <summary>
    /// Bulk-updates entities by primary key (or <see cref="BulkOptions.KeyColumns"/>).
    /// Rows are staged into a temp table via <c>SqlBulkCopy</c>, then updated with a set-based JOIN.
    /// </summary>
    public static int BulkUpdate<T>(
        this DbContext context,
        IEnumerable<T> entities,
        Action<BulkOptions>? configure = null)
        where T : class
        => BulkUpdateAsync(context, entities, configure).GetAwaiter().GetResult();

    /// <inheritdoc cref="BulkUpdate{T}(DbContext, IEnumerable{T}, Action{BulkOptions}?)"/>
    public static Task<int> BulkUpdateAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default)
        where T : class
        => BulkUpdateAsync(context, entities, configure: null, cancellationToken);

    /// <inheritdoc cref="BulkUpdate{T}(DbContext, IEnumerable{T}, Action{BulkOptions}?)"/>
    public static Task<int> BulkUpdateAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        Action<BulkOptions>? configure,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var (list, options) = Prepare(context, entities, configure);
        if (list.Count == 0)
            return Task.FromResult(0);

        return new BulkOperationExecutor(context, options).UpdateAsync(list, cancellationToken);
    }

    /// <summary>
    /// Inserts rows that do not exist and updates rows that do (MERGE without delete).
    /// Matching uses the primary key or <see cref="BulkOptions.KeyColumns"/>.
    /// </summary>
    public static int BulkUpsert<T>(
        this DbContext context,
        IEnumerable<T> entities,
        Action<BulkMergeOptions>? configure = null)
        where T : class
        => BulkUpsertAsync(context, entities, configure).GetAwaiter().GetResult();

    /// <inheritdoc cref="BulkUpsert{T}(DbContext, IEnumerable{T}, Action{BulkMergeOptions}?)"/>
    public static Task<int> BulkUpsertAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default)
        where T : class
        => BulkUpsertAsync(context, entities, configure: null, cancellationToken);

    /// <inheritdoc cref="BulkUpsert{T}(DbContext, IEnumerable{T}, Action{BulkMergeOptions}?)"/>
    public static Task<int> BulkUpsertAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        Action<BulkMergeOptions>? configure,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        var options = new BulkMergeOptions
        {
            InsertWhenNotMatched = true,
            UpdateWhenMatched = true,
            DeleteWhenNotMatchedBySource = false
        };
        configure?.Invoke(options);

        var list = Materialize(entities);
        if (list.Count == 0)
            return Task.FromResult(0);

        return new BulkOperationExecutor(context, options).MergeAsync(list, cancellationToken);
    }

    /// <summary>
    /// Bulk-deletes entities matching the primary key (or <see cref="BulkOptions.KeyColumns"/>).
    /// Keys are staged into a temp table, then deleted with a set-based JOIN.
    /// </summary>
    public static int BulkDelete<T>(
        this DbContext context,
        IEnumerable<T> entities,
        Action<BulkOptions>? configure = null)
        where T : class
        => BulkDeleteAsync(context, entities, configure).GetAwaiter().GetResult();

    /// <inheritdoc cref="BulkDelete{T}(DbContext, IEnumerable{T}, Action{BulkOptions}?)"/>
    public static Task<int> BulkDeleteAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default)
        where T : class
        => BulkDeleteAsync(context, entities, configure: null, cancellationToken);

    /// <inheritdoc cref="BulkDelete{T}(DbContext, IEnumerable{T}, Action{BulkOptions}?)"/>
    public static Task<int> BulkDeleteAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        Action<BulkOptions>? configure,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var (list, options) = Prepare(context, entities, configure);
        if (list.Count == 0)
            return Task.FromResult(0);

        return new BulkOperationExecutor(context, options).DeleteAsync(list, cancellationToken);
    }

    /// <summary>
    /// Synchronizes the destination table with the source list using SQL MERGE:
    /// insert missing rows, update matched rows, and delete target rows that are not in the source.
    /// Pass a configure callback to disable delete (<see cref="BulkMergeOptions.DeleteWhenNotMatchedBySource"/>).
    /// </summary>
    /// <remarks>
    /// An empty source is rejected when delete is enabled, because MERGE would otherwise delete every row
    /// in the target table.
    /// </remarks>
    public static int BulkMerge<T>(
        this DbContext context,
        IEnumerable<T> entities,
        Action<BulkMergeOptions>? configure = null)
        where T : class
        => BulkMergeAsync(context, entities, configure).GetAwaiter().GetResult();

    /// <inheritdoc cref="BulkMerge{T}(DbContext, IEnumerable{T}, Action{BulkMergeOptions}?)"/>
    public static Task<int> BulkMergeAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default)
        where T : class
        => BulkMergeAsync(context, entities, configure: null, cancellationToken);

    /// <inheritdoc cref="BulkMerge{T}(DbContext, IEnumerable{T}, Action{BulkMergeOptions}?)"/>
    public static Task<int> BulkMergeAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        Action<BulkMergeOptions>? configure,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        var options = new BulkMergeOptions
        {
            InsertWhenNotMatched = true,
            UpdateWhenMatched = true,
            DeleteWhenNotMatchedBySource = true
        };
        configure?.Invoke(options);

        var list = Materialize(entities);
        if (list.Count == 0)
        {
            if (options.DeleteWhenNotMatchedBySource)
            {
                throw new InvalidOperationException(
                    "BulkMerge with DeleteWhenNotMatchedBySource cannot run against an empty source because it would delete every row in the target table.");
            }

            return Task.FromResult(0);
        }

        return new BulkOperationExecutor(context, options).MergeAsync(list, cancellationToken);
    }

    private static (IReadOnlyList<T> List, BulkOptions Options) Prepare<T>(
        DbContext context,
        IEnumerable<T> entities,
        Action<BulkOptions>? configure)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        var options = new BulkOptions();
        configure?.Invoke(options);
        return (Materialize(entities), options);
    }

    private static IReadOnlyList<T> Materialize<T>(IEnumerable<T> entities)
        => entities as IReadOnlyList<T> ?? entities.ToList();
}
