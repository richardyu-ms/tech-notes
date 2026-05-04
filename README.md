# Tech Notes & Knowledge Base

Personal technical notes, learning resources, and engineering knowledge base.

## Contents

### Learning Resources
- [LLM from Scratch](llm/README.md) — Notes from "Build a Large Language Model (From Scratch)" by Sebastian Raschka
- [Semantic Kernel](semantic-kernel/README.md) — Microsoft Semantic Kernel learning notes
- [MCP (Model Context Protocol)](mcp/README.md) — MCP concepts and prototyping journey

### .NET / EF Core Performance Engineering
- [**Performance Tuning DB-Zeroing Handbook**](dotnet/performance-tuning-handbook/README.md) — Complete methodology for .NET + EF + SQL Server performance tuning: 3-phase approach (Discover → Diagnose & Fix → Validate & Monitor), 20 anti-patterns, real source code samples. 148s → 162ms (915×).
- [SQL Query Optimization: The `.ToLower()` Lesson](dotnet/ef-core-query-optimization.md) — How a single `.ToLower()` call turned an Index Seek into a 307 GB Full Table Scan, and how we fixed it (148s → 7s)
- [EF Core DeviceUltra Query Refactoring](dotnet/ef-core-query-refactoring.md) — Eliminating cross-product joins via split queries, batching, and in-memory assembly (48% faster)
- [Performance Logging Infrastructure](dotnet/performance-logging-infrastructure.md) — Building a configurable EF SQL + HTTP request duration logging stack with zero overhead
- [Service Performance Optimization Summary](dotnet/service-performance-optimization-summary.md) — End-to-end journey: 15 min → 1 min (92% reduction) across SQL, algorithms, memory, and GC

### .NET Concurrency & EF Core Migration
- [**.NET Concurrency & Multi-Threading Guide**](dotnet/concurrency/README.md) — Comprehensive guide inspired by *Java Concurrency in Practice*, adapted for .NET: ThreadPool, async/await, synchronization primitives, concurrent collections, Channels, and practical NBAaS patterns.
- [**EF Core Migration Guide**](dotnet/ef-migration/README.md) — EF6 → EF Core migration playbook: DbContext lifecycle, connection string changes, query translation differences, bulk operations, and practical migration patterns.

### Tools & Techniques
- [SQL Profiler Quick Reference](tools/sql-profiler-quick-reference.md) — One-page cheat sheet for SQL Server Profiler
- [Log Comparison Scripts](tools/log-comparison-scripts.md) — PowerShell scripts for verifying refactored code produces identical output

---

*This is a personal learning and knowledge repository. Technical patterns are generalized from real-world engineering work.*
