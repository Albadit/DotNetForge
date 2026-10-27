# Configuration

Runtime configuration comes from a `.env` file plus process environment variables, validated once at startup into
the `AppEnvironment` singleton (with `AppEnvironment.Storage` = `StorageSettings`). `appsettings*.json` only
configures logging and `AllowedHosts`. Production values and a deployment checklist:
[deployment → environment variables](../guides/deployment.md#environment-variables).

## Keys

| Key | Required | Default | Validation | Used by |
| --- | --- | --- | --- | --- |
| `DATABASE_CONNECTION_STRING` | yes (every environment) | - | key=value Npgsql form ([validation](../database/configuration.md#validation-startup)); empty stops startup; **secret** | EF Core and the CMS database's executor; the CMS database is always PostgreSQL (`DatabaseProviderRegistry` → `PostgreSqlDatabaseProvider`) |
| `DATABASE_PROVIDER` | - | - | removed: not read; a value stops startup ("no longer used"), an empty line is ignored | - |
| `DOTNETFORGE_ENV_FILE` | no (environment variable only) | `.env` in the content root / repository root | a file path; a missing file = no file values | `EnvConfigurationLoader` - the integration tests point it at a missing file so a developer's `.env` never applies |
| `DATABASES_<NAME>_PROVIDER`, `_CONNECTION_STRING` | no | - | any registered provider (`sqlite`, `postgresql`/`postgres`, `sqlserver`, `mysql`, `mongodb`), detected from the connection string when empty; validated by that provider | additional databases: `IDatabaseService`, custom API endpoints, the Data Management export ([additional databases](../database/configuration.md#more-databases)) |
| `APP_NAME` | no | `DotNetForge CMS` | - | layouts and screens via `ViewData["AppName"]`, Dashboard, Settings, `/health` |
| `APP_URL` | no | `http://localhost:5000` | must be an absolute URI | the links in password-reset and confirmation emails (`AccountService`, never the request's `Host` header); Kestrel URLs come from `launchSettings.json` / `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS`, not from it |
| `STORAGE_S3_SERVICE_URL` | no | empty = AWS S3 | absolute URL | `S3FileStorage` (R2, MinIO, Supabase, SeaweedFS endpoint) |
| `STORAGE_S3_BUCKET` | outside Development | - | non-empty | `S3FileStorage` |
| `STORAGE_S3_ACCESS_KEY_ID` | outside Development | - | non-empty | `S3FileStorage` |
| `STORAGE_S3_SECRET_ACCESS_KEY` | outside Development | - | non-empty; **secret** | `S3FileStorage` |
| `STORAGE_S3_REGION` | without a service URL (AWS) | `auto` when `STORAGE_S3_SERVICE_URL` is set | - | `S3FileStorage` (signing region) |
| `EXTENSIONS_PATH` | no | `extensions/` next to the app, else at the repository root | absolute, or relative to the content root | `ExtensionLoader`, `ExtensionRuntime` (code loading), runtime Razor compilation of extension views ([extensions](extensions.md#discovery)) |
| `STORAGE_S3_FORCE_PATH_STYLE` | no | `false` | `true` or `false` (`bool.TryParse`, case-insensitive) | `S3FileStorage` (`endpoint/bucket/key` URLs for MinIO, Supabase, SeaweedFS) |
| `SMTP_HOST` | to send email | empty = no email | required (with `SMTP_FROM`) once any of `SMTP_HOST`, `SMTP_FROM`, `SMTP_USERNAME`, `SMTP_PASSWORD` is set | `SmtpEmailSender` ([Email (SMTP)](#email-smtp)) |
| `SMTP_PORT` | no | `587` | 1-65535 | `SmtpEmailSender` |
| `SMTP_USERNAME`, `SMTP_PASSWORD` | when the server needs authentication | - | `SMTP_PASSWORD` is a **secret** | `SmtpEmailSender` (credentials only when the username is set) |
| `SMTP_FROM` | to send email | - | an email address (`MailAddress.TryCreate`) | sender address |
| `SMTP_FROM_NAME` | no | `APP_NAME` | - | sender display name |
| `SMTP_TLS` | no | `true` | only `false` (case-insensitive) turns it off | STARTTLS (`SmtpClient.EnableSsl`); implicit TLS on port 465 is not supported |

Uploaded media always goes to S3-compatible object storage; there is no provider key, because R2, AWS S3, MinIO,
Supabase and SeaweedFS all speak the same API and differ only in endpoint, region and path style. When **no**
`STORAGE_S3_*` key is set, Development stores uploads in `<devRoot>/storage/media` instead, so a fresh checkout runs
without a bucket; every other environment refuses to start. Setting any one of the keys selects S3 and makes the
required ones mandatory, in Development too. Details and provider choice:
[media storage](media-storage.md#storage-architecture), [provider choice](media-storage.md#provider-choice).

All keys are read by `EnvConfigurationLoader` (`src/DotNetForge.Infrastructure/Configuration/`); constants:
`ProviderKey`, `ConnectionKey`, `AdditionalDatabasePrefix`, `AppNameKey`, `AppUrlKey`, `S3ServiceUrlKey`,
`S3BucketKey`, `S3AccessKeyIdKey`, `S3SecretAccessKeyKey`, `S3RegionKey`, `S3ForcePathStyleKey`, `ExtensionsPathKey`,
`SmtpHostKey`, `SmtpPortKey`, `SmtpUsernameKey`, `SmtpPasswordKey`, `SmtpFromKey`, `SmtpFromNameKey`, `SmtpTlsKey`.

### Email (SMTP)

Optional. Leave every `SMTP_*` value empty and the app sends no email: the sign-in screen has no **Forgot password?**
link and email confirmation cannot be turned on ([authentication](authentication.md#self-service-accounts)). Set
at least `SMTP_HOST` and `SMTP_FROM` to send the password-reset and confirmation emails through `SmtpEmailSender`
([email](email.md)); `EmailSettings.IsConfigured` is true exactly then. These keys are the only SMTP configuration:
[Email → Configuration](../pages/email-configuration.md) shows them read-only (with a connection test, test sends and
the email log); changes need a restart. A local SMTP catcher works for development - Mailpit from
`docker/mail/mailpit.yml` (filled in in `.env.developer`).

Changing any key requires a restart.

## Extension settings

Extensions add no core keys. An extension reads its own values - typically secrets - with
`ExtensionContext.GetEnvironmentValue(name)` (`src/DotNetForge.Sdk/ExtensionContext.cs`), which uses
`AppEnvironment.Lookup`: the same resolution as the core keys ([Resolution rules](#resolution-rules): non-empty
process environment variable, else non-empty `.env` value, else `null`). The `.env` file is parsed once at startup,
so a change needs a restart. Non-secret options belong in the manifest `settings` instead
([extensions → Manifest](extensions.md#manifest)).

Keys the samples read (none is in `.env.example`; each is documented by its extension):

| Key | Extension | Without it |
| --- | --- | --- |
| `DISCORD_CLIENT_ID`, `DISCORD_CLIENT_SECRET` | `extensions/authentication/discord-login` | `ConfigureServices` throws → the extension shows **not running** on [Plugins](../pages/plugins.md) and cannot be enabled |
| `S3_CONNECTOR_ACCESS_KEY_ID`, `S3_CONNECTOR_SECRET_ACCESS_KEY` | `extensions/connectors/s3-storage` | the storage factory throws when media storage is first used while the connector is enabled |
| `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN` | `extensions/providers/sms-notifications` | only needed for its `twilio` gateway; the default `log` gateway works offline |

Treat them like the core secrets ([Secrets](#secrets)): `.env` locally, the host's secret store in production.

## Database

The loader only reads the database keys. The provider registry binds the CMS database to PostgreSQL (refusing any
other `DATABASE_PROVIDER`) and each additional database to its provider (from `DATABASES_<NAME>_PROVIDER`, or by
detection from the connection string); the provider validates the settings and applies its defaults. All of
this happens while services are registered, before the app serves anything. The rules, detection table, messages
and examples for each database are in [database configuration](../database/configuration.md).

## Development vs. other environments

`Program.cs` calls `EnvConfigurationLoader.Load(builder.Environment.ContentRootPath,
builder.Environment.IsDevelopment())` (`isDevelopment` defaults to `true` for tests and tools). Outside Development
(`ASPNETCORE_ENVIRONMENT` other than `Development`; `docker/Dockerfile` sets `Production`) the deployment directory is
treated as **read-only**: nothing may default to a path inside it.

| Setting | Development | Any other environment |
| --- | --- | --- |
| CMS database (PostgreSQL) | `DATABASE_CONNECTION_STRING` optional; default the local server `localhost:5432`, database `dotnetforge` | required |
| Uploaded media | S3 when any `STORAGE_S3_*` key is set, otherwise `<devRoot>/storage/media` | S3 required |
| SQLite additional database | connection string optional; default `<devRoot>/storage/<name>.db` | required; absolute `Data Source` on a writable volume (or in-memory) |
| Other additional databases | connection string required | connection string required |

`<devRoot>` is `AppPaths.DevelopmentDataRoot`: the repository root when running from a checkout (the folder containing
`DotNetForge.slnx`), otherwise the content root. Defaults never depend on the process working directory, so
`dotnet run --project src/DotNetForge.Web`, F5 and the test host all use the same `storage/` at the repository root.
`SqliteDatabaseProvider` creates a SQLite additional database's directory if missing (skipped for in-memory
databases) - outside Development that is the configured volume, never the deployment directory.

Requirements for a read-only container (`/tmp`, volumes, Data Protection keys in the database):
[deployment → read-only deployment requirements](../guides/deployment.md#read-only-deployment-requirements).

## Resolution rules

1. The `.env` file is read from the **content root** (`builder.Environment.ContentRootPath`) if it exists there,
   otherwise from the **repository root** (the folder containing `DotNetForge.slnx`; `AppPaths.Resolve`). In a
   checkout the content root is `src/DotNetForge.Web`, so the repository root's `.env` is used. The file is
   optional: without it Development runs on the local PostgreSQL and local-storage defaults, and containers set environment
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
`APP_URL`, named-database keys, storage and email, in that order; then the providers validate the databases. Only the
first failure is reported.

| Situation | Message |
| --- | --- |
| Any database setting | see [database configuration → Validation](../database/configuration.md#validation-startup) |
| Invalid `DATABASES_*` key | `'<key>' is not a valid database setting. Use DATABASES_<NAME>_PROVIDER or DATABASES_<NAME>_CONNECTION_STRING, where <NAME> has only letters and digits.` |
| Bad `APP_URL` | `APP_URL must be a valid absolute URL. Got '<x>'.` |
| No S3 storage, outside Development | `File storage is not configured: outside Development uploaded media is stored in S3-compatible object storage. Set STORAGE_S3_BUCKET, STORAGE_S3_ACCESS_KEY_ID and STORAGE_S3_SECRET_ACCESS_KEY, plus STORAGE_S3_SERVICE_URL (Cloudflare R2, MinIO, Supabase) or STORAGE_S3_REGION (AWS S3).` |
| Invalid service URL | `STORAGE_S3_SERVICE_URL must be an absolute URL. Got '<x>'.` |
| No service URL and no region | `STORAGE_S3_REGION is required when STORAGE_S3_SERVICE_URL is not set (AWS S3).` |
| Non-boolean path-style flag | `STORAGE_S3_FORCE_PATH_STYLE must be 'true' or 'false'. Got '<x>'.` |
| S3 selected without bucket, access key id or secret | `<KEY> is required for S3 storage.` (e.g. `STORAGE_S3_BUCKET is required for S3 storage.`) |
| Partial SMTP configuration | `Set both SMTP_HOST and SMTP_FROM to send email, or leave every SMTP_* value empty.` |
| Invalid sender | `SMTP_FROM must be an email address. Got '<x>'.` |
| Invalid port | `SMTP_PORT must be a port number (1-65535). Got '<x>'.` |

Configuration is only validated, never probed: an unreachable database or S3 endpoint is not a configuration error
(a database failure crashes startup; a storage failure surfaces on the first upload - see
[logging and error handling](logging-and-error-handling.md)).

## `.env.example`

The tracked template lists the keys in four sections (Database, Application, File storage, Email) with comments; the
rarely needed `EXTENSIONS_PATH` is left out (its default is right for checkouts and published apps):

- `DATABASE_CONNECTION_STRING` is empty and must be set (the comment gives the local server of
  `docker/databases/postgres.yml`: `Host=localhost;Port=5432;Database=dotnetforge;Username=postgres;Password=postgres;GSS Encryption Mode=Disable`); empty `STORAGE_S3_*` = local `storage/media` at the repository root in
  Development.
- An example of an additional database (`DATABASES_REPORTS_*`, SQL Server).

`.env.developer` (also tracked) is the same contract filled in for the local RustFS or Ceph S3 server and Mailpit of
`docker/compose.dev.yml` - public development credentials only ([development → production-like S3 and
email](../guides/development.md#production-like-s3-and-email)).
- Outside Development: a PostgreSQL connection string and S3-compatible storage.
- PostgreSQL example connection string ending in `GSS Encryption Mode=Disable` (avoids a Kerberos library probe in
  slim containers, see [logging](logging-and-error-handling.md#expected-startup-log-lines)).
- S3 examples for Cloudflare R2 (`STORAGE_S3_REGION=auto`), AWS S3 (no service URL, real region) and the local S3
  server from `docker/s3/seaweedfs.yml` (`http://localhost:8333`, `STORAGE_S3_FORCE_PATH_STYLE=true`).

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
| Secrets | `DATABASE_CONNECTION_STRING` (database password), `STORAGE_S3_ACCESS_KEY_ID` / `STORAGE_S3_SECRET_ACCESS_KEY`, `SMTP_PASSWORD`, extension secrets ([Extension settings](#extension-settings)) - in `.env` locally, in the host's secret store in production | documentation, `.env.example`, logs, manifests |
| Uploaded media | `STORAGE_*`: object storage or an absolute path on a volume ([media storage](media-storage.md#storage-architecture)) | the deployment directory |
| Database | `DATABASE_*`: PostgreSQL ([deployment → database](../guides/deployment.md#database)); also holds the ASP.NET Core Data Protection key ring (table `DataProtectionKeys`) | the deployment directory |

## Other configuration sources

| Source | Contents |
| --- | --- |
| `src/DotNetForge.Web/appsettings.json` | log levels (`Default: Information`, `Microsoft.AspNetCore: Warning`, `Microsoft.EntityFrameworkCore.Database.Command: Warning`), `AllowedHosts: *` |
| `src/DotNetForge.Web/appsettings.Development.json` | `Microsoft.AspNetCore: Information` |
| `src/DotNetForge.Web/Properties/launchSettings.json` | profiles `http` (`http://localhost:5000`) and `https` (`https://localhost:5001;http://localhost:5000`), `ASPNETCORE_ENVIRONMENT=Development` |
| `.vscode/launch.json` | F5 profile with `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://localhost:5000` |
| `docker/Dockerfile` | `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_HTTP_PORTS=8080` ([Docker](../guides/deployment.md#docker)) |
| `docker/compose.dev.yml` | development-only PostgreSQL 17, RustFS S3 and Mailpit for `.env.developer`; one file per service in `docker/databases/`, `docker/s3/`, `docker/mail/` ([development](../guides/development.md#docker-services-for-development)) |
| `UserSecretsId` `dotnetforge-cms` in the csproj | present but unused - secrets belong in `.env` or the environment |
| `Setting` rows | runtime key/value settings edited on the [Settings screen](../pages/settings.md); the `users.*` keys are the Advanced User Settings, read by `UserAccountSettingsService` and edited on [Advanced Settings](../pages/advanced-settings.md); no code reads the others |
| Extension manifest `settings` | `ExtensionContext` (code and views) and `ViewData["Settings"]` in every extension view ([extensions](extensions.md#manifest)); read-only, no screen edits them |

## Design-time configuration

The `dotnet ef` factory in `src/DotNetForge.Data/DesignTimeDbContextFactory.cs` does **not** read `.env`:

| Factory | Context | Connection string |
| --- | --- | --- |
| `PostgreSqlDesignTimeDbContextFactory` | `PostgreSqlDbContext` (`Database/Providers/PostgreSql/Migrations/`) | `DATABASE_CONNECTION_STRING` from the process environment, else the placeholder `Host=localhost;Database=dotnetforge_design` (never opened while generating migrations) |

Migration commands: [database](../architecture/database.md).

## Secrets

`.env` and `.env.*` are git-ignored (only `.env.example` and `.env.developer` - development credentials for the local containers - are tracked), excluded from the build and from the Docker
build context (`docker/Dockerfile.dockerignore`). `StorageSettings.S3SecretAccessKey` and `EmailSettings.Password` are never logged or rendered. Never put real
connection strings or keys in documentation or `.env.example`.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.
The setup-wizard part of the same specification is in [installation → Planned](installation.md#planned-not-implemented).

### Requirements

| Key | Target use | Today |
| --- | --- | --- |
| `APP_URL` | public base URL for absolute links and redirects (and, per the developer guide, the URL the app is reached at) | ✔ links in emails; Kestrel URLs come from `launchSettings.json` / `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS` |
| `APP_NAME` | display name in the admin UI **and in emails** | ✔ admin UI and emails (subject, body, default sender name) |

- Moving data between sites belongs to [transfer and updates](transfer-and-updates.md).

### Rules and validation

| Rule (spec) | Today |
| --- | --- |
| `.env` must exist and be parseable, otherwise startup aborts | `.env` is optional: Development falls back to the local PostgreSQL and local storage; outside Development a missing connection string aborts startup. The process environment alone can configure the app (containers). `DotEnvParser` silently ignores malformed lines instead of failing. |
| `DATABASE_PROVIDER` selects `sqlite` or `postgresql` | deliberately not: the CMS database is PostgreSQL only (empty = `postgresql`); SQLite, SQL Server, MySQL and MongoDB are additional databases (`DATABASES_<NAME>_PROVIDER`) ([database configuration](../database/configuration.md)) |
| A malformed file aborts with a descriptive error naming the offending key | ✔ for every validated key (see [Failure behaviour](#failure-behaviour)); no error for unparseable lines |

### Acceptance criteria

- [x] `.env.example` contains `DATABASE_CONNECTION_STRING`, `APP_NAME` and `APP_URL` (plus
  the `STORAGE_S3_*` and `SMTP_*` keys and an additional-database example).
- [ ] Copying `.env.example` to `.env` with default values starts the app on SQLite in Development - changed: the CMS
  runs on PostgreSQL and `DATABASE_CONNECTION_STRING` must be set (`.env.developer` has it filled in).
- [x] A valid `DATABASE_CONNECTION_STRING` starts against PostgreSQL
  (`PostgreSqlDatabaseProvider` → `PostgreSqlDbContext`, `DatabaseInitializer.InitializeAsync` → `MigrateAsync`).
- [x] A `DATABASE_PROVIDER` value aborts startup ("no longer used", `EnvConfigurationLoader`); the CMS database is always PostgreSQL.
- [x] An empty `DATABASE_CONNECTION_STRING` aborts startup with a descriptive error - in every environment.
- [ ] A missing `.env` aborts startup with guidance to copy `.env.example` - deliberately not: without `.env`,
  Development runs on the local PostgreSQL default; outside Development the missing connection string aborts startup.
- [x] `.env` is excluded from Git via `.gitignore` and secrets are never committed (`.gitignore`: `.env`, `.env.*`,
  `!.env.example`, `!.env.developer`).

## Where to change things

- New key: add a constant and parsing/validation in `EnvConfigurationLoader` (`Load`, or `LoadStorage` and its
  helpers for storage), a property on `AppEnvironment` / `StorageSettings`, a line in `.env.example`, a test in
  `tests/DotNetForge.Tests/EnvConfigurationTests.cs`, a row in the table above and, if production needs it, in
  [deployment → environment variables](../guides/deployment.md#environment-variables). Consume it by injecting
  `AppEnvironment`, never by reading the environment directly.
- A new default location: only under the content root and only in Development; outside Development require an
  explicit absolute path.
