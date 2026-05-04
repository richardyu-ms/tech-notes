// =============================================================================
// NBAaS EF Core Migration Patterns — Real Upgrade Code Examples
//
// Demonstrates actual patterns from the EF6 → EF Core 8 migration in the
// NBAaS (Azure-Network-Graph) project, with EF6 "before" vs EF Core "after".
//
// Uses SQLite in-memory — no external database needed.
// Run:  dotnet run          — list all examples
//       dotnet run <number> — run a specific example
// =============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Diagnostics;

// ── Entry Point ──────────────────────────────────────────────────────────────

var examples = new (string Name, Func<Task> Action)[]
{
    ("01. Composite Keys: [Key,Column(Order)] → HasKey() Fluent API", Example01_CompositeKeys),
    ("02. DbContext Configuration: Constructor → OnConfiguring", Example02_DbContextConfig),
    ("03. Interceptor: DbInterception.Add → AddInterceptors()", Example03_Interceptor),
    ("04. ExecuteSqlCommand → ExecuteSqlRaw", Example04_RawSql),
    ("05. ChangeTracker: Manual Detach → ChangeTracker.Clear()", Example05_ChangeTracker),
    ("06. Lazy Loading: Default On → Explicit Off", Example06_LazyLoading),
    ("07. StringComparison in LINQ: Silent → Throws", Example07_StringComparison),
    ("08. Conditional Compilation: #if NET8_0_OR_GREATER", Example08_ConditionalCompilation),
};

if (args.Length > 0 && int.TryParse(args[0], out int choice) && choice >= 1 && choice <= examples.Length)
{
    await RunExample(examples[choice - 1]);
}
else
{
    Console.WriteLine("=== EF Core Migration Patterns (EF6 → EF Core 8) ===\n");
    Console.WriteLine("  NOTE: Concurrency-related patterns (PooledDbContextFactory, Parallel.Invoke→Task.WhenAll)");
    Console.WriteLine("  are in the concurrency/Practical-Pattern/ project.\n");
    for (int i = 0; i < examples.Length; i++)
        Console.WriteLine($"  {i + 1,2}. {examples[i].Name}");
    Console.WriteLine($"\nUsage: dotnet run <number>  (1-{examples.Length})");
    Console.WriteLine("Or press Enter to run all:\n");
    Console.ReadLine();
    foreach (var example in examples)
        await RunExample(example);
}

static async Task RunExample((string Name, Func<Task> Action) example)
{
    Console.WriteLine($"\n  ╔{"".PadRight(64, '═')}╗");
    Console.WriteLine($"  ║  {example.Name,-62}║");
    Console.WriteLine($"  ╚{"".PadRight(64, '═')}╝\n");
    await example.Action();
}

// ── Helper: Create in-memory SQLite database ────────────────────────────────

static NbaasDbContext CreateDb()
{
    var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
    connection.Open();
    var options = new DbContextOptionsBuilder<NbaasDbContext>()
        .UseSqlite(connection)
        .Options;
    var db = new NbaasDbContext(options);
    db.Database.EnsureCreated();
    return db;
}

static NbaasDbContext CreateSeededDb()
{
    var db = CreateDb();
    db.Devices.AddRange(
        new Device { DeviceName = "PRA-0100-0101-01T0", DeviceType = "ToRRouter", Source = "US-East" },
        new Device { DeviceName = "PRA-0100-0101-02T0", DeviceType = "ToRRouter", Source = "US-East" },
        new Device { DeviceName = "CHS-0200-0201-01MR", DeviceType = "MiddleRouter", Source = "US-South" },
        new Device { DeviceName = "DUB-0300-0301-01CR", DeviceType = "CoreRouter", Source = "EU-West" }
    );
    db.Links.AddRange(
        new Link { StartDeviceName = "PRA-0100-0101-01T0", StartPort = "Eth1/1", EndDeviceName = "PRA-0100-0101-02T0", EndPort = "Eth1/1" },
        new Link { StartDeviceName = "PRA-0100-0101-01T0", StartPort = "Eth2/1", EndDeviceName = "CHS-0200-0201-01MR", EndPort = "Eth3/1" }
    );
    db.DeviceMetadatas.AddRange(
        new DeviceMetadataEntity { DeviceName = "PRA-0100-0101-01T0", Name = "Rack", Value = "R101" },
        new DeviceMetadataEntity { DeviceName = "PRA-0100-0101-01T0", Name = "Cluster", Value = "CL01" },
        new DeviceMetadataEntity { DeviceName = "CHS-0200-0201-01MR", Name = "Rack", Value = "R201" }
    );
    db.SaveChanges();
    db.ChangeTracker.Clear();
    return db;
}

