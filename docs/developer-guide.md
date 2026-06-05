# Developer Guide

## Prerequisites

- .NET SDK 10.0 (pinned in `global.json`).
- Optional: PostgreSQL for the non-default provider.

## Setup

```bash
cp .env.example .env          # PowerShell: Copy-Item .env.example .env
dotnet tool restore           # restores the pinned dotnet-ef tool
dotnet run                    # or: dotnet watch
```

Open <http://localhost:5000> and complete the one-time setup wizard to create the first Super Admin.

## Everyday commands (from the repo root)

| Command | Purpose |
| --- | --- |
| `dotnet run` | Run the web host. |
| `dotnet watch` | Run with hot reload. |
| `dotnet build` | Build the host + referenced libraries. |
| `dotnet test tests/DotNetForge.Tests` | Unit tests. |
| `dotnet test tests/DotNetForge.IntegrationTests` | Integration tests. |
| `dotnet format --verify-no-changes` | Verify formatting against `.editorconfig`. |

## Database & migrations

The provider is chosen by `DATABASE_PROVIDER` in `.env`.

- **SQLite (default):** migrations in `src/DotNetForge.Data/Migrations` are applied on startup.
- **PostgreSQL:** set `DATABASE_PROVIDER=postgresql` and a non-empty `DATABASE_CONNECTION_STRING`.
  The schema is created from the model on first run.

Create a migration:

```bash
dotnet ef migrations add <Name> \
  --project src/DotNetForge.Data --startup-project src/DotNetForge.Data --output-dir Migrations
```

To target PostgreSQL at design time, set `DATABASE_PROVIDER=postgresql` and
`DATABASE_CONNECTION_STRING` in the environment before running the command.

## Project layout

See [ARCHITECTURE.md](../ARCHITECTURE.md). The web host is at the repository root; core libraries are
under `src/`; tests under `tests/`; extensions under `extensions/`; runtime data under `storage/`.

## Troubleshooting

- **Startup aborts with a configuration error:** ensure `.env` exists and `DATABASE_PROVIDER` is
  `sqlite` or `postgresql` (PostgreSQL also needs a connection string).
- **Stuck on the setup page:** the CMS is not yet installed; complete `/setup`.
- **`dotnet ef` not found:** run `dotnet tool restore`.
