# SQL Profiler Quick Reference

One-page cheat sheet for common SQL Server Profiler tasks.

## Quick Start

1. Open **SQL Server Profiler** → File → New Trace
2. Connect to your SQL Server instance
3. Choose template: **TSQL_Duration** (for performance) or **Standard** (for general)
4. Set filters (see below) → Run

## Essential Events to Capture

| Event | Category | Use Case |
|-------|----------|----------|
| `SQL:BatchCompleted` | TSQL | Capture completed queries with duration, reads, writes |
| `RPC:Completed` | Stored Procedures | Capture parameterized queries from EF/ORMs |
| `Showplan XML` | Performance | Capture execution plans (expensive — use sparingly) |

## Must-Have Columns

| Column | Why |
|--------|-----|
| **Duration** | Query execution time (microseconds) |
| **CPU** | CPU time consumed |
| **Reads** | Logical page reads |
| **Writes** | Page writes |
| **TextData** | The actual SQL statement |
| **DatabaseName** | Filter to your database |
| **SPID** | Session ID — filter to your app |

## Filters (Column Filters)

Set these to reduce noise:

```
DatabaseName = 'YourDatabase'        -- Filter to specific DB
Duration >= 1000000                  -- Only queries > 1 second (in microseconds)
ApplicationName LIKE '%YourApp%'     -- Filter to your application
```

## Alternative: SET STATISTICS

Run these before your query for inline diagnostics:

```sql
SET STATISTICS IO ON;    -- Shows logical/physical reads per table
SET STATISTICS TIME ON;  -- Shows CPU and elapsed time

-- Run your query here
SELECT * FROM Devices WHERE DeviceName IN ('Device1', 'Device2');

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
```

### Reading STATISTICS IO Output

```
Table 'Device'. Scan count 1, logical reads 4845, physical reads 0, read-ahead reads 0.
```

| Metric | Meaning |
|--------|---------|
| **Scan count** | Number of times the table was accessed (high = Nested Loop Join) |
| **Logical reads** | Pages read from buffer pool (memory). 1 page = 8 KB |
| **Physical reads** | Pages read from disk (cold cache) |
| **Read-ahead reads** | Pages pre-fetched by SQL Server |

**Rule of thumb:** `Logical reads × 8 KB = Total data volume processed`

## DMV Queries for Live Analysis

### Top 10 Slowest Queries (by average elapsed time)

```sql
SELECT TOP 10
    qs.total_elapsed_time / qs.execution_count AS avg_elapsed_time,
    qs.execution_count,
    SUBSTRING(qt.text, (qs.statement_start_offset/2) + 1,
        ((CASE qs.statement_end_offset
            WHEN -1 THEN DATALENGTH(qt.text)
            ELSE qs.statement_end_offset END
        - qs.statement_start_offset)/2) + 1) AS query_text
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) qt
ORDER BY avg_elapsed_time DESC;
```

### Table Sizes (for I/O analysis)

```sql
SELECT
    t.name AS [Table],
    p.rows AS [Rows],
    CAST(ROUND(SUM(a.total_pages) * 8 / 1024.0, 2) AS NUMERIC(36,2)) AS [Size_MB]
FROM sys.tables t
JOIN sys.indexes i ON t.object_id = i.object_id
JOIN sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
JOIN sys.allocation_units a ON p.partition_id = a.container_id
WHERE t.is_ms_shipped = 0 AND i.object_id > 255
GROUP BY t.name, p.rows
HAVING p.rows > 1000
ORDER BY [Size_MB] DESC;
```

## Common Patterns

### Identifying Non-SARGable Predicates

Watch for these in query plans:
- **Table Scan** or **Index Scan** where you expected a **Seek**
- Functions wrapping indexed columns: `WHERE LOWER(Column) = ...`, `WHERE CAST(Column AS ...) = ...`
- Implicit conversions: mismatched types forcing conversion

### Quick Performance Checklist

- [ ] Are there Table Scans where Index Seeks are expected?
- [ ] Are there functions on indexed columns in WHERE clauses?
- [ ] Is `Logical reads` proportional to result set size (not entire table)?
- [ ] Is `Scan count` reasonable (high count + high reads = Nested Loop + Scan)?
- [ ] Are there missing indexes suggested in the execution plan XML?
