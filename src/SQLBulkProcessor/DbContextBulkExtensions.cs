using SQLBulkProcessor.Internal;

namespace SQLBulkProcessor;

/// <summary>
/// High-performance bulk Insert, Update, Upsert, Delete, and Merge extensions for EF Core
/// <see cref="DbContext"/> instances backed by SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// Each call maps the entity type from the current EF Core model and writes a single table with
/// <c>SqlBulkCopy</c> and set-based SQL. Identity, computed, rowversion, and temporal period columns are
/// skipped unless you opt in (see <see cref="BulkOptions.KeepIdentity"/>). Owned reference types that share the
/// owner's table are included. Other navigations (including collections and related entities such as
/// <c>Order.Customer</c>) are ignored; foreign-key scalar columns are written as-is.
/// </para>
/// <para>
/// These methods do not use the change tracker. Entities are not attached, and related graphs are not inserted.
/// Insert principals first, copy generated keys onto dependents (use <see cref="BulkOptions.OutputIdentity"/>),
/// then insert children.
/// </para>
/// <para>
/// Operations enlist in the current EF Core transaction when one is present. Empty sequences return <c>0</c>
/// without connecting, except <see cref="BulkMerge{T}"/> with delete enabled, which throws to avoid wiping the
/// target table.
/// </para>
/// <para>
/// Requires a SQL Server connection (<c>Microsoft.Data.SqlClient.SqlConnection</c>).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// await db.BulkInsertAsync(products);
/// await db.BulkUpdateAsync(products);
/// await db.BulkUpsertAsync(products, o => o.KeyColumns = ["Sku"]);
/// await db.BulkDeleteAsync(products);
/// await db.BulkMergeAsync(products);
/// </code>
/// </example>
public static class DbContextBulkExtensions
{
    /// <summary>
    /// Bulk-inserts entities into the mapped SQL Server table using <c>SqlBulkCopy</c>.
    /// </summary>
    /// <typeparam name="T">The mapped entity type. Must not be an owned type.</typeparam>
    /// <param name="context">The EF Core context that owns the SQL Server connection and model.</param>
    /// <param name="entities">Entities to insert. Enumerated once and materialized if not already a list.</param>
    /// <param name="configure">
    /// Optional callback to set <see cref="BulkOptions"/>. Use <see cref="BulkOptions.OutputIdentity"/> to copy
    /// generated identity values back onto <paramref name="entities"/>, or <see cref="BulkOptions.KeepIdentity"/>
    /// to insert explicit key values (<c>IDENTITY_INSERT</c> is enabled automatically).
    /// </param>
    /// <returns>The number of source entities written (0 when <paramref name="entities"/> is empty).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="entities"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not mapped to a table, or the connection is not SQL Server.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Identity, computed, rowversion, and temporal period columns are omitted unless
    /// <see cref="BulkOptions.KeepIdentity"/> is set. Related entities on navigation properties are not inserted;
    /// only columns of <typeparamref name="T"/>'s table are sent. <c>SqlBulkCopy</c> does not check foreign keys
    /// unless <see cref="BulkOptions.CheckConstraints"/> is true.
    /// </para>
    /// <para>
    /// This synchronous overload blocks on the async pipeline. Prefer
    /// <see cref="BulkInsertAsync{T}(DbContext, IEnumerable{T}, Action{BulkOptions}?, CancellationToken)"/> in
    /// ASP.NET and other async contexts.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var rows = db.BulkInsert(products, o =>
    /// {
    ///     o.BatchSize = 5_000;
    ///     o.OutputIdentity = true;
    /// });
    /// </code>
    /// </example>
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

