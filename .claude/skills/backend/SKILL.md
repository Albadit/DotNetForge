---
name: backend
description: >-
  Plans, builds, changes, and verifies server-side code and databases across
  backend languages and frameworks. Use when creating or debugging APIs,
  routes, server actions, webhooks, authentication/authorization, SQL schemas,
  migrations, RLS, database functions, and backend integrations. For work
  primarily involving Rust, Axum, Tokio, SQLx, PostgreSQL through Rust, or
  Rust services, also use the rust-backend skill for language-specific rules.
---

# Backend Engineering

The backend is where identity, permissions, business invariants, and data consistency are enforced. Deliver the smallest safe, well-tested change that respects the repository's architecture.

## Project standards

Read `CLAUDE.md` and applicable `docs/backend_design_and_development_standards.md` sections if they exist. Use [reference/standards-map.md](reference/standards-map.md) to locate applicable subjects. If the task concerns Rust, use the sibling **rust-backend** skill's `SKILL.md` and focused references as the Rust-specific authority; do not apply TypeScript/ESLint conventions to Rust.

## Workflow

1. **Identify the entry point and caller:** browser, service, webhook, scheduled worker; determine authentication and authorizations expected.
2. **Enforce the actual boundary:** server-side permission checks and/or correctly configured database policies, including negative and cross-tenant cases. Never rely on client-supplied roles or tenant IDs.
3. **Validate input:** types, bounds, formats, payload size, unknown writable fields; return consistent public error shapes without internal secrets.
4. **Respect data integrity:** constraints, transactional updates, idempotency for retryable events, safe locking and concurrency, and versioned contracts.
5. **Migrate safely:** never mutate applied migrations; understand production table sizes, locks, backfills, index operations, and rollback. Update fresh-schema snapshots when applicable.
6. **Protect integrations:** timeouts, bounded retries with jitter, idempotency keys, signature verification for incoming webhooks, secure secret storage, observability.
7. **Prove the rule:** authorized and unauthorized callers, cross-tenant access, invalid input, duplicates, error paths, persistence checks under real roles when needed.
8. **Validate the final state:** repository-native formatting/linting/typechecks, compilation, tests, database checks, and controlled smoke tests. Record actual results and unresolved blockers.

Never run destructive operations or test suites against an unknown or live database. Treat unidentified connection strings as production.

## Collaboration boundaries

Security audits: `security`; performance profiling: `performance`; operational recovery: `reliability` / `operations`; privacy and retention: `compliance`; full test-harness construction: `testing`. Keep local safeguards in place regardless of other skills.
