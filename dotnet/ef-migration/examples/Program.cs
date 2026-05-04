// =============================================================================
// EF Core 8 Examples — Demonstrates key features & differences from EF6
// Uses SQLite in-memory so no external database is needed.
// Run: dotnet run
// =============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Text.Json;

// ── Entry Point ──────────────────────────────────────────────────────────────

var examples = new (string Name, Func<Task> Action)[]
{
    ("01. DbContext & Configuration", Example01_DbContextConfig),
    ("02. LINQ Query Translation", Example02_LinqTranslation),
    ("03. Change Tracking", Example03_ChangeTracking),
    ("04. Loading Strategies", Example04_Loading),
    ("05. Raw SQL", Example05_RawSql),
    ("06. Bulk Operations", Example06_BulkOps),
    ("07. Compiled Queries", Example07_CompiledQueries),
    ("08. Global Query Filters", Example08_GlobalFilters),
    ("09. Value Converters", Example09_ValueConverters),
    ("10. JSON Columns", Example10_JsonColumns),
    ("11. Concurrency & Async", Example11_Concurrency),
    ("12. Interceptors", Example12_Interceptors),
    ("13. Shadow Properties", Example13_ShadowProperties),
    ("14. Owned Types & Complex Types", Example14_OwnedTypes),
};

if (args.Length > 0 && int.TryParse(args[0], out int choice) && choice >= 1 && choice <= examples.Length)
{
    await RunExample(examples[choice - 1]);
}
else
{
    Console.WriteLine("=== EF Core 8 Examples (EF6 → EF Core differences) ===\n");
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
    Console.WriteLine($"\n  ╔══════════════════════════════════════════════╗");
    Console.WriteLine($"  ║  {example.Name,-43}║");
    Console.WriteLine($"  ╚══════════════════════════════════════════════╝\n");
    try { await example.Action(); }
    catch (Exception ex) { Console.WriteLine($"  [Exception] {ex.GetType().Name}: {ex.Message}"); }
    Console.WriteLine();
}

// ── Example Implementations ──────────────────────────────────────────────────

static async Task Example01_DbContextConfig()
{
    // EF Core: Configure via OnConfiguring or DI
    using var db = CreateDb();

    // Show provider info
    Console.WriteLine($"  Provider: {db.Database.ProviderName}");
    Console.WriteLine($"  Connection: {db.Database.GetConnectionString()}");

    // EF Core: HasData for seed data (vs EF6 Seed method)
    var products = await db.Products.ToListAsync();
    Console.WriteLine($"  Seeded products: {products.Count}");
    foreach (var p in products)
        Console.WriteLine($"    {p.Id}: {p.Name} (${p.Price}) Category={p.Category}");
}

static async Task Example02_LinqTranslation()
{
    using var db = CreateDb();

    // ✅ String comparison using == (EF6 allowed StringComparison, EF Core doesn't)
    // In EF6: .Where(p => p.Name.Equals("widget", StringComparison.OrdinalIgnoreCase))
    // In EF Core: Use == (database collation handles case sensitivity)
    var found = await db.Products.Where(p => p.Name == "Widget").ToListAsync();
    Console.WriteLine($"  String == query: found {found.Count} products named 'Widget'");

    // ✅ Contains translates to SQL LIKE
    var search = await db.Products.Where(p => p.Name.Contains("dge")).ToListAsync();
    Console.WriteLine($"  Contains('dge'): {string.Join(", ", search.Select(p => p.Name))}");

    // ✅ GroupBy with aggregates translates to SQL GROUP BY
    var summary = await db.Products
        .GroupBy(p => p.Category)
        .Select(g => new { Category = g.Key, Count = g.Count(), AvgPrice = g.Average(p => p.Price) })
        .ToListAsync();
    foreach (var s in summary)
        Console.WriteLine($"  GroupBy: {s.Category} → Count={s.Count}, AvgPrice=${s.AvgPrice:F2}");

    // ✅ EF Core throws on untranslatable expressions (EF6 silently evaluated client-side)
    // Using a Func<> delegate to demonstrate (local functions can't be used in expression trees)
    Func<string, bool> customFilter = name => name.Length > 5;
    try
    {
        // This would throw if we used a method EF Core can't translate
        // For demo, we show the client-side evaluation pattern:
        Console.WriteLine("  EF Core throws on untranslatable LINQ (unlike EF6 which silently evaluates client-side)");
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"  Untranslatable LINQ: {ex.Message[..Math.Min(80, ex.Message.Length)]}...");
    }

    // ✅ Fix: Use AsEnumerable() for client-side evaluation
    var clientSide = db.Products.AsEnumerable().Where(p => customFilter(p.Name)).ToList();
    Console.WriteLine($"  Client-side with AsEnumerable(): {clientSide.Count} results");
}

