# HTTP APIs, contracts, validation and integrations

## Endpoint checklist

For each changed endpoint, record HTTP method/path, request and response DTO, auth requirement, tenant/resource scope, status codes, pagination, error codes, side effects, rate limits, and idempotency behavior. Cover static, dynamic, nested, authenticated, administrative, internal, webhook, streaming, and health routes as relevant.

## Implementation

- Prefer Axum/Tower patterns already used in the repository. Put shared application dependencies in `State`, not mutable global statics. Keep HTTP handlers thin and test business rules separately.
- Treat every HTTP body, query parameter, path parameter, header, uploaded file, and webhook payload as untrusted input. Bound maximum body size and nested structure; validate type, format, length, range, and domain invariants.
- Use distinct create/update/input DTOs. Do not deserialize arbitrary entity structs and write every field back to the database. Reject or explicitly ignore unknown write fields according to the API contract, with a security preference toward strict input for mutating endpoints.
- Enforce authentication and resource authorization before returning sensitive data or side effects. Validate ownership using database-backed permissions, verified claims, or both—not the browser's view of the user.
- Use consistent HTTP status semantics: 400/422 invalid requests by contract, 401 unauthenticated, 403 unauthorized, 404 missing (or deliberately hidden), 409 conflict, 429 rate limit, 5xx server/dependency failures. Choose 400 vs 422 consistently.
- Return stable machine-readable error `code`, human-readable `message`, and correlation/request ID where useful. Never include SQL traces, credentials, auth tokens, infrastructure addresses, or raw debug output.
- Specify pagination max page size and stable sort ordering; use keyset pagination for large mutable datasets where appropriate. Consider sparse fieldsets or projections to avoid excess data.
- Document versioning, deprecation, cursor behavior, time zones, decimal/money representation, nullability, and consistency expectations. Update OpenAPI/SDKs/contract tests alongside code.
- Use correct caching semantics and headers; avoid caching personalized data in shared caches; set `Vary` as needed.
- Validate CORS origins and credential modes explicitly. CORS is not authentication or CSRF protection.

## Webhooks and external integrations

- Verify signature against the exact raw body when required; validate freshness and prevent replay; never trust webhook JSON fields alone as proof of origin.
- Acknowledge only when required durable work has completed or been safely queued. Use deduplication/idempotency keys and durable state if the provider retries.
- Set connect/request timeouts and bounded retry budgets with exponential backoff and jitter for retryable operations. Do not retry non-idempotent operations unless the remote protocol safely supports it.
- Bound outgoing concurrency; protect against SSRF for user-configured URLs (schemes, DNS/IP ranges including redirects, allowlists where practical).
- Limit payload size, redirects, response size, and decompression ratios for external data.

## Verification matrix

For each route/operation, test: allowed caller, anonymous caller, forbidden role, foreign tenant, malformed and oversized input, nonexistent record, duplicate request, network timeout, database failure, and contract compatibility. Check response code/body and persistence/side effects; do not test status code only.
