# Phase 1.2 — Benchmarking: Establishing a Reproducible Baseline

## Why Benchmarking Matters

Logs tell you what happened in production. Benchmarking tells you what happens **under controlled conditions** — same machine, same data, same parameters, every time. The difference matters because:

**You can't prove an optimization worked without a baseline.** If you fix a query and it "feels faster," that's not evidence. Maybe the SQL Server cache was warm. Maybe the machine was idle. Maybe the data changed. A benchmark gives you a number you can compare against.

**Logs capture one moment; benchmarks capture a range.** A single log entry might show 148s for a device filter query, but that could be an outlier. A benchmark gives you min, P50, P99 across multiple runs — so you know the *typical* behavior, not just one observation.

**Benchmarks force you to pick a representative workload.** The act of choosing "a large datacenter with 1,801 devices" as your benchmark workload forces you to think about whether your fix will work at that scale — and whether it'll also work for a 100-device DC or a 10,000-device DC.

### What benchmarking gives you

| What | Description | Example from our journey |
|------|-------------|--------------------------|
| **A starting number** | The exact duration, row count, and resource usage before any change | 148,234 ms, 177,444 rows, 40.8M logical reads |
| **A comparison target** | After fixing, measure again with the same setup | 162 ms, 1,801 rows, ~5,000 logical reads |
| **A scaling prediction** | How the fix behaves at different data sizes | Works for 1,801 devices; also tested with 100 and 5,000 |
| **Proof for the PR** | Concrete numbers for code review, not "trust me it's faster" | Fill in the before/after template — see Phase 3 for the format |

---

## The Benchmark Methodology

1. **Pick a representative workload** — must be production-scale data (dev with 100 devices hides problems)
2. **Freeze the database state** — backup/restore or snapshot so data doesn't change between runs
3. **Warm-up run** — one discarded run fills SQL buffer pool + JIT
4. **Run 3–5 measured iterations** — capture min/avg/P99, not just one number
5. **Capture timing** — workflow phase logs (APISUMMARY) + EF interceptor logs
6. **Capture SQL metrics** — SSMS `SET STATISTICS IO ON; SET STATISTICS TIME ON;` + actual execution plans (`Ctrl+M`)
7. **Record database state** — row counts, table sizes
8. **Document everything** — fill in the template below

**Key metrics and red flags:**

| Metric | Red flag |
|--------|----------|
| Wall-clock duration | > SLA target |
| SQL queries executed | > 50 per request → N+1 |
| Rows returned vs expected | > 5× ratio → join explosion |
| Total logical reads | > 1M → suspicious for single-entity queries |
| Execution plan top operator | Scan → bad; Seek → good |
| Key Lookups | Present → index needs INCLUDE columns |

---

## Baseline Capture Template

Copy this for every method/endpoint you benchmark — it becomes the "before" in your before/after comparison ([Phase 3 — Validation](../Phase3_Validate_and_Monitor/01_Validation.md)).

```markdown
## Baseline: [Method/Endpoint Name] — [Date]
**Workload:** [datacenter, device count, filter parameters]
**App Version / Database / Machine:** [...]
```
| Metric | Run 1 | Run 2 | Run 3 | Avg |
|--------|-------|-------|-------|-----|
| Wall-clock (ms) || | | |
| SQL total (ms) | | | | |
| SQL count | | | | |
| Rows returned / Expected | | | | |
| Total logical reads | | | | |
| Plan top operator | | | | |

---

## Running the Benchmark After a Fix

Repeat the exact same benchmark (same workload, parameters, machine): warm-up → 3–5 runs → fill in "After" column → compute speedup (`baseline_ms / optimized_ms`). Full before/after template is in [Phase 3 — Validation](../Phase3_Validate_and_Monitor/01_Validation.md).

---

## Common Pitfalls

- **Dev-only data** — 100 devices hides problems that appear at 1,800+. Always use production-scale data.
- **Cold vs warm cache** — do a warm-up run; report both cold and warm numbers.
- **Ignoring P99** — average looks good but tail latency may still be bad. Capture min/P50/P95/P99.
- **Database state changed** — use a snapshot or freshly restored backup between baseline and after.

---

**→ Next: [Dashboard](03_Dashboard.md)**
**← Back to [Logging System](01_Logging_System.md)**
**← Back to [Phase 1 — Discover](README.md)**
