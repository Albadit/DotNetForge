<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="src/DotNetForge.Web/wwwroot/img/logo-dark.svg" />
    <img src="src/DotNetForge.Web/wwwroot/img/logo.svg" alt="DotNetForge" height="64" />
  </picture>
</p>

# DotNetForge CMS

A modular, secure, lightweight **hybrid CMS** built on **ASP.NET Core MVC** (C#, EF Core, Razor),
targeting **.NET 10**, on **PostgreSQL** (SQLite, SQL Server, MySQL and MongoDB can be added as extra databases for
your own data). It runs as a
**Traditional** CMS (visual admin), a **Headless** CMS (token-secured API), or **both at once**
(**Hybrid**) over the same content.

Content is structured and kept apart from presentation: define **content types** (blog posts, products, events...)
with a visual field builder, manage their entries in the **Content Manager**, and show them on any page with a
**content list** in the page builder - cards, a list, a carousel, a table or a detail page - mapping each element to a
field. Editing an entry updates every page that shows it. Details:
[structured content](.docs/features/structured-content.md).

Product goals and scope: [`.docs/product.md`](.docs/product.md). The technical documentation - architecture,
every screen, every feature, verified against the code - starts at [`.docs/README.md`](.docs/README.md); what is
built versus planned is in
[`.docs/implementation-status.md`](.docs/implementation-status.md).

## Requirements

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) (pinned via [`global.json`](global.json)).
- PostgreSQL - e.g. `docker compose -f docker/databases/postgres.yml up -d` (Docker), or any PostgreSQL server.

## Quick start

```bash
# 1. Configure the environment (secrets live in .env, never committed)
cp .env.example .env          # Windows PowerShell: Copy-Item .env.example .env

# 2. Start PostgreSQL and set DATABASE_CONNECTION_STRING in .env to
#    Host=localhost;Port=5432;Database=dotnetforge;Username=postgres;Password=postgres;GSS Encryption Mode=Disable
#    (or copy .env.developer instead of .env.example: it has it filled in)
docker compose -f docker/databases/postgres.yml up -d

# 3. Build everything (incl. extensions) and run the web app, from the repository root
dotnet build
dotnet run --project src/DotNetForge.Web
```

Then open <http://localhost:5000>. On first run the app redirects to **`/setup`**, where you create
the first administrator (assigned the **Super Admin** role). After setup you land on the admin
dashboard at **`/admin`**; the setup wizard is then permanently blocked.

