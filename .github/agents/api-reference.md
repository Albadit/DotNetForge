# API Reference (for AI agents)

The headless API is served under `/api`. Authenticate with a bearer token created on the admin **API Tokens** screen
(Super Admin / Admin / Developer):

```http
Authorization: Bearer dnf_<8 hex>_<secret>
```

Tokens are stored as salted PBKDF2 hashes; the plaintext is shown once. Requests are matched by prefix and verified
against the hash (successful verifications are cached for 10 minutes; revocation and expiry are checked on every
request). Missing/unknown/revoked/expired token → `401`; missing permission key → `403`; more than 300 requests per
minute from one IP → `429`. Before installation every `/api` call is redirected (`302`) to `/setup`.
`POST /api/content/pages` applies the same rules as the Pages screen (slug normalization, uniqueness, length
limits; `400 { "error": ... }`) and is audited.

## Endpoints

| Method | Route | Required permission |
| --- | --- | --- |
| GET | `/api/content/pages` | `content.read` |
| GET | `/api/content/{type}` | `content.read` (only `pages` returns data) |
| POST | `/api/content/pages` | `content.create` |
| GET | `/api/content-types`, `/api/content-types/{type}` | `schema.read` |
| GET | `/api/entries/{type}` (`q`, `filter[field][op]=`, `sort=-field`, `page`, `pageSize`, `depth`, `status=draft` + `entries.update`) | `entries.read` |
| GET | `/api/entries/{type}/{id or slug}` | `entries.read` |
| POST | `/api/entries/{type}` (`{ data, publish }`) | `entries.create` (+ `entries.publish` to publish) |
| PUT | `/api/entries/{type}/{id}` (`{ data, publish, version }`, 409 on a stale version) | `entries.update` |
| POST | `/api/entries/{type}/{id}/publish`, `/unpublish` | `entries.publish` |
| DELETE | `/api/entries/{type}/{id}` | `entries.delete` |
| GET | `/api/media` | `media.read` |
| GET | `/api/users` | `users.read` |
| GET | `/api/roles` | `roles.read` |
| GET | `/api/settings` | `settings.read` |
| GET | `/api/extensions` | `extensions.read` |
| any | `/api/custom/{path}` | an endpoint's operations (method + base path + optional sub-path, each with its own SQL); no API token: the endpoint's own token, one for all its operations (`Authorization: Bearer dnfe_…`, shown on **API Endpoints**), or none for a public endpoint ([custom endpoints](../../.docs/features/custom-endpoints.md)) |
| GET | `/health` | none (not under `/api`) |

Responses are JSON.
Response shapes, behaviour and conventions for new endpoints:
[headless-api.md](../../.docs/features/headless-api.md). Code: `src/DotNetForge.Api/Controllers/`.

Interactive docs: **API Docs** (`/admin/api-docs`, Super Admin / Developer) shows Scalar over the OpenAPI document
`GET /admin/api-docs/openapi/v1.json` - every `/api/*` action with the key it needs, plus the enabled custom endpoints
(one group per endpoint, its operations inside) with their tokens filled in ([api-docs.md](../../.docs/features/api-docs.md)). Code: `src/DotNetForge.Web/ApiDocs/`.

## Permission keys

`core.read|manage`, `content.read|create|update|delete|publish`, `schema.read`,
`entries.read|create|update|delete|publish`, `media.read|upload|update|delete`,
`users.read|create|update|delete`, `roles.read|create|update|delete`, `settings.read|update`,
`extensions.read|manage`, `webhooks.read|manage` - 31 keys (`DotNetForge.Shared.Constants.PermissionKeys.All`). There is
no key for custom endpoints: API tokens never open them.
