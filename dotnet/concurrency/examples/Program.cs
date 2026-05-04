// =============================================================================
// .NET Concurrency Examples — Runnable Code
// Run: dotnet run
// Each example demonstrates a different concurrency mechanism.
// =============================================================================

using System.Collections.Concurrent;
using System.Threading.Channels;

class Program
{
    static async Task Main(string[] args)
    {
        var examples = new (string Name, Func<Task> Action)[]
        {
            ("01. Thread Basics", Example01_ThreadBasics),
            ("02. ThreadPool & Hill-Climbing", Example02_ThreadPool),
            ("03. Lock & Monitor", Example03_LockAndMonitor),
            ("04. Interlocked", Example04_Interlocked),
            ("05. Volatile vs Interlocked", Example05_VolatileVsInterlocked),
            ("06. ReaderWriterLock", Example06_ReaderWriterLock),
            ("07. SemaphoreSlim", Example07_Semaphore),
            ("08. Reset Events", Example08_ResetEvents),
            ("09. SpinLock", Example09_SpinLock),
            ("10. Concurrent Collections", Example10_ConcurrentCollections),
            ("11. Task Basics", Example11_TaskBasics),
            ("12. Parallel Loops", Example12_ParallelLoops),
            ("13. PLINQ", Example13_PLINQ),
            ("14. Cancellation", Example14_Cancellation),
            ("15. Channels", Example15_Channels),
            ("16. async/await State Machine", Example16_AsyncAwait),
            ("17. Parallel.Invoke vs Task.WhenAll", Example17_ParallelInvokeVsTaskWhenAll),
            ("18. Deadlock Demo", Example18_DeadlockDemo),
        };

        if (args.Length > 0 && int.TryParse(args[0], out int choice) && choice >= 1 && choice <= examples.Length)
        {
            await RunExample(examples[choice - 1]);
        }
        else
        {
            Console.WriteLine("=== .NET Concurrency Examples ===\n");
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
        Console.WriteLine($"\n{"",3}╔══════════════════════════════════════════════╗");
        Console.WriteLine($"{"",3}║  {example.Name,-43}║");
        Console.WriteLine($"{"",3}╚══════════════════════════════════════════════╝\n");
        try { await example.Action(); }
        catch (Exception ex) { Console.WriteLine($"  [Exception] {ex.GetType().Name}: {ex.Message}"); }
        Console.WriteLine();
    }

    // =========================================================================
    // 01. Thread Basics
    // =========================================================================
    static Task Example01_ThreadBasics()
    {
        Console.WriteLine($"  Main thread: {Thread.CurrentThread.ManagedThreadId}");

        // Foreground thread — keeps process alive
        var t1 = new Thread(() =>
        {
            Console.WriteLine($"  Foreground thread: {Thread.CurrentThread.ManagedThreadId}");
            Thread.Sleep(100);
        });
        t1.Name = "Worker-1";
        t1.Start();

        // Background thread — won't prevent process exit
        var t2 = new Thread(() =>
        {
            Console.WriteLine($"  Background thread: {Thread.CurrentThread.ManagedThreadId}");
            Thread.Sleep(100);
        });
        t2.IsBackground = true;
        t2.Name = "Worker-2";
        t2.Start();

        // Thread-local storage
        var tls = new ThreadLocal<int>(() => Thread.CurrentThread.ManagedThreadId * 10);
        var t3 = new Thread(() => Console.WriteLine($"  ThreadLocal value: {tls.Value}"));
        t3.Start();

        t1.Join();
        t2.Join();
        t3.Join();
        tls.Dispose();

        Console.WriteLine("  All threads completed.");
        return Task.CompletedTask;
    }

    // =========================================================================
    // 02. ThreadPool & Hill-Climbing Thread Injection
    // =========================================================================
    static Task Example02_ThreadPool()
    {
        // --- Part A: Inspect pool configuration ---
        ThreadPool.GetMinThreads(out int workerMin, out int ioMin);
        ThreadPool.GetMaxThreads(out int workerMax, out int ioMax);
        Console.WriteLine($"  Worker threads: min={workerMin}, max={workerMax}");
        Console.WriteLine($"  IOCP threads:   min={ioMin}, max={ioMax}");
        Console.WriteLine($"  ProcessorCount: {Environment.ProcessorCount}");
        Console.WriteLine();

        // --- Part B: Basic QueueUserWorkItem ---
        var countdown = new CountdownEvent(5);
        for (int i = 0; i < 5; i++)
        {
            int id = i;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Console.WriteLine($"  Work item {id} on thread {Thread.CurrentThread.ManagedThreadId} (IsThreadPoolThread={Thread.CurrentThread.IsThreadPoolThread})");
                countdown.Signal();
            });
        }
        countdown.Wait();
        countdown.Dispose();
        Console.WriteLine("  All work items completed.\n");

