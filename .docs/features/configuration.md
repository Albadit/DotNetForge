# Configuration

Runtime configuration comes from a `.env` file plus process environment variables, validated once at startup into
the `AppEnvironment` singleton (with `AppEnvironment.Storage` = `StorageSettings`). `appsettings*.json` only
configures logging and `AllowedHosts`. Production values and a deployment checklist:
[deployment → environment variables](../guides/deployment.md#environment-variables).

## Keys

| Key | Required | Default | Validation | Used by |
| --- | --- | --- | --- | --- |
| `DATABASE_PROVIDER` | no (recommended outside Development) | detected from the connection string | a registered provider: `sqlite`, `postgresql`/`postgres`, `sqlserver`, `mysql`, `mongodb` | `DatabaseProviderRegistry` → the provider configures EF Core and `IDatabaseService` ([database configuration](../database/configuration.md)) |
| `DATABASE_CONNECTION_STRING` | outside Development | empty = SQLite at `<devRoot>/storage/dotnetforge.db` (Development only) | validated by the chosen provider ([validation](../database/configuration.md#validation-startup)); **secret** | the provider's EF Core configuration and executor |
| `DATABASE_NAME` | MongoDB without a database in the URL | from the URL path | - | `MongoDbDatabaseProvider` |
| `DATABASES_<NAME>_PROVIDER`, `_CONNECTION_STRING`, `_NAME` | no | - | as above, per named database | `IDatabaseService` ([more databases](../database/configuration.md#more-databases)) |
| `APP_NAME` | no | `DotNetForge CMS` | - | layouts and screens via `ViewData["AppName"]`, Dashboard, Settings, `/health` |
| `APP_URL` | no | `http://localhost:5000` | must be an absolute URI | validated only - **not used anywhere else** (Kestrel URLs come from `launchSettings.json` / `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS`) |
| `STORAGE_S3_SERVICE_URL` | no | empty = AWS S3 | absolute URL | `S3FileStorage` (R2, MinIO, Supabase, SeaweedFS endpoint) |
| `STORAGE_S3_BUCKET` | outside Development | - | non-empty | `S3FileStorage` |
| `STORAGE_S3_ACCESS_KEY_ID` | outside Development | - | non-empty | `S3FileStorage` |
| `STORAGE_S3_SECRET_ACCESS_KEY` | outside Development | - | non-empty; **secret** | `S3FileStorage` |
| `STORAGE_S3_REGION` | without a service URL (AWS) | `auto` when `STORAGE_S3_SERVICE_URL` is set | - | `S3FileStorage` (signing region) |
| `EXTENSIONS_PATH` | no | `extensions/` next to the app, else at the repository root | absolute, or relative to the content root | `ExtensionLoader`, runtime Razor compilation of extension views ([extensions](extensions.md#discovery)) |
| `STORAGE_S3_FORCE_PATH_STYLE` | no | `false` | `true` or `false` (`bool.TryParse`, case-insensitive) | `S3FileStorage` (`endpoint/bucket/key` URLs for MinIO, Supabase, SeaweedFS) |

Uploaded media always goes to S3-compatible object storage; there is no provider key, because R2, AWS S3, MinIO,
Supabase and SeaweedFS all speak the same API and differ only in endpoint, region and path style. When **no**
`STORAGE_S3_*` key is set, Development stores uploads in `<devRoot>/storage/media` instead, so a fresh checkout runs
without a bucket; every other environment refuses to start. Setting any one of the keys selects S3 and makes the
required ones mandatory, in Development too. Details and provider choice:
[media storage](media-storage.md#storage-architecture), [provider choice](media-storage.md#provider-choice).

All keys are read by `EnvConfigurationLoader` (`src/DotNetForge.Infrastructure/Configuration/`); constants:
`ProviderKey`, `ConnectionKey`, `DatabaseNameKey`, `AdditionalDatabasePrefix`, `AppNameKey`, `AppUrlKey`, `S3ServiceUrlKey`,
`S3BucketKey`, `S3AccessKeyIdKey`, `S3SecretAccessKeyKey`, `S3RegionKey`, `S3ForcePathStyleKey`, `ExtensionsPathKey`.

Changing any key requires a restart.

## Database

The loader only reads the database keys. The provider registry chooses the provider (from `DATABASE_PROVIDER`, or by
detection from the connection string), and that provider validates the settings and applies its defaults. All of
this happens while services are registered, before the app serves anything. The rules, detection table, messages
and examples for each database are in [database configuration](../database/configuration.md).

## Development vs. other environments

`Program.cs` calls `EnvConfigurationLoader.Load(builder.Environment.ContentRootPath,
builder.Environment.IsDevelopment())` (`isDevelopment` defaults to `true` for tests and tools). Outside Development
(`ASPNETCORE_ENVIRONMENT` other than `Development`; `docker/Dockerfile` sets `Production`) the deployment directory is
treated as **read-only**: nothing may default to a path inside it.

| Setting | Development | Any other environment |
| --- | --- | --- |
| SQLite database | `DATABASE_CONNECTION_STRING` optional; default `<devRoot>/storage/dotnetforge.db` | required; absolute `Data Source` on a writable volume (or in-memory), or use a database server |
| Uploaded media | S3 when any `STORAGE_S3_*` key is set, otherwise `<devRoot>/storage/media` | S3 required |
| PostgreSQL, SQL Server, MySQL, MongoDB | same rules | same rules |

`<devRoot>` is `AppPaths.DevelopmentDataRoot`: the repository root when running from a checkout (the folder containing
`DotNetForge.slnx`), otherwise the content root. Defaults never depend on the process working directory, so
`dotnet run --project src/DotNetForge.Web`, F5 and the test host all use the same `storage/` at the repository root. `SqliteDatabaseProvider` creates the SQLite file's
directory if missing (skipped for in-memory databases) - outside Development that is the configured volume, never
the deployment directory. `AppEnvironment.ResolveConnectionString()` still has a relative
`Data Source=storage/dotnetforge.db` fallback; the loader always sets a connection string, so only hand-built
instances reach it.

Requirements for a read-only container (`/tmp`, volumes, Data Protection keys in the database):
[deployment → read-only deployment requirements](../guides/deployment.md#read-only-deployment-requirements).

## Resolution rules

1. The `.env` file is read from the **content root** (`builder.Environment.ContentRootPath`) if it exists there,
   otherwise from the **repository root** (the folder containing `DotNetForge.slnx`; `AppPaths.Resolve`). In a
   checkout the content root is `src/DotNetForge.Web`, so the repository root's `.env` is used. The file is
   optional: without it Development runs on the SQLite and local-storage defaults, and containers set environment
   variables instead (`docker/Dockerfile.dockerignore` keeps `.env` out of the image).
2. For each key, a **non-empty process environment variable wins**; otherwise a non-empty `.env` value; otherwise
   unset. An empty value (`STORAGE_S3_REGION=`) counts as unset.
3. `DotEnvParser` supports `KEY=VALUE`, `#` comments, blank lines, an `export ` prefix, and matching single or
   double quotes around the value. Keys are case-insensitive. Lines without `=` are ignored. No variable expansion,
   no multi-line values, no inline comments.

## Failure behaviour

`EnvConfigurationLoader.Load` throws `ConfigurationException`; `Program.cs` prints
`[DotNetForge] Configuration error: <message>` to stderr and exits with code `1`; database configuration errors
(`DatabaseConfigurationException`, raised while services are registered) are reported the same way. The loader checks
`APP_URL`, named-database keys and storage, in that order; then the providers validate the databases. Only the first
failure is reported.

| Situation | Message |
| --- | --- |
| Any database setting | see [database configuration → Validation](../database/configuration.md#validation-startup) |
| Invalid `DATABASES_*` key | `'<key>' is not a valid database setting. Use DATABASES_<NAME>_PROVIDER, DATABASES_<NAME>_CONNECTION_STRING or DATABASES_<NAME>_NAME, where <NAME> has only letters and digits.` |
| Bad `APP_URL` | `APP_URL must be a valid absolute URL. Got '<x>'.` |
| No S3 storage, outside Development | `File storage is not configured: outside Development uploaded media is stored in S3-compatible object storage. Set STORAGE_S3_BUCKET, STORAGE_S3_ACCESS_KEY_ID and STORAGE_S3_SECRET_ACCESS_KEY, plus STORAGE_S3_SERVICE_URL (Cloudflare R2, MinIO, Supabase) or STORAGE_S3_REGION (AWS S3).` |
| Invalid service URL | `STORAGE_S3_SERVICE_URL must be an absolute URL. Got '<x>'.` |
| No service URL and no region | `STORAGE_S3_REGION is required when STORAGE_S3_SERVICE_URL is not set (AWS S3).` |
| Non-boolean path-style flag | `STORAGE_S3_FORCE_PATH_STYLE must be 'true' or 'false'. Got '<x>'.` |
| S3 selected without bucket, access key id or secret | `<KEY> is required for S3 storage.` (e.g. `STORAGE_S3_BUCKET is required for S3 storage.`) |

Configuration is only validated, never probed: an unreachable database or S3 endpoint is not a configuration error
(a database failure crashes startup; a storage failure surfaces on the first upload - see
[logging and error handling](logging-and-error-handling.md)).

## `.env.example`

The tracked template lists the keys in three sections (Database, Application, File storage) with comments; the
rarely needed `EXTENSIONS_PATH` is left out (its default is right for checkouts and published apps):

- Works as-is in Development (empty `DATABASE_CONNECTION_STRING` = SQLite, empty `STORAGE_S3_*` = local
  `storage/media`), writing to `storage/` at the repository root.
- Outside Development: PostgreSQL or an absolute SQLite path, and S3-compatible storage.
- PostgreSQL example connection string ending in `GSS Encryption Mode=Disable` (avoids a Kerberos library probe in
  slim containers, see [logging](logging-and-error-handling.md#expected-startup-log-lines)).
- S3 examples for Cloudflare R2 (`STORAGE_S3_REGION=auto`), AWS S3 (no service URL, real region) and the local S3
  server from `docker/compose.dev.yml` (`http://localhost:8333`, `STORAGE_S3_FORCE_PATH_STYLE=true`).

## ASP.NET Core variables

Read by the framework, not by `EnvConfigurationLoader`:

| Variable | Effect in this app |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Development` enables the defaults above, the developer exception page, cookie `SecurePolicy = SameAsRequest` and a report-only CSP; anything else gets the strict rules, `UseExceptionHandler("/error")`, HSTS and `Secure` cookies ([security](security.md)) |
| `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS` | Kestrel listen addresses; `docker/Dockerfile` sets `ASPNETCORE_HTTP_PORTS=8080` |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | set `true` behind a TLS-terminating reverse proxy: ASP.NET Core's built-in forwarded-headers handling takes the scheme and client IP from `X-Forwarded-Proto` / `X-Forwarded-For`, so `Secure` cookies, rate-limit partitions and audit IP addresses are right. There is no `UseHttpsRedirection`. See [behind a reverse proxy](../guides/deployment.md#behind-a-reverse-proxy) |
| `ASPNETCORE_TEMP` | where ASP.NET Core buffers request bodies over 64 KB (multipart media uploads); default the OS temp directory (`/tmp` on Linux). It must be writable: a read-only container needs `--tmpfs /tmp` (or `ASPNETCORE_TEMP` pointing at a writable volume) |

## Where each kind of configuration lives

| Kind | Where | Never |
| --- | --- | --- |
| Development config | `.env` copied from `.env.example`, `src/DotNetForge.Web/Properties/launchSettings.json`, `src/DotNetForge.Web/appsettings.Development.json`; runtime data in `<contentRoot>/storage/` (git-ignored) | committed `.env` |
| Production config | process environment variables set by the host or orchestrator ([environment variables](../guides/deployment.md#environment-variables)); the image contains no configuration | files inside the deployment directory |
| Secrets | `DATABASE_CONNECTION_STRING` (database password), `STORAGE_S3_ACCESS_KEY_ID` / `STORAGE_S3_SECRET_ACCESS_KEY` - in `.env` locally, in the host's secret store in production | documentation, `.env.example`, logs |
| Uploaded media | `STORAGE_*`: object storage or an absolute path on a volume ([media storage](media-storage.md#storage-architecture)) | the deployment directory |
| Database | `DATABASE_*`: PostgreSQL or a SQLite file on a volume ([deployment → database](../guides/deployment.md#database)); also holds the ASP.NET Core Data Protection key ring (table `DataProtectionKeys`) | the deployment directory |

## Other configuration sources

| Source | Contents |
| --- | --- |
| `src/DotNetForge.Web/appsettings.json` | log levels (`Default: Information`, `Microsoft.AspNetCore: Warning`, `Microsoft.EntityFrameworkCore.Database.Command: Warning`), `AllowedHosts: *` |
| `src/DotNetForge.Web/appsettings.Development.json` | `Microsoft.AspNetCore: Information` |
| `src/DotNetForge.Web/Properties/launchSettings.json` | profiles `http` (`http://localhost:5000`) and `https` (`https://localhost:5001;http://localhost:5000`), `ASPNETCORE_ENVIRONMENT=Development` |
| `.vscode/launch.json` | F5 profile with `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://localhost:5000` |
| `docker/Dockerfile` | `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_HTTP_PORTS=8080` ([Docker](../guides/deployment.md#docker)) |
| `docker/compose.dev.yml` | development-only PostgreSQL 17 (port 5432) and SeaweedFS S3 (port 8333, bucket `dotnetforge`) to run against |
| `UserSecretsId` `dotnetforge-cms` in the csproj | present but unused - secrets belong in `.env` or the environment |
| `Setting` rows | runtime key/value settings edited on the [Settings screen](../pages/settings.md); no code reads them yet |
| Extension manifest `settings` | passed to admin extension views as `ViewData["Settings"]` ([extensions](extensions.md)) |

## Design-time configuration

The `dotnet ef` factories in `src/DotNetForge.Data/DesignTimeDbContextFactory.cs` do **not** read `.env` or detect
the provider; the `--context` option picks it (MongoDB has no migrations):

| Factory | Context | Connection string |
| --- | --- | --- |
| `DesignTimeDbContextFactory` | `DotNetForgeDbContext` (SQLite migrations) | `DATABASE_CONNECTION_STRING` from the process environment, else `Data Source=:memory:` (generating migrations never opens it) |
| `PostgreSqlDesignTimeDbContextFactory` | `PostgreSqlDbContext` (`Migrations/PostgreSql/`) | `DATABASE_CONNECTION_STRING`, else the placeholder `Host=localhost;Database=dotnetforge_design` (never opened while generating migrations) |
| `SqlServerDesignTimeDbContextFactory` | `SqlServerDbContext` (`Migrations/SqlServer/`) | `DATABASE_CONNECTION_STRING`, else a `localhost` placeholder |
| `MySqlDesignTimeDbContextFactory` | `MySqlDbContext` (`Migrations/MySql/`) | `DATABASE_CONNECTION_STRING`, else a `localhost` placeholder |

Migration commands: [database](../architecture/database.md).

## Secrets

`.env` and `.env.*` are git-ignored (only `.env.example` is tracked), excluded from the build and from the Docker
build context (`docker/Dockerfile.dockerignore`). `StorageSettings.S3SecretAccessKey` is never logged or rendered. Never put real
connection strings or keys in documentation or `.env.example`.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.
The setup-wizard part of the same specification is in [installation → Planned](installation.md#planned-not-implemented).

### Requirements

| Key | Target use | Today |
| --- | --- | --- |
| `APP_URL` | public base URL for absolute links and redirects (and, per the developer guide, the URL the app is reached at) | validated only; Kestrel URLs come from `launchSettings.json` / `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS` |
| `APP_NAME` | display name in the admin UI **and in emails** | ✔ admin UI; no email sending exists ([email](email.md)) |

- Moving data between databases (e.g. SQLite → PostgreSQL) belongs to
  [transfer and updates](transfer-and-updates.md).

### Rules and validation

| Rule (spec) | Today |
| --- | --- |
| `.env` must exist and be parseable, otherwise startup aborts | `.env` is optional: Development falls back to SQLite and local storage; outside Development a missing connection string aborts startup. The process environment alone can configure the app (containers). `DotEnvParser` silently ignores malformed lines instead of failing. |
| `DATABASE_PROVIDER` selects `sqlite` or `postgresql` | ✔ and more: any registered provider (`sqlite`, `postgresql`, `sqlserver`, `mysql`, `mongodb`); when empty it is detected from `DATABASE_CONNECTION_STRING` ([database configuration](../database/configuration.md)) |
| A malformed file aborts with a descriptive error naming the offending key | ✔ for every validated key (see [Failure behaviour](#failure-behaviour)); no error for unparseable lines |

### Acceptance criteria

- [x] `.env.example` contains `DATABASE_PROVIDER`, `DATABASE_CONNECTION_STRING`, `APP_NAME` and `APP_URL` (plus
  `DATABASE_NAME`, the `STORAGE_S3_*` keys and an example for each database).
- [x] Copying `.env.example` to `.env` with default values starts the app on SQLite in Development
  (`SqliteDatabaseProvider.Normalize`; outside Development an absolute path is required by design).
- [x] `DATABASE_PROVIDER=postgresql` with a valid `DATABASE_CONNECTION_STRING` starts against PostgreSQL
  (`PostgreSqlDatabaseProvider` → `PostgreSqlDbContext`, `DatabaseInitializer.InitializeAsync` → `MigrateAsync`); the
  same holds for SQL Server, MySQL and MongoDB.
- [x] An unknown `DATABASE_PROVIDER` aborts startup with a descriptive error listing the registered providers
  (`DatabaseProviderRegistry.Resolve`).
- [x] Outside Development an empty `DATABASE_CONNECTION_STRING` aborts startup with a descriptive error.
- [ ] A missing `.env` aborts startup with guidance to copy `.env.example` - deliberately not: without `.env`,
  Development runs on the SQLite defaults; outside Development the missing connection string aborts startup.
- [x] `.env` is excluded from Git via `.gitignore` and secrets are never committed (`.gitignore`: `.env`, `.env.*`,
  `!.env.example`).

## Where to change things

- New key: add a constant and parsing/validation in `EnvConfigurationLoader` (`Load`, or `LoadStorage` and its
  helpers for storage), a property on `AppEnvironment` / `StorageSettings`, a line in `.env.example`, a test in
  `tests/DotNetForge.Tests/EnvConfigurationTests.cs`, a row in the table above and, if production needs it, in
  [deployment → environment variables](../guides/deployment.md#environment-variables). Consume it by injecting
  `AppEnvironment`, never by reading the environment directly.
- A new default location: only under the content root and only in Development; outside Development require an
  explicit absolute path.