// =============================================================================
// 01. Composite Keys: [Key,Column(Order=N)] → HasKey() in Fluent API
// =============================================================================
// NBAaS had ~30+ entities with composite keys defined via data annotations.
// EF6 supported: [Key, Column(Order=0)] / [Key, Column(Order=1)]
// EF Core DOES NOT support Column(Order) for key ordering — must use HasKey().
//
// Real code (NgsDbContext.OnModelCreating):
//   modelBuilder.Entity<Link>().HasKey(x => new {
//       x.StartDeviceName, x.StartPort, x.EndDeviceName, x.EndPort });
//   modelBuilder.Entity<DeviceMetadata>().HasKey(x => new {
//       x.DeviceName, x.Name });
//   // ... 30+ more composite key definitions
// =============================================================================
static async Task Example01_CompositeKeys()
{
    Console.WriteLine("  ┌─── EF6 (BEFORE) ───────────────────────────────────────────┐");
    Console.WriteLine("  │  public class Link                                         │");
    Console.WriteLine("  │  {                                                         │");
    Console.WriteLine("  │      [Key, Column(Order = 0)]                              │");
    Console.WriteLine("  │      public string StartDeviceName { get; set; }           │");
    Console.WriteLine("  │      [Key, Column(Order = 1)]                              │");
    Console.WriteLine("  │      public string StartPort { get; set; }                 │");
    Console.WriteLine("  │      [Key, Column(Order = 2)]                              │");
    Console.WriteLine("  │      public string EndDeviceName { get; set; }             │");
    Console.WriteLine("  │      [Key, Column(Order = 3)]                              │");
    Console.WriteLine("  │      public string EndPort { get; set; }                   │");
    Console.WriteLine("  │  }                                                         │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");
    Console.WriteLine();
    Console.WriteLine("  ┌─── EF Core (AFTER) ────────────────────────────────────────┐");
    Console.WriteLine("  │  // OnModelCreating:                                       │");
    Console.WriteLine("  │  modelBuilder.Entity<Link>().HasKey(x => new {             │");
    Console.WriteLine("  │      x.StartDeviceName, x.StartPort,                      │");
    Console.WriteLine("  │      x.EndDeviceName, x.EndPort                            │");
    Console.WriteLine("  │  });                                                       │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");

    using var db = CreateSeededDb();

    // Query using composite key — works the same in both EF6 and EF Core
    var link = await db.Links.FirstOrDefaultAsync(l =>
        l.StartDeviceName == "PRA-0100-0101-01T0" && l.StartPort == "Eth1/1");

    Console.WriteLine($"\n  Found link: {link?.StartDeviceName}:{link?.StartPort} → {link?.EndDeviceName}:{link?.EndPort}");
    Console.WriteLine($"  Total links: {await db.Links.CountAsync()}");

    Console.WriteLine("\n  [Impact] NBAaS had to add HasKey() for 30+ entities in OnModelCreating.");
    Console.WriteLine("  Without this, EF Core throws: 'The entity type requires a primary key to be defined.'");
    Console.WriteLine("  The [Key] attribute alone is not enough for composite keys in EF Core.");
}

