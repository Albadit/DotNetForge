---
name: rust-backend
description: >-
  Designs, implements, refactors, debugs, and verifies production-grade Rust
  backend services and APIs. Use automatically for Rust/Axum/Actix Web/Poem,
  Tokio, SQLx, Diesel, PostgreSQL, Supabase, REST, webhooks, workers,
  authentication, authorization, multitenancy, migrations, async concurrency,
  observability, backend security, backend performance, and Rust backend tests.
  Use when adding routes, changing schemas, fixing server errors, reviewing
  Rust backend architecture, or preparing a Rust service for production.
---

# Rust Backend Engineering

Build trustworthy, maintainable, secure, fast, and observable Rust services. Every change must preserve the application's invariants, enforce permissions at the real trust boundary, and be verified with evidence. Favor simple, idiomatic Rust over speculative abstraction.

## Activate and scope

- Select this skill for work primarily involving Rust server-side code, infrastructure-facing Rust libraries, databases accessed by Rust, or backend integration tests. You may use it alongside the project's security, testing, performance, and operations skills when those topics are substantial.
- Do **not** invoke solely because the repository happens to contain a Cargo manifest if the requested work is unrelated to Rust backend behavior.
- For mixed frontend/backend tasks, apply this skill to Rust backend changes and the relevant frontend skill to browser/UI changes.
- Inspect `CLAUDE.md`, workspace manifests, readmes, existing code, deployment configuration, and local conventions before proposing structural changes. Project-specific requirements override examples in this skill, but do not override security or data-safety guardrails.
- If present, `docs/rust_backend_design_and_development_standards.md` is the authoritative engineering quality bar. Otherwise use this skill's references. Read only the relevant sections via [reference/standards-map.md](reference/standards-map.md).

## Default workflow

1. **Discover the system.** Identify Cargo workspace/crates, Rust edition/MSRV, runtime, HTTP framework, database access, auth provider, tenancy model, jobs, event consumers, API contracts, observability, CI, and environments. Build an endpoint/data-flow inventory for the touched feature. Baseline current tests, warnings, and failure modes where practical.
2. **Describe the trust boundary.** Determine caller identity, authentication source, requested resource, tenant ownership, granted actions, and downstream credentials. Identify which layer **actually enforces** authorization. Never trust client-supplied identity, role, or tenant IDs without server-side verification.
3. **Design the smallest safe change.** Write down behavior, invariants, expected failures, schema effects, backwards compatibility, test cases, and rollback strategy. Match existing idioms instead of gratuitously rewriting stable modules. Choose feature modules and well-defined interfaces, not a maze of generic layers.
4. **Implement idiomatic Rust.** Prefer explicit types, ownership clarity, `Result` error paths, narrow public interfaces, cheap shared state, and bounded asynchronous operations. Avoid `unwrap`/`expect` on untrusted runtime paths, leaking internal errors, global mutable state, blocking the Tokio executor, uncontrolled tasks, and needless `Arc<Mutex<...>>` usage.
5. **Secure API and data access.** Validate request size, type, shape, and semantics; reject unwanted writable fields. Authorize on every entry point. Use parameterized SQL, transactions for atomic invariants, and enforced tenant scoping. Translate internal errors to stable public error codes. Restrict privileged operations and redact secrets/PII.
6. **Migrate carefully.** Never edit applied migrations. Inspect schema drift and deployment order; prefer additive/expand-contract migrations for live systems. Assess locks, table sizes, index strategy, `NOT NULL`/backfills, constraints, and rollback. Use only local/disposable databases for integration runs or destructive commands unless explicit, authorized production procedures are in place.
7. **Test adversarially.** Cover success, invalid input, unauthenticated, forbidden, nonexistent, cross-tenant, duplicate/retry, concurrency, downstream timeout, and failure recovery. Test against real Postgres roles and policies when RLS is a protection boundary. Assert exact observable outcomes and data changes.
8. **Verify and report.** Format, lint, compile, test, validate migrations/contracts, inspect warnings, and run smoke tests against a disposable environment. Repeat after fixes. Report commands and real results; distinguish passed, failed, skipped, blocked, and not run.

## Mandatory Rust quality gates

Run the applicable checks from the repository root, using the existing workspace/feature matrix. Do not invent results:

```bash
cargo fmt --all -- --check
cargo clippy --workspace --all-targets -- -D warnings
cargo check --workspace
cargo test --workspace
cargo build --workspace --release
```

When the project supports them, additionally run `cargo audit`, `cargo deny check`, `cargo sqlx prepare --check`, integration/API tests, migration verification, and selected feature-matrix checks. Do not blindly use `--all-features` when features are mutually exclusive. Run the exact commands in CI or document why a gate is not applicable. `-D warnings` is a first-party code quality target; investigate upstream/dependency noise separately.

## Non-negotiable decisions

- **Permission first:** authenticate and authorize before sensitive reads/writes or side effects; test negative cases, not only successful requests.
- **RLS nuance:** Supabase/PostgREST user-scoped requests can enforce RLS. A privileged direct SQLx/PostgreSQL connection does **not** automatically adopt the caller's JWT/RLS context. Enforce authorization in Rust unless a carefully reviewed, transaction-scoped database-role/claim arrangement is explicitly implemented and tested. Never assume RLS protects a privileged pool.
- **Data integrity:** use unique constraints, foreign keys, check constraints and transaction isolation where appropriate, rather than assuming application validation prevents races.
- **Bounded work:** control request/body size, pool sizes, fan-out, queues, timeouts, retries, and cancellation. No infinite retry loops or unbounded spawned tasks.
- **No silent failures:** errors must be propagated, classified, logged safely, or intentionally handled. No empty error-swallowing branches.
- **No reckless writes:** never run destructive schema/data changes or tests against unknown or production connections. Treat unclear credentials as production.
- **Measure performance:** benchmark and profile before adding caching, extra threads, new indexes, or algorithmic complexity.
- **Preserve contracts:** coordinate changes with OpenAPI, generated clients, API consumers, migrations, and compatibility requirements.

## Read references selectively

Use [reference/standards-map.md](reference/standards-map.md) to pick only the relevant details. Key references:

- [Architecture and idiomatic Rust](reference/architecture-and-rust.md)
- [HTTP APIs, contracts, and validation](reference/api-and-validation.md)
- [PostgreSQL, SQLx, and Supabase](reference/database-and-supabase.md)
- [Security, identity, and multitenancy](reference/security-and-multitenancy.md)
- [Async runtime and performance](reference/async-and-performance.md)
- [Testing and quality gates](reference/testing-and-verification.md)
- [Observability, operations, and deployment](reference/operations-and-observability.md)

## Delivery format

For substantial changes, summarize **what changed**, **why**, **security/data-contract implications**, **tests and actual outcomes**, **measured performance if relevant**, **migration/deployment notes**, and **remaining risks/blockers**. If audit evidence is required, create `docs/rust-backend-final-quality-report.md` and record the endpoint matrix and reproducible checks. Never declare production-ready based only on compilation or a passing happy-path test.

## Related skill boundaries

Use a dedicated security skill for a formal threat model or penetration-test plan; performance for deep benchmarks; reliability for distributed failure semantics; operations for infrastructure and rollout; compliance for privacy/legal retention; testing for extensive test-system design. This skill still owns safe and correct Rust backend implementation and must apply all relevant safeguards even when those skills are not invoked.
