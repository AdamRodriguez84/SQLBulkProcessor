# SQLBulkProcessor

High-performance bulk **Insert**, **Update**, **Upsert**, **Delete**, and **Merge** for Entity Framework Core on SQL Server.

The library maps entities from your EF Core model, streams them with `SqlBulkCopy`, and applies set-based SQL (`UPDATE`/`DELETE`/`MERGE`) against a session-scoped temp table. It is designed as a drop-in NuGet package for EF Core 8 projects.

## Requirements

- .NET 8
- Entity Framework Core 8 with the SQL Server provider
- SQL Server (Microsoft.Data.SqlClient)

## Install

Project reference (local pack):

```bash
dotnet pack src/SQLBulkProcessor/SQLBulkProcessor.csproj -c Release
dotnet add package SQLBulkProcessor --source ./src/SQLBulkProcessor/bin/Release
```

Or add a project reference:

```xml
<ItemGroup>
  <PackageReference Include="SQLBulkProcessor" Version="1.0.0" />
</ItemGroup>
```

## Quick start

```csharp
using Microsoft.EntityFrameworkCore;
using SQLBulkProcessor;

await db.BulkInsertAsync(products);
await db.BulkUpdateAsync(products);
await db.BulkUpsertAsync(products);
await db.BulkDeleteAsync(products);
await db.BulkMergeAsync(products);
```

Sync overloads (`BulkInsert`, `BulkUpdate`, `BulkUpsert`, `BulkDelete`, `BulkMerge`) are also available.

Empty collections are a no-op and return `0`, except `BulkMerge` with delete enabled (the default), which throws so it cannot wipe the target table.

## Operations

| Method | Behavior |
| --- | --- |
| `BulkInsert` | `SqlBulkCopy` into the destination table |
| `BulkUpdate` | Copy to temp table, `UPDATE ... JOIN` on keys |
| `BulkUpsert` | Copy to temp table, `MERGE` insert + update |
| `BulkDelete` | Copy keys to temp table, `DELETE ... JOIN` |
| `BulkMerge` | `MERGE` insert + update + **delete target rows not in the source** |

`BulkMerge` is a table sync. To merge without deleting unmatched target rows, use `BulkUpsert` or set `DeleteWhenNotMatchedBySource = false`.

## Options

```csharp
await db.BulkInsertAsync(products, options =>
{
    options.BatchSize = 5_000;
    options.TimeoutSeconds = 120;
    options.UseTableLock = true;
    options.OutputIdentity = true;   // write generated IDs back onto entities
    options.KeepIdentity = false;    // set true to insert explicit ID values
    options.ExcludeColumns = ["CreatedAt"];
    options.OnRowsCopied = copied => Console.WriteLine($"Copied {copied}");
});

await db.BulkUpsertAsync(products, options =>
{
    options.KeyColumns = ["Sku"];    // match on a business key instead of PK
    options.InsertWhenNotMatched = true;
    options.UpdateWhenMatched = true;
});

await db.BulkMergeAsync(products, options =>
{
    options.DeleteWhenNotMatchedBySource = true;
});
```

Column filters (`IncludeColumns`, `ExcludeColumns`, `KeyColumns`) accept **property names or SQL column names**.

## Transactions

Operations enlist in the current EF Core transaction and connection:

```csharp
await using var tx = await db.Database.BeginTransactionAsync();
await db.BulkInsertAsync(newProducts);
await db.BulkUpdateAsync(changedProducts);
await tx.CommitAsync();
```

## Mapping notes

- Table, schema, keys, identity, computed, rowversion, and temporal period columns come from the EF Core model.
- TPH discriminators are included. Mixed TPH collections of a base type are supported.
- Owned reference types mapped to the same table are included. Owned collections are not.
- Value converters and enums are applied when reading entity values.
- Change tracker is **not** used. Entities are not attached or marked `Unchanged` after a bulk call.
- Source rows for upsert/merge must be unique on the match key (`MERGE` fails on duplicate source keys).

## Benchmark

The test project includes a SQL Server catalog benchmark with a deterministic 25,000-row seed (mixed categories, brands, warehouses, prices, nullable columns, and variable-length text). Each operation is timed twice on the same data:

- **EF ChangeTracker** — standard `AddRange` / `UpdateRange` / `RemoveRange` / `SaveChanges`, plus load-and-mutate for upsert and merge
- **SQLBulkProcessor** — `BulkInsert` / `BulkUpdate` / `BulkUpsert` / `BulkDelete` / `BulkMerge`

The output table reports both means and the bulk speedup.

```bash
dotnet test tests/SQLBulkProcessor.Tests -c Release --filter Category=Benchmark
```

The test is skipped when SQL Server is not reachable. Override the connection with `SQLBULKPROCESSOR_CONNECTION`. Exclude it from a normal run with `--filter Category!=Benchmark`.

## Pack

```bash
dotnet pack src/SQLBulkProcessor/SQLBulkProcessor.csproj -c Release
```

The package includes this README, an MIT license, and a symbol package (`.snupkg`).
