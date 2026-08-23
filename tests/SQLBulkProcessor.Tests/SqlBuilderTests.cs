using SQLBulkProcessor;
using SQLBulkProcessor.Internal;

namespace SQLBulkProcessor.Tests;

public class SqlBuilderTests
{
    private static ColumnMapping Column(
        string name,
        string storeType = "nvarchar(200)",
        bool key = false,
        bool identity = false)
        => new(
            name,
            name,
            name,
            storeType,
            typeof(string),
            isNullable: true,
            isKey: key,
            isIdentity: identity,
            isComputed: false,
            isRowVersion: false,
            isPeriod: false,
            converter: null,
            getter: _ => null,
            setter: null);

    [Fact]
    public void CreateTempTable_IncludesRowIdAndNullableColumns()
    {
        var sql = SqlBuilder.CreateTempTable(
            "[#tmp]",
            [Column("Id", "int", key: true), Column("Name")],
            includeRowId: true);

        Assert.Contains("[_BulkRowId] INT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Id] int NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Name] nvarchar(200) NULL", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateFromTemp_SetsNonKeyColumns()
    {
        var sql = SqlBuilder.UpdateFromTemp(
            "[dbo].[Products]",
            "[#tmp]",
            [Column("Id", "int", key: true)],
            [Column("Name"), Column("Price", "decimal(18,2)")]);

        Assert.Equal(
            "UPDATE T SET T.[Name] = S.[Name], T.[Price] = S.[Price] FROM [dbo].[Products] AS T INNER JOIN [#tmp] AS S ON T.[Id] = S.[Id] OPTION (RECOMPILE);",
            sql);
    }

    [Fact]
    public void DeleteFromTemp_JoinsOnCompositeKey()
    {
        var sql = SqlBuilder.DeleteFromTemp(
            "[OrderLines]",
            "[#tmp]",
            [Column("OrderId", "int", key: true), Column("LineNumber", "int", key: true)]);

        Assert.Equal(
            "DELETE T FROM [OrderLines] AS T WITH (TABLOCK) INNER JOIN [#tmp] AS S ON T.[OrderId] = S.[OrderId] AND T.[LineNumber] = S.[LineNumber] OPTION (RECOMPILE, MAXDOP 1);",
            sql);
    }

    [Fact]
    public void Merge_IncludesInsertUpdateAndDelete()
    {
        var options = new BulkMergeOptions
        {
            InsertWhenNotMatched = true,
            UpdateWhenMatched = true,
            DeleteWhenNotMatchedBySource = true,
            UseHoldLock = true
        };

        var sql = SqlBuilder.Merge(
            "[dbo].[Products]",
            "[#tmp]",
            [Column("Id", "int", key: true)],
            [Column("Name"), Column("Price", "decimal(18,2)")],
            [Column("Name"), Column("Price", "decimal(18,2)")],
            options,
            outputIdentity: null,
            outputTable: null);

        Assert.Contains("MERGE [dbo].[Products] WITH (HOLDLOCK) AS T USING [#tmp] AS S ON (T.[Id] = S.[Id])", sql, StringComparison.Ordinal);
        Assert.Contains("WHEN MATCHED THEN UPDATE SET T.[Name] = S.[Name], T.[Price] = S.[Price]", sql, StringComparison.Ordinal);
        Assert.Contains("WHEN NOT MATCHED BY TARGET THEN INSERT ([Name], [Price]) VALUES (S.[Name], S.[Price])", sql, StringComparison.Ordinal);
        Assert.Contains("WHEN NOT MATCHED BY SOURCE THEN DELETE OPTION (RECOMPILE);", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTempIndex_ClustersKeyColumns()
    {
        var sql = SqlBuilder.CreateTempIndex(
            "[#tmp]",
            [Column("OrderId", "int", key: true), Column("LineNumber", "int", key: true)]);

        Assert.Equal("CREATE CLUSTERED INDEX [IX_bulk_keys] ON [#tmp] ([OrderId], [LineNumber]);", sql);
    }

    [Fact]
    public void InsertViaMergeOutput_AlwaysInsertsAndCapturesRowId()
    {
        var identity = Column("Id", "int", key: true, identity: true);
        var sql = SqlBuilder.InsertViaMergeOutput(
            "[dbo].[Products]",
            "[#tmp]",
            "[#ids]",
            [Column("Name"), Column("Price", "decimal(18,2)")],
            identity);

        Assert.Contains("ON 1 = 0", sql, StringComparison.Ordinal);
        Assert.Contains("OUTPUT INSERTED.[Id], S.[_BulkRowId] INTO [#ids] ([Id], [_BulkRowId])", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void WrapIdentityInsert_TogglesSessionSetting()
    {
        var sql = SqlBuilder.WrapIdentityInsert("[dbo].[Products]", "MERGE ...;");
        Assert.Equal("SET IDENTITY_INSERT [dbo].[Products] ON; MERGE ...; SET IDENTITY_INSERT [dbo].[Products] OFF;", sql);
    }
}