// =============================================================================
// 02. DbContext Configuration
// =============================================================================
// EF6: DbContext(string nameOrConnectionString) base constructor
// EF Core: OnConfiguring(DbContextOptionsBuilder) or constructor injection
//
// Real code (NgsDbContext.cs):
//   protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
//   {
//       if (optionsBuilder.IsConfigured) return;  // pooled path already configured
//       if (_databaseType == DataBaseType.Sqlite)
//           optionsBuilder.UseSqlite(connectionString);
//       else
//           optionsBuilder.UseSqlServer(connectionString,
//               sql => sql.CommandTimeout(Constant.EFSqlCommandTimeOutInSeconds));
//   }
// =============================================================================
static async Task Example02_DbContextConfig()
{
    Console.WriteLine("  ┌─── EF6 (BEFORE) ───────────────────────────────────────────┐");
    Console.WriteLine("  │  public NgsDbContext(string connectionString)              │");
    Console.WriteLine("  │      : base(connectionString) { }   // that's it!         │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");
    Console.WriteLine();
    Console.WriteLine("  ┌─── EF Core (AFTER) ────────────────────────────────────────┐");
    Console.WriteLine("  │  // Constructor for pooling:                               │");
    Console.WriteLine("  │  public NgsDbContext(DbContextOptions<NgsDbContext> opts)  │");
    Console.WriteLine("  │      : base(opts) { ChangeTracker.LazyLoadingEnabled=false;}│");
    Console.WriteLine("  │                                                            │");
    Console.WriteLine("  │  // Fallback for non-pooled:                               │");
    Console.WriteLine("  │  protected override void OnConfiguring(builder) {          │");
    Console.WriteLine("  │    if (builder.IsConfigured) return;                       │");
    Console.WriteLine("  │    if (isSqlite) builder.UseSqlite(connStr);               │");
    Console.WriteLine("  │    else builder.UseSqlServer(connStr, o => o.Timeout(300));│");
    Console.WriteLine("  │  }                                                         │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");

    // Demonstrate: options constructor (pooling path)
    using var db = CreateDb();
    Console.WriteLine($"\n  Provider: {db.Database.ProviderName}");
    Console.WriteLine($"  IsConfigured: true (via DbContextOptions constructor)");

    // Show the dual-path: NBAaS supports both SqlServer and SQLite
    Console.WriteLine("\n  [NBAaS Dual Provider Support]");
    Console.WriteLine("  NBAaS detects provider by checking connection string for ':memory:' token.");
    Console.WriteLine("  SQLite is used for unit tests; SqlServer for production.");
    Console.WriteLine("  EF6 had no provider-switch in OnConfiguring — connection was set in constructor.");
    await Task.CompletedTask;
}

