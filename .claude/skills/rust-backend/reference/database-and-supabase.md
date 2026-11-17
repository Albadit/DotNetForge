# PostgreSQL, SQLx, schema safety, and Supabase

## Data model and SQLx

- Identify the actual persistence path: direct SQLx/Diesel pool, Supabase REST/PostgREST with caller JWT, Supabase admin client, RPC, or mixed access. Their authorization behaviors differ.
- Use parameter binding for data values and allowlisted identifiers for dynamic SQL. Avoid string concatenation with user-controlled identifiers or clauses.
- Enforce real invariants with database constraints: foreign keys, unique indexes, checks, not-null and appropriate transaction isolation. Handle uniqueness/serialization errors as intentional domain outcomes where appropriate.
- Keep queries explicit and observable. Select only needed fields. Avoid N+1 patterns, `SELECT *` on large tables, and unbounded result sets. Use `EXPLAIN (ANALYZE, BUFFERS)` only on safe/non-production workloads or with production-approved procedures.
- Prefer SQLx checked macros for static SQL when supported. Keep `.sqlx` prepared metadata synchronized if using offline compilation and verify with `cargo sqlx prepare --check`.
- Size connection pools against real Postgres/provider connection limits across all replicas and background workers. Avoid long transactions and hold time-sensitive locks as briefly as possible.

## Safe migrations

- Never rewrite an applied migration. Create a new numbered/timestamped migration and include fresh-schema snapshot changes if the repository maintains them.
- Review table cardinality, rewrite/lock risk, index build strategy, type changes, backfill cost, constraints, triggers, replication, and application compatibility.
- Prefer expand/contract for breaking schema updates: add nullable column/new schema, backfill in bounded batches, release code compatible with both, migrate reads/writes, enforce constraints, remove old schema only after compatibility window.
- Use transactions where supported. `CREATE INDEX CONCURRENTLY` cannot run inside a transaction block; use migration tooling/configuration appropriate to nontransactional changes and plan for partial failures. Do not claim every migration must be one transaction.
- Test migration from a realistic previous schema, clean bootstrap, and rollback/recovery plan on disposable databases.
- Never automatically apply destructive migrations to production during local development, testing, or review.

## Supabase and RLS: essential distinctions

- PostgREST/REST requests with a verified user access token are mapped to database roles/claims; grants and RLS policies can protect the rows returned by those APIs.
- A normal direct PostgreSQL connection from Rust does **not** automatically become the Supabase Auth user. A powerful/owner/BYPASSRLS pool can bypass policies. A `service_role` Supabase API credential also bypasses RLS for admin requests. Do **not** call `.execute(&pool)` and assume user RLS applies.
- Choose a defensible architecture: (A) authorize every operation server-side in Rust and enforce scoped SQL, with limited DB grants; or (B) deliberately implement transaction-local effective role/verified claims in Postgres and test `SET LOCAL`/session reset/pooling/revocation behavior end-to-end. Do not invent session claims or trust caller-provided role strings.
- For tables exposed through Supabase Data API, review both `GRANT` privileges and RLS policies for `anon`/`authenticated`; adding an RLS policy does not replace grants. Protect views/functions/storage policies as well.
- Use `USING` for visible/modifiable existing rows and `WITH CHECK` for permitted new values where applicable. Verify `INSERT`, `SELECT`, `UPDATE`, and `DELETE` independently.
- `SECURITY DEFINER` functions require an explicit safe `search_path`, schema-qualified identifiers, least privilege, restricted `EXECUTE`, and an audit of owner/BYPASSRLS effects.
- Do not expose `service_role`/secret keys to browsers, logs, test fixtures, or third-party clients. If an administrative task needs elevated access, isolate and tightly authorize it.
- Test policies using actual effective database roles/claims or actual PostgREST authenticated requests, not only postgres/service-role fixtures. Exercise same-tenant, cross-tenant and no-membership cases.

## Query tuning

Benchmark realistic row counts and representative distribution. Check indexes on joins, constraints, ownership filters, sort/pagination keys, and RLS predicates. Do not add indexes to every column blindly; measure write amplification and maintenance burden. Monitor slow queries, pool waits, lock waits, and replica lag.

## Official resources

- https://docs.rs/sqlx/latest/sqlx/
- https://supabase.com/docs/guides/database/postgres/row-level-security
- https://supabase.com/docs/guides/api/securing-your-api
- https://supabase.com/docs/guides/auth/jwts
