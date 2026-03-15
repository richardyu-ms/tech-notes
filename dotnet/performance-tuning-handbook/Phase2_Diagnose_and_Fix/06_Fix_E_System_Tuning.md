# Fix E — System-Level Tuning

**Layer:** Cross-cutting | **Effort:** Low (config changes) | **Impact:** 10-25%

> Anti-pattern: **X3** (Debug Logging in Hot Path), algorithmic complexity

Apply after query-level fixes (A-C) and client-side fixes (D).

---

## E1. Server GC

Switch from Workstation GC (UI-optimized) to Server GC (throughput-optimized) for batch services:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

**When:** High-throughput batch workloads assembling large object graphs (>1k complex objects per request).

---

## E2. Gate Verbose SQL Logging

```ini
EnableVerboseSqlLogging=false    # Default off — high overhead when on
```

Use threshold-based logging (Phase 1 EF Interceptor) instead. Only enable verbose mode for short, targeted debug sessions.

---

## E3. Algorithm Optimization: O(n×m) → O(1) Dictionary

Replace linear scans inside loops with dictionary lookups:

| Situation | Action |
|-----------|--------|
| Collection > 100 items, called in a loop | Use `Dictionary<K,V>` |
| Any `GetBy{Key}` called inside `foreach` | Pre-build hash index |
| Collection built once, queried many times | Amortize build cost |

**Example:**
```csharp
// Before: O(n×m) — linear scan per device
foreach (var device in devices)  // n devices
{
    var meta = allMetadata.FirstOrDefault(m => m.DeviceName == device.Name);  // m items scanned
}

// After: O(n+m) — dictionary lookup
var metaDict = allMetadata.ToDictionary(
    m => m.DeviceName, StringComparer.OrdinalIgnoreCase);
foreach (var device in devices)
{
    metaDict.TryGetValue(device.Name, out var meta);  // O(1)
}
```

---

**← Back to [Fix D — Client-Side](05_Fix_D_Client_Side.md)**
**← Back to [Phase 2 Overview](README.md)**
**→ Next: [Phase 3 — Validate & Monitor](../Phase3_Validate_and_Monitor/README.md)**
