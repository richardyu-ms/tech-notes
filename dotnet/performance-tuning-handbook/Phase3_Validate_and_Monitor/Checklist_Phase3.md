# Phase 3 Checklist — Validate & Monitor

Use this after applying a fix (Phase 2) to prove it worked and set up monitoring. For the high-level overview, see the [General Checklist](../Checklist.md).

---

## Validation

- [ ] Same workload as baseline used (same datacenter, parameters, DB snapshot)
- [ ] Warm-up run completed; 3-5 measured runs recorded
- [ ] Before/after comparison table filled in (see [template](01_Validation.md))
- [ ] Execution plan shows Index Seek (not Scan), no Key Lookups
- [ ] Output diff shows zero meaningful differences
- [ ] All existing tests pass + new edge-case tests added
- [ ] Tested at production scale (not just dev)

---

## Monitoring

- [ ] Dashboard updated to show new version's data
- [ ] First 24h of production data reviewed
- [ ] Version-over-version P50/P99 compared — no regression
- [ ] Error rate checked — zero SqlExceptions
- [ ] Alert thresholds configured (see [Monitoring](02_Monitoring.md))
- [ ] Index usage stats verified — seeks > 0 for key indexes

---

## Iterative Loop

- [ ] Dashboard reviewed for current top bottleneck
- [ ] Before/after row added to version history
- [ ] Anti-pattern IDs logged for fixed items
- [ ] Decision: continue optimizing or close cycle?

---

**← Back: [Phase 2 Checklist](../Phase2_Diagnose_and_Fix/Checklist_Phase2.md)**
**← Back to [General Checklist](../Checklist.md)**
**← Back to [Validate & Monitor](README.md)**
