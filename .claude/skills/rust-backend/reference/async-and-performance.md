# Tokio, concurrency, performance, and resource controls

## Async correctness

- Inspect the runtime (`tokio`, `async-std`, etc.) and do not impose Tokio-specific APIs on other runtimes.
- Do not run synchronous filesystem I/O, expensive CPU work, synchronous HTTP clients, or blocking locks on async executor threads. Use async alternatives or bounded `spawn_blocking` only when appropriate.
- Avoid holding `std::sync::Mutex` guards or database transactions across `.await` unnecessarily. Choose task-safe lock patterns based on actual use.
- Treat cancellation as normal: request timeouts and dropped futures must not leave inconsistent state. Coordinate durable state changes, outbox records, and side effects transactionally when needed.
- `tokio::spawn` is not a durability mechanism. Track spawned tasks, propagate failure/cancellation, impose concurrency limits, and decide how shutdown drains or abandons work.
- Use bounded channels and backpressure. Ensure producers cannot exhaust memory or starve executor tasks.
- Define connect, read, write, and overall deadlines; use retry budgets, jitter, and circuit-breaker patterns only for retryable downstream failures.
- Consider sync vs async traits, `Send` constraints, blocking adaptors, stream sizes, and ownership when designing reusable layers.

## Optimize by evidence

Measure realistic request distributions and data volumes before changing architecture. Capture p50/p95/p99 latency, throughput, request/error rate, resource use, database pool waits, query duration, queue depth, CPU flamegraphs, allocator/memory data, and saturation indicators.

Optimization order: correct algorithm and query plan; avoid duplicate work; bound fan-out; reduce allocations/clones only when measured; optimize serialization; apply narrowly scoped caching; adjust pools/worker counts based on saturation; reevaluate under representative concurrency.

## Cache correctness

Define cache key tenancy/auth scope, TTL, invalidation strategy, stale-data tolerance, stampede protection, error behavior, and maximum memory. Never cache private responses under a key shared across users or tenants. Do not allow an anonymous request to warm a privileged response cache.

## Benchmarks and load tests

- Benchmark representative operations with Criterion or equivalent; use meaningful distributions, warm-up, repeatability, and confidence intervals.
- Run controlled load tests only in authorized disposable/staging environments. Never direct them at unknown or production URLs.
- Compare before/after at the same load and environment. A single low-load latency number cannot prove scalability.
- Stress test cancellation, slow clients, pool saturation, queue overflow, upstream failures, and graceful shutdown in addition to happy-path throughput.

## Service level controls

Set explicit ceilings for HTTP body, response buffering, query result rows, upload bytes, task concurrency, queue capacity, connection pool counts, and external retries. Exceeding a limit should result in a deliberate error with telemetry—not process collapse.
