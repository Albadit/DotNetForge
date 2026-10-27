# Database configuration

All other configuration keys: [features/configuration.md](../features/configuration.md).

## CMS database

The CMS runs on **PostgreSQL** - its **main** database:

| Key | Purpose |
| --- | --- |
| `DATABASE_CONNECTION_STRING` | key=value Npgsql connection string. **Secret. Required** - also in Development; without it startup stops with how to set it |

```env
DATABASE_CONNECTION_STRING=Host=db;Port=5432;Database=dotnetforge;Username=cms;Password=<secret>;GSS Encryption Mode=Disable
```

There is no provider setting and no fallback: `DATABASE_PROVIDER` is not read (a value stops startup with "no longer
used"), and an empty connection string stops startup - also in Development. The local server of
`docker/databases/postgres.yml` (`docker compose -f docker/databases/postgres.yml up -d`) is
`Host=localhost;Port=5432;Database=dotnetforge;Username=postgres;Password=postgres;GSS Encryption Mode=Disable` ([development](../guides/development.md)).

Why PostgreSQL only: one migration set, one SQL dialect for the CMS and its custom endpoints, and no query
restrictions for databases that can't run them. SQLite, SQL Server, MySQL and MongoDB remain available as
[additional databases](#more-databases) for your own data.

## How the provider is chosen

- **CMS database:** always the provider registered as `postgresql` (`DatabaseProviderRegistry.CmsProvider`); there is
  no setting, and the connection string is never used to detect another database.
- **Additional databases:** `DATABASES_<NAME>_PROVIDER` names the provider. An unknown name stops startup with the
  list of registered names. Empty: every registered provider is asked whether it recognizes the connection string
  (`IDatabaseProvider.CanHandle`); **exactly one** match is used, **none or several** stop startup and ask for the
  provider.

| Connection string | Detected as (additional databases) |
| --- | --- |
| empty | SQLite (Development default `storage/<name>.db`) |
| `Data Source=` / `Filename=` without server keys | SQLite |
| `Host=` | PostgreSQL |
| `Initial Catalog`, `Trusted_Connection`, `Integrated Security`, `TrustServerCertificate` | SQL Server |
| `mongodb://`, `mongodb+srv://` | MongoDB |
| `Server=…;Uid=…` (MySQL style) | not detected: ambiguous, set `DATABASES_<NAME>_PROVIDER=mysql` |

## Validation (startup)

The chosen provider validates the settings. Invalid configuration stops the process with exit code 1 and
`[DotNetForge] Configuration error: <message>`:

| Situation | Message |
| --- | --- |
| CMS database not PostgreSQL | `DATABASE_PROVIDER '<x>' is not supported: the CMS database is PostgreSQL. SQLite, SQL Server, MySQL and MongoDB can be added as additional databases (DATABASES_<NAME>_PROVIDER).` |
| CMS database without connection string, outside Development | `PostgreSQL needs a connection string for database 'main' (DATABASE_CONNECTION_STRING).` |
| PostgreSQL URL | `PostgreSQL connection strings must use the key=value form, not a URL: …` |
| Unknown provider name (additional) | `Unknown database provider '<x>' for database '<name>'. Registered providers: mongodb, mysql, postgres, postgresql, sqlite, sqlserver.` |
| Undetectable connection string (additional) | `Cannot tell which database the connection string of '<name>' is for. Set DATABASES_<NAME>_PROVIDER to one of: …` |
| Ambiguous connection string (additional) | `The connection string of '<name>' fits several databases (…). Set DATABASES_<NAME>_PROVIDER.` |
| SQLite, empty, outside Development | `DATABASES_<NAME>_CONNECTION_STRING is required outside Development: the deployment directory is read-only, …` |
| SQLite, relative path, outside Development | `The SQLite data source must be an absolute path outside Development (got '<x>').` |
| SQL provider without connection string | `<Provider> needs a connection string for database '<name>' (DATABASES_<NAME>_CONNECTION_STRING).` |
| MongoDB, not a MongoDB URL | `MongoDB needs a connection string like 'mongodb://<host>:27017/<database>?replicaSet=rs0' in DATABASES_<NAME>_CONNECTION_STRING.` |
| MongoDB without database name | `MongoDB needs a database name in the URL path of DATABASES_<NAME>_CONNECTION_STRING (mongodb://host:27017/<database>).` |

Configuration is validated, not probed. An unreachable server is not a configuration error: the CMS database's first
use at startup (migrations) fails with the driver's connection error; an additional database fails on its first
command.

## More databases

`IDatabaseService` can also reach further databases, of any registered provider. Each gets a name and its own
provider:

```env
DATABASES_REPORTS_PROVIDER=sqlserver
DATABASES_REPORTS_CONNECTION_STRING=Server=reports.example.com;Initial Catalog=reports;User Id=reader;Password=<secret>;TrustServerCertificate=true
DATABASES_EVENTS_PROVIDER=mongodb
DATABASES_EVENTS_CONNECTION_STRING=mongodb://events-db:27017/events?replicaSet=rs0
DATABASES_LOCAL_PROVIDER=sqlite
DATABASES_LOCAL_CONNECTION_STRING=Data Source=/data/local.db
```

```csharp
await db.QueryAsync(DatabaseCommand.Find("daily_sales").On("reports").OrderBy("day"));
```

- Keys follow the pattern `DATABASES_<NAME>_PROVIDER` and `DATABASES_<NAME>_CONNECTION_STRING`.
  - `<NAME>` is letters and digits only; it is matched case-insensitively and stored in lower case.
  - `main` is reserved.
- A SQLite database defaults to `storage/<name>.db` in Development (the folder is created on first use).
- Additional databases have no model: names are used as written and must be plain identifiers
  ([query routing → Names](query-routing.md#names-and-schema)). The CMS itself only uses its own database.
- Used by: extensions and your own code (`IDatabaseService`), [custom API endpoints](../features/custom-endpoints.md)
  (the SQL ones), and the export of [Data Management](../pages/data-management.md).

## Code

- **Loading:** `EnvConfigurationLoader` (`src/DotNetForge.Infrastructure/Configuration/`) reads the keys as written
  into `AppEnvironment.Database` and `AppEnvironment.AdditionalDatabases` (`DatabaseSettings`).
- **Resolution:** `DatabaseProviderRegistry.Resolve` with the provider's `Normalize`, called by
  `AddDotNetForgeDatabases` during service registration.
- **Tests:**
  - `tests/DotNetForge.Tests/EnvConfigurationTests.cs` (reading);
  - `tests/DotNetForge.Tests/DatabaseProviderTests.cs` (resolution and validation; `Only_postgresql_hosts_the_cms`,
    `In_development_the_cms_database_defaults_to_the_local_postgresql`);
  - `tests/DotNetForge.IntegrationTests/ReadOnlyDeploymentTests.cs`
    (`The_cms_database_is_postgresql_and_other_providers_are_only_additional`,
    `Outside_development_the_cms_database_needs_a_connection_string`).