    /// <summary>
    /// Asynchronously bulk-inserts entities into the mapped SQL Server table using <c>SqlBulkCopy</c>.
    /// </summary>
    /// <typeparam name="T">The mapped entity type. Must not be an owned type.</typeparam>
    /// <param name="context">The EF Core context that owns the SQL Server connection and model.</param>
    /// <param name="entities">Entities to insert. Enumerated once and materialized if not already a list.</param>
    /// <param name="configure">
    /// Optional callback to set <see cref="BulkOptions"/>. Use <see cref="BulkOptions.OutputIdentity"/> to copy
    /// generated identity values back onto <paramref name="entities"/>, or <see cref="BulkOptions.KeepIdentity"/>
    /// to insert explicit key values (<c>IDENTITY_INSERT</c> is enabled automatically).
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the bulk copy.</param>
    /// <returns>
    /// A task that produces the number of source entities written (0 when <paramref name="entities"/> is empty).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="entities"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not mapped to a table, or the connection is not SQL Server.
    /// </exception>
    /// <remarks>
    /// Identity, computed, rowversion, and temporal period columns are omitted unless
    /// <see cref="BulkOptions.KeepIdentity"/> is set. Navigation properties are not followed. Enable
    /// <see cref="BulkOptions.CheckConstraints"/> if foreign-key violations should fail the insert.
    /// </remarks>
    /// <example>
    /// <code>
    /// await db.BulkInsertAsync(products, o =>
    /// {
    ///     o.OutputIdentity = true;
    ///     o.ExcludeColumns = ["CreatedAt"];
    /// });
    /// </code>
    /// </example>
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
    /// Bulk-updates existing rows by primary key (or <see cref="BulkOptions.KeyColumns"/>).
    /// </summary>
    /// <typeparam name="T">The mapped entity type. Must not be an owned type.</typeparam>
    /// <param name="context">The EF Core context that owns the SQL Server connection and model.</param>
    /// <param name="entities">Entities whose non-key columns should be written to matching target rows.</param>
    /// <param name="configure">
    /// Optional callback to set <see cref="BulkOptions"/>. Set <see cref="BulkOptions.KeyColumns"/> to match on a
    /// business key instead of the primary key. <see cref="BulkOptions.IncludeColumns"/> and
    /// <see cref="BulkOptions.ExcludeColumns"/> limit which non-key columns are updated.
    /// </param>
    /// <returns>
    /// Rows reported as affected by the set-based <c>UPDATE</c> (0 when <paramref name="entities"/> is empty).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="entities"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not mapped, has no key columns, or the connection is not SQL Server.
    /// </exception>
    /// <remarks>
    /// Rows are staged into a session-scoped temp table with <c>SqlBulkCopy</c>, then applied with
    /// <c>UPDATE ... FROM ... INNER JOIN</c>. Identity, computed, rowversion, and period columns are not updated.
    /// Source entities must already have key values; unmatched keys are skipped (no insert).
    /// Prefer
    /// <see cref="BulkUpdateAsync{T}(DbContext, IEnumerable{T}, Action{BulkOptions}?, CancellationToken)"/> in
    /// async contexts.
    /// </remarks>
    /// <example>
    /// <code>
    /// products.ForEach(p => p.Price += 1);
    /// db.BulkUpdate(products);
    /// </code>
    /// </example>
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

