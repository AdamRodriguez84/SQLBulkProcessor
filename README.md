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

The test project includes a SQL Server catalog benchmark with a deterministic seed. Each operation is timed twice on the **same in-memory dataset**:

- **EF ChangeTracker** — standard `AddRange` / `UpdateRange` / `RemoveRange` / `SaveChanges`, plus load-and-mutate for upsert and merge
- **SQLBulkProcessor** — `BulkInsert` / `BulkUpdate` / `BulkUpsert` / `BulkDelete` / `BulkMerge`

Setup (truncate / reload) is excluded from the timings so the comparison is the operation itself. Measured on SQL Server (local instance), .NET 8, Release. ChangeTracker is 1 iteration; bulk is the mean of 3 iterations after a 2,000-row warmup.

### 25,000 rows

| | |
| --- | --- |
| Shape | 12 categories, 20 brands, 8 warehouses |
| Mix | 16,107 active, 2,534 discontinued, 2,020 out of stock |
| Text | ~216 character descriptions, nullable tags and discontinued dates |
| Price range | $2.04 – $2,511.63 |
| Upsert | 12,500 existing rows updated + 12,500 new rows inserted |
| Merge | 20,000 updated, 5,000 inserted, 5,000 deleted (table sync) |

| Operation | ChangeTracker | SQLBulkProcessor | Speedup |
| --- | ---: | ---: | ---: |
| Insert | 6.120 s (4,085 rows/s) | 305.4 ms (81,872 rows/s) | **20.0×** |
| Update | 6.950 s (3,597 rows/s) | 474.4 ms (52,693 rows/s) | **14.6×** |
| Upsert (50/50) | 15.264 s (1,638 rows/s) | 586.8 ms (42,605 rows/s) | **26.0×** |
| Delete | 782.0 ms (31,971 rows/s) | 319.5 ms (78,255 rows/s) | **2.4×** |
| Merge (sync) | 20.993 s (1,191 rows/s) | 611.1 ms (40,907 rows/s) | **34.4×** |

On this dataset, bulk insert, update, upsert, and merge finished in well under a second while ChangeTracker took 6–21 seconds. Delete is closer (ChangeTracker already issues relatively cheap `DELETE` statements by key) but bulk is still more than twice as fast.

### 200,000 rows

Same generator, seed, and column mix, scaled to 200,000 rows.

| | |
| --- | --- |
| Mix | 128,897 active, 20,047 discontinued, 16,167 out of stock |
| Text | ~216 character descriptions, nullable tags and discontinued dates |
| Price range | $2.00 – $2,512.57 |
| Upsert | 100,000 existing rows updated + 100,000 new rows inserted |
| Merge | 160,000 updated, 40,000 inserted, 40,000 deleted (table sync) |

| Operation | ChangeTracker | SQLBulkProcessor | Speedup |
| --- | ---: | ---: | ---: |
| Insert | 50.181 s (3,986 rows/s) | 2.227 s (89,799 rows/s) | **22.5×** |
| Update | 55.386 s (3,611 rows/s) | 3.290 s (60,798 rows/s) | **16.8×** |
| Upsert (50/50) | 119.235 s (1,677 rows/s) | 3.952 s (50,607 rows/s) | **30.2×** |
| Delete | 7.737 s (25,848 rows/s) | 5.148 s (38,846 rows/s) | **1.5×** |
| Merge (sync) | 162.626 s (1,230 rows/s) | 5.921 s (33,776 rows/s) | **27.5×** |

Speedups hold at 8× the row count. Insert, update, upsert, and merge stay in the 17–30× range; ChangeTracker merge takes about 2.7 minutes while bulk finishes in about 6 seconds.

```bash
dotnet test tests/SQLBulkProcessor.Tests -c Release --filter Category=Benchmark
dotnet test tests/SQLBulkProcessor.Tests -c Release --filter Size=200k
```

The tests are skipped when SQL Server is not reachable. Override the connection with `SQLBULKPROCESSOR_CONNECTION`. Exclude them from a normal run with `--filter Category!=Benchmark`.

SQL Server integration tests (insert / update / upsert / delete / merge against a real database) run with the default test suite when SQL Server is available, or explicitly:

```bash
dotnet test tests/SQLBulkProcessor.Tests -c Release --filter Category=Integration
```

## Pack

```bash
dotnet pack src/SQLBulkProcessor/SQLBulkProcessor.csproj -c Release
```

The package includes this README, an MIT license, and a symbol package (`.snupkg`).

Merges into `main` pack and publish through GitHub Actions (`.github/workflows/build.yml`). nuget.org uses [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) (OIDC) rather than a long-lived API key. The nuget.org policy must list this repository and the workflow file `build.yml`. Set the Actions variable `NUGET_USER` to your nuget.org profile name if it is not the same as the GitHub owner.
