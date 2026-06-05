# Developer Documentation & AI Agent Guide

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

This file defines the developer onboarding guide for **DotNetForge CMS** and the `/agents` documentation folder consumed by AI coding agents. It covers project setup, local execution, database configuration, building every extension type, adding API endpoints and permissions, testing, linting, and packaging/installing/updating extensions.

## Purpose

DotNetForge CMS is a modular **hybrid CMS** built with ASP.NET Core MVC (C#, Entity Framework Core, Razor Views), running cross-platform on Windows, macOS, and Linux. Because the extension system is central to the product, a developer or an AI coding agent must be able to set up the project, run it locally, and build any supported extension type from this document alone.

This file owns two concerns from the source specification:

1. **Developer Documentation** - how to set up, run, extend, test, lint, and package the CMS.
2. **The `/agents` folder** - the AI-agent knowledge base that lets coding agents understand and safely extend the system.

For deeper detail on shared concerns, follow the cross-links:

- System layout and core architecture: [Architecture](architecture.md)
- Extension types, manifest, and loading: [Extension System](extensions.md)
- Testing strategy and code-quality rules: [Testing & Quality](testing_quality.md)
- Roles, permission areas, and RBAC: [User Roles & Permissions](user_roles_permissions.md)
- Security model and secret handling: [Security](security.md)
- API tokens and scopes: [API Tokens](api_tokens.md)

---

## Prerequisites

The following tooling must be installed before setting up DotNetForge CMS:

| Tool | Purpose | Required |
| --- | --- | --- |
| .NET SDK (ASP.NET Core) | Build and run the C# / MVC solution | Yes |
| Git | Clone the repository and manage version control | Yes |
| SQLite | Default development database (zero-config, file-based) | Yes (default) |
| PostgreSQL | Production-grade relational database | Optional |
| Node.js + npm | Frontend tooling and ESLint linting | Optional (frontend assets) |
| EF Core CLI (`dotnet-ef`) | Apply and manage database migrations | Recommended |

The CMS is cross-platform. All commands below work on Windows, macOS, and Linux.

---

## How to Set Up the Project

1. Clone the repository from GitHub.
2. Restore dependencies and build the solution from the repository root.
3. Copy the environment template to a real environment file.
4. Configure the database provider and admin credentials (see [Configuring `.env`](#how-to-configure-env)).
5. Apply database migrations to create the schema.
6. Run the CMS locally (see [Running the CMS Locally](#how-to-run-the-cms-locally)).

```bash
# 1. Clone
git clone <repository-url> DotNetForgeCMS
cd DotNetForgeCMS

# 2. Restore & build
dotnet restore
dotnet build

# 3. Create your environment file
cp .env.example .env        # macOS/Linux
copy .env.example .env      # Windows

# 4. Install frontend tooling (only if working on frontend assets)
npm install

# 5. Apply database migrations
dotnet ef database update --project src/DotNetForge.Data --startup-project src/DotNetForge.Web
```

The solution follows the structure defined in [Architecture](architecture.md), with `src/` for core projects, `tests/` for the test projects, `extensions/` for user extensions, `storage/` for runtime data, and `agents/` for the AI-agent docs.

---

## How to Run the CMS Locally

Run the web project from the repository root:

```bash
dotnet run --project src/DotNetForge.Web
```

The application starts at the URL defined by `APP_URL` in `.env` (default `http://localhost:5000`).

On first run, the CMS detects that it is **not yet installed** and redirects to the setup/registration page. Completing setup creates the first admin user, assigns the **Super Admin** role, marks the CMS as installed, and redirects to the admin dashboard. The full first-time installation flow is owned by [Installation & Setup](installation_setup.md).

---

## How to Configure `.env`

DotNetForge CMS uses environment-based configuration. Copy `.env.example` to `.env` and set the values for your environment. The `.env` file holds secrets and **must never be committed to Git** - it is excluded via `.gitignore`. See [Security](security.md) for secret-handling rules.

```env
DATABASE_PROVIDER=sqlite
DATABASE_CONNECTION_STRING=
APP_NAME=DotNetForge CMS
APP_URL=http://localhost:5000
```

| Variable | Description | Example |
| --- | --- | --- |
| `DATABASE_PROVIDER` | Database engine: `sqlite` or `postgresql`. | `sqlite` |
| `DATABASE_CONNECTION_STRING` | Provider-specific connection string. Leave empty to use the SQLite default. | (see below) |
| `APP_NAME` | Display name of the application. | `DotNetForge CMS` |
| `APP_URL` | Base URL the app binds to and uses for generated links. | `http://localhost:5000` |

Supported database providers:

```env
DATABASE_PROVIDER=sqlite
DATABASE_PROVIDER=postgresql
```

> Secrets such as `DATABASE_CONNECTION_STRING` and SMTP credentials live only in `.env`. Never commit them to Git.

---

## How to Use SQLite

SQLite is the **default** provider and requires no separate database server, making it ideal for local development.

1. Set the provider in `.env`:

   ```env
   DATABASE_PROVIDER=sqlite
   DATABASE_CONNECTION_STRING=Data Source=storage/dotnetforge.db
   ```

   Leaving `DATABASE_CONNECTION_STRING` empty falls back to the built-in default SQLite path.

2. Apply migrations to create the database file and schema:

   ```bash
   dotnet ef database update --project src/DotNetForge.Data --startup-project src/DotNetForge.Web
   ```

3. Run the CMS. The SQLite database file is created under `storage/` and is excluded from Git via `.gitignore`.

---

## How to Use PostgreSQL

PostgreSQL is the recommended provider for production and multi-tenant deployments.

1. Create a PostgreSQL database and a user with privileges on it.

2. Set the provider and connection string in `.env`:

   ```env
   DATABASE_PROVIDER=postgresql
   DATABASE_CONNECTION_STRING=Host=localhost;Port=5432;Database=dotnetforge;Username=dotnetforge;Password=<secret>
   ```

3. Apply migrations against PostgreSQL:

   ```bash
   dotnet ef database update --project src/DotNetForge.Data --startup-project src/DotNetForge.Web
   ```

4. Run the CMS. The provider is selected at startup from `DATABASE_PROVIDER`, so switching engines requires only an `.env` change plus a migration.

Moving data between SQLite and PostgreSQL is handled by the provider-aware import/export tooling described in [Transfer](transfer_updates.md). EF Core uses parameterized queries throughout to protect against SQL injection (see [Security](security.md)).

---

## How to Create an Extension

Extensions are the primary customization surface of DotNetForge CMS. The supported extension types are: **Theme, Authentication provider, Connector, Library, Admin extension, Widget, Provider, Plugin, Module**. The full type catalog, loading model, and validation rules are owned by [Extension System](extensions.md).

Every extension lives under the `extensions/` folder (separated from the core CMS so updates never overwrite it) and must include a manifest named **`dotnetforge.extension.json`**.

### Extension Manifest

The manifest tells the CMS what the extension is, what it does, and how to load it. The CMS validates every manifest before installation; invalid, unsafe, or incompatible extensions must not be installed.

```json
{
  "id": "dotnetforge.theme.default",
  "name": "Default Theme",
  "description": "The default frontend theme for DotNetForge CMS.",
  "version": "1.0.0",
  "type": "theme",
  "author": "DotNetForge",
  "website": "https://example.com",
  "license": "MIT",
  "entryPoint": "DefaultTheme",
  "dependencies": [],
  "permissions": [
    "content.read",
    "media.read"
  ],
  "routes": [],
  "settings": {
    "supportsLayouts": true,
    "supportsDarkMode": true
  }
}
```

**Required manifest fields:**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `id` | string | Yes | Globally unique extension identifier (e.g. `dotnetforge.theme.default`). |
| `name` | string | Yes | Human-readable display name. |
| `description` | string | Yes | Short description of what the extension does. |
| `version` | string | Yes | Semantic version (e.g. `1.0.0`). A single `version` field only. |
| `type` | string | Yes | One of the supported extension types. |
| `author` | string | Yes | Extension author or organization. |
| `entryPoint` | string | Yes | The class/component the CMS loads to activate the extension. |
| `permissions` | string[] | Yes | Permissions the extension requires (see [Adding Permissions](#how-to-add-permissions)). |

**Optional fields** such as `website`, `license`, `dependencies`, `routes`, and `settings` may also be supplied as shown above.

> Manifest correctness note: the manifest declares `version` exactly **once**. Do not add a duplicate `version` key or a misspelled `vrsion` key - there is a single, correctly spelled `version` field.

### Creating an extension

1. Create a folder under the appropriate `extensions/` subfolder (`themes/`, `plugins/`, `modules/`, `widgets/`, `providers/`, `connectors/`, `authentication/`, `libraries/`, `admin/`).
2. Add a valid `dotnetforge.extension.json` manifest.
3. Implement the `entryPoint` against the relevant extension interface (extension points use interfaces and dependency injection).
4. Declare required `permissions` in the manifest.
5. Test, then package and install (see later sections).

---

## How to Create a Theme

Themes affect **only public-facing CMS pages**. The **admin area is never affected by public frontend themes** (nor are the setup page or the login page unless explicitly configured).

1. Create a theme folder under `extensions/themes/`.
2. Add a manifest with `"type": "theme"`.
3. Provide layouts, page templates, and static assets.
4. Expose theme settings (e.g. `supportsLayouts`, `supportsDarkMode`) via the manifest `settings` block.
5. Support theme preview, per-page theme selection, and a default site theme.

Full theme capabilities (layout support, page templates, theme manifest, preview, per-page selection, multi-tenant theming) are owned by [Themes](themes.md).

---

## How to Create a Module

Modules are placed onto pages by the page builder. The CMS ships a built-in Razor module, and extension modules can be added.

1. Create a module folder under `extensions/modules/`.
2. Add a manifest with `"type": "module"`.
3. Implement the module against the module extension interface; render output via Razor.
4. Declare the permissions the module needs to read or write content.
5. The module becomes available in the page builder so editors can place it on pages.

Modules may also act as targets for dynamic routes. Page-builder behavior and page fields are owned by [Content Manager](content_manager.md); dynamic-route handlers are owned by [Dynamic Routes & Multi-Tenancy](dynamic_routes.md).

---

## How to Create a Dashboard Widget

The dashboard renders widgets (project, content, user, and media statistics; recent activity; installed extensions; system health; update status). Developers and users can add custom widgets through the extension system.

1. Create a widget folder under `extensions/widgets/`.
2. Add a manifest with `"type": "widget"`.
3. Implement the widget against the dashboard widget interface and set its `entryPoint`.
4. Declare any data permissions the widget needs to read.
5. Once installed and enabled, the widget appears as a placeable dashboard tile.

Dashboard composition is owned by [Dashboard](dashboard.md).

---

## How to Create an Authentication Provider

Authentication providers (e.g. Email, Auth0, GitHub, Google, Microsoft, and others) can be added through extensions.

1. Create a provider folder under `extensions/authentication/`.
2. Add a manifest with `"type": "authentication provider"`.
3. Implement the authentication provider interface and set its `entryPoint`.
4. Expose provider configuration (client ID/secret, callback URLs) through provider settings; store secrets in `.env`, never in code or Git.
5. The provider appears in the providers list with **Name**, **Status**, and a **Settings/Edit** action.

The built-in provider catalog and advanced user settings are owned by [Authentication Providers](authentication.md).

---

## How to Create a Connector

Connectors integrate external services - most commonly external/cloud storage backends for the File Manager.

1. Create a connector folder under `extensions/connectors/`.
2. Add a manifest with `"type": "connector"`.
3. Implement the connector interface (for storage connectors, the external storage abstraction) and set its `entryPoint`.
4. Declare the permissions the connector requires (e.g. `media.read`, `media.write`).
5. Once installed, the connector becomes selectable wherever its capability is consumed (e.g. as a media storage backend).

External storage connectors for media are referenced by [File Manager](file_manager.md).

---

## How to Create API Endpoints

DotNetForge CMS exposes its functionality over an API so the CMS can run headless or hybrid. Extensions may register additional API endpoints.

1. Create an API controller/handler in the API project (`src/DotNetForge.Api`) or within an extension that declares `routes` in its manifest.
2. Use attribute routing; support multi-tenant resolution where applicable (tenant context resolves from domain, path, header, or token - see [Dynamic Routes & Multi-Tenancy](dynamic_routes.md)).
3. Protect every endpoint with permission checks. API access is governed by API tokens scoped to permissions; see [API Tokens](api_tokens.md).
4. Validate all input on every endpoint (see [Security](security.md)).
5. Add unit/integration tests for the endpoint (see [Testing & Quality](testing_quality.md)).

Every admin and API action must enforce permission checks - there are no unauthenticated privileged endpoints.

---

## How to Add Permissions

Permissions are required by the manifest and enforced on every admin and API action. They are organized into the permission areas: **Collection types, Single types, Plugins, Settings, Extensions, Media, Users, Roles, API, Webhooks**.

1. Define the permission key (e.g. `content.read`, `media.write`) for the feature.
2. Declare required permissions in the extension manifest `permissions` array.
3. Enforce the permission in the controller/handler before performing the action.
4. Map the permission to the appropriate permission area so it appears under Roles & Permissions.
5. Custom extension permissions automatically become assignable to roles and selectable for API tokens.

Roles (most-privileged to least: **Super Admin, Admin, Editor, Author, Authenticated, Public**), permission areas, and assignment rules are owned by [User Roles & Permissions](user_roles_permissions.md).

---

## How to Write Unit Tests

The solution includes a dedicated test project (`tests/DotNetForge.Tests`) and an integration test project (`tests/DotNetForge.IntegrationTests`).

Write unit tests for the areas the testing spec requires, including core services, permissions, content manager, media validation, API token generation, extension manifest validation, webhook signing, and import/export. Add integration tests for the setup flow, authentication, and API endpoints.

```csharp
public class ExtensionManifestValidatorTests
{
    [Fact]
    public void Validate_RejectsManifest_WithMissingRequiredField()
    {
        var manifest = new ExtensionManifest { Id = "x" }; // missing required fields
        var result = ExtensionManifestValidator.Validate(manifest);
        Assert.False(result.IsValid);
    }
}
```

Place tests beside the feature they cover and assert on observable behavior. The full testing strategy, required coverage, and example tests are owned by [Testing & Quality](testing_quality.md).

---

## How to Run Unit Tests

Run all tests from the repository root:

```bash
dotnet test
```

Run a single test project:

```bash
dotnet test tests/DotNetForge.Tests
dotnet test tests/DotNetForge.IntegrationTests
```

See [Testing & Quality](testing_quality.md) for test project structure and coverage expectations.

---

## How to Run Frontend Linting

The project includes frontend linting support (ESLint) for any JavaScript or TypeScript used in the admin UI or themes.

```bash
npm run lint        # check
npm run lint:fix    # auto-fix where possible
```

Linting configuration, formatting rules, naming conventions, and dependency-hygiene rules (avoid unnecessary packages, do not commit generated files) are owned by [Testing & Quality](testing_quality.md).

---

## How to Package an Extension

Package an extension as a distributable archive containing the manifest, the compiled entry point, and any assets.

1. Ensure `dotnetforge.extension.json` is valid and present at the extension root.
2. Build the extension so its `entryPoint` is compiled.
3. Bundle the extension folder (manifest + assets + binaries) into a single archive.
4. Verify the package installs cleanly into a clean `extensions/` subfolder.

The CMS supports marketplace-style distribution through an external API; package layout must match what the marketplace and installer expect (see [Extension System](extensions.md) and [Marketplace](extensions.md)).

---

## How to Install an Extension

Extensions can be installed from the admin Marketplace, from a manifest/package upload, or by placing the extension folder under the appropriate `extensions/` subfolder.

1. Provide the extension package (marketplace install, upload, or folder placement).
2. The CMS validates the manifest before installing. Invalid, unsafe, or incompatible extensions are rejected and never loaded.
3. The extension's declared permissions are registered.
4. Enable the extension to activate it.

Installation, enable/disable, configure, and remove actions are surfaced in the admin Plugins/Marketplace UI (see [Extension System](extensions.md)).

---

## How to Update an Extension

1. The CMS checks for extension updates and surfaces availability in the admin UI.
2. Updating an extension replaces only that extension's files under `extensions/` - **core CMS updates must not overwrite custom extensions**, and extension updates are isolated from the core.
3. The new manifest is validated before the update is applied.
4. If an update fails, restore the previous working version where possible (backups are created before updates).

Core update and rollback safety is owned by [Updates & Rollbacks](transfer_updates.md).

---

## The `/agents` Folder

DotNetForge CMS must include an `/agents` folder. This folder contains documentation that **AI agents use to understand how the CMS works** so they can extend it safely and consistently.

Keep this exact tree:

```text
/agents
  cms-overview.md
  architecture-summary.md
  extension-system.md
  permissions.md
  api-reference.md
  development-guidelines.md
```

### What each file must explain

| File | Must explain |
| --- | --- |
| `cms-overview.md` | What DotNetForge CMS is: a modular hybrid CMS (ASP.NET Core MVC, EF Core, Razor) running in Traditional, Headless, and Hybrid modes across Windows/macOS/Linux, with SQLite and PostgreSQL support. |
| `architecture-summary.md` | How the architecture works: project structure, separation of core CMS from extensions, database layer, and how pages and modules work together. |
| `extension-system.md` | How extensions work: the supported extension types, the `dotnetforge.extension.json` manifest, validation, loading, and the enable/disable/update/remove lifecycle. |
| `permissions.md` | How permissions work: default roles (Super Admin → Public), permission areas, RBAC, and how custom permissions are declared and enforced. |
| `api-reference.md` | How APIs work: headless/hybrid usage, API tokens and scopes, endpoint conventions, and tenant-aware resolution. |
| `development-guidelines.md` | How developers (and agents) should add new features **safely**: use interfaces for extension points, dependency injection, migrations, manifest validation, permission checks on every action, and keeping secrets out of Git. |

Collectively, these files must explain:

- What DotNetForge CMS is.
- How the architecture works.
- How pages and modules work.
- How extensions work.
- How permissions work.
- How APIs work.
- How developers should add new features safely.

The canonical, full detail for these topics lives in the sibling spec files; the `/agents` files are concise, agent-oriented summaries that point to: [Architecture](architecture.md), [Extension System](extensions.md), [User Roles & Permissions](user_roles_permissions.md), [API Tokens](api_tokens.md), and [Security](security.md).

---

## Acceptance Criteria

- [ ] A developer can clone the repo, copy `.env.example` to `.env`, restore, build, apply migrations, and run the CMS locally at `APP_URL`.
- [ ] `.env` configuration is documented, and secrets are kept in `.env` and never committed to Git.
- [ ] Using SQLite (the default provider) is documented end to end, including the connection string and migrations.
- [ ] Using PostgreSQL is documented end to end, including the connection string and migrations.
- [ ] Switching providers requires only a `DATABASE_PROVIDER`/connection-string change plus migrations.
- [ ] Creating an extension is documented, including the `extensions/` location and a valid `dotnetforge.extension.json` manifest.
- [ ] The manifest documents exactly the required fields `id`, `name`, `description`, `version`, `type`, `author`, `entryPoint`, `permissions`, with a single correctly spelled `version` field (no duplicate `version`, no `vrsion`).
- [ ] Creating a theme is documented, and themes are confirmed to never affect the admin area.
- [ ] Creating a module is documented, including page-builder availability and the built-in Razor module.
- [ ] Creating a dashboard widget is documented via the extension system.
- [ ] Creating an authentication provider is documented, with secrets stored in `.env`.
- [ ] Creating a connector is documented, including external storage connectors for media.
- [ ] Creating API endpoints is documented, with permission checks, input validation, and tenant-aware resolution.
- [ ] Adding permissions is documented and mapped to the permission areas and role/token assignment.
- [ ] Writing unit tests is documented, covering required areas and pointing to the test projects.
- [ ] Running unit tests is documented (`dotnet test`, including per-project runs).
- [ ] Running frontend linting (ESLint) is documented.
- [ ] Packaging, installing, and updating an extension are each documented, including manifest validation before install/update and isolation from core updates.
- [ ] The `/agents` folder tree is present exactly as specified and lists `cms-overview.md`, `architecture-summary.md`, `extension-system.md`, `permissions.md`, `api-reference.md`, and `development-guidelines.md`.
- [ ] Each `/agents` file's required content is documented (what the CMS is, how the architecture works, how pages/modules work, how extensions work, how permissions work, how APIs work, how to add features safely).
- [ ] The document cross-links to [Architecture](architecture.md), [Extension System](extensions.md), and [Testing & Quality](testing_quality.md).
