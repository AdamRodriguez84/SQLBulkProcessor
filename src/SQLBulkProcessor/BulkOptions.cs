namespace SQLBulkProcessor;

/// <summary>
/// Options that apply to every bulk operation.
/// </summary>
public class BulkOptions
{
    /// <summary>
    /// Number of rows copied per internal batch. 0 (default) sends all rows in a single batch.
    /// </summary>
    public int BatchSize { get; set; }

    /// <summary>
    /// Command and bulk-copy timeout in seconds. Use 0 for no timeout. Default is 60.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// When true, acquires a table lock during <c>SqlBulkCopy</c>. Faster for large loads. Default is true.
    /// </summary>
    public bool UseTableLock { get; set; } = true;

    /// <summary>
    /// When true, identity column values on the source entities are written to the destination.
    /// Requires <c>IDENTITY_INSERT</c> (handled automatically).
    /// </summary>
    public bool KeepIdentity { get; set; }

    /// <summary>
    /// When true, database-generated identity values are written back onto the source entities
    /// after insert / upsert / merge.
    /// </summary>
    public bool OutputIdentity { get; set; }

    /// <summary>
    /// When true, insert and update triggers fire during <c>SqlBulkCopy</c> into the destination table.
    /// MERGE / UPDATE / DELETE always fire triggers.
    /// </summary>
    public bool FireTriggers { get; set; }

    /// <summary>
    /// When true, check constraints while bulk copying into the destination table.
    /// </summary>
    public bool CheckConstraints { get; set; }

    /// <summary>
    /// Stream rows from the entity reader into <c>SqlBulkCopy</c> without buffering a DataTable. Default is true.
    /// </summary>
    public bool EnableStreaming { get; set; } = true;

    /// <summary>
    /// Property or column names that uniquely identify a row. Defaults to the entity primary key.
    /// </summary>
    public string[]? KeyColumns { get; set; }

    /// <summary>
    /// When set, only these property or column names are included (keys are always kept when required).
    /// </summary>
    public string[]? IncludeColumns { get; set; }

    /// <summary>
    /// Property or column names to skip.
    /// </summary>
    public string[]? ExcludeColumns { get; set; }

    /// <summary>
    /// How often (in rows) to raise <see cref="OnRowsCopied"/>. 0 uses ~10% of the source count.
    /// </summary>
    public int NotifyAfter { get; set; }

    /// <summary>
    /// Optional progress callback receiving the number of rows copied so far.
    /// </summary>
    public Action<long>? OnRowsCopied { get; set; }
}

/// <summary>
/// Options for upsert and merge operations.
/// </summary>
public class BulkMergeOptions : BulkOptions
{
    /// <summary>
    /// Insert rows that exist in the source but not in the target. Default is true.
    /// </summary>
    public bool InsertWhenNotMatched { get; set; } = true;

    /// <summary>
    /// Update rows that exist in both source and target. Default is true.
    /// </summary>
    public bool UpdateWhenMatched { get; set; } = true;

    /// <summary>
    /// Delete target rows that are not present in the source. Default is false.
    /// <see cref="DbContextBulkExtensions.BulkMerge{T}"/> turns this on unless you override it.
    /// </summary>
    public bool DeleteWhenNotMatchedBySource { get; set; }

    /// <summary>
    /// Apply <c>HOLDLOCK</c> on the MERGE target to reduce race conditions. Default is true.
    /// </summary>
    public bool UseHoldLock { get; set; } = true;
}
