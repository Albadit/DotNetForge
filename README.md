# DotNetForge CMS

A modular, secure, lightweight **hybrid CMS** built on **ASP.NET Core MVC** (C#, EF Core, Razor),
targeting **.NET 10**, with **SQLite** (default) and **PostgreSQL** support. It runs as a
**Traditional** CMS (visual admin), a **Headless** CMS (token-secured API), or **both at once**
(**Hybrid**) over the same content.

Product goals and scope: [`.docs/product.md`](.docs/product.md). The technical documentation - architecture,
every screen, every feature, verified against the code - starts at [`.docs/README.md`](.docs/README.md); what is
built versus planned is in
[`.docs/implementation-status.md`](.docs/implementation-status.md).

## Requirements

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) (pinned via [`global.json`](global.json)).
- Optionally PostgreSQL if you don't use the default SQLite provider.

## Quick start

```bash
# 1. Configure the environment (secrets live in .env, never committed)
cp .env.example .env          # Windows PowerShell: Copy-Item .env.example .env

# 2. Run the app from the repository root
dotnet run                    # or: dotnet watch   (hot reload)
```

Then open <http://localhost:5000>. On first run the app redirects to **`/setup`**, where you create
the first administrator (assigned the **Super Admin** role). After setup you land on the admin
dashboard at **`/admin`**; the setup wizard is then permanently blocked.

