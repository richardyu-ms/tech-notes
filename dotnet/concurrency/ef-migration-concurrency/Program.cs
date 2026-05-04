// =============================================================================
// EF Migration Concurrency Patterns — From NBAaS EF6 → EF Core 8 Migration
//
// These patterns sit at the intersection of concurrency and EF Core migration.
// They demonstrate how EF Core's truly-async behavior required fundamental
// changes to NBAaS's concurrency architecture.
//
// Part of: concurrency/ (not ef-migration/) because the core lesson is about
// threading, pooling, and async — not EF API surface changes.
//
// Run:  dotnet run          — list all examples
//       dotnet run <number> — run a specific example
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;

class Program
{
    static async Task Main(string[] args)
    {
        var examples = new (string Name, Func<Task> Action)[]
        {
            ("01. Parallel.Invoke → Task.WhenAll (ThreadPool Starvation Fix)", Example01_ParallelInvokeToWhenAll),
            ("02. PooledDbContextFactory Thread Safety (NBAaS EF Core)", Example02_PooledFactory),
        };

        if (args.Length > 0 && int.TryParse(args[0], out int choice) && choice >= 1 && choice <= examples.Length)
        {
            await RunExample(examples[choice - 1]);
        }
        else
        {
            Console.WriteLine("=== EF Migration Concurrency Patterns ===\n");
            Console.WriteLine("  These patterns emerged from NBAaS's EF6 → EF Core 8 migration.");
            Console.WriteLine("  EF Core's truly-async DB calls exposed concurrency bugs that");
            Console.WriteLine("  were hidden by EF6's synchronous internals.\n");
            Console.WriteLine("  See also:");
            Console.WriteLine("    concurrency/Practical-Pattern/  — fundamental concurrency patterns");
            Console.WriteLine("    ef-migration/Practical-Pattern/ — pure EF API migration patterns\n");
            for (int i = 0; i < examples.Length; i++)
                Console.WriteLine($"  {i + 1,2}. {examples[i].Name}");
            Console.WriteLine($"\nUsage: dotnet run <number>  (1-{examples.Length})");
            Console.WriteLine("Or press Enter to run all:\n");
            Console.ReadLine();
            foreach (var example in examples)
                await RunExample(example);
        }
    }

    static async Task RunExample((string Name, Func<Task> Action) example)
    {
        Console.WriteLine($"\n  ╔{"".PadRight(64, '═')}╗");
        Console.WriteLine($"  ║  {example.Name,-62}║");
        Console.WriteLine($"  ╚{"".PadRight(64, '═')}╝\n");
        await example.Action();
    }

    // =========================================================================
    // 01. Parallel.Invoke → Task.WhenAll Migration (NBAaS Improvement)
    // =========================================================================
    // This is the key concurrency improvement in the EF Core migration.
    //
    // BEFORE (EF6): Parallel.Invoke was used to run loaders concurrently.
    //   Parallel.Invoke(
    //       () => pngLoader.ExecuteAsync().Wait(),
    //       () => cpgLoader.ExecuteAsync().Wait(),
    //       () => dmdLoader.ExecuteAsync().Wait()
    //   );
    //
    // PROBLEM: With EF Core, ExecuteAsync truly awaits DB calls. Each
    //   Parallel.Invoke lambda calls .Wait() which blocks a ThreadPool thread.
    //   The awaited async DB call ALSO needs a ThreadPool thread to complete.
    //   With N loaders × M DCs, this leads to ThreadPool starvation.
    //
    // AFTER (EF Core):
    //   var tasks = new List<Task> {
    //       Task.Run(async () => { await pngLoader.ExecuteAsync(); ... }),
    //       dpgLoader.ExecuteAsync(),
    //       ...
    //   };
    //   Task.WhenAll(tasks).Wait();  // single blocking point
    //
    // Java doesn't have this problem with virtual threads:
    //   try (var scope = StructuredTaskScope.ShutdownOnFailure()) {
    //       scope.fork(() -> pngLoader.execute());
    //       scope.fork(() -> dpgLoader.execute());
    //       scope.join();
    //   }
    // =========================================================================
    static async Task Example01_ParallelInvokeToWhenAll()
    {
        const int loaderCount = 8;
        int originalMinThreads, originalMinIOCP;
        ThreadPool.GetMinThreads(out originalMinThreads, out originalMinIOCP);

        // Temporarily reduce ThreadPool to show the starvation problem
        ThreadPool.SetMinThreads(4, 4);

        Console.WriteLine("  --- Parallel.Invoke (BEFORE — sync-over-async) ---");
        var sw1 = Stopwatch.StartNew();
        try
        {
            Parallel.Invoke(
                Enumerable.Range(0, loaderCount)
                    .Select<int, Action>(i => () => SimulateAsyncDbWork($"Loader-{i}", 100).Wait())
                    .ToArray()
            );
        }
        catch (AggregateException) { /* may timeout under starvation */ }
        sw1.Stop();
        Console.WriteLine($"  Completed in {sw1.ElapsedMilliseconds}ms\n");

        Console.WriteLine("  --- Task.WhenAll (AFTER — truly async) ---");
        var sw2 = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, loaderCount)
            .Select(i => SimulateAsyncDbWork($"Loader-{i}", 100))
            .ToList();
        await Task.WhenAll(tasks);
        sw2.Stop();
        Console.WriteLine($"  Completed in {sw2.ElapsedMilliseconds}ms\n");

