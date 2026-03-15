# Fix B — Add or Fix Indexes

**Layer:** 2 (Index Utilization) | **Effort:** Medium | **Impact:** 2-10×

> Anti-patterns: **I2** (Missing Covering Index), **I3** (Wrong Composite Key Order)

---

## Index Types

| Index Type | When to Use | Pattern |
|------------|-------------|---------|
| **Covering** | Read-heavy; eliminate Key Lookup | `CREATE INDEX ... ON [T]([SeekKey]) INCLUDE ([col1],[col2],...)` |
| **Composite** | Multi-column WHERE / JOIN ON | Most selective column first |
| **Filtered** | Queries always filter by a flag | `WHERE [IsActive] = 1` |

---

## Example: Covering Index

**Before** — query seeks on `DeviceName` but does Key Lookup for other columns:
```sql
-- Existing index only has DeviceName as key
SELECT DeviceName, DatacenterName, Status FROM Device WHERE DeviceName = @name
-- Plan: Index Seek → Key Lookup (expensive bookmark lookup for each row)
```

**After** — covering index eliminates Key Lookup:
```sql
CREATE NONCLUSTERED INDEX IX_Device_DeviceName_Covering
ON [dbo].[Device] ([DeviceName])
INCLUDE ([DatacenterName], [Status]);
-- Plan: Index Seek only (all columns satisfied from index)
```

---

## Verification

After creating indexes, check the execution plan:

| Good Signs | Still Problems |
|------------|----------------|
| `Index Seek` on your new index | `Index Scan` — predicate may have a function |
| No `Key Lookup` | `Key Lookup` — missing INCLUDE columns |
| Thin arrows (low row count) | Thick arrows — join explosion still present |

Verify with DMV: `user_seeks > 0` and `user_scans ≈ 0` means the index is working.

### Index usage check query

```sql
SELECT
    OBJECT_NAME(s.object_id) AS TableName,
    i.name AS IndexName,
    s.user_seeks,
    s.user_scans,
    s.user_lookups,
    s.user_updates
FROM sys.dm_db_index_usage_stats s
INNER JOIN sys.indexes i
    ON s.object_id = i.object_id AND s.index_id = i.index_id
WHERE OBJECT_NAME(s.object_id) = 'Device'
ORDER BY s.user_seeks DESC;
```

---

**← Back to [Phase 2 Overview](README.md)**
**→ Next: [Fix C — Reshape Query Architecture](04_Fix_C_Query_Architecture.md)**
