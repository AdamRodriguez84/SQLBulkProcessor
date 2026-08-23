namespace SQLBulkProcessor.Tests.Benchmark;

public class CatalogSeedTests
{
    [Fact]
    public void Create_BuildsDeterministicCatalogWithUniqueSkus()
    {
        var first = CatalogSeed.Create();
        var second = CatalogSeed.Create();

        Assert.Equal(CatalogSeed.DefaultRowCount, first.Count);
        Assert.Equal(first.Items.Select(x => x.Sku), second.Items.Select(x => x.Sku));
        Assert.Equal(first.Count, first.Items.Select(x => x.Sku).Distinct().Count());
        Assert.Equal(12, first.CategoryCount);
        Assert.Equal(20, first.BrandCount);
        Assert.Equal(8, first.WarehouseCount);
        Assert.True(first.ActiveCount > 0);
        Assert.True(first.DiscontinuedCount > 0);
        Assert.True(first.OutOfStockCount > 0);
        Assert.True(first.AverageDescriptionLength > 80);
        Assert.True(first.MinPrice > 0);
        Assert.True(first.MaxPrice > first.MinPrice);
        Assert.Contains(first.Items, item => item.DiscontinuedAt is not null);
        Assert.Contains(first.Items, item => item.Tags is not null);
        Assert.Contains(first.Items, item => item.Tags is null);
        Assert.All(first.Items, item => Assert.StartsWith("SKU-", item.Sku, StringComparison.Ordinal));
    }

    [Fact]
    public void Clone_CopiesValuesWithoutSharingInstances()
    {
        var catalog = CatalogSeed.Create(count: 10);
        var clone = CatalogSeed.Clone(catalog.Items);

        Assert.Equal(catalog.Items[0].Sku, clone[0].Sku);
        Assert.NotSame(catalog.Items[0], clone[0]);
        clone[0].Name = "changed";
        Assert.NotEqual("changed", catalog.Items[0].Name);
        Assert.Equal(0, clone[0].Id);
    }
}
