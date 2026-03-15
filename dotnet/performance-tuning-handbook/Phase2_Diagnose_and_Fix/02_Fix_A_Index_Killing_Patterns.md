# Fix A — Eliminate Index-Killing Patterns in Code

**Layer:** 2 (Index Utilization) | **Effort:** Low | **Impact:** 10-20×

> Anti-patterns: **I1** (Function on Column), **I5** (Date Function in Predicate)

---

## Quick Reference

| Index Killer | Index Friendly | Why |
|-------------|----------------|-----|
| `col.ToLower() == val` | `col == val` | Function wraps column → full scan |
| `col.Substring(0,3) == "ATL"` | `col.StartsWith("ATL")` | Prefix LIKE can still seek |
| `col.Trim() == val` | Normalize data on write | Same as above |
| `DateTime.Year == 2025` | `col >= @start AND col < @end` | Range allows seek |
| `list.Contains(col.ToLower())` | `hashSet.Contains(col)` | No function + O(1) lookup |

---

## Example: `.ToLower()` Removal

**Before** — forces full index scan:
```csharp
.Where(d => d.DatacenterName.ToLower() == datacenterName.ToLower())
```

**After** — SQL Server CI collation handles case-insensitivity; enables index seek:
```csharp
.Where(d => d.DatacenterName == datacenterName)
```

## Example: Date Function → Range

**Before** — function on column prevents seek:
```csharp
.Where(d => d.CreatedDate.Year == 2025)
```

**After** — range predicate allows index seek:
```csharp
var start = new DateTime(2025, 1, 1);
var end = new DateTime(2026, 1, 1);
// ...
.Where(d => d.CreatedDate >= start && d.CreatedDate < end)
```

---

## Key Learning

> SQL Server's default collation (`CI_AS`) is already case-insensitive. Calling `.ToLower()` to "make it case-insensitive" is redundant *and* destructive — it prevents index usage.

This was the single most impactful one-line fix in our entire optimization journey. Removing `.ToLower()` from a JOIN predicate changed the execution plan from a full Clustered Index Scan (40M logical reads, 95.2% plan cost) to an Index Seek — dropping the query from 148 seconds to under 10 seconds.

---

## Verification

After removing index killers, re-capture the execution plan and confirm **Index Scan → Index Seek**.

---

**← Back to [Phase 2 Overview](README.md)**
**→ Next: [Fix B — Add or Fix Indexes](03_Fix_B_Indexes.md)**
