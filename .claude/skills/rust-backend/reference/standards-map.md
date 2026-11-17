# Rust Backend skill reference map

The root `SKILL.md` contains the execution workflow. Use the least number of additional documents needed for the requested change. When present, read the matching sections of `docs/rust_backend_design_and_development_standards.md` as the comprehensive standard.

| User request / code touchpoint | Read these references | Comprehensive standard |
| --- | --- | --- |
| New service, architecture, module restructure, Rust cleanup | `architecture-and-rust.md` | §1, §2, §3, §12 |
| Endpoint, handler, JSON API, OpenAPI, validation, webhook | `api-and-validation.md`, often `security-and-multitenancy.md` | §4, §6 |
| PostgreSQL, SQLx, Diesel, migration, index, Supabase | `database-and-supabase.md` | §5, §7 |
| Login, tokens, JWKS, roles, permissions, tenants, file upload | `security-and-multitenancy.md` | §6, §7 |
| Tokio, concurrency, queues, cache, profiling, throughput | `async-and-performance.md` | §8, §9 |
| Test coverage, bugfix regression, CI gates, full deep scan | `testing-and-verification.md` | §10, §11, §15 |
| Docker, configuration, tracing, logging, health, deployment | `operations-and-observability.md` | §9, §11, §13 |
| Production readiness across all areas | Relevant references only, then all verification chapters | §14, §15, §16 |

## First inspect repository reality

Locate `Cargo.toml` / `Cargo.lock`, `rust-toolchain.toml`, `.cargo/config.toml`, crate graph, application entrypoints, router, persistence adapters, migrations, tests, CI, container manifests, environment variable templates, and `CLAUDE.md`.

Choose the established framework and database style unless a real problem justifies changing them. Do not add dependencies, new layers, or new infrastructure automatically just because this map mentions them. For non-PostgreSQL projects, apply equivalent principles rather than copying SQLx/Postgres instructions.

## Sources for checking syntax and changed APIs

- Rust: https://doc.rust-lang.org/book/ and https://doc.rust-lang.org/cargo/
- Tokio: https://tokio.rs/tokio/tutorial
- Axum: https://docs.rs/axum/latest/axum/
- SQLx: https://docs.rs/sqlx/latest/sqlx/
- Supabase: https://supabase.com/docs
- OWASP ASVS: https://owasp.org/www-project-application-security-verification-standard/
- RustSec Advisory Database: https://rustsec.org/

Validate syntax against the version locked in the repository. Do not treat latest documentation as proof an older crate version supports a feature.
