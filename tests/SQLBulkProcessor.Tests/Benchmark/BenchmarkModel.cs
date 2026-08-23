using Microsoft.EntityFrameworkCore;

namespace SQLBulkProcessor.Tests.Benchmark;

public class CatalogItem
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string WarehouseCode { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Cost { get; set; }
    public int QuantityOnHand { get; set; }
    public int ReorderLevel { get; set; }
    public double WeightKg { get; set; }
    public double Rating { get; set; }
    public bool IsActive { get; set; }
    public ProductStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DiscontinuedAt { get; set; }
    public string? Tags { get; set; }
}

public sealed class SeededCatalog
{
    public required IReadOnlyList<CatalogItem> Items { get; init; }
    public int Count => Items.Count;
    public int CategoryCount { get; init; }
    public int BrandCount { get; init; }
    public int WarehouseCount { get; init; }
    public int ActiveCount { get; init; }
    public int DiscontinuedCount { get; init; }
    public int OutOfStockCount { get; init; }
    public double AverageDescriptionLength { get; init; }
    public decimal MinPrice { get; init; }
    public decimal MaxPrice { get; init; }
}

public static class CatalogSeed
{
    public const int DefaultRowCount = 25_000;
    public const int LargeRowCount = 200_000;
    public const int DefaultSeed = 42;

    private static readonly string[] Categories =
    [
        "Electronics", "Home", "Outdoor", "Beauty", "Grocery", "Sporting",
        "Automotive", "Toys", "Office", "Health", "Garden", "Pets"
    ];

    private static readonly string[] Brands =
    [
        "Northwind", "Contoso", "Fabrikam", "AdventureWorks", "WideWorld",
        "Litware", "Tailspin", "BlueYonder", "Woodgrove", "Proseware",
        "Alpine", "Humongous", "Lucerne", "NodPublishers", "GraphicDesign",
        "Coho", "ADatum", "ThePhoneCompany", "Southridge", "Consolidated"
    ];

    private static readonly string[] Warehouses =
    [
        "US-EAST", "US-WEST", "US-CENT", "EU-WEST", "EU-NORTH", "APAC-1", "APAC-2", "LATAM-1"
    ];

    private static readonly string[] TagPool =
    [
        "bestseller", "clearance", "seasonal", "eco", "bundle", "limited", "online-only", "warehouse"
    ];

