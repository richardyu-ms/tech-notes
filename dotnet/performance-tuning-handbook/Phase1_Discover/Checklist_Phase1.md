# Phase 1 Checklist — Discover

Use this during an active investigation. For the high-level overview, see the [General Checklist](../Checklist.md).

---

## Logging

- [ ] HTTP request duration logging enabled with appropriate threshold
- [ ] Workflow phase summaries logging enabled (per-phase duration breakdown)
- [ ] EF SQL interceptor enabled with slow query threshold configured
- [ ] Verbose SQL logging disabled in production (high overhead)
- [ ] Correlation ID links all layers — can trace from HTTP → phase → SQL
- [ ] Log volume is manageable at current threshold settings
- [ ] Logs are being ingested to telemetry system (e.g., Kusto)

---

## Benchmarking

- [ ] Representative workload selected using **production-scale data**
- [ ] Specific parameters documented (datacenter, device count, filters)
- [ ] Database state frozen (backup/snapshot) for reproducibility
- [ ] Warm-up run completed and discarded
- [ ] 3–5 measured runs recorded
- [ ] Machine specs and environment documented
- [ ] Key metrics captured: wall-clock duration, SQL count, SQL duration, row count, logical reads
- [ ] Row ratio computed (`returned / expected`) — should be near 1:1; >5× = join explosion
- [ ] Execution plan saved for the slowest query
- [ ] Top table by logical reads identified

---

## Dashboard

- [ ] Telemetry tables populated with recent data (last 24h)
- [ ] Key tiles configured: version trend, top costly buildouts, engine breakdown, method heatmap, SQL stats, error rate
- [ ] Auto-refresh interval set for production monitoring
- [ ] Dashboard link documented and shared with team
- [ ] At least 14 days of data visible for trend analysis

---

## Advanced Diagnostics

- [ ] **Execution plan** captured (actual, not estimated) for target query
- [ ] Plan operators reviewed — identify scans, key lookups, expensive joins
- [ ] Highest-cost operator identified as optimization target
- [ ] **I/O statistics** captured — logical reads per table recorded
- [ ] Table with most logical reads identified
- [ ] **DMV queries** run: missing index recommendations, index usage stats, fragmentation
- [ ] **SQL Profiler / Extended Events** configured if EF-generated SQL needs capturing

---

## Prioritization

- [ ] Impact score computed for each bottleneck: `Frequency × Duration × Blast Radius`
- [ ] Priority assigned (P0–P3) based on impact score
- [ ] Quick wins identified (easy fixes regardless of score)
- [ ] Top 1–3 targets selected for Phase 2
- [ ] Baseline numbers captured for each target
- [ ] Root cause hypothesis formed for each target
- [ ] Fix difficulty estimated

---

**→ Next: [Phase 2 Checklist](../Phase2_Diagnose_and_Fix/Checklist_Phase2.md)**
**← Back to [General Checklist](../Checklist.md)**
**← Back to [Phase 1 Overview](README.md)**