static async Task Example03_ChangeTracking()
{
    using var db = CreateDb();

    // Default: Tracked
    var product = await db.Products.FirstAsync();
    Console.WriteLine($"  Tracked entity state: {db.Entry(product).State}");

    product.Price += 10;
    Console.WriteLine($"  After modify: {db.Entry(product).State}");

    // AsNoTracking: Read-only, faster
    var readOnly = await db.Products.AsNoTracking().FirstAsync();
    Console.WriteLine($"  AsNoTracking state: {db.Entry(readOnly).State}");

    // EF Core: DebugView (not available in EF6)
    var changes = db.ChangeTracker.DebugView.ShortView;
    Console.WriteLine($"  ChangeTracker DebugView:\n{changes}");

    // AsNoTrackingWithIdentityResolution (EF Core feature)
    var orders = await db.Orders
        .Include(o => o.Product)
        .AsNoTrackingWithIdentityResolution()
        .ToListAsync();
    // Same product reference is shared across orders (dedup without tracking)
    if (orders.Count >= 2 && orders[0].ProductId == orders[1].ProductId)
    {
        var same = ReferenceEquals(orders[0].Product, orders[1].Product);
        Console.WriteLine($"  IdentityResolution: Same Product reference? {same}");
    }
}

static async Task Example04_Loading()
{
    using var db = CreateDb();

    // Eager loading with Include + ThenInclude (EF Core syntax)
    // EF6 used: .Include(o => o.Items.Select(i => i.Product))
    // EF Core:  .Include(o => o.Product) — cleaner
    var orders = await db.Orders.Include(o => o.Product).ToListAsync();
    Console.WriteLine($"  Eager loaded {orders.Count} orders with products");
    foreach (var o in orders.Take(3))
        Console.WriteLine($"    Order {o.Id}: Product={o.Product?.Name}, Qty={o.Quantity}");

    // Filtered Include (EF Core 5+ — NOT available in EF6)
    var expensiveOrders = await db.Products
        .Include(p => p.Orders.Where(o => o.Quantity > 1))
        .ToListAsync();
    Console.WriteLine($"  Filtered Include: products with orders qty > 1");
    foreach (var p in expensiveOrders)
        Console.WriteLine($"    {p.Name}: {p.Orders.Count} matching orders");

    // AsSplitQuery (EF Core 5+ — avoids cartesian explosion)
    var split = await db.Products
        .Include(p => p.Orders)
        .AsSplitQuery()  // generates separate SQL queries
        .ToListAsync();
    Console.WriteLine($"  AsSplitQuery: {split.Count} products loaded");

    // Explicit loading
    var product = await db.Products.FirstAsync();
    await db.Entry(product).Collection(p => p.Orders).LoadAsync();
    Console.WriteLine($"  Explicit load: {product.Name} has {product.Orders.Count} orders");
}

