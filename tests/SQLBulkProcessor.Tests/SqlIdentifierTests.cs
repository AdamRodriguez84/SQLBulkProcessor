using SQLBulkProcessor.Internal;

namespace SQLBulkProcessor.Tests;

public class SqlIdentifierTests
{
    [Fact]
    public void Quote_WrapsWithBrackets()
    {
        Assert.Equal("[Products]", SqlIdentifier.Quote("Products"));
    }

    [Fact]
    public void Quote_EscapesClosingBrackets()
    {
        Assert.Equal("[Weird]]Name]", SqlIdentifier.Quote("Weird]Name"));
    }

    [Fact]
    public void Qualify_IncludesSchema()
    {
        Assert.Equal("[dbo].[Products]", SqlIdentifier.Qualify("dbo", "Products"));
        Assert.Equal("[Products]", SqlIdentifier.Qualify(null, "Products"));
    }

    [Fact]
    public void TempTable_IsLocalTemp()
    {
        var name = SqlIdentifier.TempTable();
        Assert.StartsWith("[#bulk_", name, StringComparison.Ordinal);
        Assert.EndsWith("]", name);
    }
}
