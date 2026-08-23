using Microsoft.EntityFrameworkCore;

namespace SQLBulkProcessor.Tests;

public enum ProductStatus
{
    Draft = 0,
    Active = 1,
    Discontinued = 2
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public ProductStatus Status { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[]? RowVersion { get; set; }
}

public class OrderLine
{
    public int OrderId { get; set; }
    public int LineNumber { get; set; }
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public abstract class Animal
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class Dog : Animal
{
    public string Breed { get; set; } = string.Empty;
}

public class Cat : Animal
{
    public bool Indoor { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Address Address { get; set; } = new();
}

public class Address
{
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public class UnmappedThing
{
    public int Id { get; set; }
}

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Animal> Animals => Set<Animal>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityColumn();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Price).HasColumnType("decimal(18,2)");
            entity.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.ToTable("OrderLines");
            entity.HasKey(x => new { x.OrderId, x.LineNumber });
            entity.Property(x => x.Sku).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<Animal>(entity =>
        {
            entity.ToTable("Animals");
            entity.HasDiscriminator<string>("AnimalType")
                .HasValue<Dog>("Dog")
                .HasValue<Cat>("Cat");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.OwnsOne(x => x.Address, address =>
            {
                address.Property(x => x.City).HasMaxLength(100).HasColumnName("City");
                address.Property(x => x.Country).HasMaxLength(100).HasColumnName("Country");
            });
        });
    }

    public static TestDbContext Create()
        => Create("Server=.;Database=SQLBulkProcessorTests;Trusted_Connection=True;TrustServerCertificate=True;");

    public static TestDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(60))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        return new TestDbContext(options);
    }
}