    /// <summary>
    /// Asynchronously bulk-updates existing rows by primary key (or <see cref="BulkOptions.KeyColumns"/>).
    /// </summary>
    /// <typeparam name="T">The mapped entity type. Must not be an owned type.</typeparam>
    /// <param name="context">The EF Core context that owns the SQL Server connection and model.</param>
    /// <param name="entities">Entities whose non-key columns should be written to matching target rows.</param>
    /// <param name="configure">
    /// Optional callback to set <see cref="BulkOptions"/>. Set <see cref="BulkOptions.KeyColumns"/> to match on a
    /// business key instead of the primary key.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel staging and the <c>UPDATE</c>.</param>
    /// <returns>
    /// A task that produces the rows affected by the set-based <c>UPDATE</c>
    /// (0 when <paramref name="entities"/> is empty).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="entities"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not mapped, has no key columns, or the connection is not SQL Server.
    /// </exception>
    /// <remarks>
    /// Rows are staged into a temp table via <c>SqlBulkCopy</c>, then updated with a set-based JOIN.
    /// Unmatched keys are not inserted; use <see cref="BulkUpsertAsync{T}(DbContext, IEnumerable{T}, Action{BulkMergeOptions}?, CancellationToken)"/>
    /// for insert-or-update.
    /// </remarks>
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
    /// Inserts rows that do not exist and updates rows that do (SQL <c>MERGE</c> without delete).
    /// </summary>
    /// <typeparam name="T">The mapped entity type. Must not be an owned type.</typeparam>
    /// <param name="context">The EF Core context that owns the SQL Server connection and model.</param>
    /// <param name="entities">
    /// Source rows to merge. Must be unique on the match key or SQL Server <c>MERGE</c> will fail.
    /// </param>
    /// <param name="configure">
    /// Optional callback to set <see cref="BulkMergeOptions"/>. Defaults are insert-when-not-matched and
    /// update-when-matched, with <see cref="BulkMergeOptions.DeleteWhenNotMatchedBySource"/> off.
    /// Set <see cref="BulkOptions.KeyColumns"/> to match on a business key (for example <c>Sku</c>).
    /// </param>
    /// <returns>
    /// Rows reported as affected by <c>MERGE</c> (0 when <paramref name="entities"/> is empty).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="entities"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not mapped, has no key columns, or the connection is not SQL Server.
    /// </exception>
    /// <remarks>
    /// Matching uses the primary key or <see cref="BulkOptions.KeyColumns"/>. Target rows that are not in the
    /// source are left unchanged. For a full table sync that also deletes unmatched target rows, use
    /// <see cref="BulkMerge{T}"/>. Prefer
    /// <see cref="BulkUpsertAsync{T}(DbContext, IEnumerable{T}, Action{BulkMergeOptions}?, CancellationToken)"/>
    /// in async contexts.
    /// </remarks>
    /// <example>
    /// <code>
    /// db.BulkUpsert(products, o =>
    /// {
    ///     o.KeyColumns = ["Sku"];
    ///     o.OutputIdentity = true;
    /// });
    /// </code>
    /// </example>
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

    /// <summary>
    /// Asynchronously inserts rows that do not exist and updates rows that do (SQL <c>MERGE</c> without delete).
    /// </summary>
    /// <typeparam name="T">The mapped entity type. Must not be an owned type.</typeparam>
    /// <param name="context">The EF Core context that owns the SQL Server connection and model.</param>
    /// <param name="entities">
    /// Source rows to merge. Must be unique on the match key or SQL Server <c>MERGE</c> will fail.
    /// </param>
    /// <param name="configure">
    /// Optional callback to set <see cref="BulkMergeOptions"/>. Defaults are insert-when-not-matched and
    /// update-when-matched, with delete-when-not-matched-by-source off.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel staging and the <c>MERGE</c>.</param>
    /// <returns>
    /// A task that produces the rows affected by <c>MERGE</c> (0 when <paramref name="entities"/> is empty).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="entities"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not mapped, has no key columns, or the connection is not SQL Server.
    /// </exception>
    /// <remarks>
    /// Does not delete target rows that are absent from <paramref name="entities"/>. Use
    /// <see cref="BulkMergeAsync{T}(DbContext, IEnumerable{T}, Action{BulkMergeOptions}?, CancellationToken)"/>
    /// for a synchronizing merge.
    /// </remarks>
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
    /// Bulk-deletes rows whose keys match the source entities.
    /// </summary>
    /// <typeparam name="T">The mapped entity type. Must not be an owned type.</typeparam>
    /// <param name="context">The EF Core context that owns the SQL Server connection and model.</param>
    /// <param name="entities">
    /// Entities providing match keys. Only key columns are staged; other properties are ignored.
    /// </param>
    /// <param name="configure">
    /// Optional callback to set <see cref="BulkOptions"/>. Set <see cref="BulkOptions.KeyColumns"/> to delete by a
    /// business key instead of the primary key.
    /// </param>
    /// <returns>
    /// Rows reported as affected by the set-based <c>DELETE</c> (0 when <paramref name="entities"/> is empty).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="entities"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not mapped, has no key columns, or the connection is not SQL Server.
    /// </exception>
    /// <remarks>
    /// Keys are staged into a temp table, then deleted with <c>DELETE ... FROM ... INNER JOIN</c>. Related rows in
    /// other tables are not deleted unless the database enforces <c>ON DELETE CASCADE</c>. This method does not
    /// follow navigations. Prefer
    /// <see cref="BulkDeleteAsync{T}(DbContext, IEnumerable{T}, Action{BulkOptions}?, CancellationToken)"/> in
    /// async contexts.
    /// </remarks>
    /// <example>
    /// <code>
    /// db.BulkDelete(expiredOrders);
    /// </code>
    /// </example>
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

