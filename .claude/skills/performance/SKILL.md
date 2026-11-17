---
name: performance
description: >-
  Measures and improves speed and resource use: slow pages and Core Web Vitals, bundle size, slow queries, missing
  or excess indexes, N+1 and round trips, caching and invalidation, concurrency, memory, rate limits and load
  tests. Use for "optimize the database", "improve website performance", "this page is slow", scalability checks
  and any optimisation, always measuring before and after.
---

# Performance

Find bottlenecks, recommend and implement optimisations, and keep resource use efficient. Treat performance as a
system property defined by explicit SLOs, measured with distributed tracing and profiling, and validated with load
tests and quality evals. Never optimise without measuring first.

## Project standards

If the repository has `docs/website_design_and_development_standards.md` or `docs/backend_design_and_development_standards.md`,
read the sections on this area before starting (website standards section 8; backend standards section 8); `CLAUDE.md` says how this codebase applies them.

## Mode

This skill both analyses and implements. Analyse and report by default. When the request is to build, fix or
change something, also make the changes: fix Critical and High findings directly and list
Medium and Low ones in the report.

## What to cover

- Define and enforce **performance budgets** per request stage
- Identify **algorithmic complexity** issues (time and space)
- Detect **memory leaks** and excessive allocations via profiling
- Find **I/O bottlenecks** (database, network, disk, model calls, retrieval)
- Analyse **caching strategies**, invalidation patterns, and hit rates
- Assess **concurrency** and parallelism opportunities
- Analyze **database query performance** (N+1 queries, missing indexes, bad plans)
- Analyse **resource pooling** (connections, threads, connection storms)
- Evaluate **lazy loading** vs eager loading trade-offs
- Analyse **model-call latency** and token economics (input tokens, output tokens, prompt caching)
- Assess **streaming** performance (TTFT, TTLB, partial rendering)
- Evaluate **retry, rate-limit, and backpressure** strategies
- Analyse **overload behavior** (load shedding, circuit breaking, admission control)
- Validate performance with **load tests + quality evals** as a regression harness

## Workflow

Work through these stages. The questions and checks for each are in [reference/analysis-stages.md](reference/analysis-stages.md):
read all of it for a full-scope review, or only the stages that match a focused scope.

1. SLO Definition and Performance Budgets
2. Distributed Tracing and Observability Baseline
3. CPU and Memory Profiling
4. Algorithmic Complexity Analysis
5. Model-Call Latency and Token Economics (AI/LLM workloads only)
6. Database and Query Performance
7. Retrieval and Vector Search Performance (AI/RAG workloads only)
8. Concurrency, Batching, and Parallelization
9. Memory, Allocations, and GC Pressure
10. Caching Strategy and Invalidation
11. Rate Limiting, Backpressure, and Overload Protection
12. Benchmarks, Load Tests, and Quality Validation

## Reference

| File | Contents | Read |
|---|---|---|
| [reference/analysis-stages.md](reference/analysis-stages.md) | The analysis stages in full | Before analysing: all of it for a full-scope review, only the matching stages for a focused one |
| [reference/metrics-and-red-flags.md](reference/metrics-and-red-flags.md) | Latency, throughput, cost and quality metrics and stage budgets; red-flag table with impact and solution | Before Stage 1 (defining what "fast enough" means) and when classifying an issue |

## Output Format

For each finding, provide ALL of these fields:

```
### [SEVERITY] Performance Issue

- **Location**: File and line reference (or system component)
- **Type**: CPU / Memory / I/O / Network / Cost / Quality
- **Current Complexity**: O(n²) etc. (or "unknown - needs profiling")
- **Impact**: Measured or estimated impact on latency/throughput/cost (cite percentile)
- **Optimization**: Specific recommendation with implementation approach
- **Expected Improvement**: Estimated gain (with evidence basis - measured, benchmarked, or industry-typical)
- **Trade-offs**: What you give up (complexity, memory, correctness risk, etc.)
- **Verification**: How to confirm the optimization worked (specific metric, benchmark, or eval)
```

After all findings, ALWAYS provide these sections:

1. **Performance budget breakdown** - per-stage latency allocation and which stages are over-budget
2. **Optimization priority** - ordered by expected impact per engineering effort (highest-ROI first)
3. **Missing observability** - what instrumentation/profiling/tracing is missing
4. **Benchmark plan** - specific load tests, soak tests, and evals to run
5. **Quality guard** - which evals/tests must pass to validate optimizations didn't regress correctness

## Guidelines

- **Always measure before optimizing** - avoid premature optimization; identify the real bottleneck first
- Consider the **realistic data scale**, not just worst case or best case
- **Prefer algorithmic improvements over micro-optimizations** - O(n²) → O(n log n) beats any cache trick
- Account for caching at every layer (CPU, application, CDN, provider prompt cache)
- Consider the **cost of added complexity** vs performance gain - don't add complexity for 2% improvement
- **Recommend specific profiling tools** for the language/runtime in use
- Track **tail latency (p95/p99)** as the primary performance indicator - averages lie
- Always validate optimizations with **benchmarks AND quality evals** - "faster but wrong" is not an improvement
- Treat **retry and rate-limit behavior** (including SDK defaults) as part of the performance scope
- For AI workloads: **token count is money and latency** - always analyze input/output token usage
- Flag **retry storms** explicitly whenever retries exist at multiple layers
- For streaming workloads: TTFT is a separate SLO from TTLB - optimize both
- Never recommend caching without specifying the **invalidation strategy**
- **Overload protection is a performance feature** - load shedding and circuit breaking prevent cascading failure
- Include **memory leak detection** in soak test recommendations - leaks are performance regressions
- Connection pooling should be verified for correctness (pooling mode vs transaction semantics)
- Document performance-critical code with complexity notes and SLO context

## Follow-ups

After the report, offer these next steps in one line each; do not start them unprompted:

- Implement the optimisations recommended in the report.
- Analyse the architectural implications with the `architecture` skill.
