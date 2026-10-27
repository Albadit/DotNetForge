# Development guide

Set up, run, debug and change DotNetForge CMS locally. Architecture: [codebase.md](../architecture/codebase.md).

## Prerequisites

- .NET SDK 10.0 - `global.json` pins `10.0.100` with `rollForward: latestFeature` (any 10.0.1xx+ feature band works).
- PostgreSQL - the CMS database. Easiest: Docker (`docker/databases/postgres.yml`), or any local PostgreSQL server.
  The integration tests need it too ([testing](testing.md#databases)).
- No Node.js: the frontend is plain CSS/JS in `src/DotNetForge.Web/wwwroot/` ([design system](../architecture/design-system.md)).

## First run

```bash
cp .env.example .env          # PowerShell: Copy-Item .env.example .env
docker compose -f docker/databases/postgres.yml up -d    # PostgreSQL 17 on localhost:5432
dotnet tool restore           # restores the pinned dotnet-ef tool (only needed for migrations)
dotnet build                  # the whole solution, including the extensions
dotnet run --project src/DotNetForge.Web
```

Open <http://localhost:5000>. You are redirected to `/setup`; create the first administrator (email + a password with
≥ 8 characters, a letter and a digit). You land on `/admin` signed in as Super Admin.

`DATABASE_CONNECTION_STRING` is required - there is no fallback, also not in Development. For the server of
`docker/databases/postgres.yml` (which also creates the `dotnetforge` database) it is
`Host=localhost;Port=5432;Database=dotnetforge;Username=postgres;Password=postgres;GSS Encryption Mode=Disable` (`.env.developer` has it).
Missing or invalid values stop startup with `[DotNetForge] Configuration error: ...`
([configuration](../features/configuration.md)).

## Everyday commands (repository root)

`DotNetForge.slnx` at the root is the solution; `dotnet build` and `dotnet test` without arguments use it.

| Command | Purpose |
| --- | --- |
| `dotnet run --project src/DotNetForge.Web` | build and run only the web app (profile `http`, `http://localhost:5000`) - extensions keep their last build, so run `dotnet build` first after changing one |
| `dotnet run --project src/DotNetForge.Web --launch-profile https` | also listen on `https://localhost:5001` |
| `dotnet watch --project src/DotNetForge.Web` | run with hot reload |
| `dotnet build` | build the whole solution, including the sample extensions with code (into each extension's own `bin/`) |
| `dotnet test` | unit and integration tests (or one project: `dotnet test tests/DotNetForge.Tests`) |
| `dotnet format src/DotNetForge.Web --verify-no-changes --severity error` | the CI style check (run per project; see [testing](testing.md#ci)) |

VS Code: **Terminal → Run Build Task** offers `build`, `watch (hot reload)`, `run`, `test: all`, `test: unit`,
`test: integration`, `clean`, `restore tools` and the `ef: add migration (…)` tasks - only
`ef: add migration (PostgreSQL)` still applies (the SQLite, SQL Server and MySQL ones target contexts that no longer
exist).
**F5** runs "Debug DotNetForge.Web" (builds first, opens the browser when Kestrel is listening).

### Where the app finds its files

The web app's content root is `src/DotNetForge.Web` (views, `wwwroot/`, `appsettings*.json`). Repository-level
folders are found by `AppPaths` (`src/DotNetForge.Infrastructure/Configuration/AppPaths.cs`), which looks next to
the app first and then at the repository root (the folder holding `DotNetForge.slnx`):

| What | In a checkout | Published (`/app`) |
| --- | --- | --- |
| `.env` | repository root | next to the app (optional; environment variables are the norm) |
| `extensions/` | repository root (or `EXTENSIONS_PATH`) | `/app/extensions` (copied by `dotnet publish`) |
| Development `storage/` (local media, SQLite additional databases) | repository root | not used - explicit settings are required outside Development |

## Resetting local state

| Want to | Do |
| --- | --- |
| Start over (back to the setup wizard) | stop the app, drop and re-create the PostgreSQL database (`docker compose -f docker/databases/postgres.yml down -v`, then `up -d`), delete `storage/media/`, start again |
| Re-create the starter pages | delete all pages on the Pages screen, restart (seeding re-creates them when there are no pages) |
| Unlock a locked account | wait 15 minutes, or clear `LockoutEndUtc` on the `Users` row |

In Development local media (`storage/media/`) and SQLite additional databases (`storage/<name>.db`) are in the
repository root's `storage/`, whatever the working directory; the CMS data is in PostgreSQL. `storage/` is git-ignored and only exists in
Development; production never writes there ([deployment](deployment.md#read-only-deployment-requirements)). The
ignore rule is `/storage/` - the root folder only. It used to be `storage/`, which on case-insensitive file systems
(Windows, macOS) also matched the source folders `src/DotNetForge.Abstractions/Storage/` and
`src/DotNetForge.Infrastructure/Storage/`, so `IFileStorage.cs` and `S3FileStorage.cs` were silently left out of
commits. If a new file under a `Storage/` folder does not show up in `git status`, check `git check-ignore -v <file>`.

Extension state (which extensions are enabled) is in the database, so re-creating the database also disables every
extension.

## Docker services for development

`docker/` holds one compose file per service, plus two that combine them (all in the compose project `dotnetforge`,
so a service started from its own file and from a combined file is the same container with the same data):

| File | Starts | Ports |
| --- | --- | --- |
| `docker/compose.dev.yml` | PostgreSQL + RustFS + Mailpit - the services `.env.developer` is filled in for | 5432, 9000/9001, 1026/8026 |
| `docker/compose.app.yml` | the app built from `docker/Dockerfile` (Production, read-only) + the `compose.dev.yml` services | 8080 + the above |
| `docker/databases/postgres.yml` | PostgreSQL 17 - the CMS database (`DNF_POSTGRES_PORT` changes the host port) | 5432 |
| `docker/databases/mongodb.yml` | MongoDB 8, single-node replica set - additional databases, tests | 27017 |
| `docker/databases/sqlserver.yml` | SQL Server 2022 - additional databases, tests | 1433 |
| `docker/databases/mysql.yml` | MySQL 8.4 - additional databases, tests | 3306 |
| `docker/s3/rustfs.yml` | RustFS (S3 with access keys) + bucket `dotnetforge` | 9000, 9001 |
| `docker/s3/ceph.yml` | Ceph RADOS Gateway (S3 with access keys) + bucket `dotnetforge` | 7480 |
| `docker/s3/seaweedfs.yml` | SeaweedFS S3 **without authentication** + bucket `dotnetforge` (live S3 tests, S3 connector sample) | 8333 |
| `docker/mail/mailpit.yml` | Mailpit (SMTP with a sign-in, web inbox) | 1026, 8026 |

```bash
docker compose -f docker/databases/mysql.yml up -d      # one service
docker compose -f docker/compose.dev.yml down           # stop (add -v to delete the data)
```

Each file's header has the `.env` values for its service. PostgreSQL is published on port 5432; when another
PostgreSQL already uses it, start compose with `DNF_POSTGRES_PORT=5433` (PowerShell: `$env:DNF_POSTGRES_PORT=5433`)
and use `Port=5433` in the connection string. Every password and key in these files is public: development only,
never expose the ports.

## Production-like S3 and email

`.env.developer` (tracked) is a complete `.env` for the services of `docker/compose.dev.yml`: the data in PostgreSQL
17, media in a real S3 server with access keys and signed requests, email over SMTP with a sign-in - the same code
paths as in production, with everything local in Docker.

```bash
cp .env.developer .env
docker compose -f docker/compose.dev.yml up -d
dotnet run --project src/DotNetForge.Web
```

| Service | Endpoint | Credentials (development only) |
| --- | --- | --- |
| PostgreSQL 17 - the CMS database (`.env.developer` sets it explicitly; `.env.example` leaves it empty = the same server) | `localhost:5432` | `postgres` / `postgres`, database `dotnetforge` |
| RustFS (S3) - the active block in `.env.developer` | S3 `http://localhost:9000`, console `http://localhost:9001/rustfs/console/` | `dotnetforge-dev` / `dotnetforge-dev-secret`; bucket `dotnetforge` created by `rustfs-bucket` (a signed request) |
| Ceph RADOS Gateway (S3) - instead of RustFS: start `docker/s3/ceph.yml`, comment out the RustFS block and uncomment the Ceph block | S3 `http://127.0.0.1:7480` | same keys and bucket, created by the image on first start (about a minute) |
| Mailpit (SMTP) | SMTP `localhost:1026`, inbox `http://localhost:8026` | `dotnetforge` / `dotnetforge-dev` - mail without (or with a wrong) sign-in is refused |

- Choose one S3 server: both blocks set the same `STORAGE_S3_*` keys (path-style, region `us-east-1`).
- Ceph must be addressed as `127.0.0.1`: with `localhost` the gateway takes the host name for a bucket name
  (virtual-host style) and answers `NoSuchBucket`. Ceph comes from the official `quay.io/ceph/demo:latest-squid`
  image (monitor, manager, OSD and gateway in one container; no longer rebuilt since Dec 2024). Its monitor needs a
  fixed address, so it has its own network `172.30.250.0/24`.
- Mailpit listens on 1026/8026 so it runs beside another Mailpit on the default 1025/8025. TLS stays off
  (`SMTP_TLS=false`): `SmtpClient` validates certificates and Mailpit's would be self-signed, so STARTTLS itself is
  not exercised locally.
- `.env.developer` is excluded from the Docker build context like every `.env.*` but `.env.example`. Media stored in
  one S3 server stays there when you switch to the other.

## The app in Docker

`docker/compose.app.yml` builds the production image and runs it like production - `Production` environment,
read-only root filesystem with a `/tmp` tmpfs, non-root - against PostgreSQL, RustFS and Mailpit:

```bash
docker compose -f docker/compose.app.yml up -d --build     # http://localhost:8080 → /setup on the first start
docker compose -f docker/compose.app.yml down              # add -v to delete the data
```

- The app starts once PostgreSQL is healthy and the bucket exists; it reaches the services by their names
  (`postgres`, `mailpit`).
- Media links are signed for `http://rustfs.localhost:9000`: browsers resolve `*.localhost` to 127.0.0.1 (the
  published port), and inside Docker RustFS has the network alias `rustfs.localhost` - one address that works for the
  app and the browser.
- The auth cookie is Secure-only outside Development; browsers accept it on `http://localhost`.
- Emails land in Mailpit (`http://localhost:8026`). Code changes need `--build` again.

More on the image: [deployment → Docker](deployment.md#docker).

## Working on extensions

Everything a CMS user adds lives in `extensions/`; the core in `src/` is not touched
([extensions](../features/extensions.md), [extension development guide](extension-development.md),
[extensions/README.md](../../extensions/README.md) for trying each sample).

| Change | What to do |
| --- | --- |
| Manifest (`dotnetforge.extension.json`) | picked up without a restart (file watcher); refresh **Plugins** |
| Razor views or `Views/Resources/` files | picked up on the next request (runtime compilation) |
| C# code | `dotnet build` (the samples are in `DotNetForge.slnx`; your own project: `dotnet build extensions/<type>/<name>`), then **restart** the app - code loads only at startup. Files are never locked, so you can build while the app runs |
| A new extension with code | add a `.csproj` next to the manifest (settings come from `extensions/Directory.Build.props`, output goes to its `bin/`), set `"assembly": "bin/<Name>.dll"` and `entryPoint`; add the project to `DotNetForge.slnx` if `dotnet build` should build it |
| Try it | sign in as the Super Admin → **Plugins** → **Enable** (every extension starts disabled) |

An extension whose code is missing or failed shows **not running** with the reason on the Plugins screen, and the
startup log has `Extension {id} is enabled but not running: ...` (warning; information while disabled). Secrets for extensions go in `.env` under the names the
extension documents ([configuration → extension settings](../features/configuration.md#extension-settings)).

## Working on the UI

Every built-in screen is built from the Forge design system - read
[design-system.md](../architecture/design-system.md) first (components, tag helpers, behaviours, accessibility).

| Change | Where | Picked up |
| --- | --- | --- |
| Styles (tokens, components, screen layout) | `src/DotNetForge.Web/wwwroot/css/forge/` (`tokens.css`, `components.css`, `admin.css`, ...) | on reload (files are served from `wwwroot`, links carry `asp-append-version`) |
| Behaviour | `wwwroot/js/forge/ui.js` (data attributes), `pages.js` (Pages), `entries.js` (Content Manager), `content-types.js` (Content types), a new page script via `@section Scripts` | on reload |
| Icons | `wwwroot/img/icons.svg` (`<symbol id="…">`, Lucide paths) | on reload |
| Views, layouts, partials | `Areas/Admin/Views/`, `Views/` | on the next request (Razor runtime compilation is on) |
| Tag helpers, navigation tree | `src/DotNetForge.Web/Ui/UiTagHelpers.cs`, `Areas/Admin/Navigation/AdminNavigation.cs` | rebuild and restart |

- Check both themes (theme menu in the top bar) and a phone width (below 64rem the sidebar is a drawer).
- In Development the CSP is report-only: watch the browser console for violations, then run
  `dotnet test tests/DotNetForge.IntegrationTests --filter AdminUiTests` (inline style/script, one `<h1>`, icons
  present in the sprite).
- No CSS/JS libraries, web fonts or CDNs; no build step.

## Database and migrations

The CMS database is PostgreSQL ([configuration → Database](../features/configuration.md#database)). Its one
migration set, `src/DotNetForge.Data/Database/Providers/PostgreSql/Migrations/` (context `PostgreSqlDbContext`), is
applied at startup. Details: [database.md](../architecture/database.md), [database layer](../database/architecture.md).

After changing an entity or `DotNetForgeDbContext`, add **one** migration:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/DotNetForge.Data --startup-project src/DotNetForge.Data --context PostgreSqlDbContext --output-dir Database/Providers/PostgreSql/Migrations
```

Generating migrations never connects to a database. Afterwards strip the UTF-8 BOM EF adds to the generated files
(`.editorconfig` requires `utf-8`) and check `PostgreSqlDbContextModelSnapshot.cs` sits in the migration folder.

To work with an additional database (extensions, custom API endpoints, exports), start its file from
`docker/databases/` (`mongodb.yml`, `sqlserver.yml`, `mysql.yml`, or `postgres.yml` with another database name) and
set `DATABASES_<NAME>_PROVIDER` and `DATABASES_<NAME>_CONNECTION_STRING`; a SQLite one needs no server
(empty connection string = `storage/<name>.db`) ([more databases](../database/configuration.md#more-databases)).

Step-by-step checklist: [database-change skill](../../.claude/skills/database-change/SKILL.md).

## Debugging tips

- **Where things are logged:** the console (`dotnet run`) or the VS Code Debug Console. Application code barely
  logs; check **Audit Logs** in the admin for user actions. See
  [logging and errors](../features/logging-and-error-handling.md).
- **Exception details:** run with `ASPNETCORE_ENVIRONMENT=Development` (default for the launch profiles) to get the
  developer exception page.
- **SQL:** raise `Microsoft.EntityFrameworkCore.Database.Command` to `Information` in `src/DotNetForge.Web/appsettings.Development.json`.
- **Inspect the DB:** connect to `localhost:5432` (database `dotnetforge`, `postgres` / `postgres`) with `psql` or any
  PostgreSQL client, e.g. `docker compose -f docker/databases/postgres.yml exec postgres psql -U postgres dotnetforge`.
  The CMS's tables are mixed-case: quote them (`SELECT * FROM "Pages";`).
- **Uploaded media (local provider):** files are under `storage/media/<yyyy>/<MM>/`; the `MediaFiles.RelativePath`
  column holds each file's key.
- **CSP in Development** is report-only: violations appear in the browser console instead of breaking the page.
- **Extension views** are compiled at runtime from `extensions/`; edits apply on the next request. Extension
  **code** is loaded at startup: rebuild and restart. The startup log lists every extension with code
  (`Extension {id} {version} loaded from ...` or `... is enabled but not running: ...` - a disabled one is only logged as information).
- **Extension endpoints answer 404:** the extension is not enabled (or not running) - check **Plugins**.
- **Scheduled publishing** runs every 15 s; its log line appears only when something was due.

## Conventions

- C#: file-scoped namespaces, nullable enabled, 4-space indent, braces preferred, `System` usings first
  (`.editorconfig`). LF line endings, UTF-8, final newline. JS/CSS/JSON/YAML: 2-space indent.
- Package versions only in `Directory.Packages.props`; shared MSBuild properties in `Directory.Build.props`.
- Comments explain *why* and reference the `.docs` document that owns the behaviour (e.g. `(.docs/features/content-pages-and-routing.md)`).
- Validate on the server; never log or render secrets; store passwords and tokens only as hashes.
- Commit messages in this repository use short prefixes: `feat:`, `fix:`, `add:`, `update:`.
- Never write runtime files under the content root; use `IFileStorage` (or the OS temp directory for transient
  work). `ReadOnlyDeploymentTests` enforces this.
- No inline `<script>`, event-handler attributes, `style=` or `<style>` in views (the Content-Security-Policy blocks
  them in production); put behaviour in `src/DotNetForge.Web/wwwroot/js/forge/` (e.g. `data-confirm` handled by
  `ui.js`), styles in `src/DotNetForge.Web/wwwroot/css/forge/`, and render validation errors with `<form-errors />`
  ([design system](../architecture/design-system.md)).
- Follow the [architectural rules](../architecture/codebase.md#architectural-rules-for-changes).

## Troubleshooting

### The app exits immediately with "Configuration error"
- **Cause:** missing/invalid `.env` or environment variable.
- **Fix:** set `DATABASE_CONNECTION_STRING` to a key=value PostgreSQL string (`Host=…;Database=…`); remove
  `DATABASE_PROVIDER` (no longer used; other databases are `DATABASES_<NAME>_*`); `APP_URL` must be absolute; once any
  `STORAGE_S3_*` key is set, the bucket and keys are required.
- **Outside Development** (e.g. `dotnet run --project src/DotNetForge.Web --no-launch-profile` with
  `ASPNETCORE_ENVIRONMENT=Production`) the connection string and S3 storage must be configured
  ([configuration](../features/configuration.md)).

### Startup fails with a connection error to `localhost:5432`
- **Cause:** the PostgreSQL of `DATABASE_CONNECTION_STRING` isn't running, or the port is taken by another server.
- **Fix:** `docker compose -f docker/databases/postgres.yml up -d`, or set `DATABASE_CONNECTION_STRING` to your server
  (another port: `DNF_POSTGRES_PORT=5433` when starting compose, `Port=5433` in the string).

### Every page redirects to `/setup`
- **Cause:** the database is not installed (fresh or replaced DB).
- **Fix:** complete `/setup`. If you expected an existing install, check which database `DATABASE_CONNECTION_STRING`
  points to.

### Redirected to "Access denied"
- **Cause:** the screen requires `Super Admin`/`Admin` (Users, Roles, Audit Logs, API Tokens, Settings) or
  `Super Admin` (Plugins), or the action needs a permission the role lacks (e.g. an Author editing someone else's
  page). The sidebar hides screens the role cannot open, so this usually comes from a bookmarked URL or a button. See [authorization](../features/authorization.md).

### "429 Too Many Requests" while testing sign-in
- **Cause:** more than 10 sign-in or setup posts per minute from one IP (rate limit). **Fix:** wait a minute.

### "Account is temporarily locked"
- **Cause:** 5 wrong passwords. **Fix:** wait 15 minutes or clear `LockoutEndUtc`.

### Sign-in says "Invalid email or password" with the right password
- **Cause:** email casing differs from the stored value (exact match). **Fix:** use the exact casing used at setup.

### An upload says "could not be stored right now"
- **Cause:** the storage provider failed (S3 endpoint unreachable, wrong keys, missing bucket, volume not writable).
  **Investigate:** console log `Storing upload ... failed` with the provider's exception.

### Startup fails on PostgreSQL with "relation ... already exists"
- **Cause:** the database was created by an older version (with `EnsureCreated`) and has no migrations history.
  **Fix:** recreate it, or baseline it ([deployment → Database](deployment.md#database)).

### `dotnet ef` not found
- **Fix:** `dotnet tool restore`.

### An extension shows "not running" on Plugins
- **Cause:** its code could not be loaded or configured - the row and the startup log give the reason, e.g.
  `'bin/X.dll' was not found. Build the extension (dotnet build "...") and restart the app.`, a missing entry type,
  or an exception from `ConfigureServices` (the Discord sample throws until `DISCORD_CLIENT_ID`/`_SECRET` are set).
  "Its code was added or changed after the app started" means the manifest's `assembly` changed: restart.
- **Fix:** build the extension, set what it needs in `.env`, restart.

### Enable is refused with "needs ... - enable it first"
- **Cause:** a `dependencies` entry is not enabled (Contact Form needs Markdown Kit). **Fix:** enable the dependency
  first; disabling works in the opposite order.


## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.
Extension authoring targets are in [extension development → Planned](extension-development.md#planned-not-implemented);
testing and linting targets in [testing → Planned](testing.md#planned-not-implemented).

### Requirements

**Tooling and commands the specification expects:**

| Item | Target | Today |
| --- | --- | --- |
| Node.js + npm (optional) | `npm install`, then `npm run lint` / `npm run lint:fix` (ESLint) for admin and theme JS/TS | no `package.json`, no ESLint |
| Run URL | the app is reached at `APP_URL` | Kestrel URLs come from `launchSettings.json` / `ASPNETCORE_URLS`; see [configuration → Planned](../features/configuration.md#planned-not-implemented) |
| PostgreSQL end to end | create the database and a user; set the `Host=…` connection string; migrations; run | ✔ PostgreSQL migration set applied at startup; local server via `docker/databases/postgres.yml` |
| Switching provider | only a `.env` change plus migrations | ✘ by design: the CMS database is PostgreSQL only; other databases are additional databases ([database configuration](../database/configuration.md)) |

**Adding API endpoints** (beyond the [conventions for new endpoints](../features/headless-api.md#conventions-for-new-endpoints),
which are ✔):

- extensions may register endpoints, declared in the manifest `routes` (✔ registered by code -
  `IExtension.MapEndpoints` or MVC controllers; `routes` is informational);
- no unauthenticated privileged endpoint; every endpoint has unit/integration tests.

**Adding permissions:**

| Step | Today |
| --- | --- |
| Define the dotted key (e.g. `content.read`) | ✔ `PermissionKeys` + `PermissionKeys.All` (shown on the token-create screen) |
| Declare it in an extension manifest's `permissions` | ✔ shape validated by `ManifestValidator`; not checked against `PermissionKeys`, not enforced |
| Enforce it before the action | ✔ API via `[RequireApiPermission]`; the admin area checks role names only ([authorization](../features/authorization.md)) |
| Map it to a permission area (Collection types, Single types, Plugins, Settings, Extensions, Media, Users, Roles, API, Webhooks) so it appears under Roles & Permissions | ✘ |
| Custom extension permissions automatically become assignable to roles and selectable for API tokens | ✘ |

**Agents folder.** The specification requires an `agents/` folder at the root with exactly these six files; the
repository keeps the same six files in `.github/agents/`, as short quick references that point into `.docs/`:

| File | Must explain |
| --- | --- |
| `cms-overview.md` | what the CMS is: modular hybrid CMS (ASP.NET Core MVC, EF Core, Razor), Traditional/Headless/Hybrid modes, Windows/macOS/Linux, SQLite and PostgreSQL (today: PostgreSQL for the CMS, SQLite as an additional database) |
| `architecture-summary.md` | project structure, core vs extensions, database layer, **how content pages and modules work together** |
| `extension-system.md` | extension types, the `dotnetforge.extension.json` manifest, validation, loading, enable/disable/update/remove lifecycle |
| `permissions.md` | default roles (Super Admin → Public), permission areas, RBAC, how custom permissions are declared and enforced |
| `api-reference.md` | headless/hybrid usage, API tokens and scopes, endpoint conventions |
| `development-guidelines.md` | how to add features safely: interfaces for extension points, DI, migrations, manifest validation, a permission check on every action, secrets out of Git |

### Rules and validation

- Secrets - connection strings, SMTP credentials, authentication-provider client secrets - live only in `.env` and
  are never committed.
- The developer documentation cross-links the [codebase architecture](../architecture/codebase.md),
  [extensions](../features/extensions.md) and [testing](testing.md) (✔ this guide).

### Acceptance criteria

- [x] A developer can clone, copy `.env.example` to `.env`, restore, build, apply migrations and run the CMS locally
  ([First run](#first-run); PostgreSQL migrations apply at startup; the default `APP_URL` matches the `http` profile).
- [x] `.env` configuration is documented and secrets stay in `.env` ([configuration](../features/configuration.md)).
- [ ] Using SQLite is documented end to end, including the connection string and migrations - SQLite is no longer
  a CMS database; as an additional database it is documented in [database configuration](../database/configuration.md#more-databases).
- [x] Using PostgreSQL is documented end to end, including migrations ([Docker services for development](#docker-services-for-development), [Database and migrations](#database-and-migrations); `PostgreSqlDbContext` migrations).
- [ ] Switching providers requires only a provider/connection-string change - dropped: the CMS runs on PostgreSQL only.
- [x] Creating API endpoints is documented with permission checks and input validation
  ([headless API](../features/headless-api.md#conventions-for-new-endpoints)).
- [ ] Adding permissions is documented and mapped to permission areas and role/token assignment.
- [x] Writing unit tests is documented and points to the test projects ([testing](testing.md)).
- [x] Running unit tests is documented, per project ([Everyday commands](#everyday-commands-repository-root); there is
  no root-level `dotnet test`).
- [ ] Running frontend linting (ESLint) is documented.
- [ ] The `agents/` folder exists exactly as specified (the six files exist in `.github/agents/`, not at the root).
- [ ] Each agents file covers its required content (`.github/agents/architecture-summary.md` does not explain how
  content pages and modules work).
- [x] The developer documentation cross-links architecture, the extension system and testing (this guide).
