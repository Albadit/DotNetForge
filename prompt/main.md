# DotNetForge CMS - Product Overview

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

DotNetForge CMS is a modular, secure, lightweight, and developer-friendly **hybrid CMS** built on ASP.NET Core MVC. This document is the front door to the specification: it defines the product goal, the three operating modes, the technology stack, the project deliverables, and a map of every other module in this folder.

## Purpose

DotNetForge CMS exists to deliver a single application that can act as a **traditional CMS**, a **headless CMS**, or both at once (**hybrid**), without forcing a team to choose up front. The system must be modular, secure, lightweight, cross-platform, easy to update, and developer-friendly. The core CMS is kept strictly separate from user-created extensions so the platform can evolve and be updated without breaking customizations.

### Operating Modes

The system must support three modes:

| Mode | Description |
| --- | --- |
| **Traditional CMS** | A visual admin UI similar to DNN, WordPress, or Umbraco. Users create pages, manage menus, upload media, install themes, and manage content through the [Admin Area](admin_area.md). |
| **Headless CMS** | Content, media, users, pages, and extensions are managed entirely through APIs. Access is governed by [API Tokens](api_tokens.md) and granular permissions. |
| **Hybrid CMS** | The visual CMS and the API operate at the same time over the same content. |

### Target Users

- **Developers & agencies** building and shipping websites on top of a clean, extensible ASP.NET Core MVC foundation.
- **Multi-site / multi-tenant operators** running many sites from one application and database (see [Multi-Tenancy](multi_tenancy.md)).
- **Content & editorial teams** who work day-to-day in the visual admin (see [Content Manager](content_manager.md) and the [Dashboard](dashboard.md)).
- **Integrators** consuming content programmatically through the headless API using scoped tokens (see [API Tokens](api_tokens.md)).

## Main Features

### Technology Stack (Source §1)

The platform must be built with the following technologies:

- ASP.NET Core MVC
- C#
- Entity Framework Core
- Razor Views
- SQLite support (default)
- PostgreSQL support
- Environment-based configuration using `.env`
- Unit testing support
- Frontend linting support, such as ESLint
- GitHub-compatible project structure
- Cross-platform development support for Windows, macOS, and Linux

Configuration is environment-based and lives in `.env`. **Secrets live in `.env` and must never be committed to Git.** See [Installation & Setup](installation_setup.md) for the full configuration reference and [Security](security.md) for secret-handling rules.

### Project Deliverables (Source §1)

The project must include the following top-level deliverables:

- `.env.example`
- `.gitignore`
- `README.md`
- `ARCHITECTURE.md`
- Developer documentation
- Extension development documentation
- Unit test project
- Agents documentation folder