// =============================================================================
// 03. Interceptor Migration
// =============================================================================
// EF6: DbInterception.Add(new EFLoggingInterceptor(...))  — GLOBAL static
// EF Core: optionsBuilder.AddInterceptors(...)  — PER-CONTEXT
// (Renamed from Example04 after removing PooledDbContextFactory to concurrency/)
//
// Real code (NgsWebApi.Core/Dependency.cs - EF6):
//   DbInterception.Add(new EFLoggingInterceptor(logger));
//
// Real code (DependencyRegistrar.cs - EF Core):
//   optionsBuilder.AddInterceptors(new EFLoggingInterceptor(efLogger, slowQueryThresholdMs));
//
// EF6 interceptor interface:
//   IDbCommandInterceptor.ReaderExecuting(command, context)
//   IDbCommandInterceptor.ReaderExecuted(command, context)
//
// EF Core interceptor base class:
//   DbCommandInterceptor.ReaderExecuted(command, eventData, result)
//   DbCommandInterceptor.ReaderExecutedAsync(command, eventData, result, ct)
// =============================================================================
static async Task Example03_Interceptor()
{
    // Create context with interceptor (EF Core way)
    var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
    connection.Open();

    var interceptor = new SlowQueryInterceptor(thresholdMs: 0); // log ALL queries for demo

    var options = new DbContextOptionsBuilder<NbaasDbContext>()
        .UseSqlite(connection)
        .AddInterceptors(interceptor)  // EF Core: per-context interceptor
        .Options;

    using var db = new NbaasDbContext(options);
    db.Database.EnsureCreated();
    db.Devices.Add(new Device { DeviceName = "INT-DEVICE", DeviceType = "Switch", Source = "Test" });
    await db.SaveChangesAsync();

    // This query will be logged by the interceptor
    var devices = await db.Devices.ToListAsync();
    Console.WriteLine($"  Queried {devices.Count} device(s)\n");

    Console.WriteLine($"  Interceptor captured {interceptor.LoggedQueries.Count} SQL commands:");
    foreach (var log in interceptor.LoggedQueries)
    {
        Console.WriteLine($"    [{log.DurationMs:F1}ms] {log.CommandText[..Math.Min(80, log.CommandText.Length)]}...");
    }

    Console.WriteLine("\n  ┌─── EF6 (BEFORE) ───────────────────────────────────────────┐");
    Console.WriteLine("  │  DbInterception.Add(new EFLoggingInterceptor(logger));     │");
    Console.WriteLine("  │  // GLOBAL — affects ALL DbContext instances in AppDomain  │");
    Console.WriteLine("  │  // Interface: IDbCommandInterceptor                       │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");
    Console.WriteLine("  ┌─── EF Core (AFTER) ────────────────────────────────────────┐");
    Console.WriteLine("  │  optionsBuilder.AddInterceptors(new EFLoggingInterceptor());│");
    Console.WriteLine("  │  // PER-CONTEXT — isolated, testable                       │");
    Console.WriteLine("  │  // Base class: DbCommandInterceptor                       │");
    Console.WriteLine("  │  // Async overloads return ValueTask<T> (EF Core 8)        │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");

    connection.Close();
}

// =============================================================================
// 04. ExecuteSqlCommand → ExecuteSqlRaw
// =============================================================================
// EF6:  db.Database.ExecuteSqlCommand(sql, params)
// EF Core: db.Database.ExecuteSqlRaw(sql, params)
//       or db.Database.ExecuteSqlRawAsync(sql, params)
//
// Real code (SqlScriptCenter.cs):
//   db.Database.SetCommandTimeout(sqlCommandTimeout);
//   db.Database.ExecuteSqlRaw(sqlCmd);
//
// Real code (GraphBusiness.cs):
//   await WriteDb.Database.ExecuteSqlRawAsync(sb.ToString());
// =============================================================================
static async Task Example04_RawSql()
{
    using var db = CreateSeededDb();

    Console.WriteLine("  ┌─── EF6 (BEFORE) ───────────────────────────────────────────┐");
    Console.WriteLine("  │  db.Database.ExecuteSqlCommand(                            │");
    Console.WriteLine("  │      \"DELETE FROM dbo.Device WHERE Source = @p0\", \"US-East\");│");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");
    Console.WriteLine("  ┌─── EF Core (AFTER) ────────────────────────────────────────┐");
    Console.WriteLine("  │  db.Database.ExecuteSqlRaw(                                │");
    Console.WriteLine("  │      \"DELETE FROM Device WHERE Source = {0}\", \"US-East\");   │");
    Console.WriteLine("  │  // or ExecuteSqlRawAsync for async                        │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");

    var before = await db.Devices.CountAsync();
    Console.WriteLine($"\n  Devices before: {before}");

    // EF Core: ExecuteSqlRawAsync (NBAaS GraphBusiness pattern)
    var deleted = await db.Database.ExecuteSqlRawAsync(
        "DELETE FROM Device WHERE Source = {0}", "US-East");

    var after = await db.Devices.CountAsync();
    Console.WriteLine($"  ExecuteSqlRawAsync deleted {deleted} row(s)");
    Console.WriteLine($"  Devices after: {after}");

    Console.WriteLine("\n  [Also changed in NBAaS]");
    Console.WriteLine("  SetCommandTimeout: db.Database.CommandTimeout = seconds (property, not method)");
    Console.WriteLine("  EF6: db.Database.CommandTimeout = seconds (same, but different Database type)");
}

