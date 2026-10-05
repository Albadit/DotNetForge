# Architecture & Project Structure

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

This document is the canonical specification for the `ARCHITECTURE.md` deliverable. It defines the solution layout, the responsibilities of every project and folder, and the architecture of each major subsystem (core, extensions, database, authentication, authorization, content, media, themes, API, update/rollback, security, testing, and deployment).

## Purpose

DotNetForge CMS is a modular hybrid CMS built with **ASP.NET Core MVC** (C#, Entity Framework Core, Razor Views). It supports **SQLite** (default) and **PostgreSQL**, is configured through `.env`, and runs cross-platform on Windows, macOS, and Linux. It operates in three modes simultaneously:

1. **Traditional CMS** - a visual admin UI for pages, menus, media, and themes.
2. **Headless CMS** - everything managed via API using tokens and granular permissions.
3. **Hybrid CMS** - the visual CMS and the API used at the same time.

The system must be modular, secure, lightweight, cross-platform, easy to update, and developer-friendly. The overriding architectural goal is a **strict separation between the CMS core and user-created extensions** so the core can be updated without breaking extensions. This document gives a developer or AI coding agent enough detail to scaffold the solution and understand how every subsystem fits together; it cross-links sibling specification files that own the deep detail of each feature.

## Architectural Principles

These principles govern every part of the system and are enforced throughout this specification:

- Keep the CMS core separate from user extensions.
- Do not let frontend themes affect the admin area.
- Prioritize security from the beginning. See [Security](security.md).
- Make the project lightweight and avoid unnecessary dependencies.
- Use clear folder boundaries.
- Use interfaces for extension points.
- Use dependency injection (DI) throughout.
- Use database migrations for all schema changes.
- Validate extension manifests before installation.
- Make update and rollback safe.
- Make import/export database-provider aware.
- Make API permissions granular.
- Ensure everything important is documented.

## Suggested Project Structure

The solution must use the following structure. This tree is canonical and must be reproduced in `ARCHITECTURE.md` verbatim.

```text
DotNetForgeCMS/
  src/
    DotNetForge.Web/
    DotNetForge.Core/
    DotNetForge.Data/
    DotNetForge.Api/
    DotNetForge.Abstractions/
    DotNetForge.Infrastructure/
    DotNetForge.Extensions/
    DotNetForge.Shared/
  tests/
    DotNetForge.Tests/
    DotNetForge.IntegrationTests/
  extensions/
    themes/
    plugins/
    modules/
    widgets/
    providers/
    connectors/
    authentication/
    libraries/
    admin/
  storage/
    media/
    backups/
    logs/
    updates/
  agents/
    cms-overview.md
    architecture-summary.md
    extension-system.md
    permissions.md
    api-reference.md
    development-guidelines.md
  docs/
    developer-guide.md
    extension-development.md
    theme-development.md
    module-development.md
    api-guide.md
  .env.example
  .gitignore
  README.md
  ARCHITECTURE.md
  LICENSE
```

### Top-Level Folders

| Folder | Responsibility |
| --- | --- |
| `src/` | The compiled CMS core. All first-party application projects live here. Updated by core releases. |
| `tests/` | Unit and integration test projects. See [Testing Strategy](#testing-strategy) and the testing spec. |
| `extensions/` | The **dedicated extensions folder**. User-created and installed extensions live here, organized by type. Updates must never overwrite this folder. |
| `storage/` | Runtime data: uploaded media, backups, logs, and downloaded updates. Excluded from Git. |
| `agents/` | Documentation for AI agents that describes how the CMS works (see the agents-folder spec). |
| `docs/` | Human developer documentation (developer guide, extension/theme/module development, API guide). |
| Root files | `.env.example`, `.gitignore`, `README.md`, `ARCHITECTURE.md`, `LICENSE`. |

The `extensions/` folder is partitioned by extension category so the loader can discover each type quickly:

| Subfolder | Holds |
| --- | --- |
| `extensions/themes/` | Themes (public frontend look and feel). |
| `extensions/plugins/` | Plugins. |
| `extensions/modules/` | Page modules placed by the page builder. |
| `extensions/widgets/` | Dashboard and admin widgets. |
| `extensions/providers/` | Generic providers. |
| `extensions/connectors/` | External-service connectors (e.g., external media storage). |
| `extensions/authentication/` | Authentication-provider extensions. |
| `extensions/libraries/` | Shared libraries used by other extensions. |
| `extensions/admin/` | Admin-area extensions. |

The `storage/` folder separates runtime artifacts so they can be excluded from version control and managed by the update/rollback system:

| Subfolder | Holds |
| --- | --- |
| `storage/media/` | Uploaded media (local storage provider default). See [File Manager](file_manager.md). |
| `storage/backups/` | Backups created before updates and on demand. |
| `storage/logs/` | Application and delivery logs. |
| `storage/updates/` | Downloaded update packages staged for install/rollback. |

### Repository Hygiene

The project must be uploadable to GitHub and the repository must stay as small as possible. Generated files, logs, uploads, cache, build output, and secrets must be excluded via `.gitignore`. Specifically, `storage/media/`, `storage/backups/`, `storage/logs/`, `storage/updates/`, build output (`bin/`, `obj/`), and `.env` must never be committed. **Secrets live in `.env` and must never be committed to Git.** See [Security](security.md).

## Core CMS Architecture

DotNetForge CMS follows a layered architecture built on ASP.NET Core MVC. Each `src/` project has a single, clear responsibility, and dependencies flow inward toward the abstractions and shared layers. Cross-cutting wiring is done through dependency injection.

### Layer Responsibilities

| Project | Layer | Responsibility | Depends On |
| --- | --- | --- | --- |
| `DotNetForge.Abstractions` | Contracts | Interfaces and extension-point contracts (e.g., extension interfaces, provider interfaces, service interfaces). Contains no implementation. This is the surface extensions compile against, so it must stay stable across core updates. | (none) |
| `DotNetForge.Shared` | Shared kernel | Cross-cutting types: DTOs, enums, constants, result types, manifest models, and small utilities shared by all layers. | Abstractions |
| `DotNetForge.Core` | Domain & services | Core CMS business logic and services: content system, media, themes, RBAC/permission checks, settings, extension management, update/rollback orchestration. Implements interfaces from Abstractions. | Abstractions, Shared |
| `DotNetForge.Data` | Persistence | Entity Framework Core `DbContext`, entity mappings, migrations, and the SQLite/PostgreSQL provider selection. Implements repository/data interfaces. | Abstractions, Shared |
| `DotNetForge.Infrastructure` | Infrastructure | Cross-cutting infrastructure implementations: email/SMTP, file storage, webhook delivery, background jobs, logging, security primitives (hashing, token generation), and external connectors. | Abstractions, Shared, Core |
| `DotNetForge.Extensions` | Extension host | The extension loader/host: manifest discovery and validation, dependency resolution, lifecycle (install/enable/disable/update/remove), and DI registration of extension services from the `extensions/` folder. | Abstractions, Shared, Core |
| `DotNetForge.Api` | API surface | The headless/hybrid API: controllers, token authentication, permission enforcement, and DTO mapping for content, media, users, roles, settings, extensions, and dynamic routes. | Abstractions, Shared, Core |
| `DotNetForge.Web` | Presentation host | The ASP.NET Core MVC host: admin UI (Razor), public frontend rendering, theme engine, page builder, routing, middleware pipeline, and application composition root (DI registration). References the API and Extensions hosts. | All of the above |

### Composition & Bootstrapping

- `DotNetForge.Web` is the composition root. It reads `.env`, selects the database provider, registers all services via DI, mounts the API, and starts the extension host.
- On first start the application runs the **installation flow** (see the installation spec): it checks whether the CMS is installed, and if not, redirects to setup, creates the first **Super Admin** user, and marks the CMS as installed.
- All extension points are expressed as interfaces in `DotNetForge.Abstractions` and resolved through DI, so extensions plug in without modifying core projects.
- **Multi-tenancy** is resolved early in the request pipeline: the tenant is resolved **first** (by domain, subdomain, or path prefix), then the page or dynamic route is resolved within that tenant's scope. See [Multi-Tenancy](multi_tenancy.md) and [Dynamic Routes](dynamic_routes.md) for the full routing order.

## Extension Architecture

Extensions are a core feature and the central extensibility mechanism. The architecture must make it possible to update the CMS core without breaking user-created extensions.

### Core vs. Extensions Separation

- Core CMS files (`src/`) and custom extensions (`extensions/`) must be clearly separated.
- Extensions must live in the **dedicated `extensions/` folder**, organized by type (see the structure tables above).
- Updates must not overwrite custom extensions.
- Clear folder boundaries are mandatory; the core never writes user code into `extensions/`, and extensions never modify `src/`.

### Extension Points & DI

- Use **interfaces for extension points**. Extensions compile against `DotNetForge.Abstractions` and implement the relevant interface for their type.
- Use **dependency injection**. The extension host (`DotNetForge.Extensions`) registers each enabled extension's services into the DI container so the core can resolve them at runtime.
- Supported extension types: **Theme, Authentication provider, Connector, Library, Admin extension, Widget, Provider, Plugin, Module.** Extensions can customize almost every part of the CMS, including dashboard widgets, page modules, authentication providers, connectors, and dynamic-route handlers.
- The CMS supports marketplace-style installation through an external API.

### Manifest & Lifecycle

Each extension must include a manifest file named `dotnetforge.extension.json`. The CMS must validate manifests before installing; invalid, unsafe, or incompatible extensions must not be installed. The required manifest fields are:

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `id` | string | Yes | Unique extension identifier (e.g., `dotnetforge.theme.default`). |
| `name` | string | Yes | Human-readable name. |
| `description` | string | Yes | What the extension does. |
| `version` | string | Yes | Semantic version of the extension. |
| `type` | string | Yes | One of the supported extension types. |
| `author` | string | Yes | Author or vendor. |
| `entryPoint` | string | Yes | The entry class/component the host loads. |
| `permissions` | string[] | Yes | Permissions the extension requires/grants. |

> Note: the source manifest example contained two errors - a duplicated `version` key and a misspelled `vrsion` field. The correct, single field is **`version`**. Optional fields such as `website`, `license`, `dependencies`, `routes`, and `settings` may also appear. The full manifest schema, validation rules, and lifecycle (install, update, enable, disable, remove) are owned by the [Extension System](extensions.md) and [Extension Manifest](extensions.md) specs.

## Database Architecture

- Persistence uses **Entity Framework Core**, isolated in `DotNetForge.Data`.
- Two providers are supported: **SQLite** (default) and **PostgreSQL**. The active provider is selected from `.env`:

```env
DATABASE_PROVIDER=sqlite
DATABASE_PROVIDER=postgresql
```

- All schema changes go through **EF Core migrations**. The application must apply pending migrations on startup (or via an explicit migration step) so SQLite and PostgreSQL stay in sync from the same migration set.
- SQL injection is prevented by EF Core and parameterized queries (see [Security](security.md)).
- The database supports **export** and **import**. During import/export the user must select the **source** and **target** database type, making transfer **database-provider aware**. The Transfer subsystem owns this flow - see [Transfer](transfer_updates.md).
- Data must be scoped per tenant where applicable to prevent data leakage between tenants. See [Multi-Tenancy](multi_tenancy.md).

## Authentication Architecture

- Authentication is pluggable: providers are registered through DI and can be added via **Authentication provider** extensions placed in `extensions/authentication/`.
- The default `Email` provider is built in; external providers (Auth0, GitHub, Google, Microsoft, and others) are configured per provider with Name, Status, and a Settings action.
- Passwords use secure hashing; login is protected by rate limiting and account lockout after repeated failed attempts.
- Advanced user settings (default role for authenticated users, one account per email, sign-up enablement, email confirmation) govern registration behavior.
- The full provider list, provider configuration, and advanced settings are owned by [Authentication Providers](authentication.md) and [Advanced User Settings](authentication.md).

## Authorization & RBAC

- The CMS enforces **role-based access control**. Permission checks are required for **every** admin and API action.
- Default roles, from most-privileged to least: **Super Admin, Admin, Editor, Author, Authenticated, Public.**
- Permissions are organized by area: **Collection types, Single types, Plugins, Settings, Extensions, Media, Users, Roles, API, Webhooks.**
- Roles can be created, edited, duplicated, deleted, and assigned to users; permissions are assigned to roles.
- Authorization is tenant-aware: global Super Admins manage all tenants, while tenant admins manage only assigned tenants.
- The complete role/permission model is owned by [User Roles & Permissions](user_roles_permissions.md).

## Content System

- The content system is implemented in `DotNetForge.Core` and persisted via `DotNetForge.Data`.
- Pages are organized as a tree and support multiple page types (Standard, Existing page, URL redirect, File), rich page fields, per-page and per-role/user permissions, theme/layout selection, a page builder with built-in and extension modules, preview mode, draft/published versions, and content history with rollback.
- Dynamic routing supports bracket-style parameters (e.g., `/blog/[slug]`) and resolves content, modules, controllers, or extension-provided handlers at runtime, always within the resolved tenant scope.
- Detail is owned by [Content Manager](content_manager.md), [Content History](content_manager.md), and [Dynamic Routes](dynamic_routes.md).

## Media System

- Media is managed by core services with storage abstracted behind an interface: **local storage** is the default, and **external storage connectors** are supplied through extensions in `extensions/connectors/`.
- The system validates file type and size, supports configurable allowed types and max upload size, marks files public/private with private-media access control, and can scan or block unsafe uploads.
- Optional processing includes responsive variants (small/medium/large), size optimization, and EXIF auto-orientation.
- Detail is owned by [File Manager](file_manager.md).

## Theme System

- Themes affect **only public-facing CMS pages**. **The admin area is never affected by public frontend themes**; the setup page and (unless explicitly configured) the login page are also unaffected.
- Themes are Theme-type extensions in `extensions/themes/` and provide layouts, page templates, static assets, theme settings, a theme manifest, and theme preview.
- Themes can be selected per page, a default site theme can be set, and themes can be tenant-specific.
- Detail is owned by [Themes](themes.md).

## API System

- The API (`DotNetForge.Api`) exposes the full CMS for **Headless** and **Hybrid** modes: content, media, users, roles, settings, extensions, dynamic routes, and custom extension permissions.
- Access is controlled by **API tokens** with granular permissions. Tokens are securely generated and stored as **hashes**.
- API requests resolve tenant context from domain, path, header, or token, and tokens must not access other tenants unless explicitly granted.
- Rate limiting and input validation apply to all API endpoints. See [Security](security.md).
- Token management and the permission set are owned by [API Tokens](api_tokens.md); event delivery is owned by [Webhooks](webhooks.md).

## Update & Rollback System

The update and rollback architecture is what enables core updates without breaking extensions, drawing on the separation defined above and the architectural aspects of source section 3.

- Core CMS files and custom extensions are clearly separated; updates apply only to `src/`/core artifacts and must **not** overwrite the `extensions/` folder.
- The CMS supports **update checks**.
- The CMS supports **rollback to the previous working version**.
- **Backups are created before updates** and stored under `storage/backups/`. Update packages stage under `storage/updates/`.
- **Failed updates automatically restore the previous version where possible.**
- Database changes ship as EF Core migrations so updates remain provider-aware across SQLite and PostgreSQL.
- Update and rollback must be safe by design. The end-user-facing update workflow is also surfaced in the [Overview Settings](settings.md) page.

## Security Model

Security is prioritized from the beginning and enforced across every layer. Key architectural guarantees include secure password hashing, CSRF/XSS protection, SQL-injection prevention through parameterized queries and EF Core, RBAC with permission checks on every admin and API action, secure API token generation with hashed storage, rate limiting and account lockout, secure file-upload validation with configurable types/sizes, private media access control, audit logging, webhook signing, extension manifest validation, prevention of unsafe extension loading, environment-variable-based secrets that are never committed to Git, secure headers, HTTPS, and SameSite/HttpOnly/Secure cookies. The complete control set is owned by [Security](security.md).

## Testing Strategy

- Tests live in `tests/`, split into `DotNetForge.Tests` (unit) and `DotNetForge.IntegrationTests` (integration).
- **Unit tests** must cover core services, permissions, the content manager, media validation, API token generation, extension manifest validation, webhook signing, and import/export.
- **Integration tests** must cover the setup flow, authentication, and API endpoints.
- The project must include a clear test project structure, example unit tests, and instructions for running tests.
- Frontend linting (ESLint when JavaScript/TypeScript is used) and code-formatting/naming rules support code quality. Detail is owned by the testing and code-quality specs.

## Deployment Strategy

- The application is **cross-platform** (Windows, macOS, Linux) and configured entirely through `.env`; no secrets are baked into the build.
- Deployment must run EF Core migrations against the configured provider (SQLite or PostgreSQL) before serving traffic.
- HTTPS must be supported, with Secure cookies enabled in production. See [Security](security.md).
- Runtime artifacts (`storage/`) must be writable and persisted across deployments, while remaining excluded from Git.
- The repository must stay small; build output, logs, uploads, cache, and secrets are excluded via `.gitignore`.

## Cross-Linked Specifications

| Subsystem | Detailed Spec |
| --- | --- |
| Extension system & manifest | [Extension System](extensions.md), [Extension Manifest](extensions.md) |
| Roles & permissions | [User Roles & Permissions](user_roles_permissions.md) |
| Content & history | [Content Manager](content_manager.md), [Content History](content_manager.md) |
| Routing & tenancy | [Dynamic Routes](dynamic_routes.md), [Multi-Tenancy](multi_tenancy.md) |
| Media | [File Manager](file_manager.md) |
| Themes | [Themes](themes.md) |
| API & events | [API Tokens](api_tokens.md), [Webhooks](webhooks.md) |
| Auth | [Authentication Providers](authentication.md), [Advanced User Settings](authentication.md) |
| Transfer | [Transfer](transfer_updates.md) |
| Settings | [Overview Settings](settings.md) |
| Security | [Security](security.md) |

## Acceptance Criteria

- [ ] The solution reproduces the suggested project structure tree exactly, including all `src/`, `tests/`, `extensions/`, `storage/`, `agents/`, `docs/`, and root files.
- [ ] Each `src/` project (`DotNetForge.Web`, `.Core`, `.Data`, `.Api`, `.Abstractions`, `.Infrastructure`, `.Extensions`, `.Shared`) has its documented responsibility and dependency direction, with `Abstractions` containing only contracts.
- [ ] Core CMS files (`src/`) and custom extensions (`extensions/`) are clearly separated, and the `extensions/` folder is partitioned by type.
- [ ] Updates never overwrite the `extensions/` folder.
- [ ] Extension points are expressed as interfaces in `DotNetForge.Abstractions` and resolved via dependency injection.
- [ ] The extension host validates `dotnetforge.extension.json` manifests before install and rejects invalid, unsafe, or incompatible extensions.
- [ ] The required manifest fields are exactly `id`, `name`, `description`, `version`, `type`, `author`, `entryPoint`, `permissions` (no duplicated or misspelled `version` key).
- [ ] EF Core is the only persistence mechanism, with migrations driving all schema changes for both SQLite (default) and PostgreSQL.
- [ ] The database provider is selected from `.env` (`sqlite` or `postgresql`).
- [ ] Import/export requires selecting source and target database types and is provider-aware.
- [ ] Authentication providers are pluggable via DI and extensions; the `Email` provider is built in.
- [ ] RBAC enforces permission checks on every admin and API action, with default roles Super Admin, Admin, Editor, Author, Authenticated, Public.
- [ ] Permission areas cover Collection types, Single types, Plugins, Settings, Extensions, Media, Users, Roles, API, Webhooks.
- [ ] The content, media, theme, and API systems are implemented in their documented layers and cross-link their detailed specs.
- [ ] The admin area is never affected by public frontend themes.
- [ ] Tenant resolution occurs first (domain, subdomain, or path prefix), then page/dynamic-route resolution within that tenant's scope.
- [ ] The update system supports update checks, pre-update backups under `storage/backups/`, rollback to the previous working version, and automatic restore on failed updates where possible.
- [ ] The security model (hashing, CSRF/XSS/SQLi protection, hashed API tokens, rate limiting, lockout, upload validation, manifest validation, secure headers/cookies, HTTPS) is implemented per [Security](security.md).
- [ ] Secrets live in `.env` and are never committed to Git; `storage/`, build output, logs, uploads, and cache are excluded via `.gitignore`.
- [ ] Unit tests cover core services, permissions, content manager, media validation, API token generation, manifest validation, webhook signing, and import/export; integration tests cover setup, authentication, and API endpoints.
- [ ] The application builds and runs cross-platform on Windows, macOS, and Linux, applying migrations before serving traffic.
- [ ] `ARCHITECTURE.md` documents project structure, core CMS architecture, extension architecture, database architecture, authentication architecture, authorization & RBAC, content system, media system, theme system, API system, update & rollback system, security model, testing strategy, and deployment strategy.
