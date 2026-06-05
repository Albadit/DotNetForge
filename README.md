# DotNetForge CMS

A modular, secure, lightweight **hybrid CMS** built on **ASP.NET Core MVC** (C#, EF Core, Razor),
targeting **.NET 10**, with **SQLite** (default) and **PostgreSQL** support. It runs as a
**Traditional** CMS (visual admin), a **Headless** CMS (token-secured API), or **both at once**
(**Hybrid**) over the same content.

This repository implements the foundation described in [`dotnetforge_prompt/`](dotnetforge_prompt/README.md).
See [ARCHITECTURE.md](ARCHITECTURE.md) for the full design.

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
> a single command away). See [ARCHITECTURE.md](ARCHITECTURE.md#running-from-the-repository-root).

## Configuration (`.env`)

Copy `.env.example` to `.env` and set the values. Secrets are **never** committed (see
[`.gitignore`](.gitignore)).

| Key | Required | Description |
| --- | --- | --- |
| `DATABASE_PROVIDER` | Yes | `sqlite` (default) or `postgresql`. Any other value aborts startup. |
| `DATABASE_CONNECTION_STRING` | Conditional | Empty allowed for SQLite (uses `storage/dotnetforge.db`); **required** for PostgreSQL. |
| `APP_NAME` | Yes | Display name. Defaults to `DotNetForge CMS`. |
| `APP_URL` | Yes | Public base URL, e.g. `http://localhost:5000`. |

### Switching to PostgreSQL

```env
DATABASE_PROVIDER=postgresql
DATABASE_CONNECTION_STRING=Host=localhost;Port=5432;Database=dotnetforge;Username=postgres;Password=postgres
```

SQLite applies the committed EF Core migrations on startup. PostgreSQL builds the schema from the
model on first run; generating a PostgreSQL migration set is a documented next step in
[docs/developer-guide.md](docs/developer-guide.md).

## Project layout

```text
DotNetForge.Web.csproj   # ASP.NET Core MVC host (repository root) -> dotnet run/watch/build
src/                     # Core CMS libraries (Abstractions, Shared, Core, Data, Infrastructure, Extensions, Api)
tests/                   # DotNetForge.Tests (unit) + DotNetForge.IntegrationTests (integration)
extensions/              # User extensions, by type (themes, plugins, modules, widgets, ...)
storage/                 # Runtime data: media, backups, logs, updates (git-ignored)
.github/                 # CI workflow, Dependabot, and AI-agent documentation (.github/agents/)
docs/                    # Developer documentation
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full responsibilities of each project.

## The headless API

Create a scoped token from **Admin → Settings → API Tokens** (the full secret is shown once), then
call the API with a bearer token:

```bash
curl -H "Authorization: Bearer <token>" http://localhost:5000/api/content/pages
```

Every endpoint enforces the token's granular permission (`content.read`, `media.read`, ...).
Missing authentication returns `401`; a missing permission returns `403`.

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

- PBKDF2 (salted, adaptive) password hashing; account lockout + login rate considerations.
- RBAC permission checks on every admin and API action (six built-in roles).
- API tokens stored only as salted hashes; the plaintext is shown exactly once.
- HMAC-SHA256 webhook signing; extension manifest validation before install.
- CSRF protection on forms, HttpOnly/SameSite cookies, secrets confined to `.env`.

See [security.md](dotnetforge_prompt/security.md) and [ARCHITECTURE.md](ARCHITECTURE.md).

## License

[MIT](LICENSE).
