# Observability, production operations, and deployments

## Configuration

- Use typed, validated startup config. Fail early if critical settings are absent or malformed; redact all secrets on errors and `Debug` output.
- Distinguish local/staging/production; ensure test databases and endpoints cannot silently resolve to live systems.
- Keep environment-specific configuration outside committed source. Use least-privilege runtime credentials and document required variables without printing actual values.
- Centralize HTTP client/pool construction and timeouts; avoid re-creating clients and pools per request.

## Observability

- Use structured `tracing` spans and JSON logs when appropriate. Propagate request/correlation/trace IDs through HTTP, jobs and downstream calls.
- Log enough context to diagnose errors: stable event/error code, route name, operation, status, safe tenant ID or pseudonym where allowed, duration, retry count, request ID. Exclude JWTs, passwords, secret keys, raw PII, payment data and sensitive payloads.
- Add OpenTelemetry instrumentation when the environment supports it; do not introduce a full telemetry platform without a need.
- Monitor request rate/errors/latency, database queries and pools, slow external dependencies, queue lag, resource saturation, task failures and restart loops.
- Distinguish liveness (process can serve) from readiness (critical dependencies available). Never make basic liveness depend on a transient remote dependency.

## Reliability and deployment

- Make startup/shutdown intentional: signal handling, stop accepting traffic, drain bounded in-flight requests/tasks, close connections, release leases, and exit nonzero on irrecoverable boot failures.
- Design background jobs around durable queue/outbox, deduplication, visibility timeouts and leases where needed. In-memory tasks disappear on restart.
- Build reproducibly from committed lockfiles and supported toolchains; use appropriate minimal container images with non-root user and read-only filesystem when feasible.
- Include health/readiness, metrics access control, safe env templates, resource limits, backup/restoration validation, deployment runbook, and controlled rollback plan.
- Use expand/contract schema rollouts when old/new application versions overlap. Ensure migration order and changes are compatible with running instances.
- Never deploy automatically to production based on an unreviewed code-generation prompt or use production credentials for local verification.

## CI/CD gates

Run formatting, lint, compile, unit/integration and security/advisory checks, migration tests, contract tests and release build; use branch protections and least-privilege CI credentials. Treat failed/blocked gates as release blockers unless documented and explicitly accepted by an authorized owner.

## Operational evidence

For production readiness report current resource limits, meaningful SLOs, error and latency baselines, dependency requirements, data migration strategy, backup restore readiness, incident rollback path, and unverified assumptions. Report actual outcomes—not hypothetical dashboards.
