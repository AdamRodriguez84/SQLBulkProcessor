using SQLBulkProcessor;
using SQLBulkProcessor.Internal;

namespace SQLBulkProcessor.Tests;

public class EntityMetadataTests
{
    [Fact]
    public void Product_MapsTableIdentityAndRowVersion()
    {
        using var db = TestDbContext.Create();
        var metadata = EntityMetadataFactory.Get(db, typeof(Product));

        Assert.Equal("Products", metadata.TableName);
        Assert.Equal("dbo", metadata.Schema);
        Assert.Equal("[dbo].[Products]", metadata.QuotedFullName);
        Assert.NotNull(metadata.IdentityColumn);
        Assert.Equal("Id", metadata.IdentityColumn!.ColumnName);
        Assert.Contains(metadata.Columns, c => c.ColumnName == "RowVersion" && c.IsRowVersion);
        Assert.Contains(metadata.Columns, c => c.PropertyName == "Status");
    }

    [Fact]
    public void Product_InsertColumnsSkipIdentityAndRowVersion()
    {
        using var db = TestDbContext.Create();
        var metadata = EntityMetadataFactory.Get(db, typeof(Product));
        var columns = metadata.ResolveInsertColumns(new BulkOptions());

        Assert.DoesNotContain(columns, c => c.IsIdentity);
        Assert.DoesNotContain(columns, c => c.IsRowVersion);
        Assert.Contains(columns, c => c.ColumnName == "Name");
        Assert.Contains(columns, c => c.ColumnName == "Price");
    }

    [Fact]
    public void Product_KeepIdentityIncludesId()
    {
        using var db = TestDbContext.Create();
        var metadata = EntityMetadataFactory.Get(db, typeof(Product));
        var columns = metadata.ResolveInsertColumns(new BulkOptions { KeepIdentity = true });

        Assert.Contains(columns, c => c.ColumnName == "Id" && c.IsIdentity);
    }

    [Fact]
    public void OrderLine_UsesCompositeKey()
    {
        using var db = TestDbContext.Create();
        var metadata = EntityMetadataFactory.Get(db, typeof(OrderLine));
        var keys = metadata.ResolveKeys(new BulkOptions());

        Assert.Equal(2, keys.Count);
        Assert.Contains(keys, c => c.ColumnName == "OrderId");
        Assert.Contains(keys, c => c.ColumnName == "LineNumber");
    }

    [Fact]
    public void Customer_IncludesOwnedAddressColumns()
    {
        using var db = TestDbContext.Create();
        var metadata = EntityMetadataFactory.Get(db, typeof(Customer));

        Assert.Contains(metadata.Columns, c => c.ColumnName == "City");
        Assert.Contains(metadata.Columns, c => c.ColumnName == "Country");

        var customer = new Customer
        {
            Name = "Ada",
            Address = new Address { City = "London", Country = "UK" }
        };

        var city = metadata.Columns.Single(c => c.ColumnName == "City");
        Assert.Equal("London", city.GetValue(customer));
    }

    [Fact]
    public void Animal_IncludesTphDiscriminator()
    {
        using var db = TestDbContext.Create();
        var metadata = EntityMetadataFactory.Get(db, typeof(Animal));
        var discriminator = metadata.Columns.Single(c => c.ColumnName == "AnimalType");

        Assert.Equal("Dog", discriminator.GetValue(new Dog { Name = "Rex" }));
        Assert.Equal("Cat", discriminator.GetValue(new Cat { Name = "Misty" }));
    }

    [Fact]
    public void ExcludeColumns_RemovesMatchedProperties()
    {
        using var db = TestDbContext.Create();
        var metadata = EntityMetadataFactory.Get(db, typeof(Product));
        var columns = metadata.ResolveInsertColumns(new BulkOptions
        {
            ExcludeColumns = ["UpdatedAt", "Status"]
        });

        Assert.DoesNotContain(columns, c => c.PropertyName is "UpdatedAt" or "Status");
        Assert.Contains(columns, c => c.PropertyName == "Name");
    }

    [Fact]
    public void CustomKeyColumns_OverridePrimaryKey()
    {
        using var db = TestDbContext.Create();
        var metadata = EntityMetadataFactory.Get(db, typeof(Product));
        var keys = metadata.ResolveKeys(new BulkOptions { KeyColumns = ["Name"] });

        Assert.Single(keys);
        Assert.Equal("Name", keys[0].ColumnName);
    }

    [Fact]
    public void UnmappedType_Throws()
    {
        using var db = TestDbContext.Create();
        var ex = Assert.Throws<InvalidOperationException>(() => EntityMetadataFactory.Get(db, typeof(UnmappedThing)));
        Assert.Contains("not mapped", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