Optional: `docker compose -f compose.dev.yml up -d` starts PostgreSQL and an S3-compatible server for developing
against production-like backends ([development guide](.docs/guides/development.md#developing-against-postgresql-and-s3)).

## Production

The app runs from a **read-only deployment directory**: data goes to PostgreSQL (or SQLite on a volume), uploaded media
to S3-compatible object storage (Cloudflare R2 recommended), and the Data Protection key ring to the database.

```bash
docker build -t dotnetforge .
docker run --read-only --tmpfs /tmp -p 8080:8080 -e DATABASE_PROVIDER=postgresql -e DATABASE_CONNECTION_STRING=... \
  -e STORAGE_PROVIDER=s3 -e STORAGE_S3_SERVICE_URL=... -e STORAGE_S3_BUCKET=... \
  -e STORAGE_S3_ACCESS_KEY_ID=... -e STORAGE_S3_SECRET_ACCESS_KEY=... dotnetforge
```

Everything else - environment variables, reverse proxy, backups, verification: [deployment guide](.docs/guides/deployment.md).

## Running everything from the repository root

The ASP.NET Core host (`DotNetForge.Web`) lives at the **repository root** so the day-to-day commands
work with no extra arguments:

| Command | What it does |
| --- | --- |
| `dotnet run` | Builds and runs the web host (and all referenced libraries). |
| `dotnet watch` | Same, with hot reload on file changes. |
| `dotnet build` | Builds the web host and every `src/` library it references. |
| `dotnet test tests/DotNetForge.Tests` | Runs the unit tests. |
| `dotnet test tests/DotNetForge.IntegrationTests` | Runs the integration tests. |

> **Why is `dotnet test` scoped to a path?** The .NET CLI resolves a *single* project or solution
> from the current directory. `dotnet run`/`dotnet watch` need a project file in the directory, while
> `dotnet test` needs a test project - and a directory cannot hold both a runnable project **and** a
> solution without `dotnet build`/`dotnet test` becoming ambiguous. To keep `dotnet run`/`dotnet
> watch`/`dotnet build` working bare from the root, the test projects are run by path (both are also
> a single command away). See [.docs/guides/development.md](.docs/guides/development.md#why-there-is-no-solution-file).

## Configuration (`.env`)

Copy `.env.example` to `.env` and set the values. Secrets are **never** committed (see
[`.gitignore`](.gitignore)).

| Key | Required | Description |
| --- | --- | --- |
| `DATABASE_PROVIDER` | Yes | `sqlite` (default) or `postgresql`. Any other value aborts startup. |
| `DATABASE_CONNECTION_STRING` | Conditional | Empty allowed for SQLite (uses `storage/dotnetforge.db`); **required** for PostgreSQL. |
| `APP_NAME` | No | Display name. Defaults to `DotNetForge CMS`. |
| `APP_URL` | No | Public base URL, e.g. `http://localhost:5000`. Must be absolute; currently only validated. |
| `STORAGE_PROVIDER` | No | `local` (default) or `s3`; S3 needs `STORAGE_S3_*` (bucket, keys, endpoint). |

Outside Development no path may default into the deployment directory: SQLite and local storage need explicit
absolute paths.

Full reference: [.docs/features/configuration.md](.docs/features/configuration.md).

### Switching to PostgreSQL

```env
DATABASE_PROVIDER=postgresql
DATABASE_CONNECTION_STRING=Host=localhost;Port=5432;Database=dotnetforge;Username=postgres;Password=postgres
```

SQLite applies the committed EF Core migrations on startup. PostgreSQL builds the schema from the
model on first run (existing PostgreSQL databases are not migrated); see
[.docs/architecture/database.md](.docs/architecture/database.md).

## Project layout

```text
DotNetForge.Web.csproj   # ASP.NET Core MVC host (repository root) -> dotnet run/watch/build
src/                     # Core CMS libraries (Abstractions, Shared, Core, Data, Infrastructure, Extensions, Api)
tests/                   # DotNetForge.Tests (unit) + DotNetForge.IntegrationTests (integration)
extensions/              # User extensions, by type (themes, plugins, modules, widgets, ...)
storage/                 # Runtime data: media, backups, logs, updates (git-ignored)
.github/                 # CI workflow, Dependabot, and AI-agent quick references (.github/agents/)
.docs/                   # Technical documentation (start at .docs/README.md), incl. planned work per topic
.claude/skills/          # Claude Code project skills
```

See [.docs/architecture/codebase.md](.docs/architecture/codebase.md) for the responsibilities of each project and folder.

## Admin panel (server-rendered Razor)

The admin UI and the first-run **setup wizard** are **server-rendered ASP.NET Core MVC** in the
[`Areas/Admin/`](Areas/Admin/) area, served at **`/admin`** (and **`/setup`** for the wizard). No Node
build step - it ships with the host; styling is a small hand-rolled stylesheet in
[`wwwroot/css/admin.css`](wwwroot/css/admin.css). Forms post with antiforgery tokens; the only fetch
call is the Content Manager's drag-and-drop reorder (sending the token in an `X-CSRF-TOKEN` header).

Built screens: Dashboard, **Content Manager** (page tree + settings + scheduling), **Media** (upload, download,
delete; local or S3-compatible storage), Settings, Users, Roles, Audit Logs, Plugins, API Tokens. Remaining planned
areas link to documented placeholders.

**Admin extensions** are server-rendered too: drop an `admin`-type manifest with a `Views/Index.cshtml`
under [`extensions/admin/`](extensions/) and a sidebar tab appears at **`/admin/ext/{id}`** (rendered via
runtime Razor compilation, isolated in an iframe) with no code changes - see the audit-dashboard sample.

## The headless API

As a Super Admin or Admin, create a scoped token from the admin sidebar (**Settings · Global Settings → API Tokens**;
the full secret is shown once), then call the API with a bearer token:

```bash
curl -H "Authorization: Bearer <token>" http://localhost:5000/api/content/pages
```

Every endpoint enforces the token's granular permission (`content.read`, `media.read`, ...).
Missing authentication returns `401`; a missing permission returns `403`. Endpoint list:
[.docs/features/headless-api.md](.docs/features/headless-api.md).

## Testing & quality

```bash
dotnet test tests/DotNetForge.Tests              # unit tests
dotnet test tests/DotNetForge.IntegrationTests   # integration tests (setup, auth, API)
dotnet format --verify-no-changes                # style check (.editorconfig)
```

## Continuous integration

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on every push and pull request:

- **Build & test** across **Ubuntu, Windows, and macOS** (the cross-platform acceptance criterion):
  `dotnet build` + both test suites.
- **Format & style**: `dotnet format --verify-no-changes` against `.editorconfig` for every project.

[`.github/dependabot.yml`](.github/dependabot.yml) keeps NuGet and GitHub Actions dependencies current.

## Security highlights

- PBKDF2 (salted, adaptive) password hashing; per-account lockout after 5 failed sign-ins; rate-limited sign-in.
- Role-gated admin screens (six built-in roles), own-vs-any content/media permissions, and a granular permission key
  on every API action.
- Content-Security-Policy and security headers; Secure cookies outside Development; sessions end when a user is
  disabled.
- Uploads: allowlisted types, size cap, generated storage keys; private files served only after authorization.
- API tokens stored only as salted hashes; the plaintext is shown exactly once.
- Extension manifests validated before an extension is listed or rendered; HMAC-SHA256 signer ready for webhooks.
- CSRF protection on forms, HttpOnly/SameSite cookies, secrets confined to `.env`.

Controls, known gaps and remaining requirements: [.docs/features/security.md](.docs/features/security.md).

## License

[MIT](LICENSE).