        // Restore ThreadPool settings
        ThreadPool.SetMinThreads(originalMinThreads, originalMinIOCP);

        Console.WriteLine("  [Why Task.WhenAll wins]");
        Console.WriteLine("  Parallel.Invoke: each lambda blocks a ThreadPool thread with .Wait().");
        Console.WriteLine("  The async continuations ALSO need ThreadPool threads → starvation.");
        Console.WriteLine("  Task.WhenAll: only ONE .Wait() blocking point (or fully async with await).");
        Console.WriteLine("  Async continuations freely use ThreadPool threads without deadlock risk.");

        Console.WriteLine("\n  ┌─── EF6 (BEFORE) — Parallel.Invoke ────────────────────────┐");
        Console.WriteLine("  │  Parallel.Invoke(                                         │");
        Console.WriteLine("  │    () => pngLoader.ExecuteAsync(dc, meta).Wait(),         │");
        Console.WriteLine("  │    () => dpgLoader.ExecuteAsync(dc, meta).Wait(),         │");
        Console.WriteLine("  │    () => cpgLoader.ExecuteAsync(dc, meta).Wait(),         │");
        Console.WriteLine("  │    () => dmdLoader.ExecuteAsync(dc, meta).Wait(),         │");
        Console.WriteLine("  │    () => serverLoader.ExecuteAsync(dc, meta).Wait()       │");
        Console.WriteLine("  │  );                                                       │");
        Console.WriteLine("  │  // 5 ThreadPool threads blocked with .Wait()             │");
        Console.WriteLine("  │  // EF6: async was fake, so no real continuations needed  │");
        Console.WriteLine("  └────────────────────────────────────────────────────────────┘");
        Console.WriteLine();
        Console.WriteLine("  ┌─── EF Core (AFTER) — Task.WhenAll ────────────────────────┐");
        Console.WriteLine("  │  var tasks = new List<Task> {                              │");
        Console.WriteLine("  │    Task.Run(async () => {                                 │");
        Console.WriteLine("  │      await pngLoader.ExecuteAsync(dc, meta);              │");
        Console.WriteLine("  │      await Task.WhenAll(                                  │");
        Console.WriteLine("  │        cpgLoader.ExecuteAsync(dc, meta),                  │");
        Console.WriteLine("  │        Task.Run(async () => {                             │");
        Console.WriteLine("  │          await dmdLoader.ExecuteAsync(dc, meta);           │");
        Console.WriteLine("  │          await buildLoader.ExecuteAsync(dc, meta);        │");
        Console.WriteLine("  │        })                                                 │");
        Console.WriteLine("  │      );                                                   │");
        Console.WriteLine("  │    }),                                                     │");
        Console.WriteLine("  │    dpgLoader.ExecuteAsync(dc, meta),   // independent     │");
        Console.WriteLine("  │    serverLoader.ExecuteAsync(dc, meta) // independent     │");
        Console.WriteLine("  │  };                                                       │");
        Console.WriteLine("  │  Task.WhenAll(tasks).Wait();  // SINGLE blocking point    │");
        Console.WriteLine("  └────────────────────────────────────────────────────────────┘");

