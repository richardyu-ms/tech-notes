# EF Core DeviceUltra Query Refactoring

> Eliminating cross-product joins via split queries, batching, and in-memory assembly — 48% faster.

## The Problem

A REST endpoint for retrieving device data with related entities used a single monolithic EF query with 5-way joins, causing:

- **SQL cross-product explosion** — Row multiplication from wide joins
- **~19s SLOWSQL reads** — Heavy materialization
- **Change tracking overhead** — CPU/memory pressure on large result sets
- **SQL resource exhaustion** — Large filter lists (3000+ devices) caused `SqlException`

## Before: Single Monolithic EF Query

```csharp
// 5-way join → EF generates massive cross-product SQL
IQueryable<DeviceUltra> devices =
    from device in ReadDb.Devices  // no AsNoTracking
    join deviceTemplate  in ReadDb.DeviceTemplates       on device.Sku        equals deviceTemplate.HwSku into deviceTemplates
    from dt in deviceTemplates.DefaultIfEmpty()
    join deviceMetadata  in ReadDb.DeviceMetadatas       on device.DeviceName  equals deviceMetadata.DeviceName into deviceMetadatas
    join metadataProps   in ReadDb.MetadataProperties     on device.DeviceName  equals metadataProps.DeviceName into metadataProperties
    join buildMetadata   in ReadDb.BuildMetadatas         on new { device.DeviceName, dc = device.Source }
                                                          equals new { buildMetadata.DeviceName, dc = buildMetadata.DatacenterName } into buildMetadatas
    from bm in buildMetadatas.DefaultIfEmpty()
    select new DeviceUltra { /* all fields from cross-product */ };

// ALL filters applied to the single IQueryable
foreach (var f in filter)
    devices = ApplyFilter(devices, f);

return devices.ToList();  // no AsNoTracking, no batching
```

**Problem:** EF generates a single SQL with wide JOINs → row multiplication → 307 GB I/O.

## After: Split Queries + Batched Loading + Parallel Assembly

### Phase 1 — Narrow SQL: Query base table only

```csharp
var deferredFilters = new List<FilterOption>();

IQueryable<Device> deviceQuery = ReadDb.Devices
    .AsNoTracking()
    .Include(d => d.DeviceSources)
    .OrderBy(d => d.Sequence);

foreach (var f in filters)
{
    if (IsDeviceLevelFilter(f))        // DeviceName, DC, Sku, DeviceType
        deviceQuery = ApplyFilter(deviceQuery, f);
    else
        deferredFilters.Add(f);        // Vendor, Metadata → deferred to in-memory
}

var devices = deviceQuery.ToList();
if (devices.Count == 0) return empty;  // early exit
```

### Phase 2 — Batch-load related tables (2,000-device chunks)

```csharp
const int BatchSize = 2000;
for (int i = 0; i < deviceNames.Count; i += BatchSize)
{
    var batch = deviceNames.Skip(i).Take(BatchSize).ToList();

    buildMetadatas.AddRange(ReadDb.BuildMetadatas.AsNoTracking()
        .Where(bm => batch.Contains(bm.DeviceName)).ToList());
    metadataProps.AddRange(ReadDb.MetadataProperties.AsNoTracking()
        .Where(mp => batch.Contains(mp.DeviceName)).ToList());
    deviceMetadatas.AddRange(ReadDb.DeviceMetadatas.AsNoTracking()
        .Where(dm => batch.Contains(dm.DeviceName)).ToList());
}
```

### Phase 3 — In-memory dictionary lookups + parallel assembly

```csharp
var skuLookup    = templates.ToDictionary(t => t.HwSku, StringComparer.OrdinalIgnoreCase);
var bmByDevice   = buildMetadatas.ToDictionary(bm => bm.DeviceName, ...);
var propsGrouped = metadataProps.GroupBy(p => p.DeviceName).ToDictionary(...);

var results = new DeviceUltra[orderedDevices.Count];
Parallel.For(0, orderedDevices.Count, i =>
{
    var device = orderedDevices[i];
    skuLookup.TryGetValue(device.Sku, out var template);
    // ... assemble DeviceUltra from lookups ...
    results[i] = new DeviceUltra { Vendor = template?.Vendor, ... };
});
```

### Phase 4 — Apply deferred filters in-memory

```csharp
foreach (var f in deferredFilters)
    deviceUltras = ApplyFilterInMemory(deviceUltras, f);
```

### In-Memory LIKE Matching (Replaces DbFunctions.Like)

```csharp
private static bool LikeMatch(string value, string pattern)
{
    if (value == null || pattern == null) return false;
    var sb = new StringBuilder("^");
    foreach (var ch in pattern)
    {
        if (ch == '%')       sb.Append(".*");
        else if (ch == '_')  sb.Append('.');
        else                 sb.Append(Regex.Escape(ch.ToString()));
    }
    sb.Append('$');
    return Regex.IsMatch(value, sb.ToString(), RegexOptions.IgnoreCase);
}
```

## Key Changes Summary

| Aspect | Before | After |
|--------|--------|-------|
| **Query target** | `IQueryable<DeviceUltra>` (all joins in SQL) | `IQueryable<Device>` (device-level only) |
| **Filter splitting** | All filters in SQL | Device-level in SQL; vendor/metadata deferred |
| **Change tracking** | Default (tracked) | `AsNoTracking()` on all queries |
| **Batching** | None (SQL resource exhaustion risk) | 2,000-device chunks |
| **LIKE matching** | `DbFunctions.Like()` in SQL | Custom `LikeMatch()` regex in-memory |
| **Assembly** | EF materializes cross-product rows | `Parallel.For` with dictionary lookups |
| **Early exit** | None | Returns empty when no devices match |

## Results

| Component | Before | After | Improvement |
|-----------|--------|-------|-------------|
| **Total** | 4:39.74 | 2:25.68 | **~48% faster** |
| **Wiring Engine** | 1:17.90 | 0:34.46 | **~56% faster** |
| **Seed Engine** | 0:54.67 | 0:29.94 | **~45% faster** |

## Design Decisions

### Why keep vendor/metadata filters in-memory?
These predicates require joins against templates and metadata tables. Broad joins caused the original SLOWSQL via row explosion. Keeping them in-memory after narrowing the device set avoids that regression.

### Why batch at 2,000?
SQL Server's query processor has internal resource limits on large `IN (...)` clauses. Batching at 2,000 keeps plans simple and within resource limits regardless of dataset size. The batch size is tunable.

### Why `Parallel.For` for assembly?
Pure CPU work (dictionary lookups, no shared mutation) — safe to parallelize. Reduces latency on large result sets while maintaining deterministic ordering via indexed array assignment.
