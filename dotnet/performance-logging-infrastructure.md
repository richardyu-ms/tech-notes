# Performance Logging Infrastructure for .NET Services

> Building a configurable EF SQL + HTTP request duration logging stack with zero production overhead.

## Problem Statement

- **No SQL visibility** — EF queries not logged; had to use SQL Profiler for diagnostics
- **No execution metrics** — Query times not tracked; bottleneck identification difficult
- **No API timing** — HTTP endpoint durations not captured
- **No phase breakdown** — Operations showed total time but not sub-phase attribution
- **No runtime config** — No way to enable/disable logging without code changes

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│ HTTP Request Duration Logging (Middleware)               │
│ "GET /api/devices completed with 200 in 1234ms"        │
└────────────────────────┬────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────┐
│ Controller Action Execution                             │
│ (Business logic layer)                                  │
└────────────────────────┬────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────┐
│ EF SQL Query Logging (Interceptor)                      │
│ "Query executed in 1123ms (threshold: 1000ms)"          │
│ "SELECT d.Name, d.Sku FROM Device d WHERE..."          │
└─────────────────────────────────────────────────────────┘
```

## Phase 1: Entity Framework SQL Logging

### Implementation: EF Interceptor

```csharp
public class EFLoggingInterceptor : DbCommandInterceptor
{
    private readonly int _thresholdMs;
    private readonly ILogger _logger;

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        var elapsedMs = (long)eventData.Duration.TotalMilliseconds;

        if (elapsedMs >= _thresholdMs)
        {
            _logger.LogWarning(
                "SLOWSQL: Query executed in {ElapsedMs}ms (threshold: {Threshold}ms)\n{Sql}",
                elapsedMs, _thresholdMs, command.CommandText);
        }

        return result;
    }
}
```

**Key design decisions:**
- Thread-safe interceptor — uses `CommandExecutedEventData.Duration` (built-in timing)
- Logs only slow queries (above configurable threshold)
- Zero overhead for fast queries
- Rich diagnostics: SQL text, execution time, thread ID

### Configuration

```ini
EnablePerformanceLogging=true
PerformanceLoggingForSlowQueryThresholdMs=1000
```

**Recommended thresholds:**

| Environment | SQL Threshold | HTTP Threshold |
|-------------|--------------|----------------|
| Development | 100ms | 100ms |
| E2E Testing | 100ms | 5,000ms |
| Production | 1,000–5,000ms | 5,000–10,000ms |

## Phase 2: HTTP Request Duration Logging

### Implementation: ASP.NET Core Middleware

```csharp
public class RequestDurationLoggingMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!_config.EnablePerformanceLogging)
        {
            await next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();
            var elapsed = sw.ElapsedMilliseconds;

            if (elapsed >= _config.SlowHttpThresholdMs)
            {
                _logger.LogWarning(
                    "SLOWAPI: {Method} {Path} → {StatusCode} in {Elapsed}ms (threshold: {Threshold}ms)",
                    context.Request.Method, context.Request.Path,
                    context.Response.StatusCode, elapsed, _config.SlowHttpThresholdMs);
            }
            else
            {
                _logger.LogInformation(
                    "API invocation {Method} {Path} completed with status code {StatusCode} in {Elapsed}ms",
                    context.Request.Method, context.Request.Path,
                    context.Response.StatusCode, elapsed);
            }
        }
    }
}
```

### Middleware Pipeline Order

```csharp
app.UseHttpLogging();              // Built-in HTTP logging
app.UseRequestDurationLogging();   // Custom duration logging
app.UseGlobalExceptionHandler();   // Exception handling
app.MapControllers();              // Route to controllers
```

**Order is critical:** Duration logging must come BEFORE exception handler to capture the final status code.

### Features

1. **Threshold-based log levels** — Slow requests logged as Warning, fast as Information
2. **Collection parameter counting** — Automatically counts elements in collection parameters and return values (`[Params: deviceNames:2000, Return: 1500]`)
3. **Zero overhead when disabled** — If `EnablePerformanceLogging=false`, middleware skips timing entirely
4. **Exception safety** — `finally` block ensures logging even if request throws

## Phase 3: Workflow Phase Logging

### Problem
Complex operations like "Upload" or "Dump" showed total time but not where time was spent.

### Solution: Unified Metrics Architecture

```csharp
public static class PerformanceLoggingContext
{
    // Manages both query and request metrics
    public static QueryMetricsContext QueryMetrics { get; }   // All modes
    public static RequestMetricsContext RequestMetrics { get; } // REST mode only
}

public enum LoggerType
{
    // SQL metrics
    SQLSUMMARY = 0,
    SLOWSQL = 1,
    // API metrics
    APISUMMARY = 2,
    SLOWAPI = 3,
    // Workflow phases
    CLEARDBSUMMARY = 7,    // Database cleanup
    LOADETLSUMMARY = 8,    // ETL data preparation
    LOADFILESUMMARY = 9,   // File loading/parsing
    LOADDBSUMMARY = 10,    // Database upload
    DUMPDBSUMMARY = 11,    // Database export
    DUMPFILESUMMARY = 12,  // File serialization
    BUSINESSREQUESTSUMMARY = 13  // Pure business logic (calculated)
}
```

### Business Logic Time Calculation

```
BUSINESSREQUESTSUMMARY = APISUMMARY - (sum of all workflow phase durations)
```

This separates database I/O time from pure computation, enabling precise bottleneck identification.

## Log Output Examples

**Fast request:**
```log
[Information] API invocation GET /api/devices completed with status code 200 in 450 ms
```

**Slow request:**
```log
[Warning] SLOWAPI: GET /api/devices → 200 in 5234 ms (threshold: 5000 ms)
```

**Slow SQL query:**
```log
[Warning] SLOWSQL: Query executed in 1123ms (threshold: 1000ms)
SELECT d.Name, d.Sku FROM Device d WHERE d.Name IN (@p0, @p1, ...)
```

## Use Cases

1. **Identify slow endpoints** — Filter logs for `SLOWAPI` to find bottleneck APIs
2. **Correlate API ↔ SQL** — Match slow HTTP requests with the SQL queries inside them
3. **Regression detection** — Compare request durations before/after deployments
4. **Phase attribution** — Know exactly how much time is database I/O vs. business logic vs. file serialization
