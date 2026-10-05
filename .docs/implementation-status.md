# Implementation status

What the code does today versus the planned product scope. Each module links to the document whose
**Planned (not implemented)** section holds its remaining requirements and acceptance criteria. Use this before
planning work: it tells you which foundations already exist so you extend them instead of starting over. Verified
against the code in October 2026.

Legend: **Built** - usable end to end. **Partial** - some of the spec works. **Foundation** - data model, contract or
placeholder only, no behaviour. **Missing** - nothing in the code.

| Module (planned section) | Status | What exists | Main gaps | Docs |
| --- | --- | --- | --- | --- |
| [Installation & setup](features/installation.md#planned-not-implemented) | **Built** | `.env` loading/validation, install gate, one-step setup wizard, transactional first admin | `cms.installed` audit, richer wizard | [installation](features/installation.md), [configuration](features/configuration.md), [setup](pages/setup.md) |
| [Admin area](architecture/pages.md#planned-not-implemented) | **Partial** | `_AdminLayout`, full sidebar tree, placeholders for unbuilt items, admin-extension tabs | role-aware sidebar, tenant switcher | [pages architecture](architecture/pages.md) |
| [Dashboard](pages/dashboard.md#planned-not-implemented) | **Partial** | 8 count cards + system panel | widgets, activity, health, updates | [dashboard](pages/dashboard.md) |
| [Content manager](features/content-pages-and-routing.md#planned-not-implemented) | **Partial** | page tree CRUD, drag-and-drop, SEO fields, page types (stored), schedules | collection/single types, page builder/body, drafts, preview, history, review, tags, per-page permissions | [content manager](pages/content-manager.md), [content pages](features/content-pages-and-routing.md) |
| [Dynamic routes](features/content-pages-and-routing.md#planned-not-implemented) | **Partial** | `[param]` slug segments resolved by `HomeController.RenderPage` | route fields, conflict rules beyond one-per-parent, handlers, API | [content pages](features/content-pages-and-routing.md#public-resolution) |
| [Themes](features/themes.md#planned-not-implemented) | **Foundation** | `IThemeExtension`, `Tenant.DefaultTheme`, sample manifest | theme loading, layouts, per-page theme | [extensions](features/extensions.md) |
| [Users, roles & permissions](features/authorization.md#planned-not-implemented) | **Partial** | six seeded roles + matrix grants, role-gated admin screens, matrix enforced for content and media (own vs any, publish), read-only Users/Roles lists | user/role management, enforcement of `(area, action)` permissions, own-content rules | [authorization](features/authorization.md), [users](pages/users.md), [roles](pages/roles.md) |
| [Authentication](features/authentication.md#planned-not-implemented) | **Partial** | email/password sign-in, lockout, cookie session, seeded provider catalog | sign-up, password reset, email confirmation, OAuth, provider settings | [authentication](features/authentication.md), [login](pages/login.md) |
| [API tokens & headless API](features/headless-api.md#planned-not-implemented) | **Partial** | token create/revoke/list, hashed storage, bearer auth, 8 read/create endpoints with permission keys | update/delete endpoints, rotation, pagination, tenant header | [headless API](features/headless-api.md), [API tokens](pages/api-tokens.md) |
| [Audit logs](features/audit-logging.md#planned-not-implemented) | **Partial** | `AuditService` (users and API tokens as actors), 13 action types written, tenant-scoped latest-100 viewer | filters, search, export, tenant scoping, more tracked actions | [audit logging](features/audit-logging.md), [audit logs](pages/audit-logs.md) |
| [Extensions & marketplace](features/extensions.md#planned-not-implemented) | **Partial** | manifest validation, discovery, Plugins list, admin extensions rendered via runtime Razor | install/enable/disable/update/remove, marketplace, assembly loading, permission enforcement | [extensions](features/extensions.md), [plugins](pages/plugins.md), [extension host](pages/extension-host.md) |
| [Settings](pages/settings.md#planned-not-implemented) | **Partial** | key/value list + upsert (tenant scope) | overview diagnostics, logo uploads, typed settings, delete | [settings](pages/settings.md) |
| [File manager / media](features/media-storage.md#planned-not-implemented) | **Partial** | upload (multi-file, allowlist, 25 MB cap), public/private at upload, download with access control (presigned S3 URLs or streaming), delete; `IFileStorage` with local and S3-compatible providers | folders, search, tags, access grants, image variants, duplicate strategy, configurable limits, API upload, webhooks | [media storage](features/media-storage.md), [media](pages/media.md) |
| [Multi-tenancy](features/multi-tenancy.md#planned-not-implemented) | **Foundation** | `Tenant` entity, `TenantId` columns, tenant claim, per-query filters | tenant resolution, switching, isolation of public site/audit | [multi-tenancy](features/multi-tenancy.md) |
| [Webhooks](features/webhooks.md#planned-not-implemented) | **Foundation** | `Webhook`/`WebhookDelivery` tables, `WebhookEvents`, `HmacWebhookSigner` (unused), placeholder screen | CRUD, event dispatch, delivery, retries, logs | [module placeholders](pages/module-placeholders.md) |
| [Email](features/email.md#planned-not-implemented) | **Foundation** | `IEmailSender` + `FileSystemEmailSender` (unused), placeholder screens | SMTP, templates, test email | [module placeholders](pages/module-placeholders.md) |
| [Internationalization](features/internationalization.md#planned-not-implemented) | **Missing** | placeholder screen, `Tenant.DefaultLocale` | everything | [module placeholders](pages/module-placeholders.md) |
| [Transfer, updates & backups](features/transfer-and-updates.md#planned-not-implemented) | **Missing** | placeholder screen, empty `storage/backups`, `storage/updates` | everything | [module placeholders](pages/module-placeholders.md) |
| [Security](features/security.md#planned-not-implemented) | **Partial** | hashing, lockout, CSRF, Secure/HttpOnly cookies + session re-validation, HSTS, CSP and security headers, rate limiting (sign-in, setup, API), hashed tokens, traversal guards, input length limits, content/media permission checks | HTTPS redirect at the app (delegated to the proxy), extension sandboxing, upload scanning, encrypted key ring | [security](features/security.md) |
| [Testing & quality](guides/testing.md#planned-not-implemented) | **Partial** | unit + integration projects (SQLite and PostgreSQL), storage contract tests (local + live S3), read-only deployment test, 3-OS CI, PostgreSQL/S3 and read-only-container CI jobs, `dotnet format` check | AuthService unit tests, frontend linting, coverage reporting | [testing](guides/testing.md) |
| [Architecture](architecture/codebase.md#planned-not-implemented) | **Built** | layered projects, DI composition root, core/extension separation, read-only deployment, provider-neutral storage, migrations for both database providers, Docker image | Api → Data shortcut (documented) | [codebase](architecture/codebase.md), [deployment](guides/deployment.md) |
| [Developer documentation](guides/development.md#planned-not-implemented) | **Built** | this `.docs/` folder, `.github/agents/`, `.claude/skills/` | - | [README](README.md) |

## Registered but unused code

Code that exists but has no runtime caller - don't assume it does anything:

| Item | Where |
| --- | --- |
| `IEmailSender` / `EmailMessage` | contract only; no implementation is registered ([email](features/email.md)) |
| `HmacWebhookSigner` / `IWebhookSigner` | implemented and unit-tested, not registered (no webhook delivery yet) |
| `PermissionAreas.Canonical`, `WebhookEvents.IsValid` | no callers (reserved for planned role editing and webhooks) |
| `AuditActions`: `PluginInstalled`, `PluginDisabled`, `RoleChanged`, `PermissionChanged`, `WebhookCreated`, `AuditExported` | never written |
| `InstalledExtensions` table | never written |
| `AuthProviders` table | seeded, never read |
| `TokenDuration.Custom` | never offered |
| `AppEnvironment.AppUrl` | validated, never used |

Removed as dead code in the production-readiness pass: `FileSystemEmailSender`, the old `LocalFileStorage` (replaced),
`SlugHelper`, `Result<T>`, `Roles.IsBuiltIn`, `AuthService.GetRolesAsync`, `InstallationStatusCache.KnownInstalled`,
`ModulePlaceholderViewModel.Tokens`, and the unused `storage/backups|logs|updates` folders. Now in use:
`IPermissionService` (content and media permission checks), `IFileStorage`, `Roles.AdminCapable` (the `AdminArea`
policy), `src/DotNetForge.Web/wwwroot/js/site.js` (`data-confirm`), `AuditActions.MediaUploaded`/`CmsInstalled`.
