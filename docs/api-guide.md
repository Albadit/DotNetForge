# API Guide

The headless API exposes content, media, users, roles, settings, and extensions for **Headless** and
**Hybrid** usage.

## Authentication

1. In **Admin → Settings → API Tokens → Create token**, pick a name, duration, and the granular
   permissions the token needs.
2. Copy the token **once** - only its salted hash is stored.
3. Send it as a bearer token:

```bash
curl -H "Authorization: Bearer dnf_<prefix>_<secret>" http://localhost:5000/api/content/pages
```

Responses are JSON, scoped to the token's tenant.

## Endpoints

| Method | Route | Permission |
| --- | --- | --- |
| GET | `/api/content/pages` | `content.read` |
| GET | `/api/content/{type}` | `content.read` |
| POST | `/api/content/pages` | `content.create` |
| GET | `/api/media` | `media.read` |
| GET | `/api/users` | `users.read` |
| GET | `/api/roles` | `roles.read` |
| GET | `/api/settings` | `settings.read` |
| GET | `/api/extensions` | `extensions.read` |

## Status codes

| Code | Meaning |
| --- | --- |
| `200` / `201` | Success. |
| `400` | Invalid request body. |
| `401` | Missing, unknown, expired, or revoked token. |
| `403` | Token lacks the required permission (or cross-tenant access). |

## Example: create a page

```bash
curl -X POST http://localhost:5000/api/content/pages \
  -H "Authorization: Bearer dnf_<prefix>_<secret>" \
  -H "Content-Type: application/json" \
  -d '{"title":"About","slug":"about","metaDescription":"About us"}'
```

The token must carry `content.create`. The new page is created in the token's tenant.
