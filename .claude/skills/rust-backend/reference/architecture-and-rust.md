# Architecture and idiomatic Rust

## Understand before changing

Inspect the Cargo workspace and package graph; entrypoints (`main.rs`/`lib.rs`), modules, features, crate versions, edition, MSRV, core business data flows, current ownership conventions, error boundaries, and test organization. Locate the existing feature closest to the requested work and follow its successful conventions. Preserve stable behavior.

## Default shape — a modular monolith

For an ordinary Axum service, start with feature modules rather than creating a separate crate per domain object:

```text
src/
  main.rs                 # thin bootstrapping, start server
  lib.rs                  # composition, testable app factory
  config.rs               # typed configuration
  app.rs                  # router, application state
  error.rs                # unified public error mapping
  features/
    accounts/
      mod.rs
      routes.rs           # HTTP boundary / extractors
      dto.rs              # request and response contracts
      service.rs          # business behavior and authorization
      repository.rs       # data access and transactions
  infrastructure/
    database.rs
    auth.rs
    telemetry.rs
migrations/
tests/
```

This is a **starting point, not a forced rewrite**. Small services may use fewer files; a real multi-service system may use multiple crates or bounded contexts. Avoid circular dependencies, dumping all logic into handlers, and ubiquitous `utils.rs`/`common.rs` buckets.

## Rust design rules

- Prefer types that make invalid states difficult to express: `Option<T>`, domain newtypes, enums, validated constructors, and narrowly scoped DTOs.
- Prefer borrowing when simple; return owned values at clear boundaries. Avoid unnecessary `.clone()`, `Arc`, interior mutability, and fighting the borrow checker with leaked allocations.
- Keep error categories explicit. `thiserror` is useful for typed domain/infrastructure errors; `anyhow` may be appropriate at application/bootstrap boundaries. Do not turn every error into `String`.
- Prefer `Result<T, E>` for expected failures. Reserve panics for broken internal invariants and tests, never for invalid HTTP input or unavailable downstream services.
- Use explicit `match`/`?` and deliberate conversion. Do not use `unwrap()`/`expect()` in live request paths without a justified, truly immutable invariant.
- Use traits for actual substitution/testing boundaries or polymorphic behavior; avoid generic repository/service traits created preemptively for every struct.
- Keep shared `AppState` small and clone-cheap (e.g. pools and handle types) and avoid a single globally locked mutable store.
- Distinguish entities, database rows, HTTP DTOs, and external service types when their constraints or lifecycles differ. Do not duplicate identical types without reason.
- Keep feature flags additive or explicitly documented as exclusive. Test supported combinations.
- Avoid `unsafe` unless a measured requirement cannot be satisfied with safe Rust; document invariants and require focused review/tests.

## Changes and reviews

For any architectural refactor, articulate the old dependency direction, new boundary, effect on consumers, migration risk, testing strategy, and measurable benefit. Preserve API and data compatibility unless an intentional versioned break is required.

## Typical failure smells

Database calls directly in each handler; tenant IDs blindly copied from JSON; `tokio::spawn` on every request without join/cancellation; unbounded `Vec`/channel growth; one mutex around the entire database access path; duplicated auth extraction; generic error 500 for user errors; expensive startup code performed for every request; giant shared module; non-testable router bootstrap.