The full generated solution is enumerated in [Deliverables](#deliverables-source-33) below, with structure detailed in [Architecture](architecture.md).

### Admin Area Summary (Source §6)

The [Admin Area](admin_area.md) is the visual control center for the Traditional and Hybrid modes. **The admin area is never affected by public frontend [themes](themes.md);** themes only affect public-facing pages created inside the CMS. The admin sidebar is organized into the following groups (see [Admin Area](admin_area.md) for the complete, linkable menu):

- **Main** - [Dashboard](dashboard.md), [Content Manager](content_manager.md), File Manager ([File Manager](file_manager.md)), Marketplace ([Extensions](extensions.md)).
- **Global Settings** - Overview, [API Tokens](api_tokens.md), Content History, [Internationalization](internationalization.md), File Manager, Plugins, Transfer, [Webhooks](webhooks.md).
- **Administration Panel** - [Roles](user_roles_permissions.md), Users, [Audit Logs](audit_logs.md).
- **Email** - Configuration and Templates (see [Email](email.md)).
- **Users & Permissions Plugin** - Roles, Providers, Advanced Settings (see [Authentication](authentication.md) and [User Roles & Permissions](user_roles_permissions.md)).

## Module Map

This specification is split into the files below. Every module is listed here with a one-line description and a relative link.

| File | Description |
| --- | --- |
| [main.md](main.md) | Product overview, three modes, tech stack, deliverables, MVP scope (this file). |
| [installation_setup.md](installation_setup.md) | First-time install, `.env` configuration, database providers, and the registration/setup flow. |
| [admin_area.md](admin_area.md) | Admin shell, full sidebar menu, and admin-vs-frontend separation. |
| [dashboard.md](dashboard.md) | Dashboard widgets (statistics, activity, health, updates) and custom widget extension points. |
| [content_manager.md](content_manager.md) | Page tree, page types/fields, page builder, drafts, preview, and content rollback. |
| [dynamic_routes.md](dynamic_routes.md) | Bracket-style dynamic routing, route fields, conflict prevention, and extension route handlers. |
| [multi_tenancy.md](multi_tenancy.md) | Tenant resolution, per-tenant scoping, tenant admins, and tenant-aware admin context. |
| [file_manager.md](file_manager.md) | File Manager: upload, organize, public/private access, validation, and image processing. |
| [extensions.md](extensions.md) | Extension system, types, manifest format, marketplace, and validated loading. |
| [user_roles_permissions.md](user_roles_permissions.md) | RBAC, default roles, role fields, and the permission areas. |
| [authentication.md](authentication.md) | Authentication providers, advanced user settings, sign-up and email-confirmation rules. |
| [api_tokens.md](api_tokens.md) | API token creation, fields, scoped permissions, and hashed storage. |
| [webhooks.md](webhooks.md) | Webhook creation, events, headers, signing, retries, and delivery logging. |
| [internationalization.md](internationalization.md) | Locales, default-locale rules, and content localization. |
| [email.md](email.md) | SMTP configuration, test email, and default email templates. |
| [themes.md](themes.md) | Public theme system, layouts, templates, manifest, and per-page theme selection. |
| [settings.md](settings.md) | Overview/global settings, plugins page, and logo uploads. |
| [transfer_updates.md](transfer_updates.md) | Database import/export transfer, plus core update, rollback, and backup flows. |
| [audit_logs.md](audit_logs.md) | Audit log fields and tracked admin/system actions. |
| [security.md](security.md) | Security requirements: hashing, CSRF/XSS, RBAC checks, rate limiting, secure headers, and more. |
| [architecture.md](architecture.md) | Project structure, core/extension separation, and architectural documentation. |
| [developer_docs.md](developer_docs.md) | Developer setup guides, extension/theme/module authoring, and the agents folder. |
| [testing_quality.md](testing_quality.md) | Unit/integration testing requirements, linting, and code-quality standards. |

## User Flows

### Flow: Install → First Content → Publish

1. The operator clones the repository, copies `.env.example` to `.env`, and configures the database provider and admin credentials (see [Installation & Setup](installation_setup.md)).
2. On first start, the system checks whether the CMS is already installed; if not, it redirects to the registration/setup page.
3. The operator completes setup; the system creates the first admin user, assigns the **Super Admin** role, marks the CMS as installed, and redirects to the [Dashboard](dashboard.md).
4. From the [Content Manager](content_manager.md), an Editor or Author creates a page, fills in its fields (slug, title, SEO metadata), and places modules with the page builder.
5. The author saves a **draft** and uses **preview mode** to verify the result.
6. An authorized user sets the page to **Published** (optionally via a scheduled publish date), and the page becomes available on the public frontend under the active [theme](themes.md).

### Flow: Headless API Consumer

1. An admin opens **API Tokens** and creates a token scoped to the required [permission areas](user_roles_permissions.md) (e.g. `content.read`, `media.read`). See [API Tokens](api_tokens.md).
2. The system returns the token once; only its hash is stored.
3. The integrator calls the API with the token in the request. The system resolves tenant context (from domain, path, header, or token) and authorizes the request against the token's permissions (see [Multi-Tenancy](multi_tenancy.md)).
4. The integrator reads, and where granted creates/updates/deletes, content, media, and dynamic routes through the API.
5. Relevant actions emit [webhooks](webhooks.md) and are recorded in [Audit Logs](audit_logs.md).

## Role & Permission Rules

The default roles, from most-privileged to least, are: **Super Admin, Admin, Editor, Author, Authenticated, Public**. Full definitions and the complete permission areas (Collection types, Single types, Plugins, Settings, Extensions, Media, Users, Roles, API, Webhooks) live in [User Roles & Permissions](user_roles_permissions.md). The product-level summary:

| Capability | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | --- | --- | --- | --- | --- | --- |
| Complete the first-time setup (becomes first admin) | Yes (created) | No | No | No | No | No |
| Manage all tenants / switch tenant context | Yes (global) | Assigned tenants only | No | No | No | No |
| Configure settings, security, providers, extensions | Yes | Yes | No | No | No | No |
| Manage users, roles, and permissions | Yes | Yes | No | No | No | No |
| Create/publish any content | Yes | Yes | Yes | Own + assigned | No | No |
| Create/edit own content | Yes | Yes | Yes | Yes (own only) | No | No |
| Access the public site / public pages | Yes | Yes | Yes | Yes | Yes | Yes |
| Access private/authenticated-only pages | Yes | Yes | Yes | Yes | Yes | No |

Every admin and API action must enforce a permission check (see [Security](security.md)).

## Validation Rules

- `DATABASE_PROVIDER` must be one of `sqlite` or `postgresql`; any other value is rejected at startup (see [Installation & Setup](installation_setup.md)).
- Required deliverable files (`.env.example`, `.gitignore`, `README.md`, `ARCHITECTURE.md`) must be present in the repository root.
- Secrets must be supplied through `.env` and must be excluded from version control via `.gitignore`; the repository must never contain committed secrets.
- The application must detect first-run state and must not allow normal operation until the CMS is marked installed.
- The first registered user must be assigned the **Super Admin** role exactly once during setup.
- Generated files, logs, uploads, cache, and build output must be excluded from Git to keep the repository as small as possible.
- All forms and API inputs must be validated server-side; the system must reject malformed input rather than persisting it (see [Security](security.md)).

## Edge Cases

- **Already installed:** If the install check finds the CMS is already installed, the setup/registration page must be unreachable and must redirect to the [Dashboard](dashboard.md) or login.
- **Invalid database provider:** If `DATABASE_PROVIDER` is empty or unsupported, the application must fail fast with a clear configuration error instead of starting in an undefined state.
- **Frontend theme bleed:** A misconfigured or broken public [theme](themes.md) must never alter or break the admin area; the admin shell renders independently of frontend themes.
- **Tenant resolution precedence:** The tenant is resolved **first** (by domain, subdomain, or path prefix); only then is the page or dynamic route resolved within that tenant's scope. A request that matches no tenant must fall back to the default tenant or a 404 (see [Multi-Tenancy](multi_tenancy.md)).
- **Mode overlap (Hybrid):** When the visual CMS and the API operate simultaneously over the same content, both paths must enforce the same permission and validation rules so neither can bypass the other.
- **Extension vs. core update:** A core update must not overwrite user-created extensions; updates that fail must roll back to the previous working version where possible (see [Transfer & Updates](transfer_updates.md)).
- **Committed secrets:** If a secret is accidentally placed outside `.env`, the `.gitignore` rules and review process must prevent it from being committed.

## Acceptance Criteria

- [ ] The application runs on Windows, macOS, and Linux from the same codebase.
- [ ] The CMS operates in Traditional, Headless, and Hybrid modes against the same content.
- [ ] `.env.example`, `.gitignore`, `README.md`, and `ARCHITECTURE.md` exist in the repository root.
- [ ] Copying `.env.example` to `.env` and configuring it allows the app to start; missing or invalid `DATABASE_PROVIDER` produces a clear error.
- [ ] First start with no install redirects to setup; completing setup creates a **Super Admin**, marks the CMS installed, and redirects to the dashboard.
- [ ] Both SQLite (default) and PostgreSQL providers are selectable and functional.
- [ ] The admin area renders correctly regardless of the active public frontend theme.
- [ ] A scoped API token can read content/media in Headless mode, and its raw value is never persisted (only the hash).
- [ ] Every admin and API action is gated by a permission check.
- [ ] The generated solution includes all deliverables listed in [Deliverables](#deliverables-source-33) and is modular, secure, and ready for future expansion.
- [ ] No secrets are committed to Git; generated files, logs, uploads, cache, and build output are ignored.

## MVP Scope (Foundation)

The Minimum Viable Product is the **foundation** set of deliverables. These are the items that must ship in the first cut; deeper capabilities are layered on top by the linked modules.

| MVP Item | Owning Module |
| --- | --- |
| ASP.NET Core MVC solution | [Architecture](architecture.md) |
| Working setup / registration flow | [Installation & Setup](installation_setup.md) |
| Admin dashboard | [Dashboard](dashboard.md) |
| Admin sidebar / shell | [Admin Area](admin_area.md) |
| Authentication | [Authentication](authentication.md) |
| RBAC permissions | [User Roles & Permissions](user_roles_permissions.md) |
| Content manager foundation | [Content Manager](content_manager.md) |
| File Manager foundation | [File Manager](file_manager.md) |
| API token foundation | [API Tokens](api_tokens.md) |
| Extension manifest system | [Extensions](extensions.md) |
| Extension loading foundation | [Extensions](extensions.md) |
| SQLite and PostgreSQL provider support | [Installation & Setup](installation_setup.md) |
| Import/export foundation | [Transfer & Updates](transfer_updates.md) |
| Webhook foundation | [Webhooks](webhooks.md) |
| Audit logging foundation | [Audit Logs](audit_logs.md) |
| Theme foundation | [Themes](themes.md) |
| Documentation | [Developer Docs](developer_docs.md) |
| Agents folder | [Developer Docs](developer_docs.md) |
| Unit tests | [Testing & Quality](testing_quality.md) |
| `.env.example`, `.gitignore`, `ARCHITECTURE.md`, developer documentation | [Architecture](architecture.md) / [Developer Docs](developer_docs.md) |

## Deliverables (Source §33)

Generate the complete project with:

- ASP.NET Core MVC solution
- Working setup/registration flow
- Admin dashboard
- Admin sidebar
- Authentication
- RBAC permissions
- Content manager foundation
- File Manager foundation
- API token foundation
- Extension manifest system
- Extension loading foundation
- SQLite and PostgreSQL provider support
- Import/export foundation
- Webhook foundation
- Audit logging foundation
- Theme foundation
- Documentation
- Agents folder
- Unit tests
- `.env.example`
- `.gitignore`
- `ARCHITECTURE.md`
- Developer documentation

The generated project must be clean, modular, secure, and ready for future expansion.

## Implementation Notes (Source §34)

- Keep the CMS core separate from user extensions.
- Do not let frontend themes affect the admin area.
- Prioritize security from the beginning (see [Security](security.md)).
- Make the project lightweight.
- Avoid unnecessary dependencies.
- Use clear folder boundaries (see [Architecture](architecture.md)).
- Use interfaces for extension points (see [Extensions](extensions.md)).
- Use dependency injection.
- Use database migrations.
- Validate extension manifests before installation. The manifest file is named `dotnetforge.extension.json`, and its required fields are `id`, `name`, `description`, `version`, `type`, `author`, `entryPoint`, and `permissions` (see [Extensions](extensions.md)).
- Make update and rollback safe (see [Transfer & Updates](transfer_updates.md)).
- Make import/export database-provider aware.
- Make API permissions granular (see [API Tokens](api_tokens.md)).
- Ensure everything important is documented (see [Developer Docs](developer_docs.md)).
