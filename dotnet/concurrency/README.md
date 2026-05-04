# .NET Concurrency & Multi-Threading — A Comprehensive Guide

> Inspired by *Java Concurrency in Practice* — adapted for the .NET ecosystem.

## Table of Contents

- [1. Foundations: CLR Threading Model](#1-foundations-clr-threading-model)
  - [1.1 Process, AppDomain, and Thread](#11-process-appdomain-and-thread)
  - [1.2 The CLR ThreadPool](#12-the-clr-threadpool)
  - [1.3 Memory Model & Visibility](#13-memory-model--visibility)
- [2. Synchronization Primitives](#2-synchronization-primitives)
  - [2.1 lock / Monitor](#21-lock--monitor)
  - [2.2 Interlocked Operations](#22-interlocked-operations)
  - [2.2b Volatile vs Interlocked (Atomic)](#22b-volatile-vs-interlocked-atomic)
  - [2.3 ReaderWriterLockSlim](#23-readerwriterlockslim)
  - [2.4 SemaphoreSlim](#24-semaphoreslim)
  - [2.5 ManualResetEventSlim / AutoResetEvent](#25-manualreseteventslim--autoresetevent)
  - [2.6 SpinLock & SpinWait](#26-spinlock--spinwait)
- [3. Thread-Safe Collections](#3-thread-safe-collections)
- [4. Task Parallel Library (TPL)](#4-task-parallel-library-tpl)
  - [4.1 Task and Task\<T\>](#41-task-and-taskt)
  - [4.2 Task Scheduling & TaskScheduler](#42-task-scheduling--taskscheduler)
  - [4.3 Parallel.For / Parallel.ForEach / Parallel.Invoke](#43-parallelfor--parallelforeach--parallelinvoke)
  - [4.4 PLINQ (Parallel LINQ)](#44-plinq-parallel-linq)
- [5. async/await & The Asynchronous Pattern](#5-asyncawait--the-asynchronous-pattern)
  - [5.1 The State Machine Behind async/await](#51-the-state-machine-behind-asyncawait)
  - [5.2 SynchronizationContext & ConfigureAwait](#52-synchronizationcontext--configureawait)
  - [5.3 ValueTask vs Task](#53-valuetask-vs-task)
  - [5.4 Common Pitfalls: Deadlocks & Sync-over-Async](#54-common-pitfalls-deadlocks--sync-over-async)
- [6. Cancellation & Timeout](#6-cancellation--timeout)
- [7. Dataflow & Channels](#7-dataflow--channels)
- [8. Comparison: .NET vs Java Concurrency](#8-comparison-net-vs-java-concurrency)

---

## 1. Foundations: CLR Threading Model

### 1.1 Process, AppDomain, and Thread

In .NET, the execution hierarchy is:

```
┌─────────────────────────────────────────────┐
│  OS Process                                 │
│  ┌───────────────────────────────────────┐  │
│  │  CLR (Common Language Runtime)        │  │
│  │  ┌─────────────────────────────────┐  │  │
│  │  │  AppDomain (logical isolation)  │  │  │
│  │  │                                 │  │  │
│  │  │  Thread 1 ──► Managed Stack     │  │  │
│  │  │  Thread 2 ──► Managed Stack     │  │  │
│  │  │  Thread N ──► Managed Stack     │  │  │
│  │  └─────────────────────────────────┘  │  │
│  └───────────────────────────────────────┘  │
└─────────────────────────────────────────────┘
```

**Key differences from Java:**
- .NET threads are **1:1 mapped** to OS threads (same as Java's platform threads since Java 1)
- .NET 6+ removed AppDomain isolation (only one AppDomain per process)
- Java 21 introduced **Virtual Threads** (Project Loom) — .NET achieves similar scalability via `async/await` + ThreadPool instead of M:N threading

A .NET `Thread` has:
- **Managed thread ID** (`Thread.ManagedThreadId`) — assigned by CLR
- **OS thread ID** — assigned by the operating system
- **Thread-local storage** (`ThreadLocal<T>`, `[ThreadStatic]`)
- **Stack** — default 1MB on 64-bit

```csharp
// See: examples/01_ThreadBasics.cs
var thread = new Thread(() =>
{
    Console.WriteLine($"Running on thread {Thread.CurrentThread.ManagedThreadId}");
});
thread.IsBackground = true;  // won't keep process alive
thread.Start();
thread.Join();  // wait for completion
```

### 1.2 The CLR ThreadPool

The ThreadPool is the **central execution engine** in .NET. Unlike Java's `ExecutorService` (which you create and configure), .NET has a **single global ThreadPool** shared by the entire process.

```
┌───────────────────────────────────────────────────────┐
│  CLR ThreadPool                                       │
│                                                       │
│  ┌─────────────┐  ┌──────────────────┐               │
│  │ Worker       │  │ I/O Completion   │               │
│  │ Threads      │  │ Port Threads     │               │
│  │              │  │                  │               │
│  │ Task.Run()   │  │ Async I/O        │               │
│  │ Parallel.*   │  │ callbacks        │               │
│  │ Timer CBs    │  │ (socket, file)   │               │
│  └─────────────┘  └──────────────────┘               │
│                                                       │
│  Hill-Climbing Algorithm:                             │
│  - Min threads = Environment.ProcessorCount           │
│  - Adds ~1 thread/500ms when all threads busy         │
│  - Reduces threads when utilization drops              │
└───────────────────────────────────────────────────────┘
```

**Two pools:**
1. **Worker threads** — execute `Task.Run()`, `Parallel.*`, timer callbacks, `ThreadPool.QueueUserWorkItem`
2. **I/O Completion Port (IOCP) threads** — handle async I/O completions (sockets, files, database)

**The Hill-Climbing algorithm** dynamically adjusts thread count:
- **Min threads** = `Environment.ProcessorCount` (e.g., 16 on an 16-core machine). These are **pre-created at startup** — the pool is "warm" from the start with this many threads ready to go.
- When all min threads are busy, Hill-Climbing injects new threads **slowly** (~1 per 500ms). It measures throughput after each injection and decides whether adding more helps or hurts (gradient-based optimization).
- **Max threads** defaults to 32,767 — the ceiling, not a target. The pool will never voluntarily grow to max; Hill-Climbing only injects when it detects throughput improvement.
- When utilization drops, threads are **retired** (allowed to exit) back toward the min count.
- This is why **blocking ThreadPool threads is dangerous** — if you block 16 threads on IO, the pool starts at 0 available and can only recover at ~2 threads/second (1 per 500ms). A burst of 100 blocked requests takes ~50 seconds to get enough threads.

```
Thread injection timeline (16-core machine, all threads blocked):

t=0s:    16 threads (min) — all blocked on .Wait() → 0 available
t=0.5s:  Hill-Climbing injects thread 17 → immediately blocked too
t=1.0s:  injects thread 18 → blocked
t=1.5s:  injects thread 19 → blocked
...
t=25s:   finally 66 threads — maybe enough to serve new requests

Meanwhile, global queue has 100 pending requests all starving.
Compare: async/await would handle all 100 with the original 16 threads.
```

You can inspect and override these values:

```csharp
// See: examples/02_ThreadPool.cs
ThreadPool.GetMinThreads(out int workerMin, out int ioMin);
ThreadPool.GetMaxThreads(out int workerMax, out int ioMax);
Console.WriteLine($"Worker threads: min={workerMin}, max={workerMax}");
Console.WriteLine($"IOCP threads:   min={ioMin}, max={ioMax}");
// Output: Worker threads: min=16, max=32767  (on a 16-core machine)

// Override min threads (common in ASP.NET apps that block heavily):
// ThreadPool.SetMinThreads(100, 100);  // pre-creates 100 threads — band-aid, not a fix
// The real fix: use async/await instead of blocking

// Queue work to the ThreadPool
ThreadPool.QueueUserWorkItem(_ =>
{
    Console.WriteLine($"Running on pool thread {Thread.CurrentThread.ManagedThreadId}");
});
```

**Java comparison:**
| Aspect | Java | .NET |
|--------|------|------|
| Thread pool | `ExecutorService` (create per use) | `ThreadPool` (single global) |
| Pool sizing | Explicit (`newFixedThreadPool(10)`) | Automatic (Hill-Climbing) |
| Async I/O threads | Same pool (or NIO selector threads) | Separate IOCP pool |
| Virtual threads | Java 21 `Thread.ofVirtual()` | `async/await` (compiler-generated state machine) |

#### Work-Stealing Queues: Global (FIFO) vs Local (LIFO)

The ThreadPool doesn't have a single queue — it has a **two-level work queue architecture** designed for cache locality and scalability:

```
┌──────────────────────────────────────────────────────────────────────┐
│  ThreadPool Work Queue Architecture                                  │
│                                                                      │
│  ┌──────────────────────────────────────┐                            │
│  │     Global Queue (FIFO)              │                            │
│  │  ┌───┬───┬───┬───┬───┬───┐          │                            │
│  │  │ A │ B │ C │ D │ E │ F │ ──► out  │  ThreadPool.QueueUserWork  │
│  │  └───┴───┴───┴───┴───┴───┘          │  Item goes HERE            │
│  └──────────────────────────────────────┘                            │
│         ▲ steal                ▲ steal                               │
│         │                      │                                     │
│  ┌──────┴──────┐  ┌───────────┴─────┐  ┌──────────────────┐        │
│  │ Thread 1    │  │ Thread 2        │  │ Thread 3         │        │
│  │ Local Queue │  │ Local Queue     │  │ Local Queue      │        │
│  │ (LIFO/stack)│  │ (LIFO/stack)    │  │ (LIFO/stack)     │        │
│  │ ┌───┐       │  │ ┌───┐           │  │ (empty)          │        │
│  │ │ X │ ◄─pop │  │ │ Y │ ◄─pop     │  │                  │        │
│  │ │ W │       │  │ │ Z │           │  │  "I'm idle...    │        │
│  │ │ V │       │  │ └───┘           │  │   let me STEAL   │        │
│  │ └───┘       │  │                 │  │   from Global    │        │
│  │             │  │                 │  │   or Thread 1"   │        │
│  │ Task.Run()  │  │ Task.Run()      │  │                  │        │
│  │ spawned     │  │ spawned         │  └──────────────────┘        │
│  │ inside a    │  │ inside a        │        ▲                      │
│  │ running     │  │ running         │        │ steal (FIFO order)   │
│  │ task goes   │  │ task goes       │        │ from Thread 1's      │
│  │ HERE        │  │ HERE            │        │ local queue bottom   │
│  └─────────────┘  └─────────────────┘        │                      │
│                                               │                      │
│  Work-stealing: idle thread takes from ───────┘                      │
│  1. Global queue first (FIFO)                                        │
│  2. Other threads' local queues (FIFO — steal from BOTTOM)           │
└──────────────────────────────────────────────────────────────────────┘
```

**Global Queue (FIFO — First-In, First-Out):**
- `ThreadPool.QueueUserWorkItem()` and `Task.Factory.StartNew()` enqueue here
- **Multiple threads dequeue concurrently** — it's a `ConcurrentQueue<T>`, not a serial pipeline
- Once an item is dequeued, it runs independently on that thread — it does NOT block other items in the queue
- Cross-thread: work items have **no cache affinity** to any specific thread

```
Global queue is NOT serial — multiple threads grab items simultaneously:

  Global Queue: [A, B, C, D, E]

  Thread 1 idle → dequeues A → runs A ──────────────► (running)
  Thread 2 idle → dequeues B → runs B ──────────────► (running)   doesn't wait for A
  Thread 3 idle → dequeues C → runs C ──────────────► (running)   all 3 in parallel
                  [D, E remain]

  When any thread finishes → dequeues D, then E
  If ALL threads are busy → D,E wait for a thread to become free (or Hill-Climbing injects one)
```

**Local Queue (LIFO — Last-In, First-Out / stack):**
- When a **running task spawns child tasks** (e.g., `Task.Run()` inside `Task.Run()`), the child goes to the **current thread's local queue**
- The same thread pops from the top (LIFO) — **most recently added item first**
- LIFO gives **cache locality**: the child task likely touches the same memory as the parent, and that memory is still hot in the CPU cache
- Implemented as a lock-free work-stealing deque (`WorkStealingQueue` in CoreCLR)
- **Every pool thread** (including newly injected ones from Hill-Climbing) gets its own local queue

**Work-Stealing & Thread Dispatch Order:**

When a thread finishes its current work item, it checks in this order:
1. **Own local queue** — pop from top (LIFO, instant, no contention)
2. **Global queue** — dequeue (FIFO, concurrent with other threads)
3. **Other threads' local queues** — steal from bottom (FIFO, avoids contention with owner)

Stealing uses FIFO order (take from the **bottom** of another thread's local stack) — this avoids contention with the owning thread (which pops from the top).

When Hill-Climbing injects a **new thread**, it gets an empty local queue and immediately starts at step 2 (global queue), then step 3 (steal). This is why new threads help drain the global queue backlog.

**What you can control:**

| API | Where it goes | Order |
|-----|--------------|-------|
| `ThreadPool.QueueUserWorkItem()` | Global queue | FIFO |
| `Task.Run()` (from non-pool thread) | Global queue | FIFO |
| `Task.Run()` (from inside a running task) | Current thread's local queue | LIFO |
| `Task.Factory.StartNew(..., TaskCreationOptions.PreferFairness)` | **Forces** global queue | FIFO |
| `ThreadPool.UnsafeQueueUserWorkItem(..., preferLocal: true)` | Local queue (.NET 5+) | LIFO |
| `ThreadPool.UnsafeQueueUserWorkItem(..., preferLocal: false)` | Global queue (.NET 5+) | FIFO |

**How this impacts your code:**

```csharp
// Scenario: parent task spawns 1000 child tasks
await Task.Run(async () =>                      // runs on pool thread 5
{
    for (int i = 0; i < 1000; i++)
    {
        int id = i;
        Task.Run(() => Process(id));            // goes to thread 5's LOCAL queue (LIFO)
    }
    // Item 999 runs FIRST (LIFO), item 0 runs LAST
    // All initially run on thread 5 (cache-hot), then other threads steal
});

// If you need FIFO order (fairness over cache locality):
Task.Factory.StartNew(() => Process(id),
    CancellationToken.None, TaskCreationOptions.PreferFairness, TaskScheduler.Default);
// Forces global queue → FIFO → item 0 runs first
```

**Observable effects:**

1. **Execution order** — child tasks from `Task.Run()` inside a task run in **reverse order** (LIFO), not submission order. If you `Task.Run()` items 0,1,2,...,999, item 999 runs first.

2. **Thread affinity** — child tasks tend to run on the **same thread** as the parent (local queue pop), giving better cache performance. You'll see repeated `ManagedThreadId` values.

3. **Fairness / starvation** — a thread checks its **local queue BEFORE the global queue**. If a task keeps spawning children (which go to the local queue), and each child spawns more children, that thread's local queue never empties → it **never checks the global queue**. Items waiting in the global queue (e.g., incoming web requests) are starved until some other thread goes idle. This requires **all** threads to have non-empty local queues simultaneously — rare in practice, but possible with recursive divide-and-conquer patterns. `PreferFairness` fixes it by routing children to the global queue.

   ```
   Starvation scenario (all threads busy with self-replenishing local queues):
   
   Thread 1 local: [C3, C2, C1]   ← pops C3, C3 spawns C4,C5 → never empty
   Thread 2 local: [C6]           ← pops C6, C6 spawns C7    → never empty
   ...
   Thread N local: [...]          ← all threads self-feeding
   
   Global queue: [WebRequest_A, WebRequest_B]  ← nobody checks here!
   
   Fix: Task.Factory.StartNew(work, ..., TaskCreationOptions.PreferFairness, ...)
        → children go to global queue → threads interleave global + local work
   ```

4. **Parallelism** — if one thread has 1000 items in its local queue, other idle threads will **steal** from the bottom (FIFO). So work eventually spreads across all threads, just not immediately.

**Java comparison:**
| Aspect | .NET ThreadPool | Java ForkJoinPool |
|--------|----------------|-------------------|
| Architecture | Global queue + per-thread local deques | Per-thread deques (no separate global) |
| Local order | LIFO (own thread) | LIFO (own thread) |
| Steal order | FIFO (from bottom of victim's deque) | FIFO (from bottom of victim's deque) |
| Default pool | `ThreadPool` (process-wide singleton) | `ForkJoinPool.commonPool()` (since Java 8) |
| Force fairness | `TaskCreationOptions.PreferFairness` | Not directly (use `ExecutorService` instead) |
| Explicit local/global | `preferLocal` parameter (.NET 5+) | No direct API |

### 1.3 Memory Model & Visibility

The .NET memory model guarantees:

1. **Volatile reads/writes** are not reordered past each other
2. **`lock` (Monitor.Enter/Exit)** provides full memory fence
3. **`Interlocked.*`** operations provide atomic read-modify-write with full fence
4. **`Volatile.Read()` / `Volatile.Write()`** — acquire/release semantics

```csharp
// WRONG — may never see the update on another thread (compiler/CPU reordering)
private bool _running = true;
void WorkerThread() { while (_running) { /* work */ } }
void Stop() { _running = false; }

// CORRECT — volatile ensures visibility
private volatile bool _running = true;
void WorkerThread() { while (_running) { /* work */ } }
void Stop() { _running = false; }
```

**Java comparison:** Java's `volatile` provides similar acquire/release semantics. Java's `happens-before` model maps closely to .NET's guarantees, but the CLR is generally stricter on x86 (strong memory model means fewer reordering surprises than ARM/Java).

---

## 2. Synchronization Primitives

### 2.1 lock / Monitor

`lock` is syntactic sugar for `Monitor.Enter` / `Monitor.Exit`. It's the most common synchronization primitive — equivalent to Java's `synchronized`.

```csharp
// See: examples/03_LockAndMonitor.cs
private readonly object _lock = new();
private int _count;

void Increment()
{
    lock (_lock)  // Monitor.Enter(_lock)
    {
        _count++;
    }                // Monitor.Exit(_lock) — even on exception
}

// With timeout
void TryIncrement()
{
    bool acquired = false;
    try
    {
        Monitor.TryEnter(_lock, TimeSpan.FromSeconds(1), ref acquired);
        if (acquired)
            _count++;
        else
            Console.WriteLine("Could not acquire lock within timeout");
    }
    finally
    {
        if (acquired) Monitor.Exit(_lock);
    }
}

// Wait/Pulse — equivalent to Java's wait()/notify()
void ProducerConsumer()
{
    lock (_lock)
    {
        while (_count == 0)
            Monitor.Wait(_lock);   // releases lock, waits for Pulse
        _count--;
    }
    // Producer:
    lock (_lock)
    {
        _count++;
        Monitor.Pulse(_lock);      // wake one waiter
        // Monitor.PulseAll(_lock); // wake all waiters
    }
}
```

**Key design difference from Java:**
- Java: any object can be a lock (`synchronized(obj)`)
- .NET: any object can be a lock too (`lock(obj)`), but **best practice is a dedicated `object`**
- Never `lock(this)` or `lock(typeof(T))` — external code might lock on the same reference

### 2.2 Interlocked Operations

Atomic operations without locks — equivalent to Java's `AtomicInteger`, `AtomicLong`, etc.

```csharp
// See: examples/04_Interlocked.cs
private int _counter;
private long _total;

void AtomicOperations()
{
    Interlocked.Increment(ref _counter);        // atomic ++
    Interlocked.Decrement(ref _counter);        // atomic --
    Interlocked.Add(ref _total, 100);           // atomic +=
    Interlocked.Exchange(ref _counter, 0);      // atomic set, returns old value
    
    // Compare-and-swap (CAS) — foundation of lock-free algorithms
    int oldVal, newVal;
    do
    {
        oldVal = _counter;
        newVal = oldVal + 1;
    } while (Interlocked.CompareExchange(ref _counter, newVal, oldVal) != oldVal);
}
```

**Java comparison:**
| .NET | Java |
|------|------|
| `Interlocked.Increment(ref x)` | `atomicInt.incrementAndGet()` |
| `Interlocked.CompareExchange(ref x, newVal, expected)` | `atomicInt.compareAndSet(expected, newVal)` |
| Works on raw `int`/`long` fields | Requires wrapper `AtomicInteger`/`AtomicLong` |

### 2.2b Volatile vs Interlocked (Atomic)

`volatile` ensures **visibility** (other threads see the latest value), but does NOT make read-modify-write operations atomic. `Interlocked` provides both visibility and atomicity.

```csharp
// See: examples/05_VolatileVsInterlocked.cs

// volatile = visibility only (enough for flags, single-writer)
volatile bool _stop = false;                    // .NET
// volatile boolean stop = false;               // Java — same keyword

Volatile.Write(ref _stop, true);                // explicit fence
// VarHandle.setRelease(this, stop, true);      // Java equivalent

// Interlocked = visibility + atomicity (needed for counters, CAS)
Interlocked.Increment(ref counter);             // single hardware instruction (lock xadd)
// atomicInt.incrementAndGet();                 // Java equivalent

// CAS loop — lock-free algorithms
int oldVal;
do {
    oldVal = Volatile.Read(ref sharedMax);
    if (proposed <= oldVal) break;
} while (Interlocked.CompareExchange(ref sharedMax, proposed, oldVal) != oldVal);
// Java: atomicInt.compareAndSet(oldVal, proposed)  — note: param order differs!
```

**When to use which:**
- **`volatile`** — boolean flags, one-writer/many-reader, reference publishing
- **`Interlocked`** — counters, accumulators, CAS loops, any read-modify-write

### 2.3 ReaderWriterLockSlim

Allows multiple concurrent readers OR one exclusive writer — equivalent to Java's `ReadWriteLock`.

```csharp
// See: examples/06_ReaderWriterLock.cs
private readonly ReaderWriterLockSlim _rwLock = new();
private readonly Dictionary<string, string> _cache = new();

string Read(string key)
{
    _rwLock.EnterReadLock();
    try { return _cache.TryGetValue(key, out var val) ? val : null; }
    finally { _rwLock.ExitReadLock(); }
}

void Write(string key, string value)
{
    _rwLock.EnterWriteLock();
    try { _cache[key] = value; }
    finally { _rwLock.ExitWriteLock(); }
}

// Upgradeable read — unique to .NET (Java doesn't have this)
void ReadThenMaybeWrite(string key, string value)
{
    _rwLock.EnterUpgradeableReadLock();
    try
    {
        if (!_cache.ContainsKey(key))
        {
            _rwLock.EnterWriteLock();  // upgrade to write
            try { _cache[key] = value; }
            finally { _rwLock.ExitWriteLock(); }
        }
    }
    finally { _rwLock.ExitUpgradeableReadLock(); }
}
```

**Under the hood — how ReaderWriterLockSlim works:**

The implementation uses a **single `int` state word** manipulated via `Interlocked` operations (CAS), avoiding kernel transitions in the fast path.

```
State word layout (32-bit int):
┌────────────────────────────────────────────────────────────────┐
│ bits 31-16: writer info     │ bits 15-0: active reader count  │
│ (owner thread ID, flags)    │ (atomically incremented/decremented) │
└────────────────────────────────────────────────────────────────┘

EnterReadLock (fast path):
  1. CAS: readers++ on the state word
  2. If no writer active → succeed immediately (no kernel call)
  3. If writer active → spin briefly, then block on ManualResetEventSlim

EnterWriteLock (fast path):
  1. CAS: set writer flag + store owner thread ID
  2. If no readers and no other writer → succeed immediately
  3. If readers active → spin, then block until reader count drops to 0

EnterUpgradeableReadLock:
  1. Acquire read lock + set "upgradeable" flag
  2. Only ONE thread can hold upgradeable at a time (like a "reservation")
  3. EnterWriteLock from upgradeable → wait for other readers to drain, then upgrade
```

**Three lock levels:**

| Level | Concurrent with reads? | Concurrent with writes? | Max holders |
|-------|----------------------|------------------------|-------------|
| **Read** | ✓ Yes | ✗ No (blocks) | Unlimited |
| **Upgradeable Read** | ✓ Yes (with reads) | ✗ No | **Exactly 1** |
| **Write** | ✗ No (exclusive) | ✗ No (exclusive) | **Exactly 1** |

**Spin-then-block strategy:**

```
Thread wants lock but it's held:
  1. SpinWait loop (user-mode, ~20 iterations)
     └─ CPU stays busy but avoids expensive kernel transition
  2. If still blocked → fall back to ManualResetEventSlim.Wait()
     └─ Thread yields to OS scheduler (kernel mode)
  3. When lock released → ManualResetEventSlim.Set() wakes waiters
```

This is why it outperforms `Monitor`/`lock` for read-heavy workloads — multiple readers proceed concurrently with just an `Interlocked.Increment`, no kernel call. But for write-heavy workloads, `lock` is faster because `ReaderWriterLockSlim` has more overhead per operation.

**Java comparison:**
| Aspect | .NET `ReaderWriterLockSlim` | Java `ReentrantReadWriteLock` |
|--------|---------------------------|------------------------------|
| State tracking | Single `int` + CAS | `AbstractQueuedSynchronizer` (AQS) with int state |
| Spin before block | Yes (SpinWait) | Yes (CLH queue with spinning) |
| Upgradeable lock | ✓ `EnterUpgradeableReadLock()` | ✗ Not supported (must release read, acquire write) |
| Reentrancy | Opt-in (`LockRecursionPolicy.SupportsRecursion`) | Always reentrant |
| Fairness | Not fair (writers can starve) | Optional fair mode (`new ReentrantReadWriteLock(true)`) |
| Async support | ✗ No (blocks thread) | ✗ No (but `StampedLock` offers optimistic reads) |

**When to use vs alternatives:**
- **`lock`** — simpler, faster for short critical sections or write-heavy workloads
- **`ReaderWriterLockSlim`** — read-heavy with infrequent writes (e.g., config cache, lookup tables)
- **`ConcurrentDictionary`** — if your shared state is a dictionary, just use this instead
- **`ImmutableDictionary` + `Interlocked.Exchange`** — lock-free reads, copy-on-write for writes

### 2.4 SemaphoreSlim

Limits concurrent access to a resource — equivalent to Java's `Semaphore`. Supports async waiting.

```csharp
// See: examples/07_Semaphore.cs
// Limit to 3 concurrent database connections
private readonly SemaphoreSlim _semaphore = new(initialCount: 3, maxCount: 3);

async Task<string> QueryDatabaseAsync(string query)
{
    await _semaphore.WaitAsync();  // async wait — doesn't block a thread!
    try
    {
        // Only 3 concurrent executions reach here
        return await ExecuteQueryAsync(query);
    }
    finally
    {
        _semaphore.Release();
    }
}
```

**Key advantage over Java:** `SemaphoreSlim.WaitAsync()` is truly async — Java's `Semaphore.acquire()` always blocks the thread.

### 2.5 ManualResetEventSlim / AutoResetEvent

Signaling primitives — one thread signals, others wait.

```csharp
// See: examples/08_ResetEvents.cs

// ManualResetEventSlim — stays signaled until manually reset (like a gate)
var gate = new ManualResetEventSlim(initialState: false);

// Worker waits for gate to open
Task.Run(() => { gate.Wait(); Console.WriteLine("Gate opened!"); });

// Controller opens the gate — all waiters proceed
gate.Set();    // open gate
gate.Reset();  // close gate again

// AutoResetEvent — auto-resets after releasing ONE waiter (like a turnstile)
var turnstile = new AutoResetEvent(initialState: false);
Task.Run(() => { turnstile.WaitOne(); Console.WriteLine("One thread through"); });
turnstile.Set();  // releases exactly one waiter, then auto-resets
```

### 2.6 SpinLock & SpinWait

For extremely short critical sections where context-switching overhead exceeds the wait time.

```csharp
// See: examples/09_SpinLock.cs

// SpinLock — busy-waits instead of context-switching
private SpinLock _spinLock = new();

void CriticalSection()
{
    bool lockTaken = false;
    try
    {
        _spinLock.Enter(ref lockTaken);
        // Very short work (< 1μs) — no allocations, no I/O
    }
    finally
    {
        if (lockTaken) _spinLock.Exit();
    }
}

// SpinWait — hybrid: spins briefly, then yields, then sleeps
void WaitForCondition(ref bool flag)
{
    var spinner = new SpinWait();
    while (!Volatile.Read(ref flag))
    {
        spinner.SpinOnce();
        // Iteration 1-10: Thread.SpinWait (CPU spin)
        // Iteration 11-20: Thread.Yield() (give up timeslice)
        // Iteration 21+: Thread.Sleep(1) (context switch)
    }
}
```

**When to use each:**
| Primitive | Use When | Thread Behavior |
|-----------|----------|-----------------|
| `lock` | General purpose, medium+ critical sections | Blocks (context switch) |
| `SpinLock` | Very short critical sections (< 1μs), no nested locks | Spins (burns CPU) |
| `SemaphoreSlim` | Need async support or counting | Blocks or async waits |
| `Interlocked` | Single atomic operation on a field | Lock-free |

---

## 3. Thread-Safe Collections

.NET provides concurrent collections in `System.Collections.Concurrent` — equivalent to Java's `java.util.concurrent` collections.

```csharp
// See: examples/10_ConcurrentCollections.cs

// ConcurrentDictionary — lock-striped (multiple internal locks)
var dict = new ConcurrentDictionary<string, int>();
dict.TryAdd("key", 1);
dict.AddOrUpdate("key", 1, (k, old) => old + 1);  // atomic add-or-update
var value = dict.GetOrAdd("key", k => ExpensiveCompute(k));

// ConcurrentQueue — lock-free (CAS-based)
var queue = new ConcurrentQueue<WorkItem>();
queue.Enqueue(new WorkItem());
if (queue.TryDequeue(out var item)) { /* process */ }

// ConcurrentBag — thread-local storage + work stealing
// Optimized for producer == consumer (same thread adds and removes)
var bag = new ConcurrentBag<int>();

// BlockingCollection — bounded producer-consumer (like Java's BlockingQueue)
var bc = new BlockingCollection<int>(boundedCapacity: 100);
// Producer
Task.Run(() => { for (int i = 0; i < 1000; i++) bc.Add(i); bc.CompleteAdding(); });
// Consumer
Task.Run(() => { foreach (var x in bc.GetConsumingEnumerable()) Process(x); });
```

**Comparison with Java:**
| .NET | Java | Internal Mechanism |
|------|------|--------------------|
| `ConcurrentDictionary<K,V>` | `ConcurrentHashMap<K,V>` | Lock striping (segments) |
| `ConcurrentQueue<T>` | `ConcurrentLinkedQueue<T>` | Lock-free (CAS) |
| `ConcurrentBag<T>` | *(no equivalent)* | Thread-local + work stealing |
| `BlockingCollection<T>` | `BlockingQueue<T>` | Monitor-based blocking |
| `ConcurrentStack<T>` | `ConcurrentLinkedDeque<T>` | Lock-free (CAS) |
| `ImmutableList<T>` | `List.copyOf()` | Structural sharing |

### ConcurrentDictionary Internals

```
┌─────────────────────────────────────────────────┐
│  ConcurrentDictionary                           │
│                                                 │
│  Bucket Array (hash table)                      │
│  ┌───┬───┬───┬───┬───┬───┬───┬───┐             │
│  │ 0 │ 1 │ 2 │ 3 │ 4 │ 5 │ 6 │ 7 │             │
│  └─┬─┴─┬─┴───┴─┬─┴───┴───┴─┬─┴───┘             │
│    │   │       │           │                    │
│  Lock 0    Lock 1       Lock 2    ← Striped     │
│  (buckets  (buckets     (buckets     Locks      │
│   0-3)      4-7)         8-11)                  │
│                                                 │
│  Default: concurrencyLevel = ProcessorCount     │
│           (number of lock stripes)              │
└─────────────────────────────────────────────────┘
```

⚠️ **Warning:** Iteration order of `ConcurrentDictionary` is **non-deterministic**. Never depend on `foreach` order — this was a root cause in our EF Core migration (see EFCoreMigration.md Section 10).

---

## 4. Task Parallel Library (TPL)

### 4.1 Task and Task\<T\>

`Task` is the **fundamental unit of work** in .NET — equivalent to Java's `Future<T>` + `CompletableFuture<T>` combined.

```csharp
// See: examples/11_TaskBasics.cs

// Create and start a Task
Task<int> task = Task.Run(() => ComputeExpensiveResult());
int result = await task;  // or task.Result (blocking)

// Continuations — like CompletableFuture.thenApply()
Task<string> chain = Task.Run(() => 42)
    .ContinueWith(t => $"Result: {t.Result}");

// Combinators
Task allDone = Task.WhenAll(task1, task2, task3);   // all complete
Task<Task> anyDone = Task.WhenAny(task1, task2);    // first to complete

// Exception handling
try
{
    await Task.Run(() => throw new InvalidOperationException("oops"));
}
catch (InvalidOperationException ex)
{
    // await unwraps AggregateException automatically
}

// task.Result or task.Wait() wraps in AggregateException (like Java's ExecutionException)
```

**Task states:**

```
Created ──► WaitingForActivation ──► WaitingToRun ──► Running ──► RanToCompletion
                                                          │
                                                          ├──► Faulted
                                                          └──► Canceled
```

### 4.2 Task Scheduling & TaskScheduler

```
┌────────────────────────────────────────────────────────┐
│  Task Scheduling Pipeline                              │
│                                                        │
│  Task.Run(work)                                        │
│       │                                                │
│       ▼                                                │
│  TaskScheduler.Default                                 │
│  (ThreadPoolTaskScheduler)                             │
│       │                                                │
│       ▼                                                │
│  ThreadPool Global Queue                               │
│  ┌─────────────────────────────────┐                   │
│  │ Task A │ Task B │ Task C │ ... │  ← FIFO            │
│  └─────────────────────────────────┘                   │
│       │                                                │
│  ┌────┴─────┬──────────┐                               │
│  ▼          ▼          ▼                                │
│  Thread 1   Thread 2   Thread 3                        │
│  Local Q    Local Q    Local Q   ← LIFO (cache-warm)   │
│  ┌──────┐  ┌──────┐  ┌──────┐                         │
│  │ T1.1 │  │ T2.1 │  │ T3.1 │                         │
│  │ T1.2 │  │      │  │ T3.2 │                         │
│  └──────┘  └──────┘  └──────┘                         │
│                                                        │
│  Work Stealing: Thread 2 (idle) steals from Thread 1   │
└────────────────────────────────────────────────────────┘
```

**Key design:** Each ThreadPool thread has a **local work queue** (LIFO) for cache locality. The **global queue** is FIFO. Idle threads **steal** from other threads' local queues (FIFO from the other end to minimize contention). This is identical to Java's `ForkJoinPool` design.

**How tasks flow through the scheduler:**

When you call `Task.Run(work)`, here's the exact dispatch path:

```csharp
// 1. Task.Run() → uses TaskScheduler.Default (ThreadPoolTaskScheduler)
Task.Run(() => DoWork());
//  ↓ internally calls:
//  Task.Factory.StartNew(work, CancellationToken.None,
//      TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

// 2. ThreadPoolTaskScheduler.QueueTask() decides WHERE:
//    - If called from a ThreadPool thread → thread's LOCAL queue (LIFO)
//    - If called from a non-pool thread → GLOBAL queue (FIFO)
//    - If TaskCreationOptions.PreferFairness → always GLOBAL queue (FIFO)
```

**TaskScheduler — the abstraction layer:**

`TaskScheduler` is the bridge between `Task` and the actual thread execution. .NET ships two:

| Scheduler | When used | Queue behavior |
|-----------|----------|----------------|
| `TaskScheduler.Default` | `Task.Run()`, `Task.Factory.StartNew()` | ThreadPool (global + local queues) |
| `TaskScheduler.FromCurrentSynchronizationContext()` | UI apps (WPF/WinForms) | Posts to UI thread's message loop |

```csharp
// TaskScheduler.Default — work goes to ThreadPool
Task.Run(() => Console.WriteLine("ThreadPool")); // always uses Default

// TaskScheduler.FromCurrentSynchronizationContext() — runs on UI thread
// (only meaningful in WPF/WinForms, not console apps)
await someTask.ContinueWith(t =>
{
    // Update UI safely — this runs on the UI thread
    label.Text = t.Result;
}, TaskScheduler.FromCurrentSynchronizationContext());
```

**Custom TaskScheduler — controlling where tasks execute:**

You can create your own scheduler to control thread affinity, concurrency limits, or execution order:

```csharp
// Built-in: ConcurrentExclusiveSchedulerPair — reader/writer for tasks
var pair = new ConcurrentExclusiveSchedulerPair();

// Readers — run concurrently with each other
Task.Factory.StartNew(() => ReadData(),
    CancellationToken.None, TaskCreationOptions.None, pair.ConcurrentScheduler);

// Writers — exclusive, one at a time, no concurrent readers
Task.Factory.StartNew(() => WriteData(),
    CancellationToken.None, TaskCreationOptions.None, pair.ExclusiveScheduler);

// Limited concurrency scheduler (like Java's newFixedThreadPool)
var limited = new ConcurrentExclusiveSchedulerPair(
    TaskScheduler.Default, maxConcurrencyLevel: 3).ConcurrentScheduler;
// At most 3 tasks run simultaneously — similar to SemaphoreSlim but at scheduler level
```

**Task.Factory.StartNew vs Task.Run — the queue difference:**

```csharp
// Task.Run() — safe default, always DenyChildAttach, always Default scheduler
Task.Run(() => Work());

// Task.Factory.StartNew() — more control over scheduling
Task.Factory.StartNew(
    () => Work(),
    CancellationToken.None,
    TaskCreationOptions.PreferFairness  // → GLOBAL queue (FIFO), not local (LIFO)
        | TaskCreationOptions.LongRunning, // → dedicated thread, NOT ThreadPool
    TaskScheduler.Default);

// LongRunning = bypass ThreadPool entirely → new dedicated Thread
// Use for: blocking operations that would starve the pool
// Java equivalent: Executors.newSingleThreadExecutor() or Thread.ofPlatform()
```

**TaskCreationOptions that affect scheduling:**

| Option | Effect | Use when |
|--------|--------|----------|
| `None` (default) | Local queue (LIFO) if from pool thread | Most cases |
| `PreferFairness` | Force global queue (FIFO) | Prevent starvation of older items |
| `LongRunning` | New dedicated thread (not from pool) | Blocking work that runs for seconds+ |
| `DenyChildAttach` | Prevent child tasks from attaching | Always used by `Task.Run()` |
| `AttachedToParent` | Parent waits for child completion | Fork-join patterns (rare in modern .NET) |

**Java comparison:**

| Concept | .NET | Java |
|---------|------|------|
| Default scheduler | `TaskScheduler.Default` (ThreadPool) | `ForkJoinPool.commonPool()` |
| UI scheduler | `TaskScheduler.FromCurrentSynchronizationContext()` | `Platform.runLater()` (JavaFX) |
| Custom scheduler | Subclass `TaskScheduler` | Implement `Executor`/`ExecutorService` |
| Fixed thread pool | `ConcurrentExclusiveSchedulerPair(max: N)` | `Executors.newFixedThreadPool(N)` |
| Dedicated thread | `TaskCreationOptions.LongRunning` | `Executors.newSingleThreadExecutor()` |
| Reader/writer tasks | `ConcurrentExclusiveSchedulerPair` | No direct equivalent (use `ReadWriteLock`) |

### 4.3 Parallel.For / Parallel.ForEach / Parallel.Invoke

Data parallelism — equivalent to Java's parallel streams or `ForkJoinPool`.

```csharp
// See: examples/12_ParallelLoops.cs

// Parallel.ForEach — partition data across threads
var items = Enumerable.Range(0, 10000).ToList();
Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = 4 }, item =>
{
    ProcessItem(item);
});

// Parallel.For with thread-local state (reduce pattern)
long total = 0;
Parallel.For(0, 10000,
    () => 0L,                           // localInit: per-thread accumulator
    (i, state, localSum) =>             // body: accumulate locally
    {
        return localSum + ComputeValue(i);
    },
    localSum =>                         // localFinally: merge to global
    {
        Interlocked.Add(ref total, localSum);
    });

// Parallel.Invoke — run N actions concurrently
Parallel.Invoke(
    () => LoadDataA(),
    () => LoadDataB(),
    () => LoadDataC());
```

**⚠️ Parallel.Invoke + async anti-pattern (from our EF Core migration):**

```csharp
// DANGEROUS — blocks ThreadPool threads, risks starvation
Parallel.Invoke(
    () => asyncMethod1().Wait(),  // blocks thread
    () => asyncMethod2().Wait(),  // blocks thread
    () => asyncMethod3().Wait()); // blocks thread

// CORRECT — use Task.WhenAll for async concurrency
var tasks = new List<Task>
{
    asyncMethod1(),
    asyncMethod2(),
    asyncMethod3()
};
await Task.WhenAll(tasks);  // or Task.WhenAll(tasks).Wait() if sync context required
```

### 4.4 PLINQ (Parallel LINQ)

```csharp
// See: examples/13_PLINQ.cs

var results = Enumerable.Range(0, 1_000_000)
    .AsParallel()                          // switch to parallel
    .WithDegreeOfParallelism(4)            // limit threads
    .Where(x => IsPrime(x))               // parallel filter
    .Select(x => Transform(x))            // parallel map
    .OrderBy(x => x)                       // parallel sort (AsOrdered() preserves input order)
    .ToList();

// Force ordered results (like Java's parallel stream with forEachOrdered)
var ordered = source.AsParallel().AsOrdered()
    .Where(x => x > 0)
    .ToList();  // maintains original order
```

---

## 5. async/await & The Asynchronous Pattern

### 5.1 The State Machine Behind async/await

When the compiler sees `async`, it transforms the method into a **state machine**. This is fundamentally different from Java's approach (Java uses virtual threads or callbacks).

```csharp
// What you write:
async Task<string> FetchDataAsync(string url)
{
    var client = new HttpClient();
    var response = await client.GetStringAsync(url);  // suspension point 1
    var processed = await ProcessAsync(response);      // suspension point 2
    return processed;
}

// What the compiler generates (simplified):
struct FetchDataAsyncStateMachine : IAsyncStateMachine
{
    public int _state;           // which await we're at
    public string url;           // captured parameters
    public HttpClient client;
    public string response;
    public AsyncTaskMethodBuilder<string> _builder;
    
    void MoveNext()
    {
        switch (_state)
        {
            case 0:  // initial
                client = new HttpClient();
                var awaiter1 = client.GetStringAsync(url).GetAwaiter();
                if (!awaiter1.IsCompleted)
                {
                    _state = 1;
                    _builder.AwaitUnsafeOnCompleted(ref awaiter1, ref this);
                    return;  // RETURN TO CALLER — thread is freed
                }
                goto case 1;
                
            case 1:  // after first await
                response = awaiter1.GetResult();
                var awaiter2 = ProcessAsync(response).GetAwaiter();
                if (!awaiter2.IsCompleted)
                {
                    _state = 2;
                    _builder.AwaitUnsafeOnCompleted(ref awaiter2, ref this);
                    return;  // RETURN — thread is freed again
                }
                goto case 2;
                
            case 2:  // after second await
                _builder.SetResult(awaiter2.GetResult());
                break;
        }
    }
}
```

**The key insight:** `await` does NOT block a thread. It **returns the thread to the pool** and registers a callback. When the I/O completes, the continuation runs on **any available ThreadPool thread** (or the original `SynchronizationContext`).

```
Timeline:
Thread 1: ─── FetchDataAsync() ─── await GetString ─── [thread returned to pool]
                                                            │
                                                    [I/O in progress - NO thread used]
                                                            │
Thread 3: ─── [continuation] response = result ─── await Process ─── [thread returned]
                                                                          │
Thread 5: ─── [continuation] return processed ───
```

### 5.2 SynchronizationContext & ConfigureAwait

**What is `SynchronizationContext`?**

It's an abstraction that answers: "after `await`, which thread should the continuation run on?" Different environments install different contexts:

| Context | `SynchronizationContext.Current` | Continuation runs on | How |
|---------|----------------------------------|---------------------|-----|
| **WPF / WinForms** | `DispatcherSynchronizationContext` / `WindowsFormsSynchronizationContext` | Original UI thread | Posts to UI message pump (`Dispatcher.BeginInvoke`) |
| **Legacy ASP.NET** (not Core) | `AspNetSynchronizationContext` | Same request context (single-threaded) | Ensures `HttpContext` flows correctly |
| **ASP.NET Core** | `null` (none) | Any ThreadPool thread | By design — no context to capture |
| **Console app** | `null` (none) | Any ThreadPool thread | No message pump exists |

When you write `await someTask`, the compiler-generated state machine does this:

```
1. Capture SynchronizationContext.Current (before the await)
2. Suspend — return the thread to the pool
3. When the task completes:
   a. If captured context != null → Post continuation to that context
   b. If captured context == null → Run continuation on any ThreadPool thread
```

**What does `ConfigureAwait(false)` do?**

It says: **"Skip step 3a — don't post back to the captured context, just run on any thread."**

```csharp
// DEFAULT behavior: captures SynchronizationContext, posts back to it
await httpClient.GetStringAsync(url);
// ↑ In a UI app: continuation runs on UI thread
// ↑ In ASP.NET Core: continuation runs on any pool thread (no context to capture)

// ConfigureAwait(false): explicitly skips context capture
await httpClient.GetStringAsync(url).ConfigureAwait(false);
// ↑ In a UI app: continuation runs on ANY thread (can't touch UI controls!)
// ↑ In ASP.NET Core: no difference (there was nothing to capture anyway)
```

**Why does it matter? Two reasons:**

**Reason 1: Deadlock prevention (the critical one)**

```csharp
// UI app — button click handler
void OnButtonClick(object sender, EventArgs e)
{
    // BAD: .Result blocks the UI thread synchronously
    var data = GetDataAsync().Result;  // UI thread BLOCKS here
    label.Text = data;
}

async Task<string> GetDataAsync()
{
    // await captures UI SynchronizationContext
    var response = await httpClient.GetStringAsync(url);  // <-- IO completes eventually
    // Continuation wants to run on UI thread...
    // But UI thread is blocked on .Result above!
    return response;  // NEVER REACHED → DEADLOCK
}
```

```
The deadlock timeline:

UI Thread:    OnButtonClick() → .Result → BLOCKED (waiting for task) ──────► stuck forever
                                              ↑
                                              │ needs UI thread to continue
                                              │
Pool Thread:  httpClient.GetStringAsync() completes → wants to Post to UI thread
              but UI thread is blocked → continuation queued in UI message pump
              → nobody pumps the message → DEADLOCK
```

**Fix 1: Use `ConfigureAwait(false)` in the library method:**
```csharp
async Task<string> GetDataAsync()
{
    var response = await httpClient.GetStringAsync(url).ConfigureAwait(false);
    // ↑ continuation runs on ANY pool thread, doesn't need UI thread → no deadlock
    return response;
}
```

**Fix 2 (better): Never call `.Result` or `.Wait()` — use `await` all the way up:**
```csharp
async void OnButtonClick(object sender, EventArgs e)  // async void OK for event handlers
{
    var data = await GetDataAsync();  // non-blocking — UI thread freed during IO
    label.Text = data;  // back on UI thread via SynchronizationContext
}
```

**Reason 2: Performance (minor)**

Posting to a `SynchronizationContext` has overhead (message pump dispatch, thread marshaling). In library code that doesn't need any specific thread, `ConfigureAwait(false)` avoids this cost. In hot paths, it adds up.

**Rule of thumb:**

| Code type | `ConfigureAwait(false)`? | Why |
|-----------|--------------------------|-----|
| **Library code** (NuGet packages, shared utilities) | **Always use it** | You don't know if the caller has a SyncContext. Prevents deadlocks if they call `.Result`. |
| **UI app code** (WPF/WinForms event handlers) | **Don't use it** | You need the UI thread to update controls after `await` |
| **ASP.NET Core** app code | **Doesn't matter** | No SyncContext exists, so there's nothing to capture either way |
| **Console app** code | **Doesn't matter** | Same — no SyncContext |

**ASP.NET Core made this simpler:** By removing `SynchronizationContext` entirely, the Core team eliminated the deadlock problem for web apps. You'll still see `ConfigureAwait(false)` in .NET libraries (they must be safe for ALL callers, including WPF), but in your ASP.NET Core controllers it's a no-op.

**Java comparison:**

Java doesn't have an exact equivalent because `CompletableFuture` doesn't capture a synchronization context. The closest parallel:

| .NET | Java |
|------|------|
| `await task` (captures SyncContext) | `thenApply()` — runs on completing thread or caller thread |
| `await task.ConfigureAwait(false)` | `thenApplyAsync(fn, executor)` — runs on specified executor |
| `SynchronizationContext` (UI thread posting) | `Platform.runLater()` (JavaFX) / `SwingUtilities.invokeLater()` |
| Deadlock from `.Result` on UI thread | Same deadlock if `future.get()` on EDT + `Platform.runLater()` in handler |

### 5.3 ValueTask vs Task

**The core difference:** `Task<T>` is a **class** (heap allocation every call). `ValueTask<T>` is a **struct** (zero allocation when completing synchronously).

```csharp
// Task<T> — allocates a Task object on the heap EVERY call, even for cache hits
async Task<int> GetValueAsync(string key)
{
    if (_cache.TryGetValue(key, out var val))
        return val;  // compiler creates: Task.FromResult(val) → heap allocation!
    return await FetchFromDbAsync(key);
}

// ValueTask<T> — NO allocation when completing synchronously
async ValueTask<int> GetValueAsync(string key)
{
    if (_cache.TryGetValue(key, out var val))
        return val;  // returns a ValueTask struct on the stack → zero allocation
    return await FetchFromDbAsync(key);
}
```

**What's actually happening under the hood:**

```
Task<int> version (synchronous path):
  1. Cache hit → result is 42
  2. Compiler generates: return Task.FromResult(42)
  3. Task.FromResult allocates a Task<int> object on the heap (72 bytes on x64)
  4. Caller awaits → immediately gets 42 → Task<int> becomes garbage → GC pressure

ValueTask<int> version (synchronous path):
  1. Cache hit → result is 42
  2. Compiler generates: return new ValueTask<int>(42)
  3. ValueTask is a struct → lives on the stack → NO heap allocation
  4. Caller awaits → immediately gets 42 → struct disappears when stack frame pops → no GC

ValueTask<int> version (async path — cache miss):
  1. Cache miss → must call FetchFromDbAsync(key)
  2. Compiler wraps the underlying Task inside the ValueTask struct
  3. Same as Task<T> — one heap allocation for the async state machine
  4. No savings on the async path — ValueTask only helps the sync path
```

**Practical impact — when does it matter?**

| Scenario | Sync completion % | Calls/sec | Task<T> allocs/sec | ValueTask<T> allocs/sec | Savings |
|----------|-------------------|-----------|---------------------|--------------------------|---------|
| Cache lookup (90% hit rate) | 90% | 100,000 | 100,000 | 10,000 | **90% fewer allocations** |
| DB query (always async) | 0% | 1,000 | 1,000 | 1,000 | **Zero savings** |
| Buffered stream read | 95% | 500,000 | 500,000 | 25,000 | **95% fewer allocations** |
| HTTP call (always async) | 0% | 100 | 100 | 100 | **Zero savings** |

**Rule: ValueTask only saves allocations when the method frequently completes synchronously.** If it's always async (HTTP calls, DB queries without caching), use `Task<T>` — it's simpler and has fewer restrictions.

**Real-world examples in .NET:**

```csharp
// EF Core — FindAsync returns ValueTask because entity might be in ChangeTracker (sync path)
ValueTask<User?> user = dbContext.Users.FindAsync(userId);
// Cache hit (tracked entity) → sync → zero allocation
// Cache miss (DB query) → async → normal allocation

// System.IO.Pipelines — ReadAsync returns ValueTask (buffered data = sync)
ValueTask<ReadResult> result = pipeReader.ReadAsync();
// Data already buffered → sync → zero allocation
// Must wait for network → async → normal

// IAsyncEnumerable — MoveNextAsync returns ValueTask<bool>
await foreach (var item in asyncStream)  // MoveNextAsync() called per item
// Buffered items → sync → zero allocation per iteration
// This is why IAsyncEnumerable uses ValueTask — thousands of iterations, most sync
```

**ValueTask restrictions (the tricky part):**

```csharp
// ✅ OK — await once
var result = await GetValueAsync(key);

// ❌ WRONG — await multiple times
var vt = GetValueAsync(key);
var r1 = await vt;
var r2 = await vt;  // UNDEFINED BEHAVIOR — may throw, return wrong value, or corrupt state

// ❌ WRONG — concurrent await
var vt = GetValueAsync(key);
var t1 = Task.Run(async () => await vt);
var t2 = Task.Run(async () => await vt);  // data race!

// ❌ WRONG — use .Result/.GetAwaiter().GetResult() before completion
var vt = GetValueAsync(key);
var r = vt.Result;  // only safe if vt.IsCompleted == true

// ✅ OK — if you need Task behavior, convert first
var vt = GetValueAsync(key);
var task = vt.AsTask();  // now it's a regular Task — await multiple times, cache, etc.
// But this allocates a Task, defeating the purpose!
```

**Why these restrictions?** `ValueTask<T>` can be backed by a pooled `IValueTaskSource` object that gets **recycled** after the first await. If you await again, the source may already be reused by another operation → you get someone else's result or an exception. `Task<T>` is a regular heap object that stays alive as long as you hold a reference.

**Decision flowchart:**

```
Should I use ValueTask<T>?

  Does the method frequently complete synchronously?
  ├── No → use Task<T> (simpler, no restrictions)
  └── Yes
      └── Is it a hot path (called thousands of times per second)?
          ├── No → use Task<T> (allocation cost is negligible)
          └── Yes
              └── Can you guarantee single-await consumption?
                  ├── No → use Task<T> (you need multi-await / caching)
                  └── Yes → use ValueTask<T> ✓
```

**Java comparison:**

Java doesn't have a direct equivalent. `CompletableFuture<T>` always allocates (like `Task<T>`). The closest concept is returning a raw value when synchronous and wrapping in `CompletableFuture.completedFuture()` as an optimization, but Java has no struct-based future type.

| .NET | Java |
|------|------|
| `Task<T>` (class, heap) | `CompletableFuture<T>` (class, heap) |
| `ValueTask<T>` (struct, stack when sync) | No equivalent — always heap-allocated |
| `IValueTaskSource<T>` (pooled backing) | No equivalent |
| `Task.FromResult(42)` (cached for small ints) | `CompletableFuture.completedFuture(42)` (still allocates) |

### 5.4 Common Pitfalls: Deadlocks & Sync-over-Async

**Deadlock pattern (UI/ASP.NET with SynchronizationContext):**

```csharp
// DEADLOCK in WPF/WinForms/ASP.NET (NOT ASP.NET Core)
public string GetData()
{
    return GetDataAsync().Result;  // blocks the UI thread
}

async Task<string> GetDataAsync()
{
    var data = await httpClient.GetStringAsync(url);
    // ↑ continuation wants to return to UI thread
    // ↑ but UI thread is blocked on .Result above
    // → DEADLOCK
    return data;
}
```

```
┌──────────────────────────────────────────────┐
│  Deadlock: Sync-over-Async with SyncContext  │
│                                              │
│  UI Thread: GetData() ──► .Result            │
│      │         ↑                             │
│      │         │ wants to resume here        │
│      ▼         │ (SynchronizationContext)     │
│  GetDataAsync() ──► await GetString ──►      │
│                        I/O completes         │
│                        continuation queued    │
│                        to UI thread           │
│                        BUT UI thread is       │
│                        blocked on .Result!    │
│                                              │
│             ══════ DEADLOCK ══════           │
└──────────────────────────────────────────────┘
```

**Solutions:**
```csharp
// 1. Use async all the way (best)
public async Task<string> GetDataAsync() => await httpClient.GetStringAsync(url);

// 2. ConfigureAwait(false) in the async method
async Task<string> GetDataAsync()
{
    return await httpClient.GetStringAsync(url).ConfigureAwait(false);
    // continuation doesn't need original SynchronizationContext
}

// 3. Use Task.Run to escape the SynchronizationContext (last resort)
public string GetData()
{
    return Task.Run(() => GetDataAsync()).Result;
    // Task.Run runs on ThreadPool (no SynchronizationContext)
}
```

**ThreadPool Starvation pattern (from our EF Core migration):**
```csharp
// DANGEROUS — blocks N ThreadPool threads simultaneously
Parallel.ForEach(datacenters, dc =>
{
    Parallel.Invoke(
        () => loader1.ExecuteAsync(dc).Wait(),  // blocks thread
        () => loader2.ExecuteAsync(dc).Wait(),  // blocks thread
        () => loader3.ExecuteAsync(dc).Wait()); // blocks thread
});
// With 50 datacenters × 7 loaders = 350 blocked threads
// ThreadPool can't keep up → starvation → effective deadlock

// BETTER — single blocking point per datacenter
Parallel.ForEach(datacenters, dc =>
{
    Task.WhenAll(
        loader1.ExecuteAsync(dc),
        loader2.ExecuteAsync(dc),
        loader3.ExecuteAsync(dc)
    ).Wait();  // only 1 thread blocked per datacenter
});
```

---

## 6. Cancellation & Timeout

.NET uses `CancellationToken` — a cooperative cancellation model (equivalent to Java's `Thread.interrupt()` but more flexible).

```csharp
// See: examples/14_Cancellation.cs

var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(30));  // auto-cancel after timeout

async Task DoWorkAsync(CancellationToken token)
{
    for (int i = 0; i < 1000; i++)
    {
        token.ThrowIfCancellationRequested();  // check for cancellation
        await Task.Delay(100, token);           // pass token to async APIs
    }
}

try
{
    await DoWorkAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation was canceled");
}

// Linked tokens — cancel if ANY parent cancels
var cts1 = new CancellationTokenSource();
var cts2 = new CancellationTokenSource();
var linked = CancellationTokenSource.CreateLinkedTokenSource(cts1.Token, cts2.Token);
```

---

## 7. Dataflow & Channels

### System.Threading.Channels

High-performance producer-consumer — equivalent to Java's `BlockingQueue` but with async support.

```csharp
// See: examples/15_Channels.cs

// Bounded channel — backpressure when full
var channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(100)
{
    FullMode = BoundedChannelFullMode.Wait  // block producer when full
});

// Producer
async Task ProduceAsync(ChannelWriter<WorkItem> writer)
{
    for (int i = 0; i < 1000; i++)
    {
        await writer.WriteAsync(new WorkItem(i));  // async backpressure
    }
    writer.Complete();
}

// Consumer
async Task ConsumeAsync(ChannelReader<WorkItem> reader)
{
    await foreach (var item in reader.ReadAllAsync())  // IAsyncEnumerable
    {
        await ProcessAsync(item);
    }
}

// Wire up
_ = ProduceAsync(channel.Writer);
await ConsumeAsync(channel.Reader);
```

**Pipeline pattern:**
```csharp
// Stage 1 → Stage 2 → Stage 3 (like Unix pipes)
var stage1to2 = Channel.CreateBounded<RawData>(100);
var stage2to3 = Channel.CreateBounded<ProcessedData>(100);

var pipeline = Task.WhenAll(
    ProduceRawData(stage1to2.Writer),
    TransformData(stage1to2.Reader, stage2to3.Writer),
    ConsumeResults(stage2to3.Reader));
await pipeline;
```

---

## 8. Comparison: .NET vs Java Concurrency

| Concept | .NET | Java |
|---------|------|------|
| **Thread** | `System.Threading.Thread` | `java.lang.Thread` |
| **Thread pool** | `ThreadPool` (global, auto-tuned) | `ExecutorService` (explicit creation) |
| **Lock** | `lock` / `Monitor` | `synchronized` / `ReentrantLock` |
| **Atomic ops** | `Interlocked` (on raw fields) | `AtomicInteger/Long` (wrapper objects) |
| **Read-write lock** | `ReaderWriterLockSlim` | `ReadWriteLock` |
| **Semaphore** | `SemaphoreSlim` (+ async) | `Semaphore` (sync only) |
| **Future** | `Task<T>` | `CompletableFuture<T>` |
| **Async I/O** | `async/await` (compiler state machine) | Virtual Threads (Java 21) / `CompletableFuture` |
| **Parallel loops** | `Parallel.ForEach` | `parallelStream()` |
| **Concurrent map** | `ConcurrentDictionary` | `ConcurrentHashMap` |
| **Producer-consumer** | `Channel<T>` / `BlockingCollection` | `BlockingQueue` |
| **Cancellation** | `CancellationToken` (cooperative) | `Thread.interrupt()` / `Future.cancel()` |
| **Memory model** | CLR memory model (stronger on x86) | Java Memory Model (JMM) |
| **Fork/Join** | `Parallel.Invoke` + work-stealing TaskScheduler | `ForkJoinPool` |

### Key Architectural Difference

**Java 21+ approach:** Virtual threads — M:N threading model where millions of lightweight threads are multiplexed onto a small number of OS threads. Blocking I/O is cheap because the virtual thread is "unmounted" from the OS thread.

**.NET approach:** `async/await` — compiler transforms async methods into state machines that voluntarily yield at `await` points. No new threading model needed — the existing ThreadPool handles continuations. This gives similar scalability without language-level virtual threads.

```
Java Virtual Threads:                    .NET async/await:
┌─────────────────────┐                  ┌─────────────────────┐
│ Virtual Thread 1    │                  │ async Method1()     │
│ Virtual Thread 2    │                  │ async Method2()     │
│ Virtual Thread 3    │ ← M:N mapping   │ async Method3()     │ ← state machines
│ ...                 │                  │ ...                 │
│ Virtual Thread 10K  │                  │ async Method10K()   │
├─────────────────────┤                  ├─────────────────────┤
│ OS Thread 1         │                  │ ThreadPool Thread 1 │
│ OS Thread 2         │ ← carrier       │ ThreadPool Thread 2 │ ← continuation
│ OS Thread N         │    threads       │ ThreadPool Thread N │    threads
└─────────────────────┘                  └─────────────────────┘
```

Both achieve the same goal: **don't waste OS threads on I/O waits**.
