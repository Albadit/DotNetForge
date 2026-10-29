# Product overview

This document describes the product goals and scope of DotNetForge CMS: what it is for, who uses it, the three
operating modes, the technology stack, the MVP deliverables and the cross-module user flows. It is the target, not a
description of current behaviour: what the code does today is in the [architecture](architecture/codebase.md),
[feature](README.md#features) and [screen](README.md#screens) documents, and the gap per module is tracked in
[implementation-status.md](implementation-status.md). Terms follow the [glossary](glossary.md).

## Product goal

One application that works as a **traditional CMS**, a **headless CMS**, or both at once (**hybrid**), so a team does
not have to choose up front. It must be modular, secure, lightweight, cross-platform, easy to update and
developer-friendly. The core is kept strictly separate from user-created extensions so the core can be updated
without breaking customisations ([extensions](features/extensions.md)).

## Operating modes

| Mode | Target | Today |
| --- | --- | --- |
| **Traditional** | visual admin like DNN, WordPress or Umbraco: content pages, menus, media uploads, themes | admin area + public site over the page tree ✔, media uploads ✔, a site-wide theme with a menu from `DisplayInMenu` pages through a theme extension ✔ ([content pages and routing](features/content-pages-and-routing.md), [themes](features/themes.md)) |
| **Headless** | content, media, users, content pages and extensions managed entirely through the API with scoped [API tokens](pages/api-tokens.md) | read endpoints + create content page only ([headless API](features/headless-api.md)) |
| **Hybrid** | admin and API at the same time over the same content | ✔ both run over the same database |

## Target users

- **Developers and agencies** building sites on an extensible ASP.NET Core MVC foundation.
- **Content and editorial teams** working in the admin area ([Pages](pages/pages.md), [Content Manager](pages/content-manager.md),
  [Dashboard](pages/dashboard.md)).
- **Integrators** consuming content through the headless API with scoped tokens ([headless API](features/headless-api.md)).

## Technology stack

| Requirement | Status |
| --- | --- |
| ASP.NET Core MVC, C#, Razor views | ✔ .NET 10 |
| Entity Framework Core | ✔ |
| SQLite (default) | changed: SQLite is only an additional database ([database configuration](database/configuration.md)) |
| PostgreSQL | ✔ the CMS database, with one migration set (`PostgreSqlDbContext`) ([database](architecture/database.md)); SQLite, SQL Server, MySQL and MongoDB as additional databases ([database layer](database/providers.md)) |
| Environment-based configuration in `.env`; secrets only there, never committed | ✔ ([configuration](features/configuration.md)) |
| Unit testing | ✔ xUnit ([testing](guides/testing.md)) |
| Frontend linting (ESLint) | ✘ |
| GitHub-compatible structure | ✔ CI, Dependabot |
| Windows, macOS and Linux development | ✔ CI builds and tests on all three |

## MVP scope

The foundation that must ship first; deeper capabilities are layered on by each feature. Combines the project
deliverables and the generated-solution deliverables of the specification.

| Item | Status | Notes | Docs |
| --- | :-: | --- | --- |
| ASP.NET Core MVC solution | ✔ | `DotNetForge.slnx`; web host in `src/DotNetForge.Web` | [codebase](architecture/codebase.md) |
| Setup / registration flow | ✔ | | [installation](features/installation.md) |
| Admin dashboard | ✔ | quick actions, KPI cards, recent activity, system card + widget extensions; no built-in widgets | [dashboard](pages/dashboard.md) |
| Admin sidebar / shell | ✔ | full sidebar tree, hidden per role, collapsible Settings groups, breadcrumbs, light/dark ([design system](architecture/design-system.md)); unbuilt items are placeholders | [pages architecture](architecture/pages.md), [module placeholders](pages/module-placeholders.md) |
| Authentication | ✔ | email/password, lockout, cookie, sign-up, password reset, ten built-in OAuth providers and extension providers (sign-up through a provider), two-factor authentication | [authentication](features/authentication.md), [MFA](features/mfa.md) |
| RBAC permissions | ✔ | partial: roles + seeded matrix; admin checks role names, API checks permission keys | [authorization](features/authorization.md) |
| Content manager foundation | ✔ | page tree CRUD, SEO fields, schedules (**Pages**); content types with a field builder and generated entry forms (**Content Manager**) | [Pages](pages/pages.md), [structured content](features/structured-content.md) |
| File Manager foundation | ✔ | upload, public/private files, library with search and filters, delete and bulk delete (**Media** screen); no folders or tags | [media storage](features/media-storage.md) |
| API token foundation | ✔ | | [headless API](features/headless-api.md) |
| Extension manifest system | ✔ | | [extensions](features/extensions.md) |
| Extension loading foundation | ✔ | discovery, code loading per extension (`DotNetForge.Sdk`), enable/disable, slots for every extension type; no install/update/remove or marketplace | [extensions](features/extensions.md) |
| SQLite and PostgreSQL support | ✔ | the CMS on PostgreSQL only; SQLite, SQL Server, MySQL, MongoDB as additional databases | [database providers](database/providers.md) |
| Import/export foundation | ✔ | Data Management: export, backups, import in Development | [transfer and updates](features/transfer-and-updates.md) |
| Webhook foundation | ✔ | Webhooks screen, signed delivery of entry and media events with retries and a delivery log; no webhook API, no events for content pages | [webhooks](features/webhooks.md) |
| Audit logging foundation | ✔ | | [audit logging](features/audit-logging.md) |
| Theme foundation | ✔ | one site-wide active theme (views, layout, assets, menu) and an optional per-page theme | [themes](features/themes.md) |
| Documentation (developer + extension development) | ✔ | `.docs/` | [README](README.md) |
| Agents folder | ✔ | in `.github/agents/`, not a root `agents/` | [development → Planned](guides/development.md#planned-not-implemented) |
| Unit test project | ✔ | | [testing](guides/testing.md) |
| `.env.example`, `.gitignore`, `README.md`, `ARCHITECTURE.md`, `LICENSE` | ✔ | architecture summary is [architecture/overview.md](architecture/overview.md), not a root file | |

## Implementation principles

Keep the core separate from extensions; never let public themes affect the admin area; security from the start;
lightweight with no unnecessary dependencies; clear folder boundaries; interfaces for extension points; dependency
injection; migrations for every schema change; validate extension manifests before installation; safe update and
rollback; provider-aware import/export; granular API permissions; document everything important. The rules that
apply to the code today are in [codebase → Architectural rules for changes](architecture/codebase.md#architectural-rules-for-changes);
the target architecture is in [codebase → Planned](architecture/codebase.md#planned-not-implemented).

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

Product-level capability summary per role (full model in [authorization](features/authorization.md)):

| Capability | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | :-: | :-: | :-: | :-: | :-: | :-: |
| Complete first-time setup (becomes the first admin) | created | | | | | |
| Configure settings, security, providers, extensions | ✔ | ✔ | | | | |
| Manage users, roles and permissions | ✔ | ✔ | | | | |
| Create/publish any content | ✔ | ✔ | ✔ | own + assigned | | |
| Create/edit own content | ✔ | ✔ | ✔ | ✔ (own only) | | |
| Public site / public content pages | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Private (authenticated-only) content pages | ✔ | ✔ | ✔ | ✔ | ✔ | |

Today the admin area admits the four admin-capable roles to every content page regardless of ownership and there are no
private content pages ([authorization](features/authorization.md)).

### User flows

**Install → first content → publish**

| Step | Today |
| --- | --- |
| 1. Clone, copy `.env.example` to `.env`, set the database provider | ✔ (the admin's credentials are entered in the wizard, not in `.env`) |
| 2. First start detects "not installed" and redirects to setup | ✔ |
| 3. Setup creates the first user as Super Admin, marks the CMS installed, redirects to the dashboard | ✔ |
| 4. An Editor or Author creates a content page on Pages (slug, title, SEO) and designs it with the page builder, showing entries from the Content Manager in content lists | ✔ |
| 5. Save a draft and check it in preview mode | ✘ |
| 6. An authorised user sets it **Published** (optionally scheduled); it goes live on the public site under the active theme | ✔ publish and schedule, rendered with the active theme |

**Headless API consumer**

| Step | Today |
| --- | --- |
| 1. An admin creates an API token scoped to permission keys (e.g. `content.read`, `media.read`) | ✔ |
| 2. The token is shown once; only its hash is stored | ✔ |
| 3. The integrator calls the API and the request is authorised against the token's keys | ✔ |
| 4. Read and, where granted, create/update/delete content, media and dynamic routes | read ✔, create content page ✔, entries of content types read/create/update/publish/delete ✔ (`/api/entries`); no page update/delete, media writes or dynamic-route endpoints |
| 5. Relevant actions emit [webhooks](features/webhooks.md) and are written as audit entries | partly: entry changes through `/api/entries` and `POST /api/content/pages` are audited, entry changes also fire webhooks; reads are not audited and content pages fire no webhooks |

### Rules and validation

- The CMS database is always PostgreSQL - there is no provider setting (a leftover `DATABASE_PROVIDER` value is rejected at startup, ✔ `EnvConfigurationLoader`).
  Other databases are configured as `DATABASES_<NAME>_PROVIDER` (any registered provider, or detected from the
  connection string).
- `.env.example`, `.gitignore`, `README.md` and `ARCHITECTURE.md` exist at the repository root (✔, except the
  architecture summary, kept as `.docs/architecture/overview.md`).
- Secrets come only from `.env`, which is git-ignored (✔); generated files, logs, uploads, cache and build output are
  git-ignored (✔).
- Normal operation is impossible until the CMS is installed (✔ `InstallationMiddleware`); the first user gets
  `Super Admin` exactly once (✔).
- Every form and API input is validated server-side; malformed input is rejected, never persisted
  ([security](features/security.md)).
- Every admin and API action enforces a permission check (today: role checks in the admin area, permission keys in
  the API).

### Edge cases

- **Theme bleed:** a broken or misconfigured public theme must never alter or break the admin area; the admin shell
  renders independently of themes ([themes](features/themes.md)).
- **Hybrid overlap:** the admin area and the API enforce the same permission and validation rules so neither can
  bypass the other. Today `ContentApiController` creates content pages with its own title/slug check instead of
  `PageService`, and the admin uses role checks while the API uses permission keys.
- **Core vs extension update:** a core update never overwrites user extensions; a failed update rolls back to the
  previous working version where possible ([transfer and updates](features/transfer-and-updates.md)).
- **Committed secrets:** a secret placed outside `.env` must be caught by `.gitignore` rules and review.

### Acceptance criteria

- [x] The application runs on Windows, macOS and Linux from the same codebase (`ci.yml` matrix).
- [ ] The CMS operates in Traditional, Headless and Hybrid modes against the same content (headless covers reads and
  content-page creation only).
- [x] `.env.example`, `.gitignore` and `README.md` exist at the repository root; the architecture summary is
  `.docs/architecture/overview.md`.
- [x] Copying `.env.example` to `.env` lets the app start; an unsupported connection string gives a clear error
  (`EnvConfigurationLoader`).
- [x] First start redirects to setup; setup creates a Super Admin, marks the CMS installed and redirects to the
  dashboard (`InstallationMiddleware`, `InstallationService`, `SetupController`).
- [ ] SQLite (default) and PostgreSQL are both selectable and functional - changed: the CMS runs on PostgreSQL only
  (migrated at startup, integration tests on it); SQLite is an additional database.
- [x] The admin area renders correctly regardless of the active public theme (only `HomeController` uses theme
  views; the admin keeps `_AdminLayout`).
- [x] A scoped API token can read content and media, and only its hash is persisted (`ApiTokenFactory`,
  `ContentApiController`, `MediaApiController`).
- [ ] Every admin and API action is gated by a permission check (the admin area uses role checks).
- [ ] The solution includes every MVP item above and is modular, secure and ready for expansion (see the ✘ rows).
- [ ] No secrets are committed; generated files, logs, uploads, cache and build output are ignored (`.gitignore` covers
  `.env*`, `bin/`, `obj/`, `storage/*` contents and test output, but has no `*.log` or `.cache/` entry).
