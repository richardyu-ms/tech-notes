# Fix D — Optimize Client-Side Processing

**Layer:** 4 (Client-Side Cost) | **Effort:** Low-Medium | **Impact:** 15-50%

> Anti-patterns: **C1-C6**, **X1**, **X2**, **I4**

Apply these after SQL queries are already lean (Fixes A-C).

---

## Summary of Techniques

| # | Technique | Anti-pattern | Impact |
|---|-----------|-------------|--------|
| D1 | `.AsNoTracking()` on all read-only queries | C1 | 15-20% CPU/memory |
| D2 | Parallel query execution (`Task.Run` for independent loads) | — | 5× for lookup phase |
| D3 | Pre-populated dictionaries (guarantee every key has entry) | — | Eliminates TryGetValue branches |
| D4 | `Parallel.For` for object assembly (>1k objects, pure CPU) | — | Multi-core utilization |
| D5 | `HashSet<T>` for filter values instead of `List<T>` | — | O(1) vs O(n) per lookup |
| D6 | Eliminate `.ToList().ToCollection()` → direct cast | C3 | Remove double allocation |
| D7 | Deterministic ordering of sub-collections | C4 | Prevents diff noise |
| D8 | Early exit on empty/null input | X1 | Saves DB round-trip |
| D9 | Batch `Contains()` in chunks of 2000 | I4, X2 | Avoids SqlException at >2100 params |

---

## Examples

**D1 — AsNoTracking:**
```csharp
// Before: EF tracks every entity (15-20% overhead)
var devices = await context.Devices.Where(...).ToListAsync();

// After: Skip change tracking for read-only queries
var devices = await context.Devices.AsNoTracking().Where(...).ToListAsync();
```

**D5 — HashSet for filter values:**
```csharp
// Before: O(n) per lookup
var vendorList = new List<string> { "Cisco", "Juniper", "Arista" };
devices.Where(d => vendorList.Contains(d.Vendor));

// After: O(1) per lookup
var vendorSet = new HashSet<string>(vendorList, StringComparer.OrdinalIgnoreCase);
devices.Where(d => vendorSet.Contains(d.Vendor));
```

**D8 — Early exit:**
```csharp
// Before: Executes SQL even for empty input
public async Task<List<Device>> GetDevices(List<string> names) { ... }

// After: Skip DB round-trip
public async Task<List<Device>> GetDevices(List<string> names)
{
    if (names == null || names.Count == 0) return new List<Device>();
    // ... proceed with query
}
```

---

## Key Rules

- **D1:** Add `.AsNoTracking()` to **every** read-only `IQueryable`
- **D4:** Only use `Parallel.For` for pure CPU work — never inside DB calls
- **D5:** Use `StringComparer.OrdinalIgnoreCase` on all HashSet/Dictionary for case-insensitive matching
- **D9:** Keep batch size at 2000 (SQL Server limit is 2100 parameters)

---

**← Back to [Phase 2 Overview](README.md)**
**→ Next: [Fix E — System-Level Tuning](06_Fix_E_System_Tuning.md)**
