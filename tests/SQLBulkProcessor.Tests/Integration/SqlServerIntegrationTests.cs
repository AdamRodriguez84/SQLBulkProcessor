using Microsoft.EntityFrameworkCore;
using SQLBulkProcessor;
using SQLBulkProcessor.Tests.Benchmark;

namespace SQLBulkProcessor.Tests.Integration;

[Collection("SqlServerIntegration")]
public class SqlServerIntegrationTests
{
    private readonly SqlServerFixture _fixture;

    public SqlServerIntegrationTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public async Task BulkInsert_WritesRowsAndSkipsIdentity()
    {
        await using var db = CreateDb();
        await ClearAsync(db);

        var items = new[]
        {
            Product("Alpha", 1.50m, ProductStatus.Active),
            Product("Beta", 2.25m, ProductStatus.Draft)
        };

        var affected = await db.BulkInsertAsync(items);

        Assert.Equal(2, affected);
        Assert.Equal(0, items[0].Id);
        Assert.Equal(0, items[1].Id);

        var stored = await db.Products.OrderBy(x => x.Name).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal("Alpha", stored[0].Name);
        Assert.Equal(1.50m, stored[0].Price);
        Assert.Equal(ProductStatus.Active, stored[0].Status);
        Assert.Equal("Beta", stored[1].Name);
        Assert.Equal(ProductStatus.Draft, stored[1].Status);
        Assert.True(stored[0].Id > 0);
        Assert.True(stored[1].Id > stored[0].Id);
        Assert.NotNull(stored[0].RowVersion);
        Assert.NotEmpty(stored[0].RowVersion!);
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public async Task BulkInsert_OutputIdentity_WritesIdsBackOntoEntities()
    {
        await using var db = CreateDb();
        await ClearAsync(db);

        var items = new[] { Product("One", 10m), Product("Two", 20m) };
        await db.BulkInsertAsync(items, o => o.OutputIdentity = true);

        Assert.True(items[0].Id > 0);
        Assert.True(items[1].Id > 0);
        Assert.NotEqual(items[0].Id, items[1].Id);

        var storedIds = await db.Products.OrderBy(x => x.Name).Select(x => x.Id).ToListAsync();
        Assert.Equal(new[] { items[0].Id, items[1].Id }.OrderBy(x => x), storedIds.OrderBy(x => x));
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public async Task BulkInsert_KeepIdentity_InsertsExplicitIds()
    {
        await using var db = CreateDb();
        await ClearAsync(db);

        var items = new[]
        {
            Product("KeptA", 1m),
            Product("KeptB", 2m)
        };
        items[0].Id = 50;
        items[1].Id = 60;

        await db.BulkInsertAsync(items, o => o.KeepIdentity = true);

        var stored = await db.Products.OrderBy(x => x.Id).Select(x => new { x.Id, x.Name }).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(50, stored[0].Id);
        Assert.Equal("KeptA", stored[0].Name);
        Assert.Equal(60, stored[1].Id);
        Assert.Equal("KeptB", stored[1].Name);
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public void BulkInsert_SyncOverload_WritesRows()
    {
        using var db = CreateDb();
        ClearAsync(db).GetAwaiter().GetResult();

        var affected = db.BulkInsert([Product("Sync", 9.99m)]);
        Assert.Equal(1, affected);
        Assert.Equal("Sync", db.Products.Single().Name);
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public async Task BulkUpdate_ChangesNonKeyColumnsByPrimaryKey()
    {
        await using var db = CreateDb();
        await ClearAsync(db);

        var items = new[] { Product("Alpha", 1m), Product("Beta", 2m) };
        await db.BulkInsertAsync(items, o => o.OutputIdentity = true);

        items[0].Price = 11.11m;
        items[0].Name = "AlphaUpdated";
        items[0].Status = ProductStatus.Discontinued;
        await db.BulkUpdateAsync(items);

        var stored = await db.Products.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(items[0].Id, stored[0].Id);
        Assert.Equal("AlphaUpdated", stored[0].Name);
        Assert.Equal(11.11m, stored[0].Price);
        Assert.Equal(ProductStatus.Discontinued, stored[0].Status);
        Assert.Equal("Beta", stored[1].Name);
        Assert.Equal(2m, stored[1].Price);
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public async Task BulkUpsert_InsertsMissingAndUpdatesMatchedByBusinessKey()
    {
        await using var db = CreateDb();
        await ClearAsync(db);

        await db.BulkInsertAsync([Product("Alpha", 1m), Product("Beta", 2m)]);

        var incoming = new[]
        {
            Product("Alpha", 99m, ProductStatus.Discontinued),
            Product("Gamma", 3m)
        };

        await db.BulkUpsertAsync(incoming, o => o.KeyColumns = ["Name"]);

        var stored = await db.Products.OrderBy(x => x.Name).ToListAsync();
        Assert.Equal(3, stored.Count);
        Assert.Equal("Alpha", stored[0].Name);
        Assert.Equal(99m, stored[0].Price);
        Assert.Equal(ProductStatus.Discontinued, stored[0].Status);
        Assert.Equal("Beta", stored[1].Name);
        Assert.Equal(2m, stored[1].Price);
        Assert.Equal("Gamma", stored[2].Name);
        Assert.Equal(3m, stored[2].Price);
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public async Task BulkDelete_RemovesRowsMatchingPrimaryKey()
    {
        await using var db = CreateDb();
        await ClearAsync(db);

        var items = new[] { Product("Keep", 1m), Product("Drop", 2m) };
        await db.BulkInsertAsync(items, o => o.OutputIdentity = true);

        await db.BulkDeleteAsync([items[1]]);

        var stored = await db.Products.ToListAsync();
        Assert.Single(stored);
        Assert.Equal("Keep", stored[0].Name);
        Assert.Equal(items[0].Id, stored[0].Id);
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public async Task BulkMerge_InsertsUpdatesAndDeletesUnmatchedTargetRows()
    {
        await using var db = CreateDb();
        await ClearAsync(db);

        await db.BulkInsertAsync(
        [
            Product("Alpha", 1m),
            Product("Beta", 2m),
            Product("Gamma", 3m)
        ]);

        var source = new[]
        {
            Product("Alpha", 10m, ProductStatus.Draft),
            Product("Delta", 4m)
        };

        await db.BulkMergeAsync(source, o => o.KeyColumns = ["Name"]);

        var stored = await db.Products.OrderBy(x => x.Name).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal("Alpha", stored[0].Name);
        Assert.Equal(10m, stored[0].Price);
        Assert.Equal(ProductStatus.Draft, stored[0].Status);
        Assert.Equal("Delta", stored[1].Name);
        Assert.Equal(4m, stored[1].Price);
        Assert.DoesNotContain(stored, x => x.Name is "Beta" or "Gamma");
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public async Task BulkInsert_EnlistsInEfTransaction_AndRollbackDiscardsRows()
    {
        await using var db = CreateDb();
        await ClearAsync(db);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.BulkInsertAsync([Product("Tx", 5m)]);
            Assert.Equal(1, await db.Products.CountAsync());
            await tx.RollbackAsync();
        }

        Assert.Equal(0, await db.Products.CountAsync());
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public async Task BulkInsert_EnlistsInEfTransaction_AndCommitPersistsRows()
    {
        await using var db = CreateDb();
        await ClearAsync(db);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.BulkInsertAsync([Product("Committed", 5m)]);
            await tx.CommitAsync();
        }

        Assert.Equal("Committed", (await db.Products.SingleAsync()).Name);
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public async Task CompositeKey_UpdateAndDelete_MatchBothKeyColumns()
    {
        await using var db = CreateDb();
        await ClearAsync(db);

        var lines = new[]
        {
            new OrderLine { OrderId = 10, LineNumber = 1, Sku = "A", Quantity = 1 },
            new OrderLine { OrderId = 10, LineNumber = 2, Sku = "B", Quantity = 2 },
            new OrderLine { OrderId = 11, LineNumber = 1, Sku = "C", Quantity = 3 }
        };
        await db.BulkInsertAsync(lines);

        lines[1].Quantity = 20;
        await db.BulkUpdateAsync([lines[1]]);

        await db.BulkDeleteAsync([lines[0]]);

        var stored = await db.OrderLines.OrderBy(x => x.OrderId).ThenBy(x => x.LineNumber).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(10, stored[0].OrderId);
        Assert.Equal(2, stored[0].LineNumber);
        Assert.Equal(20, stored[0].Quantity);
        Assert.Equal(11, stored[1].OrderId);
        Assert.Equal(1, stored[1].LineNumber);
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public async Task BulkInsert_MixedTphCollection_WritesDiscriminatorAndDerivedColumns()
    {
        await using var db = CreateDb();
        await ClearAsync(db);

        Animal[] animals =
        [
            new Dog { Name = "Rex", Breed = "Lab" },
            new Cat { Name = "Misty", Indoor = true }
        ];
        await db.BulkInsertAsync(animals);

        var dogs = await db.Set<Dog>().ToListAsync();
        var cats = await db.Set<Cat>().ToListAsync();
        Assert.Single(dogs);
        Assert.Equal("Rex", dogs[0].Name);
        Assert.Equal("Lab", dogs[0].Breed);
        Assert.Single(cats);
        Assert.Equal("Misty", cats[0].Name);
        Assert.True(cats[0].Indoor);
    }

    [SkippableFact(Timeout = 120_000)]
    [Trait("Category", "Integration")]
    public async Task BulkInsert_OwnedReference_WritesOwnedColumns()
    {
        await using var db = CreateDb();
        await ClearAsync(db);

        await db.BulkInsertAsync(
        [
            new Customer
            {
                Name = "Ada",
                Address = new Address { City = "London", Country = "UK" }
            }
        ]);

        var stored = await db.Customers.SingleAsync();
        Assert.Equal("Ada", stored.Name);
        Assert.Equal("London", stored.Address.City);
        Assert.Equal("UK", stored.Address.Country);
    }

    private TestDbContext CreateDb()
    {
        Skip.If(
            _fixture.ConnectionString is null,
            $"SQL Server is not available. Set {SqlServerProbe.ConnectionEnvironmentVariable} or install SQL Server / LocalDB.");
        return TestDbContext.Create(_fixture.ConnectionString);
    }

    private static Product Product(string name, decimal price, ProductStatus status = ProductStatus.Active)
        => new()
        {
            Name = name,
            Price = price,
            Status = status,
            UpdatedAt = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc)
        };

    private static Task ClearAsync(TestDbContext db)
        => db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE [dbo].[Products];
            TRUNCATE TABLE [dbo].[OrderLines];
            TRUNCATE TABLE [dbo].[Animals];
            TRUNCATE TABLE [dbo].[Customers];
            """);
}
