# Entity Framework 6 vs Entity Framework Core 8 — Differences & Migration Guide

> A comprehensive comparison with runnable code examples.

## Table of Contents

- [1. Architecture & Philosophy](#1-architecture--philosophy)
- [2. DbContext & Configuration](#2-dbcontext--configuration)
- [3. LINQ Query Translation](#3-linq-query-translation)
- [4. Loading Strategies (Eager / Lazy / Explicit)](#4-loading-strategies-eager--lazy--explicit)
- [5. Change Tracking](#5-change-tracking)
- [6. Raw SQL](#6-raw-sql)
- [7. Migrations](#7-migrations)
- [8. Stored Procedures & Functions](#8-stored-procedures--functions)
- [9. Concurrency & Async](#9-concurrency--async)
- [10. Performance](#10-performance)
- [11. Dependency Injection](#11-dependency-injection)
- [12. What's New in EF Core (No EF6 Equivalent)](#12-whats-new-in-ef-core-no-ef6-equivalent)
- [13. What's Removed in EF Core (Was in EF6)](#13-whats-removed-in-ef-core-was-in-ef6)
- [14. Migration Pitfalls Checklist](#14-migration-pitfalls-checklist)

---

## 1. Architecture & Philosophy

| Aspect | EF6 | EF Core 8 |
|--------|-----|-----------|
| **Package** | `EntityFramework` (6.4.4) | `Microsoft.EntityFrameworkCore` (8.0.x) |
| **Namespace** | `System.Data.Entity` | `Microsoft.EntityFrameworkCore` |
| **Provider model** | Built-in SQL Server; `DbProviderFactory` | Pluggable providers via `Microsoft.EntityFrameworkCore.SqlServer` |
| **Target** | .NET Framework 4.x (+ limited .NET 6) | .NET 6/7/8+ (cross-platform) |
| **Query pipeline** | LINQ → Expression Tree → DbCommandTree → SQL | LINQ → Expression Tree → SQL (direct translation, no intermediate tree) |
| **Design** | Monolithic, feature-rich | Modular, lean core, extensible |
| **EDMX** | Supported (visual designer) | **Not supported** — code-first only |
| **ObjectContext** | Supported (legacy API) | **Removed** — DbContext only |

```
EF6 Pipeline:                          EF Core Pipeline:
LINQ → ExpressionTree                 LINQ → ExpressionTree
      → DbCommandTree (canonical)           → IQueryable (relational model)
      → Provider SQL generator               → SQL (direct from provider)
      → ADO.NET DbCommand                   → DbCommand (or compiled query cache)
```

**EF Core's simpler pipeline** means:
- Faster query compilation
- Better LINQ support (more operations translate to SQL)
- Clearer error messages when translation fails (throws instead of silently evaluating client-side in EF Core 3.0+)

---

## 2. DbContext & Configuration

### EF6 — Connection in constructor or `web.config`/`app.config`

```csharp
// EF6
public class MyDbContext : DbContext  // System.Data.Entity.DbContext
{
    // Connection string from app.config by convention (name = class name)
    public MyDbContext() : base("name=MyDbContext") { }
    
    // Or explicit connection string
    public MyDbContext(string connStr) : base(connStr) { }
    
    public DbSet<Product> Products { get; set; }
    
    protected override void OnModelCreating(DbModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>()
            .HasKey(p => p.Id);
        modelBuilder.Entity<Product>()
            .Property(p => p.Name)
            .HasMaxLength(200)
            .IsRequired();
    }
}
```

### EF Core — `OnConfiguring` or DI

```csharp
// EF Core
public class MyDbContext : DbContext  // Microsoft.EntityFrameworkCore.DbContext
{
    // Option 1: Override OnConfiguring
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer(
            "Server=.;Database=MyDb;Trusted_Connection=True;TrustServerCertificate=True;");
    }
    
    // Option 2: DI injection (preferred)
    public MyDbContext(DbContextOptions<MyDbContext> options) : base(options) { }
    
    public DbSet<Product> Products { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)  // ModelBuilder, not DbModelBuilder
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name)
                .HasMaxLength(200)
                .IsRequired();
        });
    }
}
```

### Key API Differences

| Feature | EF6 | EF Core 8 |
|---------|-----|-----------|
| Model builder | `DbModelBuilder` | `ModelBuilder` |
| `HasRequired()` / `HasOptional()` | ✅ | ❌ Use nullability of navigation |
| `WillCascadeOnDelete()` | ✅ | `OnDelete(DeleteBehavior.Cascade)` |
| `Map(m => m.ToTable())` | ✅ | `ToTable("TableName")` directly |
| `HasForeignKey()` | Via `Map()` | Directly on relationship |
| Seed data | `Seed()` method in Migration | `HasData()` in `OnModelCreating` |
| Connection string source | `web.config` / `app.config` | `appsettings.json` / env vars / code |
| Schema initialization | `Database.SetInitializer<>()` | Migrations only |

---

## 3. LINQ Query Translation

### Client-side evaluation

**EF6:** Silently evaluates unsupported expressions in-memory (client-side). This can cause **hidden performance disasters** — a `.Where()` that can't translate to SQL will fetch the entire table.

**EF Core 3.0+:** **Throws an exception** when an expression can't be translated to SQL. This is a breaking change but a much safer design.

```csharp
// EF6: Silently loads ALL products into memory, then filters
var products = db.Products
    .Where(p => MyCustomMethod(p.Name))  // can't translate → client-side
    .ToList();
// WARNING: Fetches entire Products table!

// EF Core: Throws InvalidOperationException
// "The LINQ expression 'MyCustomMethod(p.Name)' could not be translated"
var products = db.Products
    .Where(p => MyCustomMethod(p.Name))  // THROWS
    .ToList();

// EF Core fix: explicitly use .AsEnumerable() for client-side
var products = db.Products
    .AsEnumerable()                       // pull data to client
    .Where(p => MyCustomMethod(p.Name))   // now it's LINQ-to-Objects
    .ToList();
```

### String comparison

```csharp
// EF6: Works (but ignores StringComparison parameter — always uses DB collation)
.Where(p => p.Name.Equals(search, StringComparison.OrdinalIgnoreCase))

// EF Core: Throws! Cannot translate StringComparison to SQL
.Where(p => p.Name.Equals(search, StringComparison.OrdinalIgnoreCase))

// EF Core fix: Use == (relies on database collation)
.Where(p => p.Name == search)

// EF Core: Explicit collation control
.Where(p => EF.Functions.Collate(p.Name, "SQL_Latin1_General_CP1_CI_AS") == search)
```

### GroupBy

```csharp
// EF6: Groups client-side if SQL can't express it
var groups = db.Orders.GroupBy(o => o.Category).ToList();

// EF Core: Translates GroupBy to SQL GROUP BY (only aggregate queries)
var summary = db.Orders
    .GroupBy(o => o.Category)
    .Select(g => new { Category = g.Key, Count = g.Count(), Total = g.Sum(o => o.Amount) })
    .ToList();
// Works ✓ — translates to: SELECT Category, COUNT(*), SUM(Amount) FROM Orders GROUP BY Category

// EF Core: Non-aggregate GroupBy throws
var groups = db.Orders.GroupBy(o => o.Category).ToList(); // THROWS in EF Core
// Fix: materialize first
var groups = db.Orders.ToList().GroupBy(o => o.Category).ToList();
```

---

## 4. Loading Strategies (Eager / Lazy / Explicit)

### Lazy Loading

```csharp
// EF6: Built-in, enabled by default via proxies
public class Order
{
    public virtual ICollection<OrderItem> Items { get; set; }  // virtual = proxy
}
// db.Orders.First().Items; ← triggers lazy load automatically

// EF Core: Opt-in, requires Microsoft.EntityFrameworkCore.Proxies package
services.AddDbContext<MyDbContext>(options =>
    options.UseLazyLoadingProxies()  // must install NuGet package
           .UseSqlServer(connStr));
// OR inject ILazyLoader manually:
public class Order
{
    private readonly ILazyLoader _lazyLoader;
    private ICollection<OrderItem> _items;
    
    public Order(ILazyLoader lazyLoader) { _lazyLoader = lazyLoader; }
    
    public ICollection<OrderItem> Items
    {
        get => _lazyLoader.Load(this, ref _items);
        set => _items = value;
    }
}
```

### Eager Loading

```csharp
// EF6
db.Orders.Include("Items").Include("Items.Product").ToList();
// or with lambda (EF6.1+):
db.Orders.Include(o => o.Items.Select(i => i.Product)).ToList();

// EF Core — cleaner syntax
db.Orders
    .Include(o => o.Items)
        .ThenInclude(i => i.Product)   // ThenInclude instead of nested Select
    .ToList();

// EF Core — filtered Include (NEW, not available in EF6)
db.Orders
    .Include(o => o.Items.Where(i => i.Quantity > 0))  // filter navigation
    .ToList();

// EF Core — AsSplitQuery (avoid cartesian explosion)
db.Orders
    .Include(o => o.Items)
    .AsSplitQuery()    // generates separate SQL queries instead of JOIN
    .ToList();
```

### Explicit Loading

```csharp
// EF6
db.Entry(order).Collection(o => o.Items).Load();
db.Entry(order).Reference(o => o.Customer).Load();

// EF Core — same API, plus async
await db.Entry(order).Collection(o => o.Items).LoadAsync();
await db.Entry(order).Reference(o => o.Customer).LoadAsync();
```

---

## 5. Change Tracking

```csharp
// EF6: Snapshot-based (+ proxy-based if virtual properties)
// All tracked entities are snapshot on read

// EF Core: Same snapshot-based default, plus explicit options
var product = db.Products.AsNoTracking().First();  // no tracking (read-only, faster)

// EF Core 8: AsNoTrackingWithIdentityResolution — no tracking but dedup entities
var orders = db.Orders
    .Include(o => o.Customer)
    .AsNoTrackingWithIdentityResolution()  // same Customer object shared
    .ToList();

// EF Core: Change tracker debug view
Console.WriteLine(db.ChangeTracker.DebugView.LongView);

// EF Core: Global no-tracking
services.AddDbContext<MyDbContext>(options =>
    options.UseSqlServer(connStr)
           .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
```

| Feature | EF6 | EF Core 8 |
|---------|-----|-----------|
| Snapshot tracking | ✅ | ✅ |
| Proxy tracking | ✅ (default with virtual) | ✅ (opt-in package) |
| `AsNoTracking()` | ✅ | ✅ |
| `AsNoTrackingWithIdentityResolution()` | ❌ | ✅ |
| `ChangeTracker.DebugView` | ❌ | ✅ |
| Global query filter | ❌ | ✅ (`HasQueryFilter()`) |

---

## 6. Raw SQL

```csharp
// EF6
db.Database.SqlQuery<Product>("SELECT * FROM Products WHERE Price > @p0", 100).ToList();
db.Database.ExecuteSqlCommand("UPDATE Products SET Price = Price * 1.1");

// EF Core — different API names, parameterized by default
db.Products.FromSqlRaw("SELECT * FROM Products WHERE Price > {0}", 100).ToList();
db.Products.FromSqlInterpolated($"SELECT * FROM Products WHERE Price > {100}").ToList();
db.Database.ExecuteSqlRaw("UPDATE Products SET Price = Price * 1.1");
db.Database.ExecuteSqlInterpolated($"UPDATE Products SET Price = {newPrice}");

// EF Core 8 — composable raw SQL (can add LINQ on top)
var expensive = db.Products
    .FromSqlInterpolated($"SELECT * FROM Products WHERE Price > {100}")
    .Where(p => p.IsActive)   // LINQ composed on top of raw SQL
    .OrderBy(p => p.Name)
    .ToList();
// Generates: SELECT ... FROM (SELECT * FROM Products WHERE Price > @p0) AS p
//            WHERE p.IsActive = 1 ORDER BY p.Name
```

---

## 7. Migrations

```bash
# EF6 (Package Manager Console)
Enable-Migrations
Add-Migration InitialCreate
Update-Database

# EF Core (CLI)
dotnet ef migrations add InitialCreate
dotnet ef database update

# EF Core (Package Manager Console)
Add-Migration InitialCreate
Update-Database
```

| Feature | EF6 | EF Core 8 |
|---------|-----|-----------|
| CLI tool | PMC only | `dotnet ef` + PMC |
| Seed data | `Seed()` method (runs every update) | `HasData()` in model (generates migration) |
| Bundles | ❌ | `dotnet ef migrations bundle` (self-contained executable) |
| Idempotent scripts | ❌ | `dotnet ef migrations script --idempotent` |
| Multiple providers | ❌ (one per project) | ✅ (different migrations per provider) |
| Automatic migrations | ✅ (`AutomaticMigrationsEnabled`) | ❌ (always explicit) |

---

## 8. Stored Procedures & Functions

```csharp
// EF6: Full stored procedure mapping
modelBuilder.Entity<Product>()
    .MapToStoredProcedures(s => s
        .Insert(i => i.HasName("sp_InsertProduct"))
        .Update(u => u.HasName("sp_UpdateProduct"))
        .Delete(d => d.HasName("sp_DeleteProduct")));

// EF Core: NO stored procedure mapping for CUD (until EF Core 7)
// EF Core 7+: Stored procedure mapping
modelBuilder.Entity<Product>()
    .InsertUsingStoredProcedure("sp_InsertProduct", sp => sp
        .HasParameter(p => p.Name)
        .HasParameter(p => p.Price)
        .HasResultColumn(p => p.Id))
    .UpdateUsingStoredProcedure("sp_UpdateProduct", sp => sp
        .HasOriginalValueParameter(p => p.Id)
        .HasParameter(p => p.Name)
        .HasParameter(p => p.Price))
    .DeleteUsingStoredProcedure("sp_DeleteProduct", sp => sp
        .HasOriginalValueParameter(p => p.Id));

// Database scalar functions
// EF6: Not supported natively
// EF Core:
[DbFunction("fn_GetDiscountedPrice", "dbo")]
public static decimal GetDiscountedPrice(int productId) => throw new NotSupportedException();
// Usage in LINQ — translates to SQL function call:
db.Products.Select(p => new { p.Name, DiscountedPrice = MyDbContext.GetDiscountedPrice(p.Id) });
```

---

## 9. Concurrency & Async

### Async API

```csharp
// EF6: Async available but returns Task<T>
Task<Product> product = db.Products.FindAsync(id);           // Task<Product>
Task<List<Product>> list = db.Products.ToListAsync();         // Task<List<Product>>
Task<int> count = db.SaveChangesAsync();                      // Task<int>

// EF Core: Returns ValueTask for Find, Task for others
ValueTask<Product?> product = db.Products.FindAsync(id);      // ValueTask! (may complete sync)
Task<List<Product>> list = db.Products.ToListAsync();          // Task<List<Product>>
Task<int> count = db.SaveChangesAsync();                       // Task<int>
```

### DbContext Thread Safety

```csharp
// BOTH EF6 AND EF Core: DbContext is NOT thread-safe
// NEVER share a DbContext across concurrent operations

// ❌ WRONG (both EF6 and EF Core)
var ctx = new MyDbContext();
Parallel.ForEach(ids, id =>
{
    ctx.Products.Find(id);  // Race condition! DbContext is not thread-safe
});

// ✅ CORRECT: One DbContext per unit-of-work
Parallel.ForEach(ids, id =>
{
    using var ctx = new MyDbContext();
    ctx.Products.Find(id);
});

// ✅ CORRECT (EF Core with DI): Use IDbContextFactory
services.AddDbContextFactory<MyDbContext>(options =>
    options.UseSqlServer(connStr));

// In consuming code:
class MyService(IDbContextFactory<MyDbContext> factory)
{
    async Task ProcessConcurrently(int[] ids)
    {
        await Task.WhenAll(ids.Select(async id =>
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var product = await ctx.Products.FindAsync(id);
        }));
    }
}
```

### Connection Resiliency

```csharp
// EF6: Manual retry or DbExecutionStrategy
public class MyExecutionStrategy : DbExecutionStrategy
{
    protected override bool ShouldRetryOn(Exception ex) => ex is SqlException;
}

// EF Core: Built-in retry
services.AddDbContext<MyDbContext>(options =>
    options.UseSqlServer(connStr, sqlOptions =>
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)));
```

---

## 10. Performance

| Feature | EF6 | EF Core 8 |
|---------|-----|-----------|
| **Compiled queries** | `CompiledQuery.Compile()` (LINQ to Entities) | `EF.CompileQuery()` / `EF.CompileAsyncQuery()` |
| **Batching** | ❌ (one SQL per entity) | ✅ (batches INSERT/UPDATE/DELETE) |
| **Query splitting** | ❌ | `AsSplitQuery()` |
| **No-tracking perf** | Good | Better (no identity resolution overhead) |
| **Bulk operations** | ❌ (need 3rd party: EF+ / Z.EntityFramework) | `ExecuteUpdate()` / `ExecuteDelete()` (EF Core 7+) |
| **Connection pooling** | ADO.NET pool (default) | ADO.NET pool + `PooledDbContextFactory` |
| **Query plan caching** | ✅ | ✅ (more aggressive) |

### Bulk Operations (EF Core 7+)

```csharp
// EF6: Must load all entities, modify, then SaveChanges
var products = db.Products.Where(p => p.Price > 100).ToList();
foreach (var p in products) p.IsExpensive = true;
db.SaveChanges();  // N individual UPDATE statements

// EF Core 7+: Direct SQL, no entity loading
db.Products
    .Where(p => p.Price > 100)
    .ExecuteUpdate(s => s.SetProperty(p => p.IsExpensive, true));
// Generates: UPDATE Products SET IsExpensive = 1 WHERE Price > 100

db.Products
    .Where(p => p.Discontinued)
    .ExecuteDelete();
// Generates: DELETE FROM Products WHERE Discontinued = 1
```

### Compiled Queries

```csharp
// EF6
static readonly Func<MyDbContext, int, Product> GetProductById =
    CompiledQuery.Compile((MyDbContext ctx, int id) =>
        ctx.Products.First(p => p.Id == id));
var product = GetProductById(db, 42);

// EF Core
static readonly Func<MyDbContext, int, Task<Product?>> GetProductById =
    EF.CompileAsyncQuery((MyDbContext ctx, int id) =>
        ctx.Products.FirstOrDefault(p => p.Id == id));
var product = await GetProductById(db, 42);
```

---

## 11. Dependency Injection

```csharp
// EF6: No built-in DI. Manual or with Unity/Autofac
public class OrderService
{
    public void CreateOrder()
    {
        using var db = new MyDbContext();  // manual instantiation
        // ...
    }
}

// EF Core: Built-in DI integration
// In Startup / Program.cs:
builder.Services.AddDbContext<MyDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// In services — injected automatically:
public class OrderService(MyDbContext db)
{
    public async Task CreateOrderAsync(Order order)
    {
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }
}

// For concurrent scenarios:
builder.Services.AddDbContextFactory<MyDbContext>(options =>
    options.UseSqlServer(connStr));

// PooledDbContextFactory — reuses DbContext instances (reduces allocation)
builder.Services.AddPooledDbContextFactory<MyDbContext>(options =>
    options.UseSqlServer(connStr));
```

---

## 12. What's New in EF Core (No EF6 Equivalent)

| Feature | Description | Since |
|---------|-------------|-------|
| **Global Query Filters** | `HasQueryFilter(p => !p.IsDeleted)` — auto-applied to all queries | EF Core 2.0 |
| **Owned Entity Types** | `OwnsOne(o => o.Address)` — value objects stored in same table | EF Core 2.0 |
| **Shadow Properties** | Properties in model but not in .NET class | EF Core 1.0 |
| **Keyless Entities** | `HasNoKey()` — for views, raw SQL | EF Core 2.1 |
| **Table Splitting** | Multiple entities → one table | EF Core 2.0 |
| **Table-per-type (TPT)** | Each type gets its own table | EF Core 5.0 |
| **Table-per-concrete-type (TPC)** | Concrete types get tables, no base table | EF Core 7.0 |
| **JSON Columns** | `OwnsOne(o => o.Metadata, b => b.ToJson())` | EF Core 7.0 |
| **Bulk Update/Delete** | `ExecuteUpdate()` / `ExecuteDelete()` | EF Core 7.0 |
| **Compiled Models** | Pre-compiled model for faster startup | EF Core 6.0 |
| **Temporal Tables** | `IsTemporal()` — auto-versioning | EF Core 6.0 |
| **Interceptors** | `AddInterceptors()` — hook into pipeline | EF Core 3.0 |
| **Value Converters** | `HasConversion<string>()` — enum to string, etc. | EF Core 2.1 |
| **DbContext Pooling** | `AddPooledDbContextFactory` | EF Core 2.0 |
| **Cosmos DB Provider** | NoSQL support | EF Core 3.0 |
| **Filtered Include** | `.Include(o => o.Items.Where(...))` | EF Core 5.0 |
| **AsSplitQuery** | Separate SQL per Include | EF Core 5.0 |
| **Raw SQL for unmapped** | `SqlQuery<T>()` for any type | EF Core 8.0 |
| **Complex Types** | Value objects without identity | EF Core 8.0 |
| **Primitive Collections** | `List<string>` mapped to JSON | EF Core 8.0 |

---

## 13. What's Removed in EF Core (Was in EF6)

| EF6 Feature | Status in EF Core | Workaround |
|-------------|-------------------|------------|
| **EDMX / Visual Designer** | ❌ Removed | Code-first only |
| **ObjectContext** | ❌ Removed | Use DbContext |
| **Database-first (model generation)** | ⚠️ Scaffold only | `dotnet ef dbcontext scaffold` |
| **Automatic Migrations** | ❌ Removed | Always explicit migrations |
| **Spatial types (`DbGeography`)** | ❌ → ✅ (via NetTopologySuite) | Install `NetTopologySuite` package |
| **Entity SQL** | ❌ Removed | Use LINQ or raw SQL |
| **Many-to-many without join entity** | ❌ → ✅ (EF Core 5.0) | Implicit join table |
| **Seed method in migrations** | ❌ Removed | `HasData()` in `OnModelCreating` |
| **TransactionScope` auto-enlistment** | ⚠️ Limited | Use `Database.BeginTransaction()` |
| **`HasRequired` / `HasOptional`** | ❌ Removed | Use nullability of navigation property |
| **Stored Procedure mapping (CUD)** | ❌ → ✅ (EF Core 7.0) | Use `InsertUsingStoredProcedure` etc. |

---

## 14. Migration Pitfalls Checklist

Based on our real-world EF6 → EF Core 8 migration of NBAaS (Azure-Network-Graph):

| # | Pitfall | EF6 Behavior | EF Core Behavior | Fix |
|---|---------|-------------|-------------------|-----|
| 1 | `StringComparison` in LINQ | Ignored silently | Throws | Use `==` (rely on DB collation) |
| 2 | Client-side evaluation | Silent | Throws | Add `.AsEnumerable()` before unsupported ops |
| 3 | `FindAsync` return type | `Task<T>` | `ValueTask<T?>` | Change to `await` (no `.Result`) |
| 4 | `ObjectContext` | Available | Removed | Rewrite to DbContext APIs |
| 5 | `Parallel.Invoke` + `.Wait()` | Works (sync internals) | ThreadPool starvation | Use `Task.WhenAll` |
| 6 | `HasRequired` / `HasOptional` | API exists | Removed | Use nullable reference types |
| 7 | Non-deterministic ordering | Same iteration order | Dict ordering varies | Add explicit `OrderBy` |
| 8 | Connection string source | `app.config` | Code / env var / DI | Use `IConfiguration` |
| 9 | Lazy loading | On by default | Opt-in package | Install Proxies or manual loading |
| 10 | `Database.SqlQuery<T>` | Returns entities | `FromSqlRaw` (must be entity type) | Use `SqlQuery<T>` for arbitrary types (EF Core 8) |
| 11 | `SaveChanges()` batching | One SQL per entity | Automatic batching | No action needed (faster by default) |
| 12 | `TransactionScope` | Auto-enlists | Must opt-in | Use `Database.BeginTransaction()` |
| 13 | Enum conversion | Stored as int (default) | Same, but `HasConversion` available | Use `HasConversion<string>()` if needed |

> For detailed analysis of each pitfall with code examples, see the project-specific [EFCoreMigration.md](../../Azure-Network-Graph/src/developer/xinlyu/DesignDoc/EFCoreMigration/EFCoreMigration.md) and [EFCoreMigrationTutorial.md](../../Azure-Network-Graph/src/developer/xinlyu/DesignDoc/EFCoreMigration/EFCoreMigrationTutorial.md).