Optional: `cp .env.developer .env` and `docker compose -f docker/compose.dev.yml up -d` run the app against PostgreSQL,
RustFS (S3) and Mailpit (SMTP); `docker compose -f docker/compose.app.yml up -d --build` runs the app itself in Docker
([development guide](.docs/guides/development.md#docker-services-for-development)).

## Production

The app runs from a **read-only deployment directory**: data goes to PostgreSQL, uploaded media
to S3-compatible object storage (Cloudflare R2 recommended), and the Data Protection key ring to the database.

```bash
docker build -f docker/Dockerfile -t dotnetforge .
docker run --read-only --tmpfs /tmp -p 8080:8080 -e "DATABASE_CONNECTION_STRING=Host=...;Database=..." \
  -e STORAGE_S3_SERVICE_URL=... -e STORAGE_S3_BUCKET=... \
  -e STORAGE_S3_ACCESS_KEY_ID=... -e STORAGE_S3_SECRET_ACCESS_KEY=... dotnetforge
```

Everything else - environment variables, reverse proxy, backups, verification: [deployment guide](.docs/guides/deployment.md).

## Everyday commands

Run from the repository root. `DotNetForge.slnx` is the solution, so `dotnet build` and `dotnet test` cover every
project; the web app is `src/DotNetForge.Web`.

| Command | What it does |
| --- | --- |
| `dotnet run --project src/DotNetForge.Web` | Builds and runs only the web app (http://localhost:5000; extensions as last built). |
| `dotnet watch --project src/DotNetForge.Web` | Same, with hot reload. |
| `dotnet build` | Builds the whole solution, including the sample extensions. |
| `dotnet test` | Runs the unit and integration tests (the integration tests need PostgreSQL, [testing](.docs/guides/testing.md#databases)). |

In VS Code, **F5** starts the debugger and **Run Build Task** offers build, run, watch and test tasks.

## Configuration (`.env`)

Copy `.env.example` to `.env` and set the values. Secrets are **never** committed (see
[`.gitignore`](.gitignore)).

| Key | Required | Description |
| --- | --- | --- |
| `DATABASE_CONNECTION_STRING` | Yes | The PostgreSQL connection string - the CMS database is always PostgreSQL; startup stops without it. |
| `DATABASES_<NAME>_PROVIDER`, `DATABASES_<NAME>_CONNECTION_STRING` | No | Additional databases (SQLite, PostgreSQL, SQL Server, MySQL, MongoDB) for extensions, custom API endpoints and exports. |
| `APP_NAME` | No | Display name. Defaults to `DotNetForge CMS`. |
| `APP_URL` | No | Public base URL, e.g. `http://localhost:5000`. Must be absolute; used for the links in emails. |
| `STORAGE_S3_*` | Outside Development | S3-compatible storage for uploads (bucket, keys, endpoint). Any provider works: the endpoint decides (R2, AWS S3, MinIO, Supabase). Empty in Development = `storage/media`. |

Outside Development nothing defaults: the PostgreSQL connection string and S3 storage are required.

Full reference: [.docs/features/configuration.md](.docs/features/configuration.md).

### Another PostgreSQL server

```env
DATABASE_CONNECTION_STRING=Host=db.example.com;Port=5432;Database=dotnetforge;Username=cms;Password=<secret>;GSS Encryption Mode=Disable
```

The app applies its committed EF Core migrations on startup; see
[.docs/architecture/database.md](.docs/architecture/database.md). Additional databases:
[.docs/database/configuration.md](.docs/database/configuration.md).

## Project layout

```text
DotNetForge.slnx         # Solution: every project below
src/
  DotNetForge.Web/       # ASP.NET Core MVC web app: admin area, public site, setup, composition root
  DotNetForge.*/         # Libraries: Abstractions, Shared, Core, Data, Content, Infrastructure, PageBuilder, Extensions, Sdk, Api
tests/                   # DotNetForge.Tests (unit) + DotNetForge.IntegrationTests (integration)
extensions/              # Your extensions, by type (themes, plugins, modules, widgets, admin, ...); read-only at runtime
docker/                  # Dockerfile, compose.dev.yml (PostgreSQL + RustFS + Mailpit), compose.app.yml (app in Docker), databases/ s3/ mail/
.docs/                   # Technical documentation (start at .docs/README.md), incl. planned work per topic
.github/                 # CI workflow, Dependabot, and AI-agent quick references (.github/agents/)
.claude/skills/          # Claude Code project skills
storage/                 # Development only, git-ignored: local media (and SQLite additional databases)
```

See [.docs/architecture/overview.md](.docs/architecture/overview.md) for a one-page architecture summary and
[.docs/architecture/codebase.md](.docs/architecture/codebase.md) for the responsibilities of each project and folder.

## Admin panel (server-rendered Razor)

The admin UI and the first-run **setup wizard** are **server-rendered ASP.NET Core MVC** in the
[`src/DotNetForge.Web/Areas/Admin/`](src/DotNetForge.Web/Areas/Admin/) area, served at **`/admin`** (and **`/setup`** for the wizard). No Node
build step - it ships with the host. Every screen uses the dependency-free **Forge design system** (light and dark,
role-aware sidebar, works on phones and without JavaScript): CSS in
[`src/DotNetForge.Web/wwwroot/css/forge/`](src/DotNetForge.Web/wwwroot/css/forge/), Razor tag helpers in
[`src/DotNetForge.Web/Ui/`](src/DotNetForge.Web/Ui/) - see [design-system.md](.docs/architecture/design-system.md).
Forms post with antiforgery tokens; the few `fetch` calls (drag-and-drop reorder, the page builder, pickers) send the
token in an `X-CSRF-TOKEN` header.

Built screens: Dashboard, **Content Manager** (entries of your content types, generated forms, drafts and publishing),
**Content types** (visual field builder, templates), **Pages** (page tree, settings, SEO, scheduling, permissions) with
the visual **page builder**, **Media** (upload, download,
delete and bulk delete; local or S3-compatible storage), Settings, Users, Roles, Audit Logs, Plugins (enable/disable extensions),
API Tokens. Remaining planned areas link to documented placeholders.

## Extensions

You customize the CMS only in [`extensions/`](extensions/), never in `src/`. An extension is a folder with a
`dotnetforge.extension.json` manifest, optional C# code compiled against `DotNetForge.Sdk`, and optional Razor views
and static files. Once a Super Admin enables it on **Plugins** it can add an admin tab, a dashboard widget, the public
theme, content on chosen pages, its own endpoints, a media storage backend, a "Sign in with ..." provider or a
reaction to audit events. Nine runnable samples ship with the repository - try them with
[extensions/README.md](extensions/README.md); write your own with the
[extension development guide](.docs/guides/extension-development.md).

## The headless API

As a Super Admin or Admin, create a scoped token from the admin sidebar (**Settings → Global Settings → API Tokens**;
the full secret is shown once), then call the API with a bearer token:

```bash
curl -H "Authorization: Bearer <token>" http://localhost:5000/api/content/pages
curl -H "Authorization: Bearer <token>" "http://localhost:5000/api/entries/blog-posts?sort=-publishedOn&pageSize=10"
```

Every endpoint enforces the token's granular permission (`content.read`, `entries.read`, `media.read`, ...).
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
- Extension manifests validated, extensions disabled until a Super Admin enables them (audited), endpoints of
  inactive extensions answer 404; extension code itself is trusted. HMAC-SHA256 signer ready for webhooks.
- CSRF protection on forms, HttpOnly/SameSite cookies, secrets confined to `.env`.

Controls, known gaps and remaining requirements: [.docs/features/security.md](.docs/features/security.md).

## License

[MIT](LICENSE).