    /// <summary>
    /// Asynchronously bulk-deletes rows whose keys match the source entities.
    /// </summary>
    /// <typeparam name="T">The mapped entity type. Must not be an owned type.</typeparam>
    /// <param name="context">The EF Core context that owns the SQL Server connection and model.</param>
    /// <param name="entities">
    /// Entities providing match keys. Only key columns are staged; other properties are ignored.
    /// </param>
    /// <param name="configure">
    /// Optional callback to set <see cref="BulkOptions"/>. Set <see cref="BulkOptions.KeyColumns"/> to delete by a
    /// business key instead of the primary key.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel staging and the <c>DELETE</c>.</param>
    /// <returns>
    /// A task that produces the rows affected by the set-based <c>DELETE</c>
    /// (0 when <paramref name="entities"/> is empty).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="entities"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not mapped, has no key columns, or the connection is not SQL Server.
    /// </exception>
    /// <remarks>
    /// Does not cascade through EF navigations. Database-level cascade rules still apply.
    /// </remarks>
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
    /// Synchronizes the destination table with the source list using SQL <c>MERGE</c>:
    /// insert missing rows, update matched rows, and delete target rows that are not in the source.
    /// </summary>
    /// <typeparam name="T">The mapped entity type. Must not be an owned type.</typeparam>
    /// <param name="context">The EF Core context that owns the SQL Server connection and model.</param>
    /// <param name="entities">
    /// The desired contents of the target table (for the match key). Must be unique on that key.
    /// An empty sequence is rejected while delete is enabled.
    /// </param>
    /// <param name="configure">
    /// Optional callback to set <see cref="BulkMergeOptions"/>. Delete-when-not-matched-by-source defaults to
    /// <see langword="true"/> for this method. Set it to <see langword="false"/> or use <see cref="BulkUpsert{T}"/>
    /// to merge without deleting unmatched target rows.
    /// </param>
    /// <returns>Rows reported as affected by <c>MERGE</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="entities"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="entities"/> is empty while <see cref="BulkMergeOptions.DeleteWhenNotMatchedBySource"/> is
    /// true (that would delete every target row), <typeparamref name="T"/> is not mapped, or the connection is not
    /// SQL Server.
    /// </exception>
    /// <remarks>
    /// This is a table sync, not a graph sync. Rows in other tables are unchanged except through database cascade
    /// rules. Source keys must be unique. Prefer
    /// <see cref="BulkMergeAsync{T}(DbContext, IEnumerable{T}, Action{BulkMergeOptions}?, CancellationToken)"/>
    /// in async contexts.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Replace the catalog with this set: insert/update listed SKUs and delete the rest.
    /// db.BulkMerge(products, o => o.KeyColumns = ["Sku"]);
    /// </code>
    /// </example>
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

    /// <summary>
    /// Asynchronously synchronizes the destination table with the source list using SQL <c>MERGE</c>:
    /// insert missing rows, update matched rows, and delete target rows that are not in the source.
    /// </summary>
    /// <typeparam name="T">The mapped entity type. Must not be an owned type.</typeparam>
    /// <param name="context">The EF Core context that owns the SQL Server connection and model.</param>
    /// <param name="entities">
    /// The desired contents of the target table (for the match key). Must be unique on that key.
    /// An empty sequence is rejected while delete is enabled.
    /// </param>
    /// <param name="configure">
    /// Optional callback to set <see cref="BulkMergeOptions"/>. Delete-when-not-matched-by-source defaults to
    /// <see langword="true"/> for this method.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel staging and the <c>MERGE</c>.</param>
    /// <returns>A task that produces the rows affected by <c>MERGE</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="entities"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="entities"/> is empty while <see cref="BulkMergeOptions.DeleteWhenNotMatchedBySource"/> is
    /// true, <typeparamref name="T"/> is not mapped, or the connection is not SQL Server.
    /// </exception>
    /// <remarks>
    /// Pass a configure callback to disable delete (<see cref="BulkMergeOptions.DeleteWhenNotMatchedBySource"/>)
    /// if you only want insert and update. An empty source with delete enabled is rejected so MERGE cannot wipe
    /// the target table.
    /// </remarks>
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
