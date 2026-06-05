# API Reference (for AI agents)

The headless API is served under `/api`. Authenticate with a bearer token minted in
**Admin → API Tokens**:

```http
Authorization: Bearer dnf_<prefix>_<secret>
```

Tokens are stored only as salted hashes; the plaintext is shown once at creation. A request is
matched by prefix and verified against the hash. Expired/revoked tokens → `401`; missing permission →
`403`.

## Endpoints (foundation)

| Method | Route | Required permission |
| --- | --- | --- |
| GET | `/api/content/pages` | `content.read` |
| GET | `/api/content/{type}` | `content.read` |
| POST | `/api/content/pages` | `content.create` |
| GET | `/api/media` | `media.read` |
| GET | `/api/users` | `users.read` |
| GET | `/api/roles` | `roles.read` |
| GET | `/api/settings` | `settings.read` |
| GET | `/api/extensions` | `extensions.read` |
| GET | `/health` | (none) |

All responses are JSON and scoped to the token's tenant. Implementations live in
`src/DotNetForge.Api/Controllers/`.

## Permission keys

`core.read/manage`, `content.read/create/update/delete/publish`, `media.read/upload/update/delete`,
`users.*`, `roles.*`, `settings.read/update`, `extensions.read/manage`, `webhooks.read/manage`
(`DotNetForge.Shared.Constants.PermissionKeys`).
