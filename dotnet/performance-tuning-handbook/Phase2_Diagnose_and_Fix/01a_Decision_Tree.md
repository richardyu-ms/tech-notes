# Diagnostic Decision Tree

> **Part of [Phase 2 — Diagnose & Fix](README.md)**

Use this tree to map a slow query to the correct fix category.

---

```mermaid
flowchart TB
    Start["Query is slow"]
    Q1{"Row count >> expected entity count?"}
    Q2{"Execution plan shows Index Scan?"}
    Q3{"Execution plan shows Key Lookup?"}
    Q4{"Plan shows large Sort / Hash Match?"}
    Q5{"SQL fast but EF materialization slow?"}
    Q6{"SqlException: ran out of internal resources?"}

    A1["JOIN EXPLOSION - Layer 1, Fix C<br>Anti-patterns: S1, S3, S5"]
    A2["Function on column / missing index - Layer 2, Fix A/B<br>Anti-patterns: I1, I5, I2, I3"]
    A3["Index hits but columns missing - Layer 2, Fix B: Add INCLUDE<br>Anti-pattern: I2"]
    A4["Sort-then-filter - Layer 1/2, Fix A: Move OrderBy after Where<br>Anti-patterns: S2, S5"]
    A5["Client-side overhead - Layer 4, Fix D: AsNoTracking/parallel/dictionary<br>Anti-patterns: C1-C6"]
    A6["Too many parameters - Layer 2, Batch Contains into chunks of 2000<br>Anti-pattern: I4"]
    A7["Profile network latency / connection pool / GC - Fix E"]

    Start --> Q1
    Q1 -- YES --> A1
    Q1 -- NO --> Q2
    Q2 -- YES --> A2
    Q2 -- NO --> Q3
    Q3 -- YES --> A3
    Q3 -- NO --> Q4
    Q4 -- YES --> A4
    Q4 -- NO --> Q5
    Q5 -- YES --> A5
    Q5 -- NO --> Q6
    Q6 -- YES --> A6
    Q6 -- NO --> A7
```

---

## How to Use

1. Start with "[Query is slow]" and answer each question using data from Phase 1
2. The leaf node tells you which **Layer**, **Fix category**, and **Anti-pattern IDs** apply
3. Go to the corresponding Fix page (A–E) for the solution

---

**← Back to [Phase 2 Overview](README.md)**
