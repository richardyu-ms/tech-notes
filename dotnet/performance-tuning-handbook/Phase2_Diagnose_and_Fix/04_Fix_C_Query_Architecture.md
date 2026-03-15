# Fix C — Reshape the Query Architecture

**Layer:** 1 (Query Shape) — the biggest lever | **Effort:** High | **Impact:** 10-900×

> Anti-patterns: **S1** (Join Explosion), **S3** (Eager Loading), **S5** (Monolithic Query)

---

## Core Principle

> **Filter narrow, load separate, assemble in-memory.**

---

## When to Apply

| Situation | Split? |
|-----------|--------|
| Base rows × join expansion > 10× expected count | **Yes** |
| Multiple 1:N relations JOINed to same base | **Yes** |
| Only 1:1 relations and base < 1k rows | **No** — single `Include()` is simpler |
| Related data only needed for a subset | **Yes** — defer loading after base filter |

---

## The Transformation Pattern

```
BEFORE (monolithic):
  FROM Base JOIN Table1 JOIN Table2 JOIN Table3...
  WHERE <all filters>
  SELECT new Entity { ... }           → N × M × K rows

AFTER (split):
  Phase 1: FROM Base WHERE <filters>  → N rows (narrow)
  Phase 2: FROM Table1 WHERE Key IN (batch of 2000) → separate queries
  Phase 3: Dictionary lookup + in-memory assembly  → N entity objects
```

---

## Example: Device Filter Query

**Before** — monolithic 5-table JOIN, 14M+ rows returned:
```csharp
var devices = await context.Devices
    .Include(d => d.DeviceMetadatas)
    .Include(d => d.DeviceSources)
    .Include(d => d.BuildMetadatas)
        .ThenInclude(b => b.BuildMetadataProperties)
    .Where(d => d.DatacenterName.ToLower() == dc.ToLower())
    .ToListAsync();  // 1,801 devices × 5 tables = 14M+ rows via JOIN explosion
```

**After** — split into separate queries, batch + dictionary assembly:
```csharp
// Phase 1: Narrow base query
var devices = await context.Devices.AsNoTracking()
    .Where(d => d.DatacenterName == dc)
    .ToListAsync();  // 1,801 rows

// Phase 2: Batch-load related data (2000 per chunk to stay under SQL param limit)
var deviceNames = devices.Select(d => d.DeviceName).ToList();
var metadatas = await LoadInBatches(deviceNames, 2000,
    batch => context.DeviceMetadatas.AsNoTracking()
        .Where(m => batch.Contains(m.DeviceName)).ToListAsync());

// Phase 3: Dictionary assembly
var metaDict = metadatas
    .GroupBy(m => m.DeviceName)
    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

foreach (var device in devices)
{
    if (metaDict.TryGetValue(device.DeviceName, out var meta))
        device.Metadatas = meta;
}
```

**Result:** 14M rows → 1,801 rows per query. **900× faster.**

---

## The Batch Helper

```csharp
/// <summary>
/// Loads data in batches of <paramref name="batchSize"/> to stay under
/// SQL Server's 2100 parameter limit.
/// </summary>
public static async Task<List<T>> LoadInBatches<T>(
    List<string> keys,
    int batchSize,
    Func<List<string>, Task<List<T>>> loader)
{
    var results = new List<T>(keys.Count);
    for (int i = 0; i < keys.Count; i += batchSize)
    {
        var batch = keys.GetRange(i, Math.Min(batchSize, keys.Count - i));
        results.AddRange(await loader(batch));
    }
    return results;
}
```

---

## Key Implementation Details

- **Batch size:** 2000 items per chunk (SQL Server parameter limit is 2100)
- **Dictionary assembly:** `Dictionary<string, List<T>>` with `StringComparer.OrdinalIgnoreCase`
- **Deferred filters:** Filters requiring joined data applied in-memory after assembly
- **Parallel assembly:** `Parallel.For` for >1k objects (pure CPU, no shared mutation)

---

**← Back to [Phase 2 Overview](README.md)**
**→ Next: [Fix D — Client-Side Optimization](05_Fix_D_Client_Side.md)**
