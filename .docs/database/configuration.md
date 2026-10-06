# Database configuration

All other configuration keys: [features/configuration.md](../features/configuration.md).

## Main database

The CMS runs on the **main** database:

| Key | Purpose |
| --- | --- |
| `DATABASE_PROVIDER` | `sqlite`, `postgresql` (alias `postgres`), `sqlserver`, `mysql`, `mongodb`, or the name of any registered provider. Empty = detect from the connection string |
| `DATABASE_CONNECTION_STRING` | The connection string, in the database's own format. **Secret.** Empty in Development = SQLite at `storage/dotnetforge.db` |
| `DATABASE_NAME` | MongoDB only: the database name when it isn't in the URL path |

Examples:

```env
# SQLite (empty connection string = storage/dotnetforge.db in Development; elsewhere an absolute path)
DATABASE_PROVIDER=sqlite
DATABASE_CONNECTION_STRING=Data Source=/data/dotnetforge.db

# PostgreSQL
DATABASE_PROVIDER=postgresql
DATABASE_CONNECTION_STRING=Host=db;Port=5432;Database=dotnetforge;Username=cms;Password=<secret>;GSS Encryption Mode=Disable

# SQL Server
DATABASE_PROVIDER=sqlserver
DATABASE_CONNECTION_STRING=Server=db,1433;Initial Catalog=dotnetforge;User Id=cms;Password=<secret>;Encrypt=true

# MySQL 8
DATABASE_PROVIDER=mysql
DATABASE_CONNECTION_STRING=Server=db;Port=3306;Database=dotnetforge;Uid=cms;Pwd=<secret>

# MongoDB (replica set or Atlas; database name in the URL path or in DATABASE_NAME)
DATABASE_PROVIDER=mongodb
DATABASE_CONNECTION_STRING=mongodb+srv://cms:<secret>@cluster0.example.mongodb.net/dotnetforge?retryWrites=true
```

## How the provider is chosen

1. **`DATABASE_PROVIDER` is set:** that provider is used. An unknown name stops startup with the list of registered
   names.
2. **Not set:** every registered provider is asked whether it recognizes the connection string
   (`IDatabaseProvider.CanHandle`).
   - **Exactly one** match is used.
   - **None or several** stop startup and ask for `DATABASE_PROVIDER`.

| Connection string | Detected as |
| --- | --- |
| empty | SQLite (development default) |
| `Data Source=` / `Filename=` without server keys | SQLite |
| `Host=` | PostgreSQL |
| `Initial Catalog`, `Trusted_Connection`, `Integrated Security`, `TrustServerCertificate` | SQL Server |
| `mongodb://`, `mongodb+srv://` | MongoDB |
| `Server=…;Uid=…` (MySQL style) | not detected: ambiguous, set `DATABASE_PROVIDER=mysql` |

Setting `DATABASE_PROVIDER` explicitly is recommended outside Development. Detection is a convenience that keeps
older `.env` files, which only had a connection string, working.

## Validation (startup)

The chosen provider validates the settings. Invalid configuration stops the process with exit code 1 and
`[DotNetForge] Configuration error: <message>`:

| Situation | Message |
| --- | --- |
| Unknown provider name | `Unknown database provider '<x>' for database 'main'. Registered providers: mongodb, mysql, postgres, postgresql, sqlite, sqlserver.` |
| Undetectable connection string | `Cannot tell which database the connection string of 'main' is for. Set DATABASE_PROVIDER to one of: …` |
| Ambiguous connection string | `The connection string of 'main' fits several databases (…). Set DATABASE_PROVIDER.` |
| SQLite, empty, outside Development | `DATABASE_CONNECTION_STRING is required outside Development: the deployment directory is read-only, so point it at a database server (PostgreSQL, SQL Server, MySQL, MongoDB) or at a SQLite file on a writable volume ('Data Source=/data/dotnetforge.db').` |
| SQLite, relative path, outside Development | `The SQLite data source must be an absolute path outside Development (got '<x>').` |
| PostgreSQL URL | `PostgreSQL connection strings must use the key=value form, not a URL: …` |
| SQL provider without connection string | `<Provider> needs a connection string for database 'main' (DATABASE_CONNECTION_STRING).` |
| MongoDB, not a MongoDB URL | `MongoDB needs a connection string like 'mongodb://<host>:27017/<database>?replicaSet=rs0' in DATABASE_CONNECTION_STRING.` |
| MongoDB without database name | `MongoDB needs a database name: put it in the URL path (mongodb://host:27017/<database>) or set DATABASE_NAME.` |

Configuration is validated, not probed. An unreachable server is not a configuration error: the database's first
use at startup (migrations) fails with the driver's connection error.

## More databases

`IDatabaseService` can also reach further databases. Each gets a name and its own provider:

```env
DATABASES_REPORTS_PROVIDER=postgresql
DATABASES_REPORTS_CONNECTION_STRING=Host=warehouse;Database=reports;Username=reader;Password=<secret>
DATABASES_EVENTS_PROVIDER=mongodb
DATABASES_EVENTS_CONNECTION_STRING=mongodb://events-db:27017/?replicaSet=rs0
DATABASES_EVENTS_NAME=events
```

```csharp
await db.QueryAsync(DatabaseCommand.Find("daily_sales").On("reports").OrderBy("day"));
```

- Keys follow the pattern `DATABASES_<NAME>_PROVIDER`, `DATABASES_<NAME>_CONNECTION_STRING` and
  `DATABASES_<NAME>_NAME`.
  - `<NAME>` is letters and digits only; it is matched case-insensitively and stored in lower case.
  - `main` is reserved.
- The same detection and validation rules apply as for the main database. A named SQLite database defaults to
  `storage/<name>.db` in Development.
- Named databases have no model: names are used as written and must be plain identifiers
  ([query routing → Names](query-routing.md#names-and-schema)). The CMS itself only uses the main database.

## Code

- **Loading:** `EnvConfigurationLoader` (`src/DotNetForge.Infrastructure/Configuration/`) reads the keys as written
  into `AppEnvironment.Database` and `AppEnvironment.AdditionalDatabases` (`DatabaseSettings`).
- **Resolution:** `DatabaseProviderRegistry.Resolve` with the provider's `Normalize`, called by
  `AddDotNetForgeDatabases` during service registration.
- **Tests:**
  - `tests/DotNetForge.Tests/EnvConfigurationTests.cs` (reading);
  - `tests/DotNetForge.Tests/DatabaseProviderTests.cs` (resolution and validation).