// =============================================================================
// 05. ChangeTracker: Manual Detach Loop → ChangeTracker.Clear()
// =============================================================================
// EF Core added ChangeTracker.Clear() — a single call to detach all entities.
// EF6 required manually iterating and setting each entry to Detached.
//
// Real code (BusinessBase.cs):
//   #if NET8_0_OR_GREATER
//       ReadDbContext.Value.ChangeTracker.Clear();
//   #else
//       var entries = ReadDbContext.Value.ChangeTracker.Entries().ToList();
//       foreach (var entry in entries)
//           entry.State = EntityState.Detached;
//   #endif
// =============================================================================
static async Task Example05_ChangeTracker()
{
    using var db = CreateSeededDb();

    // Load some entities
    var devices = await db.Devices.ToListAsync();
    var trackedBefore = db.ChangeTracker.Entries().Count();
    Console.WriteLine($"  Loaded {devices.Count} devices, tracked entries: {trackedBefore}");

    // EF6 way: manual detach loop
    Console.WriteLine("\n  ┌─── EF6 (BEFORE) ───────────────────────────────────────────┐");
    Console.WriteLine("  │  var entries = context.ChangeTracker.Entries().ToList();    │");
    Console.WriteLine("  │  foreach (var entry in entries)                             │");
    Console.WriteLine("  │      entry.State = EntityState.Detached;                   │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");

    // Simulate EF6 approach
    var sw1 = Stopwatch.StartNew();
    foreach (var entry in db.ChangeTracker.Entries().ToList())
        entry.State = EntityState.Detached;
    sw1.Stop();
    Console.WriteLine($"  EF6 manual detach: {db.ChangeTracker.Entries().Count()} tracked ({sw1.ElapsedTicks} ticks)");

    // Reload and use EF Core Clear()
    devices = await db.Devices.ToListAsync();
    Console.WriteLine($"\n  Reloaded {devices.Count} devices, tracked: {db.ChangeTracker.Entries().Count()}");

    Console.WriteLine("  ┌─── EF Core (AFTER) ────────────────────────────────────────┐");
    Console.WriteLine("  │  context.ChangeTracker.Clear();  // one call              │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");

    var sw2 = Stopwatch.StartNew();
    db.ChangeTracker.Clear();
    sw2.Stop();
    Console.WriteLine($"  EF Core Clear(): {db.ChangeTracker.Entries().Count()} tracked ({sw2.ElapsedTicks} ticks)");

    Console.WriteLine("\n  [Why this matters for NBAaS]");
    Console.WriteLine("  NBAaS resets context state between DC processing rounds.");
    Console.WriteLine("  With 100+ DCs and thousands of entities, Clear() is much faster.");
}

