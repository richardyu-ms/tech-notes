# Phase 2 Checklist — Diagnose & Fix

Use this while actively diagnosing and fixing a performance problem. For the high-level overview, see the [General Checklist](../Checklist.md).

---

## Diagnosis

- [ ] Root cause mapped to cost layer (Layer 1–4, top-down)
- [ ] Anti-pattern ID assigned from [catalog](README.md#3-anti-pattern-quick-reference) (S1–S5, I1–I5, C1–C6, X1–X4)
- [ ] [Decision tree](01a_Decision_Tree.md) followed to determine fix category

---

## Fix A: Code Patterns

- [ ] Index-killing functions removed from WHERE/JOIN clauses (`.ToLower()`, `.Trim()`, date functions)
- [ ] Execution plan re-captured — **Scan → Seek** confirmed

---

## Fix B: Indexes

- [ ] Covering index created with appropriate seek key and INCLUDE columns
- [ ] Composite key order validated (most selective column first)
- [ ] Key Lookups eliminated after index creation
- [ ] Write overhead assessed for new indexes

---

## Fix C: Query Architecture

- [ ] Monolithic JOIN identified and split into separate queries
- [ ] Row ratio after split is near 1:1
- [ ] Batch size set to 2000 items per chunk
- [ ] Dictionary assembly uses `StringComparer.OrdinalIgnoreCase`
- [ ] Deferred filters applied in-memory after base entity set is materialized

---

## Fix D: Client-Side

- [ ] `.AsNoTracking()` added to every read-only query
- [ ] `HashSet<T>` used for `Contains()` checks
- [ ] `.ToList().ToCollection()` replaced with direct cast
- [ ] Sub-collections sorted deterministically
- [ ] Early exit guards on null/empty input
- [ ] Batching strategy tested at production scale

---

## Fix E: System Tuning

- [ ] Server GC enabled for batch workloads
- [ ] Verbose SQL logging disabled in production
- [ ] O(n×m) linear scans replaced with dictionary lookups

---

## Phase 2 Exit

- [ ] Fix applied and compiles
- [ ] Anti-pattern IDs tagged in PR description
- [ ] Ready for Phase 3 validation

---

**→ Next: [Phase 3 Checklist](../Phase3_Validate_and_Monitor/Checklist_Phase3.md)**
**← Back: [Phase 1 Checklist](../Phase1_Discover/Checklist_Phase1.md)**
**← Back to [General Checklist](../Checklist.md)**
