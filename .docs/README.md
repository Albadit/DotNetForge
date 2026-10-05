# DotNetForge CMS - technical documentation

The main technical reference for DotNetForge CMS: a server-rendered ASP.NET Core MVC content-management system on
.NET 10 with EF Core (SQLite or PostgreSQL), a role-gated admin area, a public site that resolves URLs from a page
tree, a token-secured headless API, an on-disk extension system, and media in S3-compatible object storage. It is
built to run from a **read-only deployment directory** ([deployment](guides/deployment.md)). Everything here is verified against the code;
**the code is the source of truth**. Work that is planned but not built lives in a clearly marked
**Planned (not implemented)** section at the end of the document that owns the topic, and is summarised in
[implementation-status.md](implementation-status.md). Product goals and scope: [product.md](product.md).

New here? Read in this order:

1. [Architecture overview](architecture/overview.md) - one page: layout, layering, key decisions
2. [Codebase architecture](architecture/codebase.md) - projects, folders, layers, rules for changes
3. [Data flow](architecture/data-flow.md) - startup, request pipeline, routing, auth and API flows
4. [Page (screen) architecture](architecture/pages.md) - route map, layouts, navigation, how screens talk to the backend
5. [Feature documents](#features) - one authoritative document per concept
6. [Screen documents](#screens) - one per screen
7. [Development guide](guides/development.md) - run, debug, troubleshoot
8. [Deployment guide](guides/deployment.md) - production configuration, Docker, read-only filesystem, object storage

Terminology is fixed in the [glossary](glossary.md) - in particular, a **screen** is a UI view and a **content page**
(`Page`) is the content entity.

## Documentation map

```text
.docs/
├── README.md                    this entry point
├── product.md                   product goals, modes, users, scope
├── glossary.md                  one term per concept
├── implementation-status.md     built vs. planned per module, unused code
├── architecture/
│   ├── overview.md              one-page summary: layout, layering, key decisions
│   ├── codebase.md              projects, folders, layers, DI, architectural rules
│   ├── data-flow.md             startup, pipeline, routing, auth/API/DB/background/error flows
│   ├── pages.md                 screen architecture + full route map
│   ├── database.md              tables, ER diagram, migrations, seeding
│   └── dependencies.md          project refs, NuGet, DI registrations, must-not-bypass list
├── features/                    cross-screen concepts (authoritative), incl. planned-only modules
├── pages/                       one file per screen
└── guides/                      how-to: development, testing, deployment, extension development
```

## Architecture

| Document | Answers |
| --- | --- |
| [overview.md](architecture/overview.md) | What is this system, how is it layered, which decisions shape it? (one page) |
| [codebase.md](architecture/codebase.md) | What is each project/folder for? Where does new code go? Which rules must changes follow? |
| [data-flow.md](architecture/data-flow.md) | What happens at startup and on each request? How are URLs routed? |
| [pages.md](architecture/pages.md) | Which screens exist, at which routes, with which permissions? How is a screen built? |
| [database.md](architecture/database.md) | Which tables exist, what writes them, how are migrations and seeding done? |
| [dependencies.md](architecture/dependencies.md) | What references what? Which services are registered and who uses them? |

## Features

| Feature | Document |
| --- | --- |
| Install gate + first Super Admin | [installation.md](features/installation.md) |
| `.env` configuration | [configuration.md](features/configuration.md) |
| Cookie sign-in, lockout, API token authentication, hashing | [authentication.md](features/authentication.md) |
| Roles, `AdminArea` policy, permission matrix, API permission keys | [authorization.md](features/authorization.md) |
| Tenants (current single-tenant behaviour) | [multi-tenancy.md](features/multi-tenancy.md) |
| Content pages, tree rules, liveness, public URL resolution | [content-pages-and-routing.md](features/content-pages-and-routing.md) |
| Scheduled publish/unpublish job | [scheduled-publishing.md](features/scheduled-publishing.md) |
| Headless API endpoints and tokens | [headless-api.md](features/headless-api.md) |
| Extensions, manifests, admin extensions | [extensions.md](features/extensions.md) |
| Audit entries | [audit-logging.md](features/audit-logging.md) |
| Media, object storage (`IFileStorage`), provider choice | [media-storage.md](features/media-storage.md) |
| Security controls and known gaps | [security.md](features/security.md) |
| Logging and error handling | [logging-and-error-handling.md](features/logging-and-error-handling.md) |
| Themes (planned) | [themes.md](features/themes.md) |
| Webhooks (planned) | [webhooks.md](features/webhooks.md) |
| Email (planned) | [email.md](features/email.md) |
| Internationalization (planned) | [internationalization.md](features/internationalization.md) |
| Transfer, updates and backups (planned) | [transfer-and-updates.md](features/transfer-and-updates.md) |

## Screens

| Area | Screen | Route | Document |
| --- | --- | --- | --- |
| First run | Setup wizard | `/setup` | [setup.md](pages/setup.md) |
| Account | Sign in | `/account/login` | [login.md](pages/login.md) |
| Account | Access denied | `/account/denied` | [access-denied.md](pages/access-denied.md) |
| Public | Home | `/` | [public-home.md](pages/public-home.md) |
| Public | Content page | `/{slug}/...` (fallback) | [public-page.md](pages/public-page.md) |
| Public | Error | `/error` | [error.md](pages/error.md) |
| Admin | Dashboard | `/admin` | [dashboard.md](pages/dashboard.md) |
| Admin | Content Manager | `/admin/content` | [content-manager.md](pages/content-manager.md) |
| Admin | Media | `/admin/media` | [media.md](pages/media.md) |
| Admin | Settings | `/admin/settings` | [settings.md](pages/settings.md) |
| Admin | API Tokens | `/admin/api-tokens` | [api-tokens.md](pages/api-tokens.md) |
| Admin | Roles | `/admin/roles` | [roles.md](pages/roles.md) |
| Admin | Users | `/admin/users` | [users.md](pages/users.md) |
| Admin | Audit Logs | `/admin/audit-logs` | [audit-logs.md](pages/audit-logs.md) |
| Admin | Plugins | `/admin/plugins` | [plugins.md](pages/plugins.md) |
| Admin | Admin extension tab | `/admin/ext/{id}` | [extension-host.md](pages/extension-host.md) |
| Admin | Unbuilt modules (9 placeholders) | `/admin/marketplace`, `/admin/webhooks`, ... | [module-placeholders.md](pages/module-placeholders.md) |

## Guides

| Guide | For |
| --- | --- |
| [development.md](guides/development.md) | setup, commands, resetting state, migrations, debugging, conventions, troubleshooting |
| [testing.md](guides/testing.md) | test projects, what they cover, how to add tests, CI |
| [deployment.md](guides/deployment.md) | read-only deployment requirements, environment variables, Docker, reverse proxy, database, object storage, backups |
| [extension-development.md](guides/extension-development.md) | writing a manifest and an admin extension |

## Planned work

There is no separate specification folder. Each document that owns a topic ends with a
**Planned (not implemented)** section holding the remaining requirements, user flows, rules, edge cases and
acceptance criteria for that topic (criteria already met are ticked). Modules that do not exist yet at all
(themes, webhooks, email, internationalization, transfer/updates) have a feature document with a short
*Current state* and a *Planned* section. [implementation-status.md](implementation-status.md) links every module to
its planned section; [product.md](product.md) holds the product goals and MVP scope.

When you build something from a Planned section: move what is now true into the main body of the document (verified
against the code), tick or remove the acceptance criteria it satisfies, and update the status table.

## For AI coding agents

- Start from [codebase.md → Architectural rules for changes](architecture/codebase.md#architectural-rules-for-changes)
  and [dependencies.md → Dependencies that must not be bypassed](architecture/dependencies.md#dependencies-that-must-not-be-bypassed).
- Each feature and screen document ends with **Where to change things** / **Extension points**: follow them.
- Project skills in [`.claude/skills/`](../.claude/skills/) encode the recurring workflows: `admin-page`,
  `api-endpoint`, `database-change`, `admin-extension`, `verify`, plus `documentation` and `software-engineering`.
- Documentation is part of the change: when behaviour, routes, tables, configuration or permissions change, update
  the owning document (one authoritative place per concept - link, don't duplicate).

## Other documentation in the repository

| File | Role |
| --- | --- |
| [`/README.md`](../README.md) | project overview and quick start |
| [`/CLAUDE.md`](../CLAUDE.md) | orientation for Claude Code sessions |
| [`/.github/agents/`](../.github/agents/) | short AI-agent quick references (spec deliverable) pointing here |
| `extensions/themes/default-theme/README.md` | sample theme note |
