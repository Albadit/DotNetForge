# Development guide

Set up, run, debug and change DotNetForge CMS locally. Architecture: [codebase.md](../architecture/codebase.md).

## Prerequisites

- .NET SDK 10.0 - `global.json` pins `10.0.100` with `rollForward: latestFeature` (any 10.0.1xx+ feature band works).
- Optional: PostgreSQL, only if you switch provider.
- No Node.js: the frontend is plain CSS/JS in `wwwroot/`.

## First run

```bash
cp .env.example .env          # PowerShell: Copy-Item .env.example .env
dotnet tool restore           # restores the pinned dotnet-ef tool (only needed for migrations)
dotnet run                    # or: dotnet watch
```

Open <http://localhost:5000>. You are redirected to `/setup`; create the first administrator (email + a password with
≥ 8 characters, a letter and a digit). You land on `/admin` signed in as Super Admin.

Starting without `.env` fails fast with
`[DotNetForge] Configuration error: Configuration is missing. Copy '.env.example' to '.env' ...` - see
[configuration](../features/configuration.md).

## Everyday commands (repository root)

| Command | Purpose |
| --- | --- |
| `dotnet run` | build and run the web host (profile `http`, `http://localhost:5000`) |
| `dotnet run --launch-profile https` | also listen on `https://localhost:5001` |
| `dotnet watch` | run with hot reload |
| `dotnet build` | build the host and every referenced `src/` project |
| `dotnet test tests/DotNetForge.Tests` | unit tests |
| `dotnet test tests/DotNetForge.IntegrationTests` | integration tests |
| `dotnet format DotNetForge.Web.csproj --verify-no-changes --severity error` | the CI style check (run per project; see [testing](testing.md#ci)) |

VS Code: **Terminal → Run Build Task** offers `build`, `watch (hot reload)`, `run`, `test: all`, `test: unit`,
`test: integration`, `clean`, `restore tools`, `ef: add migration (SQLite)`, `ef: add migration (PostgreSQL)`. **F5** runs "Debug DotNetForge.Web" (builds first,
opens the browser when Kestrel is listening).

### Why there is no solution file

`dotnet run`/`dotnet watch` need a project file in the current directory, and a directory that contains both a
runnable project and a `.sln` makes bare `dotnet build`/`dotnet test` ambiguous. The web project therefore lives at
the root and there is no solution; test projects are run by path. The host's `DefaultItemExcludes` keeps `src/`,
`tests/`, `extensions/` etc. out of its compilation.

## Resetting local state

| Want to | Do |
| --- | --- |
| Start over (back to the setup wizard) | stop the app, delete `storage/` (SQLite database + local media), start again |
| Re-create the starter pages | delete all pages in the Content Manager, restart (seeding re-creates them when the tenant has no pages) |
| Unlock a locked account | wait 15 minutes, or clear `LockoutEndUtc` on the `Users` row |

In Development the default SQLite database (`storage/dotnetforge.db`) and local media (`storage/media/`) are under the
**content root** (the repository root), whatever the working directory. `storage/` is git-ignored and only exists in
Development; production never writes there ([deployment](deployment.md#read-only-deployment-requirements)).

## Developing against PostgreSQL and S3

`compose.dev.yml` starts PostgreSQL 17 and an S3-compatible server (SeaweedFS) with a `dotnetforge` bucket:

```bash
docker compose -f compose.dev.yml up -d
```

Copy the `.env` values from the header of `compose.dev.yml` (provider `postgresql`, `STORAGE_PROVIDER=s3`,
`STORAGE_S3_SERVICE_URL=http://localhost:8333`, `STORAGE_S3_FORCE_PATH_STYLE=true`) and run the app as usual. The S3
server runs without authentication: development only. Stop with `docker compose -f compose.dev.yml down`
(add `-v` to delete the data).

To run the production image locally with a read-only filesystem, see [deployment → Docker](deployment.md#docker).

## Database and migrations

The provider is chosen by `DATABASE_PROVIDER`. Each provider has its own migration set, applied at startup:
SQLite `src/DotNetForge.Data/Migrations/` (`DotNetForgeDbContext`), PostgreSQL
`src/DotNetForge.Data/Migrations/PostgreSql/` (`PostgreSqlDbContext`). Details: [database.md](../architecture/database.md).

After changing an entity or `DotNetForgeDbContext`, add a migration **for both providers**:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/DotNetForge.Data --startup-project src/DotNetForge.Data \
  --context DotNetForgeDbContext --output-dir Migrations
dotnet ef migrations add <Name> --project src/DotNetForge.Data --startup-project src/DotNetForge.Data \
  --context PostgreSqlDbContext --output-dir Migrations/PostgreSql --namespace DotNetForge.Data.Migrations.PostgreSql
```

Generating migrations never connects to a database. Afterwards strip the UTF-8 BOM EF adds to the generated files
(`.editorconfig` requires `utf-8`), and if EF wrote the PostgreSQL model snapshot into a namespace-derived folder
(`src/DotNetForge.Data/DotNetForge/Data/Migrations/PostgreSql/`), move it into `Migrations/PostgreSql/`.

Step-by-step checklist: [database-change skill](../../.claude/skills/database-change/SKILL.md).

## Debugging tips

- **Where things are logged:** the console (`dotnet run`) or the VS Code Debug Console. Application code barely
  logs; check **Audit Logs** in the admin for user actions. See
  [logging and errors](../features/logging-and-error-handling.md).
- **Exception details:** run with `ASPNETCORE_ENVIRONMENT=Development` (default for the launch profiles) to get the
  developer exception page.
- **SQL:** raise `Microsoft.EntityFrameworkCore.Database.Command` to `Information` in `appsettings.Development.json`.
- **Inspect the DB:** open `storage/dotnetforge.db` with any SQLite browser (stop the app first if you write to it).
- **Uploaded media (local provider):** files are under `storage/media/<tenant>/<yyyy>/<MM>/`; the `MediaFiles.RelativePath`
  column holds each file's key.
- **CSP in Development** is report-only: violations appear in the browser console instead of breaking the page.
- **Admin extension views** are compiled at runtime from `extensions/`; edits apply on the next request.
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
- No inline `<script>`, event-handler attributes or `<style>` in views (the Content-Security-Policy blocks them in
  production); put behaviour in `wwwroot/js` (e.g. `data-confirm` handled by `site.js`) and styles in `wwwroot/css`.
- Follow the [architectural rules](../architecture/codebase.md#architectural-rules-for-changes).

## Troubleshooting

### The app exits immediately with "Configuration error"
- **Cause:** missing/invalid `.env` or environment variable.
- **Fix:** copy `.env.example` to `.env`; `DATABASE_PROVIDER` must be `sqlite` or `postgresql`; PostgreSQL needs
  `DATABASE_CONNECTION_STRING`; `APP_URL` must be absolute; `STORAGE_PROVIDER=s3` needs the bucket and keys.
- **Outside Development** (e.g. `dotnet run --launch-profile` with `ASPNETCORE_ENVIRONMENT=Production`) the SQLite
  database and `STORAGE_LOCAL_PATH` must be explicit absolute paths ([configuration](../features/configuration.md)).

### Every page redirects to `/setup`
- **Cause:** the database is not installed (fresh or replaced DB).
- **Fix:** complete `/setup`. If you expected an existing install, check which DB file the working directory points
  to.

### Redirected to "Access denied"
- **Cause:** the screen requires `Super Admin`/`Admin` (Users, Roles, Audit Logs, API Tokens, Settings) or
  `Super Admin` (Plugins), or the action needs a permission the role lacks (e.g. an Author editing someone else's
  page). See [authorization](../features/authorization.md).

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
| PostgreSQL end to end | create the database and a user; set `DATABASE_PROVIDER=postgresql` and the connection string; migrations; run | ✔ PostgreSQL migration set applied at startup; local server via `compose.dev.yml` |
| Switching provider | only a `.env` change plus migrations | ✔ for an empty database; moving existing data between providers belongs to [transfer and updates](../features/transfer-and-updates.md) |

**Adding API endpoints** (beyond the [conventions for new endpoints](../features/headless-api.md#conventions-for-new-endpoints),
which are ✔):

- extensions may register endpoints, declared in the manifest `routes`;
- tenant context resolves from domain, path, header or token (today only the token's `dnf:tenant` claim);
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
| `cms-overview.md` | what the CMS is: modular hybrid CMS (ASP.NET Core MVC, EF Core, Razor), Traditional/Headless/Hybrid modes, Windows/macOS/Linux, SQLite and PostgreSQL |
| `architecture-summary.md` | project structure, core vs extensions, database layer, **how content pages and modules work together** |
| `extension-system.md` | extension types, the `dotnetforge.extension.json` manifest, validation, loading, enable/disable/update/remove lifecycle |
| `permissions.md` | default roles (Super Admin → Public), permission areas, RBAC, how custom permissions are declared and enforced |
| `api-reference.md` | headless/hybrid usage, API tokens and scopes, endpoint conventions, tenant-aware resolution |
| `development-guidelines.md` | how to add features safely: interfaces for extension points, DI, migrations, manifest validation, a permission check on every action, secrets out of Git |

### Rules and validation

- Secrets - connection strings, SMTP credentials, authentication-provider client secrets - live only in `.env` and
  are never committed.
- The developer documentation cross-links the [codebase architecture](../architecture/codebase.md),
  [extensions](../features/extensions.md) and [testing](testing.md) (✔ this guide).

### Acceptance criteria

- [x] A developer can clone, copy `.env.example` to `.env`, restore, build, apply migrations and run the CMS locally
  ([First run](#first-run); SQLite migrations apply at startup; the default `APP_URL` matches the `http` profile).
- [x] `.env` configuration is documented and secrets stay in `.env` ([configuration](../features/configuration.md)).
- [x] Using SQLite is documented end to end, including the connection string and migrations (this guide,
  [configuration](../features/configuration.md), [database](../architecture/database.md)).
- [x] Using PostgreSQL is documented end to end, including migrations ([Developing against PostgreSQL and S3](#developing-against-postgresql-and-s3), [Database and migrations](#database-and-migrations); `PostgreSqlDbContext` migrations).
- [x] Switching providers requires only a `DATABASE_PROVIDER`/connection-string change plus migrations (both providers migrate at startup - `DatabaseInitializer`; data is not copied).
- [ ] Creating API endpoints is documented with permission checks, input validation **and tenant-aware resolution**
  (first two ✔ in [headless API](../features/headless-api.md#conventions-for-new-endpoints); tenant resolution does
  not exist).
- [ ] Adding permissions is documented and mapped to permission areas and role/token assignment.
- [x] Writing unit tests is documented and points to the test projects ([testing](testing.md)).
- [x] Running unit tests is documented, per project ([Everyday commands](#everyday-commands-repository-root); there is
  no root-level `dotnet test`).
- [ ] Running frontend linting (ESLint) is documented.
- [ ] The `agents/` folder exists exactly as specified (the six files exist in `.github/agents/`, not at the root).
- [ ] Each agents file covers its required content (`.github/agents/architecture-summary.md` does not explain how
  content pages and modules work).
- [x] The developer documentation cross-links architecture, the extension system and testing (this guide).
