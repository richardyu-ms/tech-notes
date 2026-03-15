# Phase 1.5 — Prioritization: What to Fix First

Fix the thing that helps **the most users, the most often.** A 5s query called 100K times/day has far more impact than a 60s query called once a week.

---

## Impact Score Formula

$$\text{Impact Score} = \text{Frequency (calls/day)} \times \text{Duration (ms)} \times \text{Blast Radius}$$

| Blast Radius | Definition | Examples |
|-------------|-----------|---------|
| **3 — Critical path** | Blocks production | Seed pipeline, buildout, device provisioning |
| **2 — Commonly used** | Frequent user/automated calls | Device search, filter, datacenter listing |
| **1 — Internal/admin** | Low user impact | Admin dashboard, diagnostic endpoints |

---

## Priority Matrix

| Priority | Impact Score | Timeline |
|----------|-------------|----------|
| **P0 — Critical** | > 100,000 | This week |
| **P1 — High** | 50,000 – 100,000 | This sprint |
| **P2 — Medium** | 10,000 – 50,000 | Next sprint |
| **P3 — Low** | < 10,000 | Backlog |

---

## Our Real Prioritization

| # | Endpoint / Query | Impact Score | Priority |
|---|-----------------|-------------|----------|
| 1 | Device filter query | **33.3 Billion** | **P0** |
| 2 | `GET /deviceultra/{dc}` | **136.5 Million** | **P0** |
| 3 | Group engine inner loop | **84 Million** | **P1** |
| 4 | GC pauses (Workstation GC) | **~30 Million** | **P1** |
| 5 | Verbose SQL logging | **~10 Million** | **P2** |
| 6 | ToCollection double alloc | **~5 Million** | **P2** |

---

## Qualitative Adjustments

- **Fix difficulty** — A 5-minute fix (remove `.ToLower()`) goes first even if impact is moderate
- **Dependencies** — Some fixes enable others (fixing base query enables fixing datacenter endpoint)
- **Data growth** — A table growing 10%/month means today's P2 becomes tomorrow's P0
- **Risk** — Query rewrite has higher test burden than an index addition

---

## Checklist

- [ ] Impact score computed for each bottleneck
- [ ] Priority assigned (P0–P3)
- [ ] Quick wins identified
- [ ] Top 1–3 targets selected for Phase 2
- [ ] Baseline numbers captured (from benchmarking)
- [ ] Root cause hypothesis formed (from diagnostic tools)

---

**→ Next: [Phase 2 — Diagnose & Fix](../Phase2_Diagnose_and_Fix/README.md)**
**← Back to [Advanced Diagnostic Tools](04_Advanced_Diagnostic_Tools.md)**
**← Back to [Phase 1 — Discover](README.md)**
