# Index Health Monitoring

> **Part of [Phase 3 — Validate & Monitor](README.md)**

Indexes degrade over time due to fragmentation and data growth. Set up periodic checks to ensure optimizations stay effective.

---

## Index Usage

| `user_seeks` | `user_scans` | Assessment | Action |
|-------------|-------------|-----------|--------|
| High | Low | Index is effective | No action |
| Low | High | Index not seeking | Check predicates for functions |
| 0 | 0 | Unused index | Consider dropping (saves write overhead) |
| Any | High `user_updates` | Write-heavy | Evaluate if index is worth the write cost |

### Index usage query

```sql
SELECT
    OBJECT_NAME(s.object_id) AS TableName,
    i.name AS IndexName,
    i.type_desc AS IndexType,
    s.user_seeks,
    s.user_scans,
    s.user_lookups,
    s.user_updates,
    CASE
        WHEN s.user_seeks + s.user_scans = 0 THEN 'UNUSED - consider dropping'
        WHEN s.user_scans > s.user_seeks * 10 THEN 'MOSTLY SCANNED - check predicates'
        ELSE 'HEALTHY'
    END AS Assessment
FROM sys.dm_db_index_usage_stats s
INNER JOIN sys.indexes i
    ON s.object_id = i.object_id AND s.index_id = i.index_id
WHERE s.database_id = DB_ID()
ORDER BY s.user_seeks + s.user_scans DESC;
```

## Index Fragmentation

| Fragmentation | Action |
|---------------|--------|
| < 10% | No action |
| 10-30% | `ALTER INDEX ... REORGANIZE` (online, low impact) |
| > 30% | `ALTER INDEX ... REBUILD` (more resources, may lock) |

### Fragmentation check query

```sql
SELECT
    OBJECT_NAME(ips.object_id) AS TableName,
    i.name AS IndexName,
    ips.avg_fragmentation_in_percent,
    ips.page_count,
    CASE
        WHEN ips.avg_fragmentation_in_percent < 10 THEN 'OK'
        WHEN ips.avg_fragmentation_in_percent < 30 THEN 'REORGANIZE'
        ELSE 'REBUILD'
    END AS Action
FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ips
INNER JOIN sys.indexes i
    ON ips.object_id = i.object_id AND ips.index_id = i.index_id
WHERE ips.page_count > 1000  -- Skip tiny tables
ORDER BY ips.avg_fragmentation_in_percent DESC;
```

## Missing Index Recommendations

SQL Server tracks queries that would benefit from new indexes via `sys.dm_db_missing_index_details`. Review the ImpactScore (cost × impact × usage) to prioritize.

```sql
SELECT TOP 20
    ROUND(migs.avg_total_user_cost * migs.avg_user_impact *
        (migs.user_seeks + migs.user_scans), 0) AS ImpactScore,
    mid.statement AS TableName,
    mid.equality_columns,
    mid.inequality_columns,
    mid.included_columns,
    migs.user_seeks,
    migs.user_scans
FROM sys.dm_db_missing_index_groups AS mig
INNER JOIN sys.dm_db_missing_index_group_stats AS migs
    ON mig.index_group_handle = migs.group_handle
INNER JOIN sys.dm_db_missing_index_details AS mid
    ON mig.index_handle = mid.index_handle
ORDER BY ImpactScore DESC;
```

---

## Maintenance Schedule

| Frequency | Action |
|-----------|--------|
| **Weekly** | Check index usage stats — ensure seeks > 0 for key indexes |
| **Monthly** | Check fragmentation — reorganize/rebuild as needed |
| **Quarterly** | Review missing index DMV — evaluate high-impact recommendations |
| **After major data load** | Verify execution plans; `UPDATE STATISTICS` on affected tables |

---

**← Back to [Phase 3 Overview](README.md)**
**→ Next: [The Iterative Loop](04_Iterative_Loop.md)**
