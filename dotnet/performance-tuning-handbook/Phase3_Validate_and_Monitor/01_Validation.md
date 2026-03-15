# Validation: Proving the Fix

> **Part of [Phase 3 — Validate & Monitor](README.md)**

---

## Steps

| # | Step | Detail |
|---|------|--------|
| 1 | Reproduce baseline workload | Same datacenter, filters, DB snapshot |
| 2 | Run optimized code | Warm-up run first, then measure 3–5 runs |
| 3 | Capture after-numbers | Duration, row count, execution plan, logical reads |
| 4 | Compare side by side | Fill in before/after template below |
| 5 | Verify functional correctness | Diff output JSON — must be semantically identical |
| 6 | Run tests | All existing tests pass + new edge-case tests |
| 7 | Test at production scale | Use representative production snapshot |

### What "Done" Looks Like

- Before/after numbers showing measurable improvement
- Execution plan showing Seeks instead of Scans
- Output diff confirming semantic equivalence
- All tests passing
- Numbers from production-scale data

---

## Before/After Comparison Template

```markdown
## Optimization: [Method/Endpoint Name] — [Date]

**Workload:** [datacenter, device count, filter parameters]
```
| Metric | Before | After | Change |
|--------|--------|-------|--------|
| Wall-clock duration | ___ ms | ___ ms | ___× faster |
| SQL queries executed | ___ | ___ | ___% fewer |
| Total SQL duration | ___ ms | ___ ms | ___% less |
| Rows returned | ___ | ___ | ___% fewer |
| Row ratio (returned / expected) | ___× | ___× | near 1:1? |
| Logical reads | ___ pages | ___ pages | ___% fewer |
| Plan top operator | Scan/Hash/Sort | Seek/NLoop | improved? |
| Key Lookups | Yes/No | Yes/No | eliminated? |
| Output diff | — | Semantically identical | ✅ / ❌ |

---

## Our Key Results

| Optimization | Before | After | Speedup |
|-------------|--------|-------|---------|
| Device filter query | 148,234 ms, 177K rows | 162 ms, 1,801 rows | **915×** |
| DeviceUltra endpoint | 91,191 ms | ~1,800 ms | **~50×** |
| Logical reads (filter) | 40.8M pages (311 GB) | ~5,000 pages | **8,000×** |

---

## Output Diffing

1. Capture output from old code and new code (same input parameters)
2. Normalize: remove timestamps/GUIDs, sort sub-collections, normalize NULL vs empty
3. Diff the files — expect zero meaningful differences

| OK to Differ | NOT OK |
|-------------|--------|
| Sub-collection ordering (new code sorts explicitly) | Missing entities (filter logic changed) |
| NULL vs empty collection | Extra entities (filter widened) |
| Trailing whitespace | Wrong field values (wrong JOIN match) |

---

## Validation Pitfalls

| Pitfall | Mitigation |
|---------|------------|
| Different data volume (dev vs prod) | Always validate at production scale |
| Cold cache vs warm cache | Report both; do a warm-up run |
| Ignoring P99 | Capture min, P50, P95, P99 |
| No output diff | Always diff old vs new output |
| Testing only one buildout size | Test small (<100), medium (1-2k), large (5k+) |
| Not recording baseline | Capture Phase 1 baseline BEFORE changes |

---

**→ Next: [Monitoring](02_Monitoring.md)**
**← Back to [Phase 3 Overview](README.md)**
