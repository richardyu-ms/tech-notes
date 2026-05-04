// =============================================================================
// NBAaS Concurrency Patterns — Real-World Examples from Azure-Network-Graph
//
// These examples demonstrate concurrency patterns actually used (or improved)
// in the NBAaS codebase, with Java equivalents for comparison.
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
            ("01. Parallel.ForEach with ConcurrentDictionary (NBAaS GraphLoader)", Example01_ParallelForEachWithConcurrentDict),
            ("02. Task.WhenAll Dependency Chain (NBAaS Loader Pipeline)", Example02_TaskWhenAllDependencyChain),
            ("03. ForEachAsync — SemaphoreSlim Throttled (NBAaS Extension.Linq)", Example03_ForEachAsync),
            ("04. Interlocked Counters (NBAaS RequestMetrics)", Example04_InterlockedCounters),
            ("05. Double-Checked Locking (NBAaS NgsDbContext)", Example05_DoubleCheckedLocking),
            ("06. ConcurrentDictionary.AddOrUpdate (NBAaS Phase Metrics)", Example06_AddOrUpdate),
            ("07. Producer-Consumer with ConcurrentQueue (NBAaS VladRunner)", Example07_ProducerConsumer),
            ("08. .NET vs Java Concurrent Structures Comparison", Example08_DotNetVsJava),
        };

        if (args.Length > 0 && int.TryParse(args[0], out int choice) && choice >= 1 && choice <= examples.Length)
        {
            await RunExample(examples[choice - 1]);
        }
        else
        {
            Console.WriteLine("=== NBAaS Concurrency Patterns (Fundamentals) ===\n");
            Console.WriteLine("  NOTE: EF migration concurrency patterns (Parallel.Invoke→Task.WhenAll,");
            Console.WriteLine("  PooledDbContextFactory) are in concurrency/ef-migration-concurrency/\n");
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
        Console.WriteLine($"\n  ╔{"".PadRight(46, '═')}╗");
        Console.WriteLine($"  ║  {example.Name,-44}║");
        Console.WriteLine($"  ╚{"".PadRight(46, '═')}╝\n");
        await example.Action();
    }

    // =========================================================================
    // 01. Parallel.ForEach + ConcurrentDictionary
    // =========================================================================
    // NBAaS pattern: GraphLoader loads all datacenters in parallel.
    // Each DC produces graph data stored in a shared ConcurrentDictionary.
    //
    // Real code (ServerLoader.cs):
    //   var servers = new ConcurrentDictionary<string, ServersDeclarationServer>(...);
    //   Parallel.ForEach(metadataContext.Servers..., server => {
    //       servers.TryAdd(serverDec.Hostname, serverDec);
    //   });
    //
    // Java equivalent:
    //   ConcurrentHashMap<String, Server> servers = new ConcurrentHashMap<>();
    //   datacenters.parallelStream().forEach(dc -> servers.put(dc.name(), dc));
    //   // or: ForkJoinPool with submit()
    // =========================================================================
    static Task Example01_ParallelForEachWithConcurrentDict()
    {
        // Simulate loading 100 datacenters, each with multiple devices
        var allDevices = new ConcurrentDictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);
        var datacenters = Enumerable.Range(1, 100).Select(i => $"DC-{i:D3}").ToList();

        var sw = Stopwatch.StartNew();

        // NBAaS uses MaxDegreeOfParallelism to control resource usage
        Parallel.ForEach(
            datacenters,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            dc =>
            {
                // Simulate loading devices for each datacenter
                for (int d = 0; d < 50; d++)
                {
                    var deviceName = $"{dc}-DEVICE-{d:D3}";
                    allDevices.TryAdd(deviceName, new DeviceInfo
                    {
                        Name = deviceName,
                        Datacenter = dc,
                        DeviceType = d % 3 == 0 ? "Router" : d % 3 == 1 ? "Switch" : "Server"
                    });
                }
            });

        sw.Stop();

        Console.WriteLine($"  Loaded {allDevices.Count} devices from {datacenters.Count} DCs in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Routers: {allDevices.Values.Count(d => d.DeviceType == "Router")}");
        Console.WriteLine($"  Switches: {allDevices.Values.Count(d => d.DeviceType == "Switch")}");
        Console.WriteLine($"  Servers: {allDevices.Values.Count(d => d.DeviceType == "Server")}");

        // Java comparison:
        //   Java:   ConcurrentHashMap + parallelStream().forEach()
        //   .NET:   ConcurrentDictionary + Parallel.ForEach()
        //   Key difference: .NET's Parallel.ForEach uses ThreadPool + work-stealing.
        //   Java's parallelStream() uses ForkJoinPool (also work-stealing).
        //   Both limit concurrency: .NET via MaxDegreeOfParallelism, Java via ForkJoinPool size.
        Console.WriteLine("\n  [.NET vs Java]");
        Console.WriteLine("  .NET: ConcurrentDictionary + Parallel.ForEach(MaxDegreeOfParallelism)");
        Console.WriteLine("  Java: ConcurrentHashMap + parallelStream().forEach() (ForkJoinPool)");

        return Task.CompletedTask;
    }

    // =========================================================================
    // 02. Task.WhenAll with Dependency Chains
    // =========================================================================
    // NBAaS pattern: GraphLoader.FromFile loads multiple graph types per DC.
    // Some loaders have dependencies: PNG must load before CPG and DMD,
    // DMD must load before BuildMetadata. Independent loaders run in parallel.
    //
    // Real code (GraphLoader.cs):
    //   var tasks = new List<Task> {
    //       Task.Run(async () => {
    //           await pngLoader.ExecuteAsync(...);          // PNG first
    //           await Task.WhenAll(
    //               cpgLoader.ExecuteAsync(...),            // CPG after PNG
    //               Task.Run(async () => {
    //                   await dmdLoader.ExecuteAsync(...);  // DMD after PNG
    //                   await buildLoader.ExecuteAsync(...); // Build after DMD
    //               })
    //           );
    //       }),
    //       dpgLoader.ExecuteAsync(...),     // independent
    //       sliceLoader.ExecuteAsync(...),   // independent
    //   };
    //   Task.WhenAll(tasks).Wait();
    //
    // Java equivalent:
    //   CompletableFuture<Void> pngFuture = CompletableFuture.runAsync(() -> pngLoader.execute());
    //   CompletableFuture<Void> cpgFuture = pngFuture.thenRunAsync(() -> cpgLoader.execute());
    //   CompletableFuture<Void> dmdThenBuild = pngFuture
    //       .thenRunAsync(() -> dmdLoader.execute())
    //       .thenRunAsync(() -> buildLoader.execute());
    //   CompletableFuture<Void> dpg = CompletableFuture.runAsync(() -> dpgLoader.execute());
    //   CompletableFuture.allOf(cpgFuture, dmdThenBuild, dpg).join();
    // =========================================================================
    static async Task Example02_TaskWhenAllDependencyChain()
    {
        var sw = Stopwatch.StartNew();

        // Simulate NBAaS graph loading pipeline for one datacenter
        var tasks = new List<Task>
        {
            // Dependency chain: PNG → (CPG, DMD→Build)
            Task.Run(async () =>
            {
                await SimulateLoader("PNG Loader", 200);  // PNG must run first
                var innerTasks = new List<Task>
                {
                    SimulateLoader("CPG Loader", 150),     // CPG depends on PNG
                    Task.Run(async () =>
                    {
                        await SimulateLoader("DMD Loader", 100);          // DMD depends on PNG
                        await SimulateLoader("BuildMetadata Loader", 80); // Build depends on DMD
                    })
                };
                await Task.WhenAll(innerTasks);
            }),

            // Independent loaders — run in parallel with everything above
            SimulateLoader("DPG Loader", 180),
            SimulateLoader("Slice Loader", 120),
            SimulateLoader("Server Loader", 100),
            SimulateLoader("LMI Loader", 90),
        };

        await Task.WhenAll(tasks);
        sw.Stop();

        // The critical path is PNG(200) → max(CPG(150), DMD(100)+Build(80)) = 200+180 = 380ms
        // Total wall-clock should be ~max(380, 180, 120, 100, 90) ≈ 380ms (not sum of all)
        Console.WriteLine($"\n  Total wall-clock: {sw.ElapsedMilliseconds}ms (sequential would be ~920ms)");
        Console.WriteLine($"  Speedup: ~{920.0 / Math.Max(sw.ElapsedMilliseconds, 1):F1}x");

        Console.WriteLine("\n  [Dependency Graph]");
        Console.WriteLine("  PNG ──┬── CPG");
        Console.WriteLine("        └── DMD ── BuildMetadata");
        Console.WriteLine("  DPG ──────── (independent)");
        Console.WriteLine("  Slice ────── (independent)");
        Console.WriteLine("  Server ───── (independent)");
        Console.WriteLine("  LMI ──────── (independent)");

        Console.WriteLine("\n  [.NET vs Java]");
        Console.WriteLine("  .NET: Task.Run(async () => { await A; await Task.WhenAll(B,C); })");
        Console.WriteLine("  Java: CompletableFuture.thenRunAsync() + CompletableFuture.allOf()");
    }

    // =========================================================================
    // 03. ForEachAsync — SemaphoreSlim Throttled Parallel
    // =========================================================================
    // NBAaS pattern: Extension.Linq.ForEachAsync provides async Parallel.ForEach
    // with controlled concurrency via SemaphoreSlim.
    //
    // Real code (Extension.Linq.cs):
    //   public static async Task ForEachAsync<T>(
    //       this IEnumerable<T> items, int maxDegreeOfParallelism, Func<T, Task> body)
    //   {
    //       using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
    //       var tasks = items.Select(async item => {
    //           await semaphore.WaitAsync();
    //           try { await body(item); }
    //           finally { semaphore.Release(); }
    //       });
    //       await Task.WhenAll(tasks);
    //   }
    //
    // Java equivalent:
    //   Semaphore semaphore = new Semaphore(maxParallelism);
    //   List<CompletableFuture<Void>> futures = items.stream()
    //       .map(item -> CompletableFuture.runAsync(() -> {
    //           semaphore.acquire(); try { body(item); } finally { semaphore.release(); }
    //       })).toList();
    //   CompletableFuture.allOf(futures.toArray(new CompletableFuture[0])).join();
    // =========================================================================
    static async Task Example03_ForEachAsync()
    {
        var items = Enumerable.Range(1, 20).ToList();
        int maxParallelism = 4;
        var activeCount = 0;
        var maxObserved = 0;

        var sw = Stopwatch.StartNew();

        // This is the exact pattern from NBAaS Extension.Linq.ForEachAsync
        await ForEachAsync(items, maxParallelism, async item =>
        {
            var current = Interlocked.Increment(ref activeCount);
            // Track max concurrent executions
            int observed;
            do { observed = maxObserved; }
            while (current > observed && Interlocked.CompareExchange(ref maxObserved, current, observed) != observed);

            await Task.Delay(50 + Random.Shared.Next(50));  // simulate async I/O
            Interlocked.Decrement(ref activeCount);
        });

        sw.Stop();

        Console.WriteLine($"  Processed {items.Count} items with maxParallelism={maxParallelism}");
        Console.WriteLine($"  Max concurrent observed: {maxObserved} (should be ≤ {maxParallelism})");
        Console.WriteLine($"  Wall-clock: {sw.ElapsedMilliseconds}ms (sequential would be ~{items.Count * 75}ms)");

        Console.WriteLine("\n  [Why this pattern?]");
        Console.WriteLine("  Parallel.ForEach is sync-only — it blocks ThreadPool threads on await.");
        Console.WriteLine("  ForEachAsync uses SemaphoreSlim for truly async throttling.");
        Console.WriteLine("  This is critical for EF Core, which requires async I/O internally.");

        Console.WriteLine("\n  [.NET vs Java]");
        Console.WriteLine("  .NET: SemaphoreSlim.WaitAsync() + Task.WhenAll");
        Console.WriteLine("  Java: Semaphore.acquire() + CompletableFuture.allOf()");
        Console.WriteLine("  Java 21+: Virtual threads + Semaphore (no thread blocking penalty)");
    }

    // =========================================================================
    // 04. Interlocked Counters — Lock-Free Metrics
    // =========================================================================
    // NBAaS pattern: RequestMetricsContext uses Interlocked for API metrics.
    //
    // Real code (RequestMetricsContext.cs):
    //   Interlocked.Increment(ref _totalRequests);
    //   Interlocked.Add(ref _totalRequestDurationMs, durationMs);
    //   // Reset: Interlocked.Exchange(ref _totalRequests, 0);
    //
    // Java equivalent:
    //   AtomicInteger totalRequests = new AtomicInteger();
    //   AtomicLong totalDurationMs = new AtomicLong();
    //   totalRequests.incrementAndGet();
    //   totalDurationMs.addAndGet(durationMs);
    //   totalRequests.getAndSet(0);  // reset
    // =========================================================================
    static Task Example04_InterlockedCounters()
    {
        // Simulate NBAaS RequestMetricsContext
        var metrics = new RequestMetrics();
        const int numThreads = 8;
        const int requestsPerThread = 10_000;

        var sw = Stopwatch.StartNew();

        // Simulate concurrent API requests from multiple threads
        Parallel.For(0, numThreads, _ =>
        {
            var rng = Random.Shared;
            for (int i = 0; i < requestsPerThread; i++)
            {
                metrics.RecordRequest("GET", "/api/devices", 200, rng.Next(5, 50));
            }
        });

        sw.Stop();

        var (totalReqs, totalDuration) = metrics.GetAndReset();
        Console.WriteLine($"  Recorded {totalReqs:N0} requests in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Total duration: {totalDuration:N0}ms, Avg: {totalDuration / Math.Max(totalReqs, 1)}ms/req");
        Console.WriteLine($"  Expected: {numThreads * requestsPerThread:N0} requests — Match: {totalReqs == numThreads * requestsPerThread}");
        Console.WriteLine($"  After reset: {metrics.TotalRequests} requests (should be 0)");

        Console.WriteLine("\n  [Why Interlocked over lock?]");
        Console.WriteLine("  Interlocked uses CPU CAS (Compare-And-Swap) instructions — no kernel transition.");
        Console.WriteLine("  lock involves kernel mutex when contended — much slower for simple counters.");
        Console.WriteLine("  Rule: use Interlocked for single-variable atomic ops, lock for multi-variable invariants.");

        Console.WriteLine("\n  [.NET vs Java]");
        Console.WriteLine("  .NET: Interlocked.Increment(ref field) — operates on plain fields");
        Console.WriteLine("  Java: AtomicInteger.incrementAndGet() — requires wrapper object");
        Console.WriteLine("  Java 9+ also: VarHandle for low-level CAS (like .NET Interlocked)");

        return Task.CompletedTask;
    }

    // =========================================================================
    // 05. Double-Checked Locking
    // =========================================================================
    // NBAaS pattern: NgsDbContext uses double-checked locking to lazily
    // initialize the table metadata list.
    //
    // Real code (NgsDbContext.cs):
    //   if (dbTables == null)
    //       lock (GetTablesLock)
    //           if (dbTables == null)
    //               dbTables = assembly.GetTypes().Where(...).ToList();
    //
    // Also: SplitLargeFileDumpConfigProvider uses volatile + lock:
    //   private static volatile bool _isLoaded;
    //   public static void Initialize(...) {
    //       lock (_lock) { if (_isLoaded && ...) return; Reload(); }
    //   }
    //
    // Java equivalent:
    //   private static volatile List<TableConfig> tables;
    //   public static List<TableConfig> getTables() {
    //       if (tables == null) {
    //           synchronized (lock) {
    //               if (tables == null) tables = loadTables();
    //           }
    //       }
    //       return tables;
    //   }
    // =========================================================================
    static Task Example05_DoubleCheckedLocking()
    {
        var registry = new LazyTableRegistry();
        var results = new ConcurrentBag<(int threadId, int tableCount, bool wasFirst)>();

        // Multiple threads try to access tables simultaneously
        Parallel.For(0, 20, _ =>
        {
            bool wasFirst = !registry.IsInitialized;
            var tables = registry.GetTables();
            results.Add((Thread.CurrentThread.ManagedThreadId, tables.Count, wasFirst));
        });

        Console.WriteLine($"  Total calls: {results.Count}");
        Console.WriteLine($"  Initialize called: {registry.InitializeCount} time(s) (should be 1)");
        Console.WriteLine($"  All got same count: {results.All(r => r.tableCount == results.First().tableCount)}");

        // Show the Lazy<T> alternative (preferred in new code)
        var lazyTables = new Lazy<List<string>>(
            () => { Console.WriteLine("  [Lazy<T>] Initializing..."); return new List<string> { "Device", "Link", "Server" }; },
            LazyThreadSafetyMode.ExecutionAndPublication  // equivalent to double-checked locking
        );

        Console.WriteLine($"\n  Lazy<T> value: {string.Join(", ", lazyTables.Value)}");
        Console.WriteLine($"  Lazy<T> value (2nd call, no init): {string.Join(", ", lazyTables.Value)}");

        Console.WriteLine("\n  [Modern alternative]");
        Console.WriteLine("  Lazy<T> with LazyThreadSafetyMode.ExecutionAndPublication");
        Console.WriteLine("  is the same pattern but encapsulated. NBAaS BusinessBase uses");
        Console.WriteLine("  Lazy<NgsDbContext> for deferred context creation.");

        Console.WriteLine("\n  [.NET vs Java]");
        Console.WriteLine("  .NET: volatile + lock + null check (or Lazy<T>)");
        Console.WriteLine("  Java: volatile + synchronized + null check (or lazy holder idiom)");
        Console.WriteLine("  Both require volatile to prevent instruction reordering on reads.");

        return Task.CompletedTask;
    }

    // =========================================================================
    // 06. ConcurrentDictionary.AddOrUpdate — Atomic Aggregation
    // =========================================================================
    // NBAaS pattern: RequestMetricsContext tracks per-phase metrics using
    // ConcurrentDictionary.AddOrUpdate for lock-free accumulation.
    //
    // Real code (RequestMetricsContext.cs):
    //   _phaseMetrics.AddOrUpdate(
    //       phaseType,
    //       (durationMs, 1),
    //       (key, existing) => (existing.DurationMs + durationMs, existing.Count + 1));
    //
    // Java equivalent:
    //   phaseMetrics.merge(phaseType, new Metric(durationMs, 1),
    //       (existing, incoming) -> new Metric(existing.duration + incoming.duration, existing.count + 1));
    // =========================================================================
    static Task Example06_AddOrUpdate()
    {
        var phaseMetrics = new ConcurrentDictionary<string, (long DurationMs, int Count)>();

        // Simulate concurrent phase recordings from multiple API requests
        var phases = new[] { "CLEARDB", "LOADFILE", "DUMPDB", "VALIDATION", "SERIALIZE" };

        Parallel.For(0, 1000, _ =>
        {
            var phase = phases[Random.Shared.Next(phases.Length)];
            var duration = Random.Shared.Next(10, 500);

            phaseMetrics.AddOrUpdate(
                phase,
                (duration, 1),  // addValueFactory: first entry
                (key, existing) => (existing.DurationMs + duration, existing.Count + 1)  // updateValueFactory
            );
        });

        Console.WriteLine("  Phase Metrics (accumulated from 1000 concurrent recordings):");
        foreach (var (phase, (totalMs, count)) in phaseMetrics.OrderByDescending(p => p.Value.DurationMs))
        {
            Console.WriteLine($"    {phase,-15} Count={count,5}  TotalMs={totalMs,8:N0}  AvgMs={totalMs / count,5}");
        }
        Console.WriteLine($"  Total recordings: {phaseMetrics.Values.Sum(v => v.Count)}");

        Console.WriteLine("\n  [Thread-safety note]");
        Console.WriteLine("  AddOrUpdate's updateValueFactory may be called multiple times under contention.");
        Console.WriteLine("  The factory must be side-effect-free (pure function). NBAaS's tuple");
        Console.WriteLine("  arithmetic is safe — but don't do I/O or increment external counters here.");

        Console.WriteLine("\n  [.NET vs Java]");
        Console.WriteLine("  .NET: ConcurrentDictionary.AddOrUpdate(key, addValue, updateFunc)");
        Console.WriteLine("  Java: ConcurrentHashMap.merge(key, value, remappingFunction)");
        Console.WriteLine("  Java's merge() is equivalent — both may retry the function under contention.");

        return Task.CompletedTask;
    }

    // =========================================================================
    // 07. Producer-Consumer with ConcurrentQueue
    // =========================================================================
    // NBAaS pattern: VladRunner uses ConcurrentQueue<string> as a work queue.
    // N parallel workers dequeue VLAD test projects and collect results
    // in a ConcurrentBag<VladProjectExecuteResult>.
    //
    // Real code (VladExecutor.cs):
    //   public ConcurrentQueue<string> TaskQueue { get; }
    //   public ConcurrentBag<VladProjectExecuteResult> Results { get; }
    //   // Workers: while (TaskQueue.TryDequeue(out var project)) { ... Results.Add(result); }
    //
    // Real code (Runner.cs):
    //   for (int i = 0; i < parallelism; i++)
    //       tasks.Add(Task.Run(() => vladExecutor.Execute()));
    //   Task.WaitAll(tasks.ToArray());
    //
    // Java equivalent:
    //   ConcurrentLinkedQueue<String> queue = new ConcurrentLinkedQueue<>(projects);
    //   ExecutorService pool = Executors.newFixedThreadPool(parallelism);
    //   List<Future<?>> futures = IntStream.range(0, parallelism)
    //       .mapToObj(i -> pool.submit(() -> { while ((p = queue.poll()) != null) process(p); }))
    //       .toList();
    // =========================================================================
    static Task Example07_ProducerConsumer()
    {
        // Simulate VLAD test runner
        var taskQueue = new ConcurrentQueue<string>(
            Enumerable.Range(1, 30).Select(i => $"VladProject-{i:D3}")
        );
        var results = new ConcurrentBag<(string Project, bool Passed, int DurationMs)>();
        int parallelism = 4;

        var sw = Stopwatch.StartNew();

        // N workers dequeue and process
        var workers = Enumerable.Range(0, parallelism)
            .Select(workerId => Task.Run(() =>
            {
                while (taskQueue.TryDequeue(out var project))
                {
                    Thread.Sleep(Random.Shared.Next(20, 80)); // simulate work
                    results.Add((project, Random.Shared.Next(10) > 1, Random.Shared.Next(20, 80)));
                }
            }))
            .ToArray();

        Task.WaitAll(workers);
        sw.Stop();

        var passed = results.Count(r => r.Passed);
        Console.WriteLine($"  Processed {results.Count} VLAD projects with {parallelism} workers in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Passed: {passed}, Failed: {results.Count - passed}");
        Console.WriteLine($"  Queue empty: {taskQueue.IsEmpty}");

        Console.WriteLine("\n  [Pattern: Multi-Consumer Work Queue]");
        Console.WriteLine("  ConcurrentQueue.TryDequeue is lock-free (uses Interlocked CAS internally).");
        Console.WriteLine("  ConcurrentBag is optimized for the producer==consumer scenario (thread-local lists).");

        Console.WriteLine("\n  [.NET vs Java]");
        Console.WriteLine("  .NET: ConcurrentQueue<T>.TryDequeue + ConcurrentBag<T>.Add");
        Console.WriteLine("  Java: ConcurrentLinkedQueue<T>.poll + ConcurrentLinkedDeque/synchronized list");
        Console.WriteLine("  Java also: BlockingQueue (ArrayBlockingQueue, LinkedBlockingQueue) for blocking waits");
        Console.WriteLine("  .NET also: BlockingCollection<T> wraps ConcurrentQueue with blocking Take()");

        return Task.CompletedTask;
    }

    // =========================================================================
    // 08. .NET vs Java Concurrent Structures — Side-by-Side Comparison
    // =========================================================================
    static Task Example08_DotNetVsJava()
    {
        Console.WriteLine("  ╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("  ║  .NET vs Java Concurrent Data Structures — Design Philosophy           ║");
        Console.WriteLine("  ╚══════════════════════════════════════════════════════════════════════════╝");

        Console.WriteLine(@"
  ┌─────────────────────────────────────────────────────────────────────────────┐
  │ Category         │ .NET                          │ Java                     │
  ├──────────────────┼───────────────────────────────┼──────────────────────────┤
  │ Dict/Map         │ ConcurrentDictionary<K,V>     │ ConcurrentHashMap<K,V>   │
  │  (lock-free R/W) │   .GetOrAdd(), .AddOrUpdate() │   .computeIfAbsent()     │
  │                  │   Striped locks (31 buckets)   │   Striped locks (16)     │
  │                  │                               │                          │
  │ Queue            │ ConcurrentQueue<T>            │ ConcurrentLinkedQueue<T> │
  │  (lock-free)     │   Internal linked segments     │   Michael-Scott queue    │
  │                  │ ConcurrentStack<T>            │ ConcurrentLinkedDeque<T> │
  │                  │                               │                          │
  │ Blocking Queue   │ BlockingCollection<T>         │ ArrayBlockingQueue<T>    │
  │                  │   wraps any IProducerConsumer  │ LinkedBlockingQueue<T>   │
  │                  │   .Take() / .Add()            │   .take() / .put()       │
  │                  │                               │ PriorityBlockingQueue<T> │
  │                  │                               │                          │
  │ Bag (unordered)  │ ConcurrentBag<T>              │ (no direct equivalent)   │
  │                  │   Thread-local lists + steal   │                          │
  │                  │                               │                          │
  │ Atomic counter   │ Interlocked class (static)    │ AtomicInteger/Long       │
  │                  │   operates on ref fields       │   wrapper objects         │
  │                  │                               │ LongAdder (high-contention)│
  │                  │                               │                          │
  │ Lock             │ lock(obj) / Monitor            │ synchronized / ReentrantLock │
  │                  │   Always reentrant             │   ReentrantLock optional │
  │                  │ ReaderWriterLockSlim           │ ReentrantReadWriteLock   │
  │                  │ SemaphoreSlim (async-aware)    │ Semaphore                │
  │                  │                               │                          │
  │ Signal/Event     │ ManualResetEventSlim           │ CountDownLatch           │
  │                  │ CountdownEvent                 │ CyclicBarrier            │
  │                  │ Barrier                        │ Phaser                   │
  │                  │                               │                          │
  │ Thread Pool      │ ThreadPool (global, CLR)       │ ForkJoinPool (work-steal)│
  │                  │   Hill-Climbing algorithm      │ ExecutorService          │
  │                  │   Task.Run → ThreadPool        │ Executors.newFixedPool   │
  │                  │                               │                          │
  │ Async Model      │ async/await + Task<T>          │ CompletableFuture<T>     │
  │                  │   State machine generated      │   Callback chaining      │
  │                  │   SynchronizationContext       │   No context capture     │
  │                  │                               │ Virtual Threads (Java 21) │
  │                  │                               │                          │
  │ Pipeline         │ System.Threading.Channels      │ java.util.concurrent.Flow│
  │                  │   Bounded/Unbounded            │ (Reactive Streams)       │
  │                  │   Single/Multi reader/writer   │                          │
  └──────────────────┴───────────────────────────────┴──────────────────────────┘
");

        Console.WriteLine("  [Key Design Differences]");
        Console.WriteLine();
        Console.WriteLine("  1. ATOMICS: .NET uses static Interlocked class on ref fields (value types stay inline).");
        Console.WriteLine("     Java wraps in AtomicInteger/AtomicLong objects (heap allocation).");
        Console.WriteLine("     Java 9+ VarHandle provides field-level atomic ops similar to Interlocked.");
        Console.WriteLine();
        Console.WriteLine("  2. ASYNC: .NET async/await compiles to a state machine — truly non-blocking,");
        Console.WriteLine("     no thread occupied during await. Java CompletableFuture chains callbacks");
        Console.WriteLine("     (more verbose). Java 21 Virtual Threads make blocking code non-blocking");
        Console.WriteLine("     at the JVM level — different philosophy (block freely vs. never block).");
        Console.WriteLine();
        Console.WriteLine("  3. CONTEXT: .NET has SynchronizationContext (UI thread marshaling, ASP.NET).");
        Console.WriteLine("     Java has no equivalent — all continuations run on ForkJoinPool threads.");
        Console.WriteLine("     This makes .NET more prone to sync-over-async deadlocks in UI/ASP.NET.");
        Console.WriteLine();
        Console.WriteLine("  4. COLLECTIONS: .NET ConcurrentDictionary.GetOrAdd provides atomic lazy init.");
        Console.WriteLine("     Java ConcurrentHashMap.computeIfAbsent locks the bucket during computation");
        Console.WriteLine("     (safe but slower). .NET's factory may be called multiple times (faster but");
        Console.WriteLine("     requires idempotent factories).");
        Console.WriteLine();
        Console.WriteLine("  5. POOL: .NET has ONE global ThreadPool with Hill-Climbing (auto-adjusts).");
        Console.WriteLine("     Java creates custom ExecutorService/ForkJoinPool per use case.");
        Console.WriteLine("     NBAaS relies on .NET's single pool — Parallel.ForEach, Task.Run, async/await");
        Console.WriteLine("     all share the same ThreadPool, which is why starvation matters.");

        // Demonstrate: ConcurrentDictionary.GetOrAdd may call factory multiple times
        Console.WriteLine("\n  --- Demo: ConcurrentDictionary.GetOrAdd factory call count ---");
        var dict = new ConcurrentDictionary<string, string>();
        int factoryCalls = 0;

        Parallel.For(0, 20, _ =>
        {
            dict.GetOrAdd("key", k =>
            {
                Interlocked.Increment(ref factoryCalls);
                Thread.Sleep(10); // slow factory
                return $"value-from-thread-{Thread.CurrentThread.ManagedThreadId}";
            });
        });

        Console.WriteLine($"  GetOrAdd called with 20 threads: factory invoked {factoryCalls} time(s)");
        Console.WriteLine($"  Final value: {dict["key"]}");
        Console.WriteLine("  (Factory may run >1 time, but only one result is stored — lossy but lock-free)");

        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static async Task SimulateLoader(string name, int delayMs)
    {
        Console.WriteLine($"  [{name}] Starting on thread {Thread.CurrentThread.ManagedThreadId}");
        await Task.Delay(delayMs);
        Console.WriteLine($"  [{name}] Completed ({delayMs}ms)");
    }

    // NBAaS ForEachAsync implementation
    static async Task ForEachAsync<T>(IEnumerable<T> items, int maxDegreeOfParallelism, Func<T, Task> body)
    {
        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync();
            try { await body(item); }
            finally { semaphore.Release(); }
        });
        await Task.WhenAll(tasks);
    }
}

