# Service Performance Optimization: 15 min → 1 min (92% Reduction)

> End-to-end journey optimizing a .NET data processing service across SQL, algorithms, memory, and GC.

## Executive Summary

A data processing service that builds network graph data was optimized from **14 min 47s** down to **1 min 07s** — a **92% reduction** achieved through systematic optimization across multiple layers.

| Version | Duration | Improvement vs Baseline |
|---------|----------|------------------------|
| Baseline | 14m 47s | — |
| + SQL Fix | 07m 32s | ~49% faster |
| + Query Refactoring | 03m 21s | ~77% faster |
| + Algorithm Fix | 03m 12s | ~78% faster |
| + Memory Optimization | 02m 40s | ~82% faster |
| + GC Tuning | 01m 20s | ~91% faster |
| Final | 01m 07s | **~92% faster** |

## Optimization Categories

| Category | Area | Key Change | Impact |
|----------|------|------------|--------|
| **SQL / DB** | Slow Query | Removed `.ToLower()` → enabled Index Seek | **148s → 7s** (21x) |
| **SQL / DB** | Query Refactoring | Batched filters, removed cross-product joins | **4m 40s → 2m 25s** (48%) |
| **Algorithm** | Group Engine | O(n×m) List scan → O(1) Hash Dictionary | **56s → 1.3s** (42x) |
| **Memory** | Collection Allocations | Optimized `IEnumerable` handling, removed double-alloc | **23m 55s → 18m 23s** (23%) |
| **Infrastructure** | Garbage Collection | Workstation GC → Optimized Server GC | **7m 44s → 5m 56s** (23%) |
| **Observability** | Logging | EF Interceptors + Workflow Phase Logging | Enabled bottleneck detection |

---

## 1. SQL: The "Index Killer" Fix

**Root cause:** `.ToLower()` in a LINQ WHERE clause made the predicate non-SARGable.

```csharp
// Before: Full Table Scan (148s, 307 GB I/O)
devices.Where(dev => valueArray.Contains(dev.DeviceName.ToLower()));

// After: Index Seek (7s, 3 GB I/O)
devices.Where(dev => valueArray.Contains(dev.DeviceName));
```

**Key insight:** The database collation (`SQL_Latin1_General_CP1_CI_AS`) is already case-insensitive. Adding `.ToLower()` was redundant *and* destructive — it forced the optimizer to scan every row.

> See [EF Core Query Optimization](ef-core-query-optimization.md) for the full deep dive.

## 2. SQL: DeviceUltra Query Refactoring

**Root cause:** A single EF query with 5-way joins caused SQL cross-product explosion.

**Solution:** Split into phases:
1. **Narrow SQL** — Query base table only with `AsNoTracking()`
2. **Batch-load** — Fetch related tables in 2,000-device chunks
3. **Parallel assembly** — Build result objects via dictionary lookups + `Parallel.For`
4. **Deferred filters** — Apply vendor/metadata filters in-memory

> See [EF Core Query Refactoring](ef-core-query-refactoring.md) for code examples.

## 3. Algorithm: GroupEngine O(n×m) → O(1)

**Root cause:** Nested loops using `List.Find()` for device grouping.

```csharp
// Before: O(n×m) — List.Find scans the entire list for each device
foreach (var device in devices)          // n devices
    existingGroup = groups.Find(g =>     // m groups (linear scan)
        g.Name == device.GroupName);

// After: O(n) — Dictionary lookup is O(1)
var groupDict = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
foreach (var device in devices)
{
    if (!groupDict.TryGetValue(device.GroupName, out var group))
    {
        group = new Group(device.GroupName);
        groupDict[device.GroupName] = group;
    }
    group.Add(device);
}
```

**Result:** 56s → 1.3s (**97.6% reduction**, 42x faster).

## 4. Memory: ToCollection Double Allocation

**Root cause:** Extension method always created a new `List<T>` before wrapping in `Collection<T>`, even if the source was already a list. Call sites also called `.ToList()` before `.ToCollection()` — double allocation.

```csharp
// Before: Double allocation
public static Collection<T> ToCollection<T>(this IList<T> source)
    => new Collection<T>(source.ToList());  // always copies

// After: Cast if possible, copy only when needed
public static Collection<T> ToCollection<T>(this IEnumerable<T> source)
    => source is List<T> list
        ? new Collection<T>(list)
        : new Collection<T>(source.ToList());
```

Updated call sites to remove redundant `.ToList()` calls.

**Result:** 23m 55s → 18m 23s (**~23% faster**).

## 5. Infrastructure: GC Tuning

**Root cause:** Default Workstation GC caused 1,248 GC interruptions during processing.

**Fix:** Switched to Server GC with optimized settings:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

| Metric | Workstation GC | Server GC | Improvement |
|--------|---------------|-----------|-------------|
| GC Pauses | 19.4s | 4.5s | **76% reduction** |
| GC Count | 1,248 | 38 | **97% reduction** |
| Total Time | 7m 44s | 5m 56s | **23% faster** |

## 6. Observability: Performance Logging

Built a 3-layer logging stack to catch issues like these:

1. **EF SQL Interceptor** — Logs queries exceeding configurable threshold
2. **HTTP Middleware** — Logs request duration with auto log-level (Warning for slow)
3. **Workflow Phase Logging** — Breaks down business operations into sub-phases (Clear, Load, Dump) with calculated business logic time

> See [Performance Logging Infrastructure](performance-logging-infrastructure.md) for implementation details.

## Production Results (Cumulative)

### Per-Seed Processing Time

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| P80 | 84.6s | 7.1s | **11.85x** |
| P90 | 84.6s | 11.2s | **7.58x** |
| P100 | 472.4s | 106.4s | **4.44x** |

### AI-Related Workloads (Compute-Intensive)

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Per-Seed Duration | 472.4 min | 14.7 min | **32.1x** |
| E2E Total | 281 hours | 18 hours | **15.6x** |
| Core Stage Share | 89.4% | 31.9% | Bottleneck resolved |

### Workflow Shift Analysis

With the core engine optimized (89% → 32% of total time), the performance profile shifted:
- **Before:** Core generation dominated total time; overhead was ~74 min average
- **After:** Core generation is fast; PR workflow stages now dominate (~156 min average)

**Implication:** Future optimizations should focus on the PR workflow stages, not the computation engine.

## Key Lessons

1. **Attack from all angles** — No single optimization was enough; the combination of SQL + algorithm + memory + GC delivered 92%
2. **Measure before optimizing** — Performance logging infrastructure was prerequisite to finding the real bottlenecks
3. **Trust the database** — SQL Server's collation, indexes, and query optimizer are powerful; don't fight them with `.ToLower()` or wide joins
4. **Data structures matter** — `List.Find()` → `Dictionary.TryGetValue()` = 42x. `List.Contains()` → `HashSet.Contains()` = fundamental complexity class change
5. **GC tuning is free performance** — Switching from Workstation to Server GC = 23% faster with zero code changes
6. **Profile the whole pipeline** — After fixing one bottleneck, the next one reveals itself; keep profiling until the bottleneck shifts to something outside your control