// =============================================================================
// 06. Lazy Loading: Default On (EF6) → Default Off (EF Core)
// =============================================================================
// EF6: Lazy loading ON by default. Navigation properties auto-loaded.
// EF Core: Lazy loading OFF by default. Must explicitly opt in.
//
// Real code (NgsDbContext.cs):
//   public NgsDbContext(DbContextOptions<NgsDbContext> options) : base(options)
//   {
//       ChangeTracker.LazyLoadingEnabled = false;  // explicit, defensive
//   }
//
// NBAaS uses eager loading (Include) or explicit loading — never lazy.
// =============================================================================
static async Task Example06_LazyLoading()
{
    using var db = CreateSeededDb();

    Console.WriteLine($"  LazyLoadingEnabled: {db.ChangeTracker.LazyLoadingEnabled}");
    Console.WriteLine("  (EF Core default is false; NBAaS also sets it explicitly to false)");

    // Without Include: navigation property is null
    var device = await db.Devices.FirstAsync(d => d.DeviceName == "PRA-0100-0101-01T0");
    Console.WriteLine($"\n  Device without Include: {device.DeviceName}");
    Console.WriteLine($"  DeviceMetadatas: {device.DeviceMetadatas?.Count ?? 0} (null/empty — not loaded)");

    // With Include: navigation property is populated (eager loading)
    device = await db.Devices
        .Include(d => d.DeviceMetadatas)
        .FirstAsync(d => d.DeviceName == "PRA-0100-0101-01T0");
    Console.WriteLine($"\n  Device with Include: {device.DeviceName}");
    Console.WriteLine($"  DeviceMetadatas: {device.DeviceMetadatas?.Count ?? 0} items:");
    foreach (var m in device.DeviceMetadatas!)
        Console.WriteLine($"    {m.Name} = {m.Value}");

    Console.WriteLine("\n  ┌─── EF6 (BEFORE) ───────────────────────────────────────────┐");
    Console.WriteLine("  │  // Lazy loading ON — accessing device.DeviceMetadatas     │");
    Console.WriteLine("  │  // triggers a hidden SQL query (N+1 problem risk)         │");
    Console.WriteLine("  │  // NBAaS had: Configuration.LazyLoadingEnabled = false;   │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");
    Console.WriteLine("  ┌─── EF Core (AFTER) ────────────────────────────────────────┐");
    Console.WriteLine("  │  // Lazy loading OFF by default — no hidden queries        │");
    Console.WriteLine("  │  // Must use .Include() for navigation properties          │");
    Console.WriteLine("  │  // NBAaS sets LazyLoadingEnabled = false defensively      │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");
}

// =============================================================================
// 07. StringComparison in LINQ: Silently Ignored (EF6) → Throws (EF Core)
// =============================================================================
// EF6: .Equals(val, StringComparison.OrdinalIgnoreCase) in LINQ-to-SQL
//   → EF6 SILENTLY DROPS the StringComparison parameter
//   → Translates to SQL = (uses DB collation, which is CI on SQL Server)
//
// EF Core: Same code THROWS at runtime:
//   "The LINQ expression could not be translated"
//
// NBAaS fix: Changed LINQ queries from .Equals(val, OrdinalIgnoreCase) to ==
// This is safe because SQL Server collation SQL_Latin1_General_CP1_CI_AS is
// case-insensitive. StringComparison is only used in post-materialization
// (after ToList/ToListAsync) where it runs in memory.
// =============================================================================
static async Task Example07_StringComparison()
{
    using var db = CreateSeededDb();

    Console.WriteLine("  ┌─── EF6 (BEFORE) — worked but was misleading ──────────────┐");
    Console.WriteLine("  │  // This LINQ query worked in EF6:                        │");
    Console.WriteLine("  │  var devices = db.Devices.Where(d =>                      │");
    Console.WriteLine("  │      d.DeviceType.Equals(\"torrouter\",                     │");
    Console.WriteLine("  │          StringComparison.OrdinalIgnoreCase));             │");
    Console.WriteLine("  │  // EF6 silently IGNORED StringComparison parameter       │");
    Console.WriteLine("  │  // Generated SQL: WHERE DeviceType = 'torrouter'         │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");

    // EF Core: .Equals with StringComparison in IQueryable throws
    Console.WriteLine("\n  Testing EF Core behavior...");
    try
    {
        // This would throw in EF Core with SQL Server provider.
        // SQLite provider may handle it differently, so we demonstrate the pattern.
        var result = await db.Devices
            .Where(d => d.DeviceType == "torrouter")  // EF Core safe: == operator
            .ToListAsync();
        Console.WriteLine($"  Using == operator: found {result.Count} devices (case depends on DB collation)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Error: {ex.Message}");
    }

    // Post-materialization: StringComparison is safe
    var allDevices = await db.Devices.ToListAsync();
    var filtered = allDevices
        .Where(d => d.DeviceType.Equals("torrouter", StringComparison.OrdinalIgnoreCase))
        .ToList();
    Console.WriteLine($"  Post-ToList() with StringComparison: found {filtered.Count} devices");

    Console.WriteLine("\n  ┌─── EF Core (AFTER) — NBAaS fix ──────────────────────────┐");
    Console.WriteLine("  │  // In LINQ-to-SQL (IQueryable): use == operator          │");
    Console.WriteLine("  │  var devices = db.Devices.Where(d => d.DeviceType == val);│");
    Console.WriteLine("  │                                                            │");
    Console.WriteLine("  │  // Post-materialization (in-memory): StringComparison OK  │");
    Console.WriteLine("  │  var list = (await db.Devices.ToListAsync())               │");
    Console.WriteLine("  │      .Where(d => d.DeviceType.Equals(val,                  │");
    Console.WriteLine("  │          StringComparison.OrdinalIgnoreCase));              │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");

    Console.WriteLine("\n  [Safety] SQL Server collation SQL_Latin1_General_CP1_CI_AS is case-insensitive.");
    Console.WriteLine("  So == in SQL produces the same result as OrdinalIgnoreCase in memory.");
}

