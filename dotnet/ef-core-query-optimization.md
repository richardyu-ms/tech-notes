# EF Core / LINQ Query Optimization: The `.ToLower()` Lesson

> How a single `.ToLower()` call turned an Index Seek into a 307 GB Full Table Scan — and how we fixed it (148s → 7s).

## The Problem

A LINQ query filtering devices by name was taking **148 seconds** and blocking downstream deployments with HTTP 500 timeouts.

```csharp
// BROKEN CODE — Forces SQL to apply LOWER() to every row
var valueArray = filterOption.Value?.ToLower().Tokenize()?.ToList();
devices = devices.Where(dev => valueArray.Contains(dev.DeviceName.ToLower()));
```

**What happened at the SQL level:**
- EF translated this to: `WHERE LOWER(DeviceName) IN ('value1', 'value2', ...)`
- `LOWER()` applied to every row makes the predicate **non-SARGable** — the index becomes useless
- SQL Server fell back to a **Full Table Scan** on every query

### The Math Behind the 300 GB Scan

| Table | Size | Outer Loop | Total I/O |
|-------|------|------------|-----------|
| MetadataProperty | 184 MB | ~1,700 devices | **312,936 MB ≈ 305 GB** |

$$\text{Total I/O} = \text{Table Size} \times \text{Outer Loop Iterations} = 184 \text{ MB} \times 1{,}700 \approx 305 \text{ GB}$$

This matched the observed 307 GB of logical reads in the slow query logs.

## Why `.ToLower()` Broke Performance

SQL Server's collation (`SQL_Latin1_General_CP1_CI_AS`) is already **case-insensitive**. But the index histogram uses binary comparison:

```
Index Statistics Histogram:  'DEVICE-0101-0152-01BT0' (uppercase, stored as-is)
Query After .ToLower():      'device-0101-0152-01bt0' (lowercase)
Binary Comparison Result:    'DEVICE...' ≠ 'device...' ❌ MISMATCH
Optimizer Decision:          No histogram match → Use density vector → Index Scan (slow)
```

The optimizer couldn't estimate cardinality from the histogram → chose a safe but catastrophically slow plan.

## The Fix: 5 Coordinated Changes

### 1. Remove `.ToLower()` — Let Database CI Collation Work (PRIMARY FIX)

```csharp
// FIXED — database collation handles case-insensitivity natively
var valueArray = filterOption.Value?.Tokenize()?.ToHashSet() ?? new HashSet<string>();
devices = devices.Where(dev => valueArray.Contains(dev.DeviceName));
```

SQL now generates `WHERE DeviceName IN (...)` → index can be used → **Table Scan becomes Index Seek**.

### 2. `List<T>` → `HashSet<T>` — O(n) to O(1) Lookups

```csharp
// Before: List.Contains() = O(n) linear search (2,000 comparisons per row)
var valueArray = filterOption.Value?.Tokenize()?.ToList();

// After: HashSet.Contains() = O(1) hash lookup
var valueArray = filterOption.Value?.Tokenize()?.ToHashSet();
```

### 3. Remove `StringComparison.OrdinalIgnoreCase` — Cleaner SQL

```csharp
// Before: Complex EF translation
dev.DeviceName.Equals(value, StringComparison.OrdinalIgnoreCase)

// After: Direct SQL → WHERE DeviceName = @param
dev.DeviceName == value
```

### 4. Extract Filter Methods — Eliminate 80+ Lines of Duplication

Created dedicated methods: `ApplyDeviceNameFilter()`, `ApplyDatacenterFilter()`, `ApplySkuFilter()`, etc.

### 5. Add Targeted Index

```sql
CREATE NONCLUSTERED INDEX IX_MetadataProperty_DeviceName
ON MetadataProperty (DeviceName);
```

## Results

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| **Query Time** | 148s | 7s | **95.3%** (21x faster) |
| **CPU Time** | 142s | <2s | **98.6%** (71x faster) |
| **Logical Reads** | 40M pages | 405K pages | **99% reduction** |
| **I/O Data Volume** | ~311 GB | ~3 GB | **99% reduction** |
| **Query Plan** | Table Scan | Index Seek | Optimized |
| **Row Count** | 177,444 | 177,450 | Identical results |

### With Index Tuning (Stage 2)

| Metric | Stage 1 (Code Fix) | Stage 2 (+ Index) | Improvement |
|--------|--------------------|--------------------|-------------|
| **Elapsed** | 10.6s | 10.1s | ~5% faster |
| **CPU** | 2.5s | 2.0s | **20% faster** |
| **Total Reads** | 405,473 | 354,908 | 12% reduction |

## Join Optimization Pattern

Another issue found in the same codebase — a join that skipped the leading column of a composite primary key:

```csharp
// WRONG — Only uses DeviceName (skips leading column)
on device.DeviceName equals metadata.DeviceName

// Composite Primary Key: (DatacenterName, DeviceName, PropertyName)

// CORRECT — Uses both DatacenterName AND DeviceName
on new { device.DeviceName, dc = buildoutFolder }
equals new { metadata.DeviceName, dc = metadata.DatacenterName }
```

**Lesson:** Always include the leading column(s) of composite indexes/keys in your join conditions.

## Key Lessons

1. **Never apply functions to indexed columns in WHERE clauses** — `WHERE LOWER(Column)` destroys index usage
2. **Trust database collation** — SQL Server CI collation handles case-insensitivity; don't duplicate in code
3. **Use `HashSet<T>` for large Contains() operations** — O(1) vs O(n)
4. **Use `==` over `.Equals()` in EF queries** — cleaner SQL translation
5. **Profile with execution statistics** — `SET STATISTICS IO ON` reveals the real I/O picture
6. **Small code changes, massive impact** — One `.ToLower()` removal = 95% performance gain