// ── Supporting Types ──────────────────────────────────────────────────────────

record DeviceInfo
{
    public string Name { get; init; } = "";
    public string Datacenter { get; init; } = "";
    public string DeviceType { get; init; } = "";
}

// Simulates NBAaS RequestMetricsContext (lock-free counters)
class RequestMetrics
{
    private int _totalRequests;
    private long _totalDurationMs;

    public int TotalRequests => _totalRequests;

    public void RecordRequest(string method, string path, int statusCode, long durationMs)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Add(ref _totalDurationMs, durationMs);
    }

    public (int Requests, long DurationMs) GetAndReset()
    {
        var reqs = Interlocked.Exchange(ref _totalRequests, 0);
        var dur = Interlocked.Exchange(ref _totalDurationMs, 0);
        return (reqs, dur);
    }
}

// Simulates NBAaS double-checked locking for table metadata
class LazyTableRegistry
{
    private List<string>? _tables;
    private readonly object _lock = new();
    private int _initCount;

    public bool IsInitialized => _tables != null;
    public int InitializeCount => _initCount;

    public List<string> GetTables()
    {
        if (_tables == null)
        {
            lock (_lock)
            {
                if (_tables == null)
                {
                    Interlocked.Increment(ref _initCount);
                    // Simulate expensive reflection scan
                    Thread.Sleep(50);
                    _tables = new List<string>
                    {
                        "Device", "Link", "Server", "DeviceMetadata",
                        "BgpPeer", "BgpGroup", "AclInterface", "Slice"
                    };
                }
            }
        }
        return _tables;
    }
}