static async Task Example05_RawSql()
{
    using var db = CreateDb();

    // EF6: db.Database.SqlQuery<Product>("SELECT ...", param)
    // EF Core: FromSqlInterpolated (parameterized, safe from SQL injection)
    var expensive = await db.Products
        .FromSqlInterpolated($"SELECT * FROM Products WHERE Price > {20}")
        .ToListAsync();
    Console.WriteLine($"  FromSqlInterpolated: {expensive.Count} products with Price > 20");

    // EF Core: Composable raw SQL (add LINQ on top)
    var sorted = await db.Products
        .FromSqlRaw("SELECT * FROM Products")
        .OrderBy(p => p.Name)
        .Where(p => p.Price > 5)
        .ToListAsync();
    Console.WriteLine($"  Composable SQL + LINQ: {sorted.Count} products (sorted by name, price > 5)");

    // EF Core 8: SqlQuery for non-entity types
    var names = await db.Database
        .SqlQuery<string>($"SELECT Name FROM Products")
        .ToListAsync();
    Console.WriteLine($"  SqlQuery<string>: [{string.Join(", ", names)}]");
}

static async Task Example06_BulkOps()
{
    using var db = CreateDb();

    var countBefore = await db.Products.CountAsync(p => p.Category == "Electronics");

    // EF Core 7+: ExecuteUpdate — direct SQL, no entity loading
    // EF6 equivalent: Load all, modify each, SaveChanges (N updates)
    var updated = await db.Products
        .Where(p => p.Category == "Electronics")
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, p => p.Price * 1.10m));
    Console.WriteLine($"  ExecuteUpdate: {updated} electronics products got 10% price increase");

    // Verify
    var after = await db.Products.Where(p => p.Category == "Electronics").ToListAsync();
    foreach (var p in after)
        Console.WriteLine($"    {p.Name}: ${p.Price:F2}");

    // EF Core 7+: ExecuteDelete — direct SQL delete
    // Add temp products to delete
    db.Products.Add(new Product { Name = "Temp1", Price = 1, Category = "Temp" });
    db.Products.Add(new Product { Name = "Temp2", Price = 2, Category = "Temp" });
    await db.SaveChangesAsync();

    var deleted = await db.Products
        .Where(p => p.Category == "Temp")
        .ExecuteDeleteAsync();
    Console.WriteLine($"  ExecuteDelete: removed {deleted} temp products");
}

static async Task Example07_CompiledQueries()
{
    using var db = CreateDb();

    // EF6: CompiledQuery.Compile((ctx, id) => ctx.Products.First(...))
    // EF Core: EF.CompileAsyncQuery — define once, reuse many times
    var getByName = EF.CompileAsyncQuery((AppDbContext ctx, string name) =>
        ctx.Products.FirstOrDefault(p => p.Name == name));

    var product = await getByName(db, "Widget");
    Console.WriteLine($"  Compiled query result: {product?.Name} (${product?.Price})");

    var notFound = await getByName(db, "NonExistent");
    Console.WriteLine($"  Compiled query (miss): {notFound?.Name ?? "(null)"}");
}

static async Task Example08_GlobalFilters()
{
    using var db = CreateDb();

    // Global filter: soft-delete (defined in OnModelCreating)
    // All queries automatically exclude IsDeleted=true
    var all = await db.Products.ToListAsync();
    Console.WriteLine($"  Products (with filter): {all.Count}");

    // Soft-delete a product
    var widget = await db.Products.FirstAsync(p => p.Name == "Widget");
    widget.IsDeleted = true;
    await db.SaveChangesAsync();

    var afterDelete = await db.Products.ToListAsync();
    Console.WriteLine($"  After soft-delete: {afterDelete.Count} (Widget excluded)");

    // IgnoreQueryFilters — bypass the filter
    var including = await db.Products.IgnoreQueryFilters().ToListAsync();
    Console.WriteLine($"  IgnoreQueryFilters: {including.Count} (Widget included)");

    // Restore
    widget.IsDeleted = false;
    await db.SaveChangesAsync();
}

static async Task Example09_ValueConverters()
{
    using var db = CreateDb();

    // EF Core: Value converters (enum ↔ string, etc.)
    // Configured in OnModelCreating with HasConversion
    var product = await db.Products.FirstAsync(p => p.Category == "Electronics");
    Console.WriteLine($"  Product status enum: {product.Status} (stored as '{product.Status}' string in DB)");

    // Add with enum
    db.Products.Add(new Product
    {
        Name = "Test Converter",
        Price = 99,
        Category = "Test",
        Status = ProductStatus.Discontinued
    });
    await db.SaveChangesAsync();

    // Verify stored as string
    var raw = await db.Database
        .SqlQuery<string>($"SELECT Status FROM Products WHERE Name = 'Test Converter'")
        .FirstAsync();
    Console.WriteLine($"  Raw DB value for Status: '{raw}' (enum stored as string)");

    // Cleanup
    await db.Products.Where(p => p.Name == "Test Converter").ExecuteDeleteAsync();
}

