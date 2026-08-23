using Microsoft.EntityFrameworkCore;
using SQLBulkProcessor;

namespace SQLBulkProcessor.Tests;

public class BulkApiGuardTests
{
    [Fact]
    public async Task EmptyInsert_ReturnsZeroWithoutConnecting()
    {
        using var db = TestDbContext.Create();
        var result = await db.BulkInsertAsync(Array.Empty<Product>());
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task EmptyUpdate_ReturnsZeroWithoutConnecting()
    {
        using var db = TestDbContext.Create();
        var result = await db.BulkUpdateAsync(Array.Empty<Product>());
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task EmptyUpsert_ReturnsZeroWithoutConnecting()
    {
        using var db = TestDbContext.Create();
        var result = await db.BulkUpsertAsync(Array.Empty<Product>());
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task EmptyDelete_ReturnsZeroWithoutConnecting()
    {
        using var db = TestDbContext.Create();
        var result = await db.BulkDeleteAsync(Array.Empty<Product>());
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task EmptyMerge_ThrowsToProtectTargetTable()
    {
        using var db = TestDbContext.Create();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.BulkMergeAsync(Array.Empty<Product>()));
        Assert.Contains("empty source", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyMerge_WithoutDelete_ReturnsZero()
    {
        using var db = TestDbContext.Create();
        var result = await db.BulkMergeAsync(Array.Empty<Product>(), o => o.DeleteWhenNotMatchedBySource = false);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task NullContext_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ((DbContext)null!).BulkInsertAsync(new List<Product> { new() }));
    }

    [Fact]
    public async Task NullEntities_Throws()
    {
        using var db = TestDbContext.Create();
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.BulkInsertAsync<Product>(null!));
    }

    [Fact]
    public async Task UnmappedEntity_ThrowsBeforeConnecting()
    {
        using var db = TestDbContext.Create();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.BulkInsertAsync([new UnmappedThing()]));
        Assert.Contains("not mapped", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