        Console.WriteLine("\n  [.NET vs Java]");
        Console.WriteLine("  .NET: This problem is .NET-specific because of sync-over-async anti-pattern.");
        Console.WriteLine("  Java 21+: Virtual threads make blocking cheap — no starvation issue.");
        Console.WriteLine("  Java pre-21: CompletableFuture.allOf() avoids blocking like Task.WhenAll.");
    }

    // =========================================================================
    // 02. PooledDbContextFactory Thread Safety
    // =========================================================================
    // NBAaS pattern: BusinessBase uses a static PooledDbContextFactory<NgsDbContext>.
    // The factory is initialized once (lock) and then used concurrently from
    // multiple API request threads, each getting a pooled context.
    //
    // Real code (BusinessBase.cs):
    //   private static PooledDbContextFactory<NgsDbContext> _pooledFactory;
    //   private static readonly object _factoryLock = new object();
    //   public static void SetGlobalContextFactory(...) {
    //       lock (_factoryLock) { _pooledFactory = factory; }
    //   }
    //   // Usage: _pooledFactory.CreateDbContext()  — thread-safe, returns pooled instance
    //
    // Java equivalent (HikariCP connection pool):
    //   private static final HikariDataSource dataSource;
    //   static { dataSource = new HikariDataSource(config); }
    //   // Usage: dataSource.getConnection()  — thread-safe, returns pooled connection
    // =========================================================================
    static Task Example02_PooledFactory()
    {
        // Simulate the factory pattern
        var factory = new SimulatedContextFactory(poolSize: 8);
        var usageCounts = new ConcurrentDictionary<int, int>();

        // Simulate 100 concurrent API requests
        Parallel.For(0, 100, _ =>
        {
            // Each request gets a context from the pool
            var contextId = factory.CreateContext();
            usageCounts.AddOrUpdate(contextId, 1, (_, c) => c + 1);
            Thread.Sleep(Random.Shared.Next(5, 20)); // simulate query work
            factory.ReturnContext(contextId);
        });

        Console.WriteLine($"  Processed 100 requests with pool size {factory.PoolSize}");
        Console.WriteLine($"  Unique contexts used: {usageCounts.Count}");
        Console.WriteLine($"  Max reuse of single context: {usageCounts.Values.Max()}");
        Console.WriteLine($"  Context distribution: {string.Join(", ", usageCounts.OrderBy(kv => kv.Key).Select(kv => $"#{kv.Key}={kv.Value}x"))}");

        Console.WriteLine("\n  [Pool benefits]");
        Console.WriteLine("  Avoids expensive DbContext construction per request (model building, connection setup).");
        Console.WriteLine("  EF Core's PooledDbContextFactory resets ChangeTracker on return to pool.");
        Console.WriteLine("  NBAaS uses pool size 1024 (configurable) for high-throughput DC loading.");

        Console.WriteLine("\n  [Thread-safety model]");
        Console.WriteLine("  Factory init: lock(_factoryLock) — single writer, many readers after.");
        Console.WriteLine("  CreateDbContext(): internally thread-safe (ConcurrentQueue-based pool).");
        Console.WriteLine("  Each DbContext instance: NOT thread-safe — one context per thread/request.");

        Console.WriteLine("\n  [.NET vs Java]");
        Console.WriteLine("  .NET EF Core: PooledDbContextFactory<T> (pools DbContext + connection)");
        Console.WriteLine("  Java: HikariCP / C3P0 (pools JDBC connections, not ORM sessions)");
        Console.WriteLine("  Java Hibernate: Session is not pooled — connection pool + Session-per-request.");

        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static async Task SimulateAsyncDbWork(string name, int delayMs)
    {
        // Simulates EF Core async DB call — truly yields the thread
        await Task.Delay(delayMs);
    }
}

// ── Supporting Types ──────────────────────────────────────────────────────────

// Simulates PooledDbContextFactory
class SimulatedContextFactory
{
    private readonly ConcurrentQueue<int> _pool = new();
    private int _nextId;

    public int PoolSize { get; }

    public SimulatedContextFactory(int poolSize)
    {
        PoolSize = poolSize;
        for (int i = 0; i < poolSize; i++)
            _pool.Enqueue(i);
    }

    public int CreateContext()
    {
        if (_pool.TryDequeue(out var id))
            return id;
        // Pool exhausted — create new (NBAaS would throw or wait)
        return Interlocked.Increment(ref _nextId) + PoolSize;
    }

    public void ReturnContext(int id)
    {
        if (id < PoolSize)
            _pool.Enqueue(id);
    }
}