// =============================================================================
// 08. Conditional Compilation: #if NET8_0_OR_GREATER
// =============================================================================
// NBAaS uses conditional compilation to support both EF6 (.NET Framework)
// and EF Core 8 (.NET 8) in the same codebase during transition.
//
// Real examples:
//   - BusinessBase.cs: PooledDbContextFactory only on NET8
//   - EFLoggingInterceptor.cs: ValueTask returns on NET8, Task on EF6
//   - BusinessBase.cs: ChangeTracker.Clear() on NET8, manual detach on EF6
// =============================================================================
static Task Example08_ConditionalCompilation()
{
    Console.WriteLine("  NBAaS uses #if NET8_0_OR_GREATER for dual-targeting during migration.\n");
    Console.WriteLine("  ┌─── Pattern 1: PooledDbContextFactory ──────────────────────┐");
    Console.WriteLine("  │  #if NET8_0_OR_GREATER                                    │");
    Console.WriteLine("  │  private static PooledDbContextFactory<NgsDbContext> _pool;│");
    Console.WriteLine("  │  if (_pooledFactory != null)                               │");
    Console.WriteLine("  │      return _pooledFactory.CreateDbContext();              │");
    Console.WriteLine("  │  #endif                                                    │");
    Console.WriteLine("  │  return new NgsDbContext(connectionString); // fallback    │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");
    Console.WriteLine();
    Console.WriteLine("  ┌─── Pattern 2: Interceptor Async Return Type ───────────────┐");
    Console.WriteLine("  │  #if NET8_0_OR_GREATER                                    │");
    Console.WriteLine("  │  public override ValueTask<int> NonQueryExecutedAsync(...)  │");
    Console.WriteLine("  │  #else                                                     │");
    Console.WriteLine("  │  public override Task<int> NonQueryExecutedAsync(...)       │");
    Console.WriteLine("  │  #endif                                                    │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");
    Console.WriteLine();
    Console.WriteLine("  ┌─── Pattern 3: ChangeTracker Reset ─────────────────────────┐");
    Console.WriteLine("  │  #if NET8_0_OR_GREATER                                    │");
    Console.WriteLine("  │  context.ChangeTracker.Clear();                            │");
    Console.WriteLine("  │  #else                                                     │");
    Console.WriteLine("  │  foreach (var e in context.ChangeTracker.Entries().ToList())│");
    Console.WriteLine("  │      e.State = EntityState.Detached;                       │");
    Console.WriteLine("  │  #endif                                                    │");
    Console.WriteLine("  └────────────────────────────────────────────────────────────┘");

    Console.WriteLine("\n  [Strategy] Conditional compilation allows incremental migration:");
    Console.WriteLine("  1. Both EF6 and EF Core paths exist in the same codebase");
    Console.WriteLine("  2. CI/CD builds both targets, tests both code paths");
    Console.WriteLine("  3. After EF Core is validated, remove #else branches");

#if NET8_0_OR_GREATER
    Console.WriteLine("\n  This example is running on .NET 8+ (EF Core path active)");
#else
    Console.WriteLine("\n  This example is running on .NET Framework (EF6 path active)");
#endif

    return Task.CompletedTask;
}

