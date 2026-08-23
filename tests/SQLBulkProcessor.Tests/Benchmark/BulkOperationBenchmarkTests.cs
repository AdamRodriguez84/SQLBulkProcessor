using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SQLBulkProcessor;
using Xunit.Abstractions;

namespace SQLBulkProcessor.Tests.Benchmark;

[Collection("SqlServerBenchmark")]
public class BulkOperationBenchmarkTests
{
    private const int BulkIterations = 3;
    private const int TrackerIterations = 1;
    private const int WarmupRows = 2_000;
    private readonly ITestOutputHelper _output;

    public BulkOperationBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact(Timeout = 600_000)]
    [Trait("Category", "Benchmark")]
    public Task Bulk_operations_on_seeded_catalog()
        => RunCatalogBenchmarkAsync(CatalogSeed.DefaultRowCount, commandTimeoutSeconds: 180);

    [SkippableFact(Timeout = 2_700_000)]
    [Trait("Category", "Benchmark")]
    [Trait("Size", "200k")]
    public Task Bulk_operations_on_seeded_catalog_200k()
        => RunCatalogBenchmarkAsync(CatalogSeed.LargeRowCount, commandTimeoutSeconds: 600);

    private async Task RunCatalogBenchmarkAsync(int rowCount, int commandTimeoutSeconds)
    {
        var connectionString = SqlServerProbe.ResolveConnectionString();
        Skip.If(
            connectionString is null,
            $"SQL Server is not available. Set {SqlServerProbe.ConnectionEnvironmentVariable} or install SQL Server / LocalDB.");

        var catalog = CatalogSeed.Create(rowCount);
        WriteDataset(catalog);

        await using var db = BenchmarkDbContext.Create(connectionString);
        db.Database.SetCommandTimeout(commandTimeoutSeconds);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        await WarmupAsync(db, commandTimeoutSeconds);

        var comparisons = new List<OperationComparison>
        {
            await CompareAsync(db, "Insert", catalog.Count,
                setup: () => TruncateAsync(db),
                runTracker: () => ChangeTrackerCatalogOps.InsertAsync(db, CatalogSeed.Clone(catalog.Items)),
                runBulk: () => db.BulkInsertAsync(CatalogSeed.Clone(catalog.Items), o => Configure(o, commandTimeoutSeconds))),

            await CompareAsync(db, "Update", catalog.Count,
                setup: async () =>
                {
                    var rows = await ReloadWithIdsAsync(db, catalog.Items, commandTimeoutSeconds);
                    CatalogSeed.MutateForUpdate(rows);
                    return rows;
                },
                runTracker: rows => ChangeTrackerCatalogOps.UpdateAsync(db, rows),
                runBulk: rows => db.BulkUpdateAsync(rows, o => Configure(o, commandTimeoutSeconds))),

            await CompareAsync(db, "Upsert (50/50)", catalog.Count,
                setup: async () =>
                {
                    var existing = catalog.Items.Take(catalog.Count / 2).ToList();
                    await ReloadWithIdsAsync(db, existing, commandTimeoutSeconds);
                    return BuildUpsertSource(catalog);
                },
                runTracker: incoming => ChangeTrackerCatalogOps.UpsertAsync(db, incoming),
                runBulk: incoming => db.BulkUpsertAsync(incoming, options =>
                {
                    Configure(options, commandTimeoutSeconds);
                    options.KeyColumns = ["Sku"];
                })),

            await CompareAsync(db, "Delete", catalog.Count,
                setup: () => ReloadWithIdsAsync(db, catalog.Items, commandTimeoutSeconds),
                runTracker: rows => ChangeTrackerCatalogOps.DeleteAsync(db, rows),
                runBulk: rows => db.BulkDeleteAsync(rows, o => Configure(o, commandTimeoutSeconds))),

            await CompareAsync(db, "Merge (sync)", catalog.Count,
                setup: async () =>
                {
                    await ReloadWithIdsAsync(db, catalog.Items, commandTimeoutSeconds);
                    return BuildMergeSource(catalog);
                },
                runTracker: source => ChangeTrackerCatalogOps.MergeAsync(db, source),
                runBulk: source => db.BulkMergeAsync(source, options =>
                {
                    Configure(options, commandTimeoutSeconds);
                    options.KeyColumns = ["Sku"];
                    options.DeleteWhenNotMatchedBySource = true;
                }))
        };

        var table = FormatComparisonTable(comparisons);
        _output.WriteLine(table);
        Console.WriteLine(table);

        Assert.All(comparisons, pair =>
        {
            Assert.True(pair.ChangeTracker.Mean > TimeSpan.Zero);
            Assert.True(pair.Bulk.Mean > TimeSpan.Zero);
            Assert.True(
                pair.Bulk.Mean < pair.ChangeTracker.Mean,
                $"{pair.Operation}: bulk ({Format(pair.Bulk.Mean)}) should be faster than ChangeTracker ({Format(pair.ChangeTracker.Mean)}).");
        });
    }

    private static void Configure(BulkOptions options, int timeoutSeconds)
    {
        options.BatchSize = timeoutSeconds >= 600 ? 8_000 : 4_000;
        options.TimeoutSeconds = timeoutSeconds;
        options.UseTableLock = true;
    }

    private static async Task WarmupAsync(BenchmarkDbContext db, int timeoutSeconds)
    {
        var warmup = CatalogSeed.Create(WarmupRows, seed: 7);

        await TruncateAsync(db);
        await ChangeTrackerCatalogOps.InsertAsync(db, CatalogSeed.Clone(warmup.Items));

        var inserted = await ReloadWithIdsAsync(db, warmup.Items, timeoutSeconds);
        CatalogSeed.MutateForUpdate(inserted);
        await ChangeTrackerCatalogOps.UpdateAsync(db, CatalogSeed.Clone(inserted, includeIds: true));
        await db.BulkUpdateAsync(inserted, o => Configure(o, timeoutSeconds));

        await TruncateAsync(db);
        await ReloadWithIdsAsync(db, warmup.Items.Take(WarmupRows / 2).ToList(), timeoutSeconds);
        var upsertSource = CatalogSeed.Clone(warmup.Items);
        CatalogSeed.MutateForUpdate(upsertSource.Take(WarmupRows / 2).ToList());
        await ChangeTrackerCatalogOps.UpsertAsync(db, upsertSource);

        inserted = await ReloadWithIdsAsync(db, warmup.Items, timeoutSeconds);
        await ChangeTrackerCatalogOps.DeleteAsync(db, CatalogSeed.Clone(inserted, includeIds: true));

        inserted = await ReloadWithIdsAsync(db, warmup.Items, timeoutSeconds);
        await db.BulkDeleteAsync(inserted, o => Configure(o, timeoutSeconds));
        await TruncateAsync(db);
    }

    private static async Task<List<CatalogItem>> ReloadWithIdsAsync(
        BenchmarkDbContext db,
        IReadOnlyList<CatalogItem> template,
        int timeoutSeconds)
    {
        await TruncateAsync(db);
        var rows = CatalogSeed.Clone(template);
        CatalogSeed.AssignSequentialIds(rows);
        await db.BulkInsertAsync(rows, options =>
        {
            Configure(options, timeoutSeconds);
            options.KeepIdentity = true;
        });
        await db.Database.CloseConnectionAsync();
        return rows;
    }

    private static List<CatalogItem> BuildUpsertSource(SeededCatalog catalog)
    {
        var half = catalog.Count / 2;
        var existing = CatalogSeed.Clone(catalog.Items.Take(half).ToList());
        CatalogSeed.MutateForUpdate(existing);
        var fresh = CatalogSeed.Clone(CatalogSeed.Create(catalog.Count - half, seed: 99, skuOffset: 8_000_000).Items);
        return existing.Concat(fresh).ToList();
    }

    private static List<CatalogItem> BuildMergeSource(SeededCatalog catalog)
    {
        var keep = (int)(catalog.Count * 0.8);
        var insertCount = catalog.Count - keep;
        var kept = CatalogSeed.Clone(catalog.Items.Take(keep).ToList());
        CatalogSeed.MutateForUpdate(kept);
        var inserted = CatalogSeed.Clone(CatalogSeed.Create(insertCount, seed: 123, skuOffset: 9_000_000).Items);
        return kept.Concat(inserted).ToList();
    }

    private static Task TruncateAsync(BenchmarkDbContext db)
        => db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE [dbo].[CatalogItems];");

    private async Task<OperationComparison> CompareAsync(
        BenchmarkDbContext db,
        string operation,
        int rows,
        Func<Task> setup,
        Func<Task> runTracker,
        Func<Task> runBulk)
        => await CompareAsync(db, operation, rows, async () =>
        {
            await setup();
            return 0;
        }, _ => runTracker(), _ => runBulk());

    private async Task<OperationComparison> CompareAsync<TState>(
        BenchmarkDbContext db,
        string operation,
        int rows,
        Func<Task<TState>> setup,
        Func<TState, Task> runTracker,
        Func<TState, Task> runBulk)
    {
        var tracker = await MeasureNamedAsync(
            $"ChangeTracker {operation}",
            rows,
            TrackerIterations,
            async () =>
            {
                var state = await setup();
                return async () => await runTracker(state);
            });

        var bulk = await MeasureNamedAsync(
            $"Bulk {operation}",
            rows,
            BulkIterations,
            async () =>
            {
                var state = await setup();
                return async () => await runBulk(state);
            });

        _output.WriteLine($"{operation} speedup: {bulk.RowsPerSecond / Math.Max(tracker.RowsPerSecond, 0.0001):0.0}x vs ChangeTracker");
        Console.WriteLine($"{operation} speedup: {bulk.RowsPerSecond / Math.Max(tracker.RowsPerSecond, 0.0001):0.0}x vs ChangeTracker");
        await db.Database.CloseConnectionAsync();
        return new OperationComparison(operation, tracker, bulk);
    }

    private async Task<BenchmarkResult> MeasureNamedAsync(
        string name,
        int rows,
        int iterations,
        Func<Task<Func<Task>>> prepareIteration)
    {
        _output.WriteLine($"Starting {name}...");
        Console.WriteLine($"Starting {name}...");

        var samples = new TimeSpan[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var action = await prepareIteration();
            var sw = Stopwatch.StartNew();
            await action();
            sw.Stop();
            samples[i] = sw.Elapsed;
        }

        var meanTicks = samples.Average(x => x.Ticks);
        var result = new BenchmarkResult(
            name,
            rows,
            TimeSpan.FromTicks((long)meanTicks),
            samples.Min(),
            samples.Max());

        _output.WriteLine($"Finished {name}: mean={Format(result.Mean)} ({result.RowsPerSecond:N0} rows/sec)");
        Console.WriteLine($"Finished {name}: mean={Format(result.Mean)} ({result.RowsPerSecond:N0} rows/sec)");
        return result;
    }

    private void WriteDataset(SeededCatalog catalog)
    {
        _output.WriteLine(
            $"Seeded catalog: {catalog.Count:N0} rows, {catalog.CategoryCount} categories, {catalog.BrandCount} brands, {catalog.WarehouseCount} warehouses");
        _output.WriteLine(
            $"Active={catalog.ActiveCount:N0} discontinued={catalog.DiscontinuedCount:N0} out-of-stock={catalog.OutOfStockCount:N0} avg description={catalog.AverageDescriptionLength:F1} chars price={catalog.MinPrice:C}-{catalog.MaxPrice:C}");
    }

    private static string FormatComparisonTable(IReadOnlyList<OperationComparison> comparisons)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine(
            $"SQLBulkProcessor vs EF ChangeTracker  rows={comparisons[0].Bulk.Rows:N0}  bulk iterations={BulkIterations}  tracker iterations={TrackerIterations}  warmup={WarmupRows:N0}");
        sb.AppendLine(new string('-', 108));
        sb.AppendLine(
            $"{"Operation",-16} {"Rows",10} {"ChangeTracker",16} {"Tracker/sec",12} {"Bulk",14} {"Bulk/sec",12} {"Speedup",10}");
        sb.AppendLine(new string('-', 108));
        foreach (var pair in comparisons)
        {
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{pair.Operation,-16} {pair.Bulk.Rows,10:N0} {Format(pair.ChangeTracker.Mean),16} {pair.ChangeTracker.RowsPerSecond,12:N0} {Format(pair.Bulk.Mean),14} {pair.Bulk.RowsPerSecond,12:N0} {pair.Speedup,9:0.0}x"));
        }

        sb.AppendLine(new string('-', 108));
        return sb.ToString();
    }

    private static string Format(TimeSpan value)
        => value.TotalSeconds >= 1
            ? $"{value.TotalSeconds:0.000} s"
            : $"{value.TotalMilliseconds:0.0} ms";

    private sealed record BenchmarkResult(string Name, int Rows, TimeSpan Mean, TimeSpan Min, TimeSpan Max)
    {
        public double RowsPerSecond => Rows / Math.Max(Mean.TotalSeconds, 0.0001);
    }

    private sealed record OperationComparison(string Operation, BenchmarkResult ChangeTracker, BenchmarkResult Bulk)
    {
        public double Speedup => ChangeTracker.Mean.TotalSeconds / Math.Max(Bulk.Mean.TotalSeconds, 0.0001);
    }
}

[CollectionDefinition("SqlServerBenchmark", DisableParallelization = true)]
public sealed class SqlServerBenchmarkCollection
{
}
