# Tests and evidence-based final verification

## Test pyramid

- Unit: domain invariants, parsing, validation, permission predicates, deterministic transformations, error mapping.
- Integration: real router/HTTP boundaries, Postgres schema/migrations, transaction behavior, authentication, RLS effective roles, downstream integrations using controlled mocks/fakes where suitable.
- Contract: OpenAPI shape, consumer-visible status/field/nullability and backwards compatibility.
- End-to-end: critical flows across auth, writes, jobs, and external components in a disposable environment.
- Property/fuzz: parsers and invariant-sensitive transformations where the input space makes examples insufficient.
- Concurrency: duplicate requests, unique constraint races, concurrent membership changes, retries, cancellation and lost responses.
- Security: anonymous/forbidden/cross-tenant, malformed tokens, path traversal, SSRF boundaries, oversized input, replay or privilege escalation.

A test must demonstrate a rule that would fail if the protection/behavior were removed. Don't assert only that status is 200 while ignoring response data and persistence.

## Safe environments

Never run destructive tests, migrations, load tests, seed resets, or property/fuzz workloads against a production or unidentified connection. Explicitly identify the target; prefer ephemeral local Postgres/Testcontainers/Supabase local stack and deterministic fixtures. Keep tests parallel-isolated and clean up only resources they own.

## Applicable commands

```bash
cargo fmt --all -- --check
cargo clippy --workspace --all-targets -- -D warnings
cargo check --workspace
cargo test --workspace
cargo build --workspace --release
```

Add selectively:

```bash
cargo test --doc --workspace
cargo audit
cargo deny check
cargo sqlx prepare --check
cargo nextest run --workspace
```

The latter tools require installation/configuration; a missing tool is **not** a test pass. Adapt feature flags to the repo's declared matrix; only add `--all-features` when combinations are valid. `cargo test --doc --workspace` depends on the project's Cargo version/target support; use `cargo test --doc` package-by-package if needed. When CI has stricter targets, execute those too.

## Final backend endpoint audit

Build an inventory of every relevant HTTP route, background job, webhook, RPC, and data-mutation path from the router and task registrations. For each, record auth/role, happy-path, rejection-path, tenant isolation, validation, expected status, storage effects, and tested/not-tested state. For a deep-scan request, cover the whole inventory rather than just touched files.

Capture unexpected `4xx`/`5xx` outputs, panics, tracing errors, task failures, failed outbound calls, SQL exceptions, pool starvation, missing migration objects, deprecations, and compiler/clippy warnings. Distinguish expected 401/403 and validation failures from regressions. Re-test after fixes.

## Final report

Write `docs/rust-backend-final-quality-report.md` for requested deep audits or broad implementations. Include stack versions, baseline, endpoint matrix, migration state, auth/RLS matrix, commands executed with exit status, tests passed/failed/skipped, advisories, measurements, fixes, deploy/rollback notes, and remaining risks. Use `Not run`, `Blocked`, or `Not applicable` rather than fabricated successes.