    public static SeededCatalog Create(int count = DefaultRowCount, int seed = DefaultSeed, int skuOffset = 0)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        var rng = new Random(seed);
        var items = new CatalogItem[count];
        var created = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < count; i++)
            items[i] = CreateItem(i + skuOffset, rng, created);

        return new SeededCatalog
        {
            Items = items,
            CategoryCount = items.Select(x => x.Category).Distinct().Count(),
            BrandCount = items.Select(x => x.Brand).Distinct().Count(),
            WarehouseCount = items.Select(x => x.WarehouseCode).Distinct().Count(),
            ActiveCount = items.Count(x => x.IsActive),
            DiscontinuedCount = items.Count(x => x.Status == ProductStatus.Discontinued),
            OutOfStockCount = items.Count(x => x.QuantityOnHand == 0),
            AverageDescriptionLength = items.Average(x => x.Description.Length),
            MinPrice = items.Min(x => x.Price),
            MaxPrice = items.Max(x => x.Price)
        };
    }

    public static List<CatalogItem> Clone(IReadOnlyList<CatalogItem> source, bool includeIds = false)
    {
        var copy = new List<CatalogItem>(source.Count);
        foreach (var item in source)
        {
            copy.Add(new CatalogItem
            {
                Id = includeIds ? item.Id : 0,
                Sku = item.Sku,
                Name = item.Name,
                Description = item.Description,
                Category = item.Category,
                Brand = item.Brand,
                WarehouseCode = item.WarehouseCode,
                Price = item.Price,
                Cost = item.Cost,
                QuantityOnHand = item.QuantityOnHand,
                ReorderLevel = item.ReorderLevel,
                WeightKg = item.WeightKg,
                Rating = item.Rating,
                IsActive = item.IsActive,
                Status = item.Status,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
                DiscontinuedAt = item.DiscontinuedAt,
                Tags = item.Tags
            });
        }

        return copy;
    }

    public static void AssignSequentialIds(IList<CatalogItem> items, int startId = 1)
    {
        for (var i = 0; i < items.Count; i++)
            items[i].Id = startId + i;
    }

    public static void MutateForUpdate(IEnumerable<CatalogItem> items)
    {
        foreach (var item in items)
        {
            item.Price = decimal.Round(item.Price * 1.015m, 2);
            item.QuantityOnHand = Math.Max(0, item.QuantityOnHand - 3);
            item.Rating = Math.Min(5d, Math.Round(item.Rating + 0.05, 2));
            item.UpdatedAt = item.UpdatedAt.AddHours(1);
            item.IsActive = item.QuantityOnHand > 0 && item.Status != ProductStatus.Discontinued;
            item.Tags = string.IsNullOrEmpty(item.Tags) ? "updated" : item.Tags + ",updated";
        }
    }

    private static CatalogItem CreateItem(int index, Random rng, DateTime createdBase)
    {
        var skuNumber = index + 1;
        var category = Categories[WeightedCategory(rng)];
        var brand = Brands[rng.Next(Brands.Length)];
        var statusRoll = rng.Next(100);
        var status = statusRoll switch
        {
            < 70 => ProductStatus.Active,
            < 90 => ProductStatus.Draft,
            _ => ProductStatus.Discontinued
        };

        var quantity = rng.Next(100) < 8 ? 0 : rng.Next(1, 2500);
        var price = NextPrice(rng);
        var createdAt = createdBase.AddMinutes(index % (60 * 24 * 180));
        var discontinued = status == ProductStatus.Discontinued
            ? createdAt.AddDays(rng.Next(14, 400))
            : (DateTime?)null;

        var tagCount = rng.Next(0, 4);
        string? tags = null;
        if (tagCount > 0)
        {
            tags = string.Join(',', Enumerable.Range(0, tagCount)
                .Select(_ => TagPool[rng.Next(TagPool.Length)])
                .Distinct());
        }

        return new CatalogItem
        {
            Sku = $"SKU-{skuNumber:D8}",
            Name = $"{brand} {category} {skuNumber:D5}",
            Description = BuildDescription(brand, category, skuNumber, rng),
            Category = category,
            Brand = brand,
            WarehouseCode = Warehouses[rng.Next(Warehouses.Length)],
            Price = price,
            Cost = decimal.Round(price * (0.45m + (decimal)rng.NextDouble() * 0.25m), 2),
            QuantityOnHand = quantity,
            ReorderLevel = 25 + rng.Next(0, 200),
            WeightKg = Math.Round(0.05 + rng.NextDouble() * 24.95, 3),
            Rating = Math.Round(1 + rng.NextDouble() * 4, 2),
            IsActive = status == ProductStatus.Active && quantity > 0,
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddHours(rng.Next(0, 500)),
            DiscontinuedAt = discontinued,
            Tags = tags
        };
    }

    private static int WeightedCategory(Random rng)
    {
        var roll = rng.Next(100);
        return roll switch
        {
            < 22 => 0,
            < 38 => 1,
            < 50 => 2,
            < 60 => 4,
            < 68 => 5,
            < 75 => 9,
            _ => 3 + (roll % (Categories.Length - 3))
        };
    }

    private static decimal NextPrice(Random rng)
    {
        var log = Math.Pow(10, 0.3 + rng.NextDouble() * 3.1);
        return decimal.Round((decimal)log + (decimal)rng.Next(0, 99) / 100m, 2);
    }

    private static string BuildDescription(string brand, string category, int skuNumber, Random rng)
    {
        var clauses = rng.Next(2, 5);
        var parts = new string[clauses];
        for (var i = 0; i < clauses; i++)
        {
            parts[i] = i switch
            {
                0 => $"{brand} {category.ToLowerInvariant()} item {skuNumber:D8} for catalog and warehouse operations.",
                1 => $"Packed from mixed lots with quality grade {(char)rng.Next('A', 'G')} and replenishment class {rng.Next(1, 6)}.",
                2 => $"Suitable for bulk ingest benchmarks with variable-length text and nullable attributes.",
                _ => $"Handle with standard {category.ToLowerInvariant()} care; shelf notes {rng.Next(1000, 9999)}."
            };
        }

        return string.Join(' ', parts);
    }
}

public class BenchmarkDbContext : DbContext
{
    public BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options)
        : base(options)
    {
    }

    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogItem>(entity =>
        {
            entity.ToTable("CatalogItems", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityColumn();
            entity.Property(x => x.Sku).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(800).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Brand).HasMaxLength(40).IsRequired();
            entity.Property(x => x.WarehouseCode).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Price).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Cost).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Tags).HasMaxLength(200);
            entity.HasIndex(x => x.Sku).IsUnique();
            entity.HasIndex(x => x.Category);
        });
    }

    public static BenchmarkDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<BenchmarkDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(180))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .EnableSensitiveDataLogging(false)
            .Options;

        return new BenchmarkDbContext(options);
    }
}