// ── Entity Classes (Modeled after NBAaS) ─────────────────────────────────────

public class Device
{
    [Key]
    [StringLength(200)]
    public string DeviceName { get; set; } = "";

    [StringLength(100)]
    public string DeviceType { get; set; } = "";

    [StringLength(100)]
    public string Source { get; set; } = "";

    public List<DeviceMetadataEntity>? DeviceMetadatas { get; set; }
}

// NBAaS composite key: DeviceName + Name
public class DeviceMetadataEntity
{
    [StringLength(200)]
    public string DeviceName { get; set; } = "";

    [StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(500)]
    public string Value { get; set; } = "";
}

// NBAaS composite key: StartDeviceName + StartPort + EndDeviceName + EndPort
public class Link
{
    [StringLength(200)]
    public string StartDeviceName { get; set; } = "";

    [StringLength(100)]
    public string StartPort { get; set; } = "";

    [StringLength(200)]
    public string EndDeviceName { get; set; } = "";

    [StringLength(100)]
    public string EndPort { get; set; } = "";
}

// ── DbContext (Modeled after NgsDbContext) ────────────────────────────────────

public class NbaasDbContext : DbContext
{
    public NbaasDbContext(DbContextOptions<NbaasDbContext> options) : base(options)
    {
        // NBAaS: explicitly disable lazy loading (EF Core default is already false)
        ChangeTracker.LazyLoadingEnabled = false;
    }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Link> Links => Set<Link>();
    public DbSet<DeviceMetadataEntity> DeviceMetadatas => Set<DeviceMetadataEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // EF Core REQUIRES explicit composite key configuration
        // EF6 used [Key, Column(Order=N)] annotations — not supported in EF Core

        modelBuilder.Entity<Link>().HasKey(x => new
        {
            x.StartDeviceName, x.StartPort, x.EndDeviceName, x.EndPort
        });

        modelBuilder.Entity<DeviceMetadataEntity>().HasKey(x => new
        {
            x.DeviceName, x.Name
        });

        // Navigation property configuration
        modelBuilder.Entity<Device>()
            .HasMany(d => d.DeviceMetadatas)
            .WithOne()
            .HasForeignKey(dm => dm.DeviceName);
    }
}

// ── Interceptor (Modeled after EFLoggingInterceptor) ─────────────────────────

public class SlowQueryInterceptor : DbCommandInterceptor
{
    private readonly double _thresholdMs;
    public List<(string CommandText, double DurationMs)> LoggedQueries { get; } = new();

    public SlowQueryInterceptor(double thresholdMs = 1000)
    {
        _thresholdMs = thresholdMs;
    }

    // EF Core: override ReaderExecuted (was ReaderExecuted with different signature in EF6)
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        LogExecution(command, eventData);
        return result;
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        LogExecution(command, eventData);
        return result;
    }

    // EF Core 8: ValueTask return type (EF6 used Task)
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogExecution(command, eventData);
        return new ValueTask<DbDataReader>(result);
    }

    private void LogExecution(DbCommand command, CommandExecutedEventData eventData)
    {
        var durationMs = eventData.Duration.TotalMilliseconds;
        if (durationMs >= _thresholdMs)
        {
            LoggedQueries.Add((command.CommandText, durationMs));
        }
    }
}