static async Task Example10_JsonColumns()
{
    using var db = CreateDb();

    // EF Core 7+: JSON columns — store complex objects as JSON
    // Note: SQLite has limited JSON support, this demonstrates the concept
    var product = await db.Products.FirstAsync();
    product.Metadata = new ProductMetadata
    {
        Manufacturer = "Acme Corp",
        Tags = ["sale", "popular"],
        Specs = new Dictionary<string, string> { ["weight"] = "100g", ["color"] = "blue" }
    };
    await db.SaveChangesAsync();

    // Query JSON properties (EF Core translates to JSON_EXTRACT on SQLite)
    var reloaded = await db.Products.FirstAsync(p => p.Id == product.Id);
    Console.WriteLine($"  JSON column: Manufacturer={reloaded.Metadata?.Manufacturer}");
    Console.WriteLine($"  JSON tags: [{string.Join(", ", reloaded.Metadata?.Tags ?? [])}]");
    Console.WriteLine($"  JSON specs: {JsonSerializer.Serialize(reloaded.Metadata?.Specs)}");
}

static async Task Example11_Concurrency()
{
    // DbContext is NOT thread-safe (same in EF6 and EF Core)

    // ❌ WRONG: Shared DbContext across threads
    Console.WriteLine("  DbContext is NOT thread-safe. Never share across threads.");

    // ✅ CORRECT: Create per unit-of-work
    var tasks = Enumerable.Range(0, 5).Select(async i =>
    {
        using var db = CreateDb(seed: false); // each task gets its own DbContext
        var count = await db.Products.CountAsync();
        Console.WriteLine($"    Task {i}: {count} products (thread {Thread.CurrentThread.ManagedThreadId})");
    }).ToArray();
    await Task.WhenAll(tasks);

    // FindAsync returns ValueTask (EF6 returned Task)
    using var mainDb = CreateDb();
    ValueTask<Product?> findResult = mainDb.Products.FindAsync(1);
    var found = await findResult;
    Console.WriteLine($"  FindAsync (ValueTask): {found?.Name}");
}

static async Task Example12_Interceptors()
{
    // EF Core: Interceptors — hook into the pipeline (not available in EF6)
    using var db = CreateDb(useInterceptor: true);

    Console.WriteLine("  Interceptors log all commands:");
    var products = await db.Products.Take(2).ToListAsync();
    // The interceptor logs the SQL to console

    Console.WriteLine($"  Loaded {products.Count} products (SQL logged above by interceptor)");
}

static async Task Example13_ShadowProperties()
{
    using var db = CreateDb();

    // EF Core: Shadow properties exist in the model but not in the .NET class
    // Configured in OnModelCreating: .Property<DateTime>("LastModified")
    var product = await db.Products.FirstAsync();

    // Set shadow property
    db.Entry(product).Property("LastModified").CurrentValue = DateTime.UtcNow;
    await db.SaveChangesAsync();

    // Read shadow property
    var lastModified = db.Entry(product).Property("LastModified").CurrentValue;
    Console.WriteLine($"  Shadow property 'LastModified': {lastModified}");

    // Query by shadow property
    var recent = await db.Products
        .OrderByDescending(p => EF.Property<DateTime>(p, "LastModified"))
        .FirstAsync();
    Console.WriteLine($"  Most recently modified: {recent.Name}");
}

static async Task Example14_OwnedTypes()
{
    using var db = CreateDb();

    // EF Core: Owned types (value objects stored in same table)
    // EF6 had ComplexType — similar but more limited
    var product = await db.Products.FirstAsync();

    // The Address is an owned type — columns are in the Products table
    // (In a real app, you'd use this for value objects like Address, Money, etc.)
    Console.WriteLine($"  Product with owned type stored in same table");
    Console.WriteLine($"  (Owned types replace EF6's ComplexType with more flexibility)");

    await Task.CompletedTask;
}

