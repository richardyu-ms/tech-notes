# SQL Profiler Guide: Capturing & Analyzing EF-Generated SQL

Use SQL Server Profiler to capture the exact SQL that Entity Framework generates when EF logs show a slow query but you need the full SQL text, execution context, and I/O stats.

> **⚠️ Overhead:** Profiler adds **10–15%** overhead — dev/test only. For production, use Extended Events (1–3%) — see below.

---

## Quick Start

```
1. SSMS → Tools → SQL Server Profiler → Connect
2. Events: SQL:BatchCompleted + RPC:Completed + Showplan XML
3. Filters: ApplicationName LIKE '%EntityFramework%'
           DatabaseName = 'YourDatabase'
           Duration >= 1000000 (μs, = 1 second)
4. F5 → trigger your scenario → Shift+F5
5. Sort by Duration → copy slowest SQL → paste into SSMS with Ctrl+M
```

**Key columns to watch:** `TextData` (SQL), `Duration` (μs), `Reads` (logical pages), `CPU` (ms), `SPID` (session ID)

---

## Analyzing Captured SQL

Copy the slow query from Profiler into SSMS with `SET STATISTICS IO ON` + `Ctrl+M` (actual execution plan).

**Three-number check:** Duration (>1s = investigate), Reads (>10K pages = high I/O), Row count (>10× expected = join explosion).

### Warning signs in EF-generated SQL

| Pattern | Problem | Anti-Pattern |
|---------|---------|--------------|
| `LOWER([col])` / `UPPER([col])` | Function kills index | I1 |
| Multiple `LEFT OUTER JOIN` | Join explosion | S1, S3 |
| `UNION ALL` with wide `SELECT` | Over-projection | S4, S5 |
| `IN (@p0 ... @p2000+)` | Parameter limit | I4 |

### Reading the execution plan

- Read right-to-left; thick arrows = many rows flowing
- Focus on operators >20% cost
- **Clustered Index Scan** = full table scan → add index
- **Key Lookup** = index needs `INCLUDE` columns
- **⚠️ Yellow triangle** = stale statistics → `UPDATE STATISTICS`

---

## Extended Events (Production Alternative)

For production where Profiler's overhead is unacceptable, use Extended Events (1–3% overhead). Profiler is deprecated by Microsoft; XEvents is the recommended replacement.

| | SQL Profiler | Extended Events |
|--|-------------|-----------------|
| Overhead | 10-15% | 1-3% |
| Production safe | ❌ | ✅ |
| Future support | ⚠️ Deprecated | ✅ Recommended |

### Create an XEvent session

```sql
CREATE EVENT SESSION [SlowQueries] ON SERVER
ADD EVENT sqlserver.sql_batch_completed(
    ACTION(sqlserver.sql_text, sqlserver.database_name, sqlserver.session_id)
    WHERE duration > 1000000)  -- 1 second in microseconds
ADD TARGET package0.event_file(
    SET filename=N'SlowQueries.xel', max_file_size=(100))
WITH (MAX_MEMORY=4096 KB, STARTUP_STATE=ON);

ALTER EVENT SESSION [SlowQueries] ON SERVER STATE = START;
```

---

**← Back to [Phase 1 — Discover](README.md)**
