using Microsoft.Data.SqlClient;

namespace SQLBulkProcessor.Tests.Benchmark;

internal static class SqlServerProbe
{
    public const string ConnectionEnvironmentVariable = "SQLBULKPROCESSOR_CONNECTION";

    public static string? ResolveConnectionString()
        => ResolveConnectionString("SQLBulkProcessorBench");

    public static string? ResolveConnectionString(string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        var configured = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!CanOpen(ToMaster(configured)))
                return null;

            var builder = new SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = databaseName
            };
            return builder.ConnectionString;
        }

        foreach (var server in new[] { ".", "(localdb)\\mssqllocaldb" })
        {
            var master = $"Server={server};Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=4";
            if (CanOpen(master))
            {
                return $"Server={server};Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=8";
            }
        }

        return null;
    }

    private static bool CanOpen(string connectionString)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            connection.Open();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ToMaster(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        };
        return builder.ConnectionString;
    }
}