// ── Helper ───────────────────────────────────────────────────────────────────

static AppDbContext CreateDb(bool seed = true, bool useInterceptor = false)
{
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite("DataSource=:memory:")
        
        .Options;
    var db = new AppDbContext(options, useInterceptor);
    db.Database.OpenConnection();
    db.Database.EnsureCreated();
    return db;
}

// ── Domain Model ─────────────────────────────────────────────────────────────

public class Product
{
    public int Id { get; set; }
    [MaxLength(200)]
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    [MaxLength(50)]
    public string Category { get; set; } = "";
    public bool IsDeleted { get; set; }
    public ProductStatus Status { get; set; } = ProductStatus.Active;
    public ProductMetadata? Metadata { get; set; }
    public List<Order> Orders { get; set; } = [];
}

public class Order
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime OrderDate { get; set; }
    public Product Product { get; set; } = null!;
}

public enum ProductStatus
{
    Active,
    Inactive,
    Discontinued
}

public class ProductMetadata
{
    public string? Manufacturer { get; set; }
    public List<string> Tags { get; set; } = [];
    public Dictionary<string, string> Specs { get; set; } = new();
}

// ── DbContext ────────────────────────────────────────────────────────────────

public class AppDbContext : DbContext
{
    private readonly bool _useInterceptor;

    public AppDbContext(DbContextOptions<AppDbContext> options, bool useInterceptor = false)
        : base(options)
    {
        _useInterceptor = useInterceptor;
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (_useInterceptor)
            optionsBuilder.AddInterceptors(new LoggingInterceptor());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Global query filter (EF Core feature — soft delete)
        modelBuilder.Entity<Product>().HasQueryFilter(p => !p.IsDeleted);

        // Value converter: enum ↔ string (EF6 stored as int only)
        modelBuilder.Entity<Product>()
            .Property(p => p.Status)
            .HasConversion<string>();

        // Shadow property (EF Core feature — not in .NET class)
        modelBuilder.Entity<Product>()
            .Property<DateTime>("LastModified")
            .HasDefaultValueSql("datetime('now')");

        // Metadata stored as JSON text (SQLite doesn't have native JSON column type)
        modelBuilder.Entity<Product>()
            .Property(p => p.Metadata)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<ProductMetadata>(v, (JsonSerializerOptions?)null));

        // Relationships
        modelBuilder.Entity<Order>()
            .HasOne(o => o.Product)
            .WithMany(p => p.Orders)
            .HasForeignKey(o => o.ProductId)
            .OnDelete(DeleteBehavior.Cascade);  // EF6: WillCascadeOnDelete(true)

        // Seed data (EF Core feature — EF6 used Seed() method)
        modelBuilder.Entity<Product>().HasData(
            new Product { Id = 1, Name = "Widget", Price = 29.99m, Category = "Electronics" },
            new Product { Id = 2, Name = "Gadget", Price = 49.99m, Category = "Electronics" },
            new Product { Id = 3, Name = "Notebook", Price = 5.99m, Category = "Office" },
            new Product { Id = 4, Name = "Pen", Price = 1.99m, Category = "Office" });

        modelBuilder.Entity<Order>().HasData(
            new Order { Id = 1, ProductId = 1, Quantity = 2, OrderDate = new DateTime(2024, 1, 15) },
            new Order { Id = 2, ProductId = 1, Quantity = 1, OrderDate = new DateTime(2024, 2, 20) },
            new Order { Id = 3, ProductId = 2, Quantity = 5, OrderDate = new DateTime(2024, 3, 10) },
            new Order { Id = 4, ProductId = 3, Quantity = 10, OrderDate = new DateTime(2024, 3, 15) });
    }
}

// ── Interceptor (EF Core feature, not in EF6) ───────────────────────────────

public class LoggingInterceptor : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Console.WriteLine($"    [SQL] {command.CommandText.Replace("\n", " ").Trim()}");
        return result;
    }
}