        // --- Part C: Hill-Climbing starvation demo ---
        // Queue many BLOCKING work items to saturate the pool.
        // Watch how the runtime injects ~1 new thread per 500ms.
        //
        // The Hill-Climbing algorithm (PortableThreadPool.HillClimbing.cs in CoreCLR)
        // measures throughput and cautiously adds threads. This is why blocking
        // ThreadPool threads causes starvation — new threads arrive too slowly.

        Console.WriteLine("  --- Hill-Climbing Thread Injection Demo ---");
        Console.WriteLine($"  Pool starts with {workerMin} min worker threads (= ProcessorCount).");
        Console.WriteLine($"  Queuing {workerMin + 8} blocking work items to saturate the pool...\n");

        int totalItems = workerMin + 8; // enough to exceed min threads
        var tracker = new ConcurrentDictionary<int, bool>();
        var allDone = new CountdownEvent(totalItems);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < totalItems; i++)
        {
            int id = i;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int tid = Thread.CurrentThread.ManagedThreadId;
                bool isNew = tracker.TryAdd(tid, true);
                ThreadPool.GetAvailableThreads(out int avail, out int _ioAvail);

                Console.WriteLine(
                    $"  [{sw.ElapsedMilliseconds,5}ms] Item {id,2} started on thread {tid,3}" +
                    $" | unique threads so far: {tracker.Count}" +
                    $" | available: {avail}" +
                    $"{(isNew && tracker.Count > workerMin ? " ← NEW (Hill-Climbing injected)" : "")}");

                // Block the thread for 3s — simulates sync-over-async or Thread.Sleep
                Thread.Sleep(3000);
                allDone.Signal();
            });
        }

        allDone.Wait();
        allDone.Dispose();
        sw.Stop();

        Console.WriteLine($"\n  Total time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Unique threads used: {tracker.Count} (started with min={workerMin})");
        Console.WriteLine($"  Extra threads injected: {Math.Max(0, tracker.Count - workerMin)} (at ~1 per 500ms)");
        Console.WriteLine();
        Console.WriteLine("  TAKEAWAY: If this were async (Task.Delay instead of Thread.Sleep),");
        Console.WriteLine("  all items would complete in ~3s using only a few threads.");
        Console.WriteLine("  Blocking forced the pool to slowly grow, delaying later items.\n");

        // --- Part D: Global Queue (FIFO) vs Local Queue (LIFO) ---
        // When a running task spawns child tasks via Task.Run(), children go to the
        // current thread's LOCAL queue (LIFO). Items queued from non-pool threads
        // or via QueueUserWorkItem go to the GLOBAL queue (FIFO).
        Console.WriteLine("  --- Part D: Global Queue (FIFO) vs Local Queue (LIFO) ---\n");

        // D1: QueueUserWorkItem → Global queue → FIFO order
        Console.WriteLine("  [Global Queue - FIFO] QueueUserWorkItem from main thread:");
        var globalOrder = new ConcurrentQueue<int>();
        var globalDone = new CountdownEvent(10);
        for (int i = 0; i < 10; i++)
        {
            int id = i;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                globalOrder.Enqueue(id);
                globalDone.Signal();
            });
        }
        globalDone.Wait();
        globalDone.Dispose();
        Console.WriteLine($"  Execution order: [{string.Join(", ", globalOrder)}]");
        Console.WriteLine("  (Tends toward 0,1,2...9 — FIFO from global queue)\n");

        // D2: Task.Run() inside a running task → Local queue → LIFO order
        Console.WriteLine("  [Local Queue - LIFO] Task.Run() spawned from inside a task:");
        var localOrder = new ConcurrentQueue<(int id, int tid)>();
        var localDone = new CountdownEvent(10);

        // Force everything onto one thread by using a semaphore to serialize
        var gate = new ManualResetEventSlim(false);
        Task.Run(() =>
        {
            int parentTid = Thread.CurrentThread.ManagedThreadId;
            Console.WriteLine($"  Parent task on thread {parentTid}");

            // Spawn 10 child tasks — they go to THIS thread's local queue
            for (int i = 0; i < 10; i++)
            {
                int id = i;
                Task.Run(() =>
                {
                    localOrder.Enqueue((id, Thread.CurrentThread.ManagedThreadId));
                    localDone.Signal();
                });
            }
            gate.Set();
        }).Wait();

        gate.Wait();
        localDone.Wait();
        localDone.Dispose();
        gate.Dispose();

        Console.WriteLine($"  Execution order: [{string.Join(", ", localOrder.Select(x => x.id))}]");
        Console.WriteLine($"  Thread IDs:      [{string.Join(", ", localOrder.Select(x => x.tid))}]");
        Console.WriteLine("  (Tends toward 9,8,7...0 — LIFO from local queue)");
        Console.WriteLine("  (Thread IDs often repeat — same thread pops its own local queue)\n");

        // D3: PreferFairness forces global queue even from inside a task
        Console.WriteLine("  [PreferFairness] Forces global queue (FIFO) even from inside a task:");
        var fairOrder = new ConcurrentQueue<int>();
        var fairDone = new CountdownEvent(10);
        Task.Run(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                int id = i;
                Task.Factory.StartNew(() =>
                {
                    fairOrder.Enqueue(id);
                    fairDone.Signal();
                }, CancellationToken.None, TaskCreationOptions.PreferFairness, TaskScheduler.Default);
            }
        }).Wait();
        fairDone.Wait();
        fairDone.Dispose();
        Console.WriteLine($"  Execution order: [{string.Join(", ", fairOrder)}]");
        Console.WriteLine("  (Tends toward 0,1,2...9 — FIFO because PreferFairness → global queue)\n");

        Console.WriteLine("  SUMMARY:");
        Console.WriteLine("  ┌────────────────────────────────────┬──────────┬───────────────────────┐");
        Console.WriteLine("  │ API                                │ Queue    │ Order                 │");
        Console.WriteLine("  ├────────────────────────────────────┼──────────┼───────────────────────┤");
        Console.WriteLine("  │ QueueUserWorkItem()                │ Global   │ FIFO (fair)           │");
        Console.WriteLine("  │ Task.Run() from non-pool thread    │ Global   │ FIFO (fair)           │");
        Console.WriteLine("  │ Task.Run() inside a running task   │ Local    │ LIFO (cache-friendly) │");
        Console.WriteLine("  │ StartNew(PreferFairness)           │ Global   │ FIFO (forced fair)    │");
        Console.WriteLine("  │ UnsafeQueueUserWorkItem(prefLocal) │ Local    │ LIFO (.NET 5+)        │");
        Console.WriteLine("  └────────────────────────────────────┴──────────┴───────────────────────┘");

        return Task.CompletedTask;
    }

    // =========================================================================
    // 03. Lock & Monitor (producer-consumer)
    // =========================================================================
    static Task Example03_LockAndMonitor()
    {
        var lockObj = new object();
        var queue = new Queue<int>();
        bool done = false;

        // Producer
        var producer = Task.Run(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                lock (lockObj)
                {
                    queue.Enqueue(i);
                    Console.WriteLine($"  Produced: {i}");
                    Monitor.Pulse(lockObj); // wake consumer
                }
                Thread.Sleep(50);
            }
            lock (lockObj) { done = true; Monitor.Pulse(lockObj); }
        });

        // Consumer
        var consumer = Task.Run(() =>
        {
            while (true)
            {
                int item;
                lock (lockObj)
                {
                    while (queue.Count == 0 && !done)
                        Monitor.Wait(lockObj); // release lock + wait
                    if (queue.Count == 0 && done) break;
                    item = queue.Dequeue();
                }
                Console.WriteLine($"  Consumed: {item}");
            }
        });

        Task.WaitAll(producer, consumer);
        Console.WriteLine("  Producer-Consumer completed.");
        return Task.CompletedTask;
    }

    // =========================================================================
    // 04. Interlocked
    // =========================================================================
    static Task Example04_Interlocked()
    {
        int counter = 0;
        long total = 0;

        // Race condition WITHOUT Interlocked
        int unsafeCounter = 0;
        var tasks1 = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++)
                unsafeCounter++; // NOT atomic — race condition
        })).ToArray();
        Task.WaitAll(tasks1);
        Console.WriteLine($"  Unsafe counter (expect 100000): {unsafeCounter} {(unsafeCounter == 100000 ? "✓" : "✗ RACE CONDITION")}");

        // Correct WITH Interlocked
        var tasks2 = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++)
                Interlocked.Increment(ref counter);
        })).ToArray();
        Task.WaitAll(tasks2);
        Console.WriteLine($"  Safe counter   (expect 100000): {counter} ✓");

        // Compare-and-swap (CAS)
        int cas = 42;
        int old = Interlocked.CompareExchange(ref cas, 100, 42); // if cas==42, set to 100
        Console.WriteLine($"  CAS: old={old}, new={cas}");

        return Task.CompletedTask;
    }

    // =========================================================================
    // 05. Volatile vs Interlocked (Atomic) — .NET vs Java
    // =========================================================================
    static Task Example05_VolatileVsInterlocked()
    {
        // ┌─────────────────────────────────────────────────────────────────────┐
        // │  volatile  = VISIBILITY guarantee only (no atomicity for RMW ops)  │
        // │  Interlocked = VISIBILITY + ATOMICITY (atomic read-modify-write)   │
        // │                                                                   │
        // │  .NET volatile keyword      ≈  Java volatile keyword              │
        // │  .NET Volatile.Read/Write   ≈  Java VarHandle.getAcquire/setRelease│
        // │  .NET Interlocked.*         ≈  Java AtomicInteger/AtomicLong/etc.  │
        // └─────────────────────────────────────────────────────────────────────┘

        // --- Part A: volatile ensures VISIBILITY across threads ---
        // Without volatile, the compiler/CPU may cache `_stop` in a register
        // and the worker thread may never see the update.
        Console.WriteLine("  --- Part A: volatile for visibility (stop flag) ---");
        var stop = false; // non-volatile — intentional for demo
        var volatileStop = 0; // we'll use Volatile.Write to update this

        var worker = Task.Run(() =>
        {
            int spins = 0;
            // Volatile.Read ensures we see the latest value from main memory
            while (Volatile.Read(ref volatileStop) == 0)
            {
                spins++;
                if (spins % 1_000_000 == 0) { } // busy spin
            }
            Console.WriteLine($"  Worker saw stop after {spins:N0} spins");
        });

        Thread.Sleep(50); // let worker spin
        Volatile.Write(ref volatileStop, 1); // publish the flag
        worker.Wait();
        Console.WriteLine("  ✓ Volatile.Write made the flag visible across threads\n");

        // --- Part B: volatile does NOT make increment atomic ---
        Console.WriteLine("  --- Part B: volatile does NOT make ++ atomic ---");
        int volatileCounter = 0; // even with volatile, ++ is read-modify-write (3 steps)

        var tasks1 = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                // This is NOT atomic even with Volatile.Read/Write!
                // Steps: 1) read → 2) add 1 → 3) write back
                // Another thread can read between step 1 and 3
                var current = Volatile.Read(ref volatileCounter);
                Volatile.Write(ref volatileCounter, current + 1);
            }
        })).ToArray();
        Task.WaitAll(tasks1);
        Console.WriteLine($"  Volatile ++ (expect 500000): {volatileCounter:N0} ← LOST UPDATES (race condition)");

        // --- Part C: Interlocked makes increment ATOMIC ---
        int atomicCounter = 0;
        var tasks2 = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 10_000; i++)
                Interlocked.Increment(ref atomicCounter); // single hardware instruction (lock xadd)
        })).ToArray();
        Task.WaitAll(tasks2);
        Console.WriteLine($"  Interlocked ++ (expect 500000): {atomicCounter:N0} ✓ always correct\n");

        // --- Part D: Interlocked CAS loop — custom atomic operation ---
        Console.WriteLine("  --- Part D: CAS loop (lock-free max) ---");
        int sharedMax = 0;
        var tasks3 = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            int proposedMax = i;
            // CAS loop: retry until our update succeeds
            // .NET:  Interlocked.CompareExchange(ref location, newVal, expectedOld)
            // Java:  AtomicInteger.compareAndSet(expectedOld, newVal)  — note param order differs!
            int oldVal;
            do
            {
                oldVal = Volatile.Read(ref sharedMax);
                if (proposedMax <= oldVal) break; // someone already set higher
            } while (Interlocked.CompareExchange(ref sharedMax, proposedMax, oldVal) != oldVal);
        })).ToArray();
        Task.WaitAll(tasks3);
        Console.WriteLine($"  Lock-free max (expect 99): {sharedMax}\n");

        // --- Summary table ---
        Console.WriteLine("  ┌──────────────────────────┬──────────────────────────────┬──────────────────────────────┐");
        Console.WriteLine("  │ Concept                  │ .NET                         │ Java                         │");
        Console.WriteLine("  ├──────────────────────────┼──────────────────────────────┼──────────────────────────────┤");
        Console.WriteLine("  │ Visibility only           │ volatile keyword             │ volatile keyword             │");
        Console.WriteLine("  │ Explicit fence            │ Volatile.Read/Write          │ VarHandle get/setAcquire     │");
        Console.WriteLine("  │ Atomic increment          │ Interlocked.Increment        │ AtomicInteger.incrementAndGet│");
        Console.WriteLine("  │ Atomic CAS                │ Interlocked.CompareExchange  │ AtomicInteger.compareAndSet  │");
        Console.WriteLine("  │ Atomic read+write (64bit) │ Interlocked.Read (for long)  │ AtomicLong.get()             │");
        Console.WriteLine("  │ Memory ordering           │ volatile = acquire/release   │ volatile = happens-before    │");
        Console.WriteLine("  │ Full fence                │ Interlocked.MemoryBarrier()  │ VarHandle.fullFence()        │");
        Console.WriteLine("  └──────────────────────────┴──────────────────────────────┴──────────────────────────────┘");
        Console.WriteLine();
        Console.WriteLine("  KEY INSIGHT:");
        Console.WriteLine("  • volatile = \"other threads can SEE my write\" (visibility)");
        Console.WriteLine("  • Interlocked = \"other threads can SEE my write AND no update is lost\" (visibility + atomicity)");
        Console.WriteLine("  • In Java: volatile is the keyword, AtomicXxx classes provide atomicity");
        Console.WriteLine("  • In .NET: volatile is the keyword, Interlocked static methods provide atomicity");
        Console.WriteLine("  • volatile is ENOUGH for: boolean flags, one-writer scenarios, reference publishing");
        Console.WriteLine("  • Interlocked is NEEDED for: counters, accumulators, CAS loops, any read-modify-write");

        return Task.CompletedTask;
    }

    // =========================================================================
    // 06. ReaderWriterLockSlim
    // =========================================================================
    static Task Example06_ReaderWriterLock()
    {
        var rwLock = new ReaderWriterLockSlim();
        var cache = new Dictionary<string, int>();
        int readsCompleted = 0, writesCompleted = 0;

        // Multiple concurrent readers
        var readers = Enumerable.Range(0, 5).Select(i => Task.Run(() =>
        {
            for (int j = 0; j < 20; j++)
            {
                rwLock.EnterReadLock();
                try { _ = cache.Count; Interlocked.Increment(ref readsCompleted); }
                finally { rwLock.ExitReadLock(); }
            }
        })).ToArray();

        // Single writer
        var writer = Task.Run(() =>
        {
            for (int j = 0; j < 10; j++)
            {
                rwLock.EnterWriteLock();
                try { cache[$"key{j}"] = j; Interlocked.Increment(ref writesCompleted); }
                finally { rwLock.ExitWriteLock(); }
                Thread.Sleep(10);
            }
        });

        Task.WaitAll(readers.Append(writer).ToArray());
        rwLock.Dispose();
        Console.WriteLine($"  Reads: {readsCompleted}, Writes: {writesCompleted}, Cache size: {cache.Count}");
        return Task.CompletedTask;
    }

    // =========================================================================
    // 07. SemaphoreSlim (rate limiting)
    // =========================================================================
    static async Task Example07_Semaphore()
    {
        var semaphore = new SemaphoreSlim(3); // max 3 concurrent
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            await semaphore.WaitAsync();
            try
            {
                Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] Task {i} ENTERED (concurrent: {3 - semaphore.CurrentCount})");
                await Task.Delay(200);
            }
            finally
            {
                Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] Task {i} EXITED");
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        semaphore.Dispose();
        Console.WriteLine("  All tasks completed with max 3 concurrent.");
    }

    // =========================================================================
    // 08. ManualResetEventSlim / AutoResetEvent
    // =========================================================================
    static Task Example08_ResetEvents()
    {
        // ManualResetEventSlim — gate pattern (all waiters proceed)
        var gate = new ManualResetEventSlim(false);

        var waiters = Enumerable.Range(0, 3).Select(i => Task.Run(() =>
        {
            Console.WriteLine($"  Waiter {i}: waiting for gate...");
            gate.Wait();
            Console.WriteLine($"  Waiter {i}: gate opened! Proceeding.");
        })).ToArray();

        Thread.Sleep(200);
        Console.WriteLine("  Opening gate (all waiters proceed)...");
        gate.Set(); // all 3 waiters proceed

        Task.WaitAll(waiters);
        gate.Dispose();

        // AutoResetEvent — turnstile pattern (one waiter per Set)
        var turnstile = new AutoResetEvent(false);
        var waiters2 = Enumerable.Range(0, 3).Select(i => Task.Run(() =>
        {
            Console.WriteLine($"  Turnstile waiter {i}: waiting...");
            turnstile.WaitOne();
            Console.WriteLine($"  Turnstile waiter {i}: through!");
        })).ToArray();

        for (int i = 0; i < 3; i++)
        {
            Thread.Sleep(100);
            Console.WriteLine("  [Set] releasing one waiter...");
            turnstile.Set(); // releases exactly ONE waiter
        }

        Task.WaitAll(waiters2);
        turnstile.Dispose();
        return Task.CompletedTask;
    }

    // =========================================================================
    // 09. SpinLock & SpinWait
    // =========================================================================
    static Task Example09_SpinLock()
    {
        var spinLock = new SpinLock(enableThreadOwnerTracking: false);
        long counter = 0;

        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 100_000; i++)
            {
                bool lockTaken = false;
                try
                {
                    spinLock.Enter(ref lockTaken);
                    counter++; // very short critical section
                }
                finally
                {
                    if (lockTaken) spinLock.Exit();
                }
            }
        })).ToArray();

        Task.WaitAll(tasks);
        Console.WriteLine($"  SpinLock counter (expect 400000): {counter}");

        // SpinWait demo
        bool flag = false;
        var setter = Task.Run(() => { Thread.Sleep(50); Volatile.Write(ref flag, true); });
        var spinner = new SpinWait();
        while (!Volatile.Read(ref flag))
            spinner.SpinOnce();
        Console.WriteLine($"  SpinWait completed after {spinner.Count} spins (NextSpinWillYield={spinner.NextSpinWillYield})");
        setter.Wait();

        return Task.CompletedTask;
    }

    // =========================================================================
    // 10. Concurrent Collections
    // =========================================================================
    static Task Example10_ConcurrentCollections()
    {
        // ConcurrentDictionary
        var dict = new ConcurrentDictionary<string, int>();
        Parallel.For(0, 100, i =>
        {
            dict.AddOrUpdate($"key{i % 10}", 1, (k, old) => old + 1);
        });
        Console.WriteLine($"  ConcurrentDictionary: {dict.Count} keys, key0 count={dict["key0"]}");

        // ConcurrentQueue (lock-free)
        var queue = new ConcurrentQueue<int>();
        Parallel.For(0, 100, i => queue.Enqueue(i));
        Console.WriteLine($"  ConcurrentQueue: {queue.Count} items");

        // ConcurrentBag (thread-local optimized)
        var bag = new ConcurrentBag<int>();
        Parallel.For(0, 100, i => bag.Add(i));
        Console.WriteLine($"  ConcurrentBag: {bag.Count} items");

        // BlockingCollection (bounded producer-consumer)
        var bc = new BlockingCollection<int>(boundedCapacity: 10);
        var producer = Task.Run(() =>
        {
            for (int i = 0; i < 20; i++) bc.Add(i);
            bc.CompleteAdding();
        });
        int consumed = 0;
        var consumer = Task.Run(() =>
        {
            foreach (var item in bc.GetConsumingEnumerable())
                Interlocked.Increment(ref consumed);
        });
        Task.WaitAll(producer, consumer);
        bc.Dispose();
        Console.WriteLine($"  BlockingCollection: consumed {consumed} items");

        return Task.CompletedTask;
    }

    // =========================================================================
    // 11. Task Basics
    // =========================================================================
    static async Task Example11_TaskBasics()
    {
        // Task.Run — offload to ThreadPool
        var result = await Task.Run(() => { Thread.Sleep(100); return 42; });
        Console.WriteLine($"  Task.Run result: {result}");

        // Continuations
        var chain = Task.Run(() => 10)
            .ContinueWith(t => t.Result * 2)
            .ContinueWith(t => $"Result: {t.Result}");
        Console.WriteLine($"  Continuation chain: {await chain}");

        // WhenAll — wait for all
        var tasks = Enumerable.Range(1, 5).Select(i =>
            Task.Run(() => { Thread.Sleep(i * 50); return i * 10; })).ToArray();
        var results = await Task.WhenAll(tasks);
        Console.WriteLine($"  WhenAll results: [{string.Join(", ", results)}]");

        // WhenAny — first to complete
        var fast = Task.Delay(50).ContinueWith(_ => "fast");
        var slow = Task.Delay(200).ContinueWith(_ => "slow");
        var winner = await await Task.WhenAny(fast, slow);
        Console.WriteLine($"  WhenAny winner: {winner}");
    }

    // =========================================================================
    // 12. Parallel Loops
    // =========================================================================
    static Task Example12_ParallelLoops()
    {
        // Parallel.For with thread-local accumulator (reduction pattern)
        long totalSum = 0;
        Parallel.For(0L, 1_000_000L,
            () => 0L,                                           // localInit
            (i, state, localSum) => localSum + i,               // body
            localSum => Interlocked.Add(ref totalSum, localSum) // localFinally
        );
        long expected = 999999L * 1000000L / 2;
        Console.WriteLine($"  Parallel.For sum: {totalSum} (expected: {expected}) {(totalSum == expected ? "✓" : "✗")}");

        // Parallel.ForEach with MaxDegreeOfParallelism
        var items = Enumerable.Range(0, 20).ToList();
        var processedBy = new ConcurrentDictionary<int, int>();
        Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = 3 }, item =>
        {
            processedBy[item] = Thread.CurrentThread.ManagedThreadId;
        });
        var uniqueThreads = processedBy.Values.Distinct().Count();
        Console.WriteLine($"  Parallel.ForEach: {items.Count} items processed by {uniqueThreads} threads (max 3)");

        return Task.CompletedTask;
    }

    // =========================================================================
    // 13. PLINQ
    // =========================================================================
    static Task Example13_PLINQ()
    {
        // Find primes in parallel
        var primes = Enumerable.Range(2, 100_000)
            .AsParallel()
            .WithDegreeOfParallelism(4)
            .Where(IsPrime)
            .ToList();
        Console.WriteLine($"  PLINQ: Found {primes.Count} primes in [2, 100001]");

        // Ordered PLINQ
        var orderedSquares = Enumerable.Range(1, 20)
            .AsParallel()
            .AsOrdered()  // preserve input order
            .Select(x => x * x)
            .Take(10)
            .ToList();
        Console.WriteLine($"  Ordered PLINQ (first 10 squares): [{string.Join(", ", orderedSquares)}]");

        return Task.CompletedTask;

        static bool IsPrime(int n)
        {
            if (n < 2) return false;
            for (int i = 2; i * i <= n; i++)
                if (n % i == 0) return false;
            return true;
        }
    }

    // =========================================================================
    // 14. Cancellation
    // =========================================================================
    static async Task Example14_Cancellation()
    {
        // Timeout cancellation
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        int iterations = 0;
        try
        {
            while (true)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(50, cts.Token);
                iterations++;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"  Cancelled after {iterations} iterations (~300ms timeout)");
        }

        // Linked cancellation tokens
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cts1.Token, cts2.Token);

        var task = Task.Run(async () =>
        {
            await Task.Delay(5000, linked.Token);
        }, linked.Token);

        cts2.Cancel(); // cancelling either parent cancels the linked token
        try { await task; }
        catch (OperationCanceledException)
        {
            Console.WriteLine("  Linked token: cancelled via cts2");
        }

        cts1.Dispose();
        cts2.Dispose();
        linked.Dispose();
    }

    // =========================================================================
    // 15. Channels (async producer-consumer)
    // =========================================================================
    static async Task Example15_Channels()
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(5)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        // Producer — will be slowed by backpressure when channel is full
        var producer = Task.Run(async () =>
        {
            for (int i = 0; i < 20; i++)
            {
                await channel.Writer.WriteAsync(i);
                Console.WriteLine($"  Produced: {i}");
            }
            channel.Writer.Complete();
        });

        // Consumer — processes slower than producer
        var consumer = Task.Run(async () =>
        {
            int count = 0;
            await foreach (var item in channel.Reader.ReadAllAsync())
            {
                await Task.Delay(50); // simulate work
                count++;
            }
            Console.WriteLine($"  Consumer finished: {count} items processed");
        });

        await Task.WhenAll(producer, consumer);
    }

    // =========================================================================
    // 16. async/await internals
    // =========================================================================
    static async Task Example16_AsyncAwait()
    {
        Console.WriteLine($"  Before await: Thread {Thread.CurrentThread.ManagedThreadId}");

        // Each await is a potential suspension point
        await Task.Delay(50);
        Console.WriteLine($"  After await 1: Thread {Thread.CurrentThread.ManagedThreadId} (may be different!)");

        await Task.Delay(50);
        Console.WriteLine($"  After await 2: Thread {Thread.CurrentThread.ManagedThreadId}");

        // ValueTask — no allocation when completing synchronously
        var cached = await GetCachedValueAsync(42);
        Console.WriteLine($"  ValueTask (cached): {cached}");

        var computed = await GetCachedValueAsync(999);
        Console.WriteLine($"  ValueTask (computed): {computed}");

        // ConfigureAwait(false) — don't capture SynchronizationContext
        await Task.Delay(50).ConfigureAwait(false);
        Console.WriteLine($"  After ConfigureAwait(false): Thread {Thread.CurrentThread.ManagedThreadId}");
    }

    private static readonly Dictionary<int, string> _cache = new() { [42] = "cached-42" };
    static ValueTask<string> GetCachedValueAsync(int key)
    {
        if (_cache.TryGetValue(key, out var val))
            return new ValueTask<string>(val); // synchronous — no Task allocation

        return new ValueTask<string>(ComputeAsync(key));

        static async Task<string> ComputeAsync(int k)
        {
            await Task.Delay(10);
            return $"computed-{k}";
        }
    }

    // =========================================================================
    // 17. Parallel.Invoke vs Task.WhenAll (EF Core migration lesson)
    // =========================================================================
    static async Task Example17_ParallelInvokeVsTaskWhenAll()
    {
        // Simulate async loaders (like EF Core database operations)
        async Task LoaderAsync(string name, int delayMs)
        {
            Console.WriteLine($"  [{name}] started on thread {Thread.CurrentThread.ManagedThreadId}");
            await Task.Delay(delayMs);
            Console.WriteLine($"  [{name}] completed on thread {Thread.CurrentThread.ManagedThreadId}");
        }

        // ❌ BAD: Parallel.Invoke + .Wait() — blocks N threads simultaneously
        Console.WriteLine("  --- Parallel.Invoke + .Wait() (blocks threads) ---");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Parallel.Invoke(
            () => LoaderAsync("PNG", 100).Wait(),   // blocks thread
            () => LoaderAsync("DPG", 100).Wait(),   // blocks thread
            () => LoaderAsync("Server", 100).Wait()  // blocks thread
        );
        Console.WriteLine($"  Elapsed: {sw.ElapsedMilliseconds}ms (3 threads were blocked)\n");

        // ✅ GOOD: Task.WhenAll — cooperative async
        Console.WriteLine("  --- Task.WhenAll (cooperative) ---");
        sw.Restart();
        await Task.WhenAll(
            LoaderAsync("PNG", 100),
            LoaderAsync("DPG", 100),
            LoaderAsync("Server", 100)
        );
        Console.WriteLine($"  Elapsed: {sw.ElapsedMilliseconds}ms (0 threads blocked during await)\n");

        // ✅ GOOD: Task.WhenAll with dependency chains
        Console.WriteLine("  --- Task.WhenAll with dependency chain ---");
        sw.Restart();
        await Task.WhenAll(
            // Chain: PNG → CPG (CPG depends on PNG)
            Task.Run(async () =>
            {
                await LoaderAsync("PNG", 100);
                await LoaderAsync("CPG", 50);  // runs after PNG
            }),
            // Independent
            LoaderAsync("DPG", 100),
            LoaderAsync("Server", 80)
        );
        Console.WriteLine($"  Elapsed: {sw.ElapsedMilliseconds}ms (PNG→CPG=150ms, others=100ms, total≈150ms)");
    }

    // =========================================================================
    // 18. Deadlock Demo (sync-over-async)
    // =========================================================================
    static async Task Example18_DeadlockDemo()
    {
        // In a console app (no SynchronizationContext), this works fine.
        // In WPF/WinForms/old ASP.NET, this would DEADLOCK.
        Console.WriteLine("  NOTE: This demo shows patterns that would deadlock in UI apps.");
        Console.WriteLine("  In console apps (no SynchronizationContext), they work but waste threads.\n");

        // Pattern 1: .Result on async (SAFE here, DEADLOCK in UI)
        var result = Task.Run(async () =>
        {
            await Task.Delay(50);
            return "data";
        }).Result;
        Console.WriteLine($"  .Result pattern: {result} (safe in console, deadlock in UI)");

        // Pattern 2: .Wait() on async (same risk)
        Task.Run(async () => await Task.Delay(50)).Wait();
        Console.WriteLine("  .Wait() pattern: completed (safe in console, deadlock in UI)");

        // Pattern 3: The SAFE way — async all the way
        var safeResult = await SafeGetDataAsync();
        Console.WriteLine($"  async all the way: {safeResult} (always safe ✓)");

        // Show the deadlock scenario diagram
        Console.WriteLine(@"
  ┌─────────────────────────────────────────────────────────┐
  │  DEADLOCK in UI/ASP.NET (with SynchronizationContext):  │
  │                                                         │
  │  UI Thread: GetData() ──► task.Result [BLOCKED]         │
  │      ▲                                                  │
  │      │ continuation wants to resume here                │
  │      │ (SynchronizationContext.Post)                    │
  │      ▼                                                  │
  │  GetDataAsync() ──► await httpClient.GetAsync()         │
  │                        I/O completes...                 │
  │                        continuation QUEUED to UI thread │
  │                        BUT UI thread is BLOCKED!        │
  │                                                         │
  │                   ══ DEADLOCK ══                        │
  │                                                         │
  │  FIX: Use 'await' instead of '.Result' / '.Wait()'     │
  │       Or use ConfigureAwait(false)                      │
  └─────────────────────────────────────────────────────────┘");
    }

    static async Task<string> SafeGetDataAsync()
    {
        await Task.Delay(50);
        return "safe-data";
    }
}
