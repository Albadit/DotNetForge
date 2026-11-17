# Security, identity, authorization, and multitenancy

## Model the trust boundary

For each operation enumerate: principal identity, credential issuer, endpoint audience, tenant context, requested object, permitted action, cross-tenant relationships, privileged service identity, and how the server checks these facts. Map data flows across browser, backend, queue, provider, DB, and storage.

## Authentication

- Use mature authentication providers and cryptographic libraries. Never invent token formats, signing algorithms, or password hashing constructions.
- For JWTs, verify the signature against trusted issuer keys and enforce expected algorithm allowlist, issuer, audience, expiration, not-before, and token purpose; handle JWKS rotation and cache outages safely. Reject `alg=none`, unrecognized keys, and untrusted claim sources.
- For Supabase Auth JWTs, validate per official signing-key/JWKS guidance; do not merely base64-decode a token and trust `sub`, `role`, or `app_metadata`.
- Restrict privileged operations and API keys; treat user tokens differently from machine credentials.
- For cookie sessions, set `Secure`, `HttpOnly`, suitable `SameSite`, scoped domain/path, rotation/invalidation, and CSRF protections for unsafe methods. Handle XSS, OAuth redirect validation, login abuse, and session fixation.
- Use established password hashing (`Argon2id`, for example) only if the system really owns passwords. Prefer the external identity provider where already integrated.

## Authorization and tenant isolation

- Authenticate, then authorize the specific action on the specific resource. Implement deny by default.
- Never let a request choose its own trusted `user_id`, `role`, `is_admin`, `organization_id`, or `tenant_id` without validating the relationship against server-held state.
- Protect read, list, count, search, export, update, delete, subscriptions, background jobs, and file paths alike. Cross-tenant leaks occur in list endpoints and aggregates as well as detail routes.
- Prefer explicit membership and scoped query predicates; centralize authorization rules in tested application services when appropriate. Do not assume a generic middleware role check proves ownership of every record.
- Distinguish permissions attached to the requesting user from the backend's high-privilege service identity. A privileged service cannot safely substitute for a user's proof of access.
- For RLS-based designs, validate grants/policies and the effective DB role, including direct Rust connections and connection-pool contamination concerns. See `database-and-supabase.md`.
- Filter cache keys and background work by tenant and permission scope; invalidate safely when access changes.

## API and infrastructure hardening

- Limit request bodies, file sizes/types, parameter cardinality, concurrency, and per-principal rate. Validate path traversal and prohibit arbitrary filesystem or shell execution from request fields.
- Use parameterized SQL; prevent SSRF (including redirect and DNS rebinding concerns), unsafe file metadata, malicious archives, decompression bombs, and dangerous deserialization.
- Enforce transport security and sensible security headers. Harden CORS; never treat it as authentication. Ensure reverse proxy trust settings and forwarded-IP parsing cannot be spoofed.
- Store secrets in managed secret stores/environment controls; never print full config/env vars. Rotate and revoke leaked credentials. Ensure production backups and logs are protected too.
- Apply least privilege to DB roles, object storage, service credentials, CI and containers. No anonymous admin endpoints or public internal metrics.
- Track dependencies and advisories; review unsafe code, native libraries, feature flags, and container provenance.

## Mandatory negative tests

Unauthorized user, wrong role, wrong tenant, deleted membership, stale token/role, invalid/expired JWT, modified object ID, password reset/session revocation, privileged service use, duplicate/replay request, and concurrent membership change where relevant.

Use OWASP ASVS / API Security Top 10 as review guidance, calibrated to the application's actual threats. Do not claim a formal audit from static code inspection alone.
