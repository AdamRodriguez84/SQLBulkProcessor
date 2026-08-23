using Microsoft.EntityFrameworkCore;

namespace SQLBulkProcessor.Tests.Benchmark;

/// <summary>
/// Typical EF Core ChangeTracker usage: AddRange / UpdateRange / RemoveRange / SaveChanges,
/// and load-then-mutate for upsert and merge. No bulk APIs.
/// </summary>
internal static class ChangeTrackerCatalogOps
{
    public static async Task InsertAsync(BenchmarkDbContext db, IReadOnlyList<CatalogItem> items)
    {
        using var tracking = TrackingScope.Begin(db);
        db.CatalogItems.AddRange(items);
        await db.SaveChangesAsync();
    }

    public static async Task UpdateAsync(BenchmarkDbContext db, IReadOnlyList<CatalogItem> items)
    {
        using var tracking = TrackingScope.Begin(db);
        db.CatalogItems.UpdateRange(items);
        await db.SaveChangesAsync();
    }

    public static async Task DeleteAsync(BenchmarkDbContext db, IReadOnlyList<CatalogItem> items)
    {
        using var tracking = TrackingScope.Begin(db);
        db.CatalogItems.RemoveRange(items);
        await db.SaveChangesAsync();
    }

    public static async Task UpsertAsync(BenchmarkDbContext db, IReadOnlyList<CatalogItem> incoming)
    {
        using var tracking = TrackingScope.Begin(db);
        var existing = await db.CatalogItems.ToDictionaryAsync(x => x.Sku);
        foreach (var item in incoming)
        {
            if (existing.TryGetValue(item.Sku, out var current))
            {
                item.Id = current.Id;
                db.Entry(current).CurrentValues.SetValues(item);
            }
            else
            {
                db.CatalogItems.Add(item);
            }
        }

        await db.SaveChangesAsync();
    }

    public static async Task MergeAsync(BenchmarkDbContext db, IReadOnlyList<CatalogItem> incoming)
    {
        using var tracking = TrackingScope.Begin(db);
        var existing = await db.CatalogItems.ToListAsync();
        var incomingBySku = incoming.ToDictionary(x => x.Sku, StringComparer.Ordinal);

        foreach (var current in existing)
        {
            if (incomingBySku.TryGetValue(current.Sku, out var updated))
            {
                updated.Id = current.Id;
                db.Entry(current).CurrentValues.SetValues(updated);
                incomingBySku.Remove(current.Sku);
            }
            else
            {
                db.CatalogItems.Remove(current);
            }
        }

        foreach (var added in incomingBySku.Values)
            db.CatalogItems.Add(added);

        await db.SaveChangesAsync();
    }

    private sealed class TrackingScope : IDisposable
    {
        private readonly BenchmarkDbContext _db;

        private TrackingScope(BenchmarkDbContext db) => _db = db;

        public static TrackingScope Begin(BenchmarkDbContext db)
        {
            db.ChangeTracker.Clear();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            db.ChangeTracker.AutoDetectChangesEnabled = true;
            return new TrackingScope(db);
        }

        public void Dispose()
        {
            _db.ChangeTracker.Clear();
            _db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }
    }
}
