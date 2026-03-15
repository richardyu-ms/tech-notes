# Phase 3 — Validate & Monitor

**Goal:** Prove measurable improvement on the same workload, then monitor for regressions.

> **Principle:** Optimization is iterative. Every fix shifts the bottleneck. Loop: Fix → Validate → Monitor → Discover → back to Phase 2.

---

## Phase 3 Structure

| Section | Page | Purpose |
|---------|------|---------|
| **Validation** | [01_Validation.md](01_Validation.md) | Before/after comparison, output diffing, pitfalls |
| **Monitoring** | [02_Monitoring.md](02_Monitoring.md) | Dashboard signals, alerts, investigation flowchart |
| **Index Health** | [03_Index_Health.md](03_Index_Health.md) | Index usage, fragmentation, missing index checks |
| **Iterative Loop** | [04_Iterative_Loop.md](04_Iterative_Loop.md) | When to stop/restart, docs maintenance |
| **Checklist** | [Checklist_Phase3.md](Checklist_Phase3.md) | Item-by-item checklist |

---

## Phase 3 Exit

- [ ] Before/after numbers documented with measurable improvement
- [ ] Output diff confirming functional equivalence
- [ ] Dashboard monitoring in place with alerts
- [ ] Index health checked
- [ ] Next iteration target identified or cycle closed

---

**← Back to [Phase 2 — Diagnose & Fix](../Phase2_Diagnose_and_Fix/README.md)**
**← Back to [Index](../README.md)**
