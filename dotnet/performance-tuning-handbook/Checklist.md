# Performance Tuning Checklist

A high-level checklist covering all three phases. Use this as your tracking sheet during an optimization cycle. Detailed checklists are available per phase.

---

## Phase 1 — Discover

- [ ] **Logging enabled** — HTTP middleware, workflow phase summaries, EF SQL interceptor
- [ ] **Baseline captured** — wall-clock, SQL count, SQL duration, row count, logical reads
- [ ] **Dashboard configured** — version trend, engine breakdown, method heatmap, SQL stats
- [ ] **Deep diagnostics run** — execution plan, I/O stats, DMV queries
- [ ] **Priority list created** — impact score computed, P0–P3 assigned, top targets selected

→ [Phase 1 Detailed Checklist](Phase1_Discover/Checklist_Phase1.md)

---

## Phase 2 — Diagnose & Fix

- [ ] **Root cause identified** — anti-pattern ID assigned (S1–S5, I1–I5, C1–C6, X1–X4)
- [ ] **Fix A applied** — index-killing functions removed (`.ToLower()`, date functions)
- [ ] **Fix B applied** — covering/composite/filtered indexes created
- [ ] **Fix C applied** — monolithic JOINs split into batch + dictionary assembly
- [ ] **Fix D applied** — `.AsNoTracking()`, `HashSet`, parallel, batching
- [ ] **Fix E applied** — Server GC, logging gates, algorithm optimization

→ [Phase 2 Detailed Checklist](Phase2_Diagnose_and_Fix/Checklist_Phase2.md)

---

## Phase 3 — Validate & Monitor

- [ ] **Before/after comparison** — measurable improvement documented
- [ ] **Output diff** — semantically identical output confirmed
- [ ] **Tests passing** — existing + new edge-case tests
- [ ] **Monitoring active** — dashboard updated, alerts configured
- [ ] **Index health checked** — seeks > 0, fragmentation < 30%
- [ ] **Decision made** — continue optimizing or close cycle

→ [Phase 3 Detailed Checklist](Phase3_Validate_and_Monitor/Checklist_Phase3.md)

---

**← Back to [Index](README.md)**
