using SQLBulkProcessor.Tests.Benchmark;

namespace SQLBulkProcessor.Tests.Integration;

public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string DatabaseName = "SQLBulkProcessorIntegration";

    public string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        ConnectionString = SqlServerProbe.ResolveConnectionString(DatabaseName);
        if (ConnectionString is null)
            return;

        await using var db = TestDbContext.Create(ConnectionString);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (ConnectionString is null)
            return;

        try
        {
            await using var db = TestDbContext.Create(ConnectionString);
            await db.Database.EnsureDeletedAsync();
        }
        catch (Exception)
        {
            // Best-effort drop of the dedicated integration database.
        }
    }
}

[CollectionDefinition("SqlServerIntegration", DisableParallelization = true)]
public sealed class SqlServerIntegrationCollection : ICollectionFixture<SqlServerFixture>
{
}
