# API Reference (for AI agents)

The headless API is served under `/api`. Authenticate with a bearer token created on the admin **API Tokens** screen
(Super Admin / Admin):

```http
Authorization: Bearer dnf_<8 hex>_<secret>
```

Tokens are stored as salted PBKDF2 hashes; the plaintext is shown once. Requests are matched by prefix and verified
against the hash (successful verifications are cached for 10 minutes; revocation and expiry are checked on every
request). Missing/unknown/revoked/expired token → `401`; missing permission key → `403`; more than 300 requests per
minute from one IP → `429`. Before installation every `/api` call is redirected (`302`) to `/setup`.
`POST /api/content/pages` applies the same rules as the Content Manager (slug normalization, uniqueness, length
limits; `400 { "error": ... }`) and is audited.

## Endpoints

| Method | Route | Required permission |
| --- | --- | --- |
| GET | `/api/content/pages` | `content.read` |
| GET | `/api/content/{type}` | `content.read` (only `pages` returns data) |
| POST | `/api/content/pages` | `content.create` |
| GET | `/api/media` | `media.read` |
| GET | `/api/users` | `users.read` |
| GET | `/api/roles` | `roles.read` |
| GET | `/api/settings` | `settings.read` |
| GET | `/api/extensions` | `extensions.read` |
| GET | `/health` | none (not under `/api`) |

Responses are JSON, scoped to the token's tenant (`/api/settings` adds global rows; `/api/extensions` is global).
Response shapes, behaviour and conventions for new endpoints:
[headless-api.md](../../.docs/features/headless-api.md). Code: `src/DotNetForge.Api/Controllers/`.

## Permission keys

`core.read|manage`, `content.read|create|update|delete|publish`, `media.read|upload|update|delete`,
`users.read|create|update|delete`, `roles.read|create|update|delete`, `settings.read|update`,
`extensions.read|manage`, `webhooks.read|manage` (`DotNetForge.Shared.Constants.PermissionKeys`).
