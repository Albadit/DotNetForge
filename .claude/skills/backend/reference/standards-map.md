# Backend standards selection

When `docs/backend_design_and_development_standards.md` exists, read relevant sections and obey project-specific patterns in `CLAUDE.md`.

| Task | Focus |
| --- | --- |
| New route / handler | Trust boundaries, auth, validation, response contracts, error mapping |
| Database schema | Constraints, grants/RLS, migrations, indexing, live safety |
| Webhook / external call | Signature checks, replay resistance, idempotency, retries, timeouts |
| Auth / membership / tenant work | Identity proof, server-side authorization, negative tests |
| Background worker | Delivery semantics, retry budgets, observability, job ownership |
| Refactor | Stable contracts, compatibility, focused tests, rollout risk |
| Production issue | Evidence, blast radius, non-destructive reproduction, fix and verification |

For Rust-specific tasks, read `.claude/skills/rust-backend/SKILL.md` and its `reference/standards-map.md` for detailed rules. Avoid loading all reference files unnecessarily.
