# Database providers

A provider teaches DotNetForge one database technology. It implements `IDatabaseProvider`
(`src/DotNetForge.Data/Database/IDatabaseProvider.cs`) and nothing outside it knows how that database works.
Architecture: [architecture](architecture.md).

## Shipped providers

| Name | Provider class | Driver / EF provider | Hosts the CMS | Raw SQL (`CreateConnection`) | Detected from (additional databases) |
| --- | --- | --- | --- | --- | --- |
| `postgresql`, `postgres` | `PostgreSqlDatabaseProvider` | Npgsql, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 | ✔ `PostgreSqlDbContext`, migrations | ✔ | `Host=` (without `Initial Catalog`) |
| `sqlite` | `SqliteDatabaseProvider` | Microsoft.Data.Sqlite (EF Core SQLite package) | - | ✔ | empty string, `Data Source=` / `Filename=` without server keys |
| `sqlserver` | `SqlServerDatabaseProvider` | Microsoft.Data.SqlClient (EF Core SQL Server 10.0.12 package) | - | ✔ | `Initial Catalog`, `Trusted_Connection`, `Integrated Security` or `TrustServerCertificate` |
| `mysql` | `MySqlDatabaseProvider` | MySql.Data (MySql.EntityFrameworkCore 10.0.9, Oracle) | - | ✔ | never: set `DATABASES_<NAME>_PROVIDER=mysql` |
| `mongodb` | `MongoDbDatabaseProvider` | MongoDB.Driver (MongoDB.EntityFrameworkCore 10.0.4 package) | - | - (`null`) | `mongodb://` / `mongodb+srv://` |

**The CMS database is always PostgreSQL** ([configuration](configuration.md#cms-database)). The other four serve
[additional databases](configuration.md#more-databases) only: their `AddDbContext` throws
`DatabaseConfigurationException` ("The CMS database must be PostgreSQL; <X> can only be an additional database …")
and their `InitializeSchemaAsync` throws `NotSupportedException`.

Each database has its own folder, `src/DotNetForge.Data/Database/Providers/<Database>/`, with **everything** that
database needs. No code is shared between the databases' folders:

```text
Database/Providers/
├── PostgreSql/
│   ├── PostgreSqlDatabaseProvider.cs   IDatabaseProvider: settings, EF Core, migrations, error codes
│   ├── PostgreSqlDbContext.cs          the CMS context (DotNetForgeDbContext subclass that owns the migrations)
│   ├── PostgreSqlExecutor.cs           runs DatabaseCommands through ADO.NET (+ transaction)
│   ├── PostgreSqlSqlBuilder.cs         DatabaseCommand → parameterized SQL
│   ├── PostgreSqlDialect.cs            quoting, paging, LIKE escaping
│   └── Migrations/                     the CMS migrations
├── Sqlite/       SqliteDatabaseProvider, SqliteExecutor, SqliteSqlBuilder, SqliteDialect
├── SqlServer/    SqlServerDatabaseProvider, SqlServerExecutor, SqlServerSqlBuilder, SqlServerDialect
├── MySql/        MySqlDatabaseProvider, MySqlExecutor, MySqlSqlBuilder, MySqlDialect
└── MongoDb/      MongoDbDatabaseProvider, MongoExecutor, MongoKeys (key generators)
```

The code shared by all databases is provider-neutral and sits one level up in `Database/`: the `IDatabaseProvider`
contract, the router, the service, command validation, `DatabaseSchema` (model names → native names) and
`ValueCoercion`.

The host registers `PostgreSqlDbContext` as `DotNetForgeDbContext`, so application code never sees the subclass.

## Isolation and updates

A change for one database must not touch the others:

| What changes | Where it is handled | Effect on the other databases |
| --- | --- | --- |
| A driver or EF Core provider update (e.g. a new `MySql.EntityFrameworkCore`) | the version in `Directory.Packages.props` changes only when someone updates it (MongoDB is pinned exactly); adapt that database's folder | none: each provider has its own package |
| The database's behaviour, error codes or SQL syntax | that database's provider, dialect, SQL builder or executor in `Database/Providers/<Database>/` | none: the four SQL databases each have their own copy of this code |
| The CMS model (a new table or column) | one PostgreSQL migration (`Database/Providers/PostgreSql/Migrations/`, [database → Migrations](../architecture/database.md#migrations)) | none: the other databases never hold the CMS model |

The price of full separation is duplication: the SQL builder, executor and connection-string helpers exist once per
SQL database. **A fix to behaviour all SQL databases share** (for example how a filter becomes SQL) **must be made in
each of the four folders.** `DatabaseTranslationTests` runs the shared expectations against all four builders, so a copy
that was missed fails there.

## The contract

| Member | Purpose |
| --- | --- |
| `DisplayName` | Name in results, logs and errors (`PostgreSQL`) |
| `CanHandle(connectionString)` | Detection fallback for additional databases; return `false` when unsure (several matches are an error) |
| `Normalize(settings, host)` | Validate and apply defaults; throw `DatabaseConfigurationException` with an actionable message |
| `Describe(settings)` | `PostgreSQL db.internal:5432/cms`; never credentials |
| `AddDbContext(services, settings)` | Register `DotNetForgeDbContext` for the CMS database (PostgreSQL); the other providers throw `DatabaseConfigurationException` |
| `InitializeSchemaAsync(db, ct)` | Apply the CMS migrations (PostgreSQL); the other providers throw `NotSupportedException` |
| `CreateExecutor(settings, model, loggerFactory)` | The object that runs `DatabaseCommand`s; called once per configured database |
| `CreateConnection(settings)` | An **unopened** ADO.NET `DbConnection` for raw SQL - [custom API endpoints](../features/custom-endpoints.md) on an additional database. Default interface method returning `null` (MongoDB: no SQL); the four SQL providers create one from their driver's factory |
| `TranslateException(exception)` | Driver exception → `DatabaseException`, or `null` for non-database errors |

`IDatabaseExecutor` has the operations themselves:
- `QueryAsync` for Find, FindOne and Aggregate (streams records);
- `ExecuteAsync` for Count, Insert, Update and Delete;
- `BeginTransactionAsync`;
- `TestConnectionAsync`;
- `ListCollectionsAsync`: the database's own tables or collections, sorted by name, system ones left out (SQLite
  `sqlite_master` without `sqlite_%`; PostgreSQL, SQL Server and MySQL `information_schema` base tables of the current
  schema/database; MongoDB `listCollections` without `system.*`).

Insert, update and delete are operations of a `DatabaseCommand` rather than separate methods: one entry point keeps
validation, timeouts, logging and error translation in one place.

## Registration

```csharp
// src/DotNetForge.Web/Startup/DependencyRegistration.cs
services.AddDefaultDatabaseProviders();          // sqlite, postgresql (+ postgres), sqlserver, mysql, mongodb
services.AddDatabaseProvider<OracleDatabaseProvider>("oracle");   // a new provider: one line
services.AddDotNetForgeDatabases(env.Database, env.AdditionalDatabases, host);
```

- The registry is a dictionary keyed by name (case-insensitive). There are no `if/else` or `switch` statements over
  providers anywhere in the routing.
- Registering a name again replaces the earlier provider, so an application can swap a shipped provider.
- Registering the same type under another name makes an alias.
- Providers are stateless strategy objects with parameterless constructors. They are instantiated during
  registration, because the CMS database's `DbContext` has to be configured before the container is built.

## Provider notes

**PostgreSQL** (CMS and additional databases)
- Key=value connection strings only; `postgres://` URLs are rejected with the correct form in the message.
- The CMS connection string is required in every environment (no fallback); the message says how to set it.
- Add `GSS Encryption Mode=Disable` in slim containers ([deployment](../guides/deployment.md#database)).

**SQLite** (additional databases)
- Empty connection string means `storage/<name>.db` at the repository root, in Development only; the folder is
  created when the executor is created.
- Outside Development the `Data Source` must be an absolute path (the deployment directory is read-only).
  In-memory databases are allowed.
- `LIKE` is case-insensitive for ASCII by default.

**SQL Server** (additional databases)
- `Microsoft.Data.SqlClient` refuses to run in .NET's invariant-globalization mode, so `Directory.Build.props` sets
  `InvariantGlobalization=false`. The `aspnet:10.0` runtime image ships ICU.
- `LIKE` follows the column collation (case-insensitive by default); `[` is escaped as a wildcard.

**MySQL** (additional databases)
- Uses Oracle's `MySql.Data` / `MySql.EntityFrameworkCore`, because Pomelo has no EF Core 10 release. Targets MySQL 8.
- Never auto-detected: `Server=` strings are shared with SQL Server.
- `LIKE` follows the collation (`utf8mb4_0900_ai_ci`: case- and accent-insensitive).

**MongoDB** (additional databases)
- Transactions need a replica set or sharded cluster (Atlas always is one).
- No SQL: custom API endpoints can't run on it. See [MongoDB](mongodb.md).

## Testing

| Test | Where | Database |
| --- | --- | --- |
| Provider resolution, aliases, detection, validation (as additional database `extra`), only PostgreSQL hosts the CMS, the CMS connection string is required, descriptions without credentials, multiple databases, registering and replacing providers | `tests/DotNetForge.Tests/DatabaseProviderTests.cs` | none |
| SQL from each of the four SQL builders, parameters, LIKE escaping, identifier validation, MongoDB filters with typed values and escaped regexes | `tests/DotNetForge.Tests/DatabaseTranslationTests.cs` | none |
| Routing, result shape, timeouts vs cancellation, error translation, `Try*`, validation, transactions, streaming, logs without secrets (mock provider) | `tests/DotNetForge.Tests/DatabaseServiceTests.cs` | none |
| Unreachable servers → `DatabaseConnectionException`, per real driver | `tests/DotNetForge.Tests/DatabaseConnectionTests.cs` | none (closed port) |
| CRUD, EF Core ↔ service interoperability, generated keys, literal text matching, aggregates, unique conflicts, transactions, streaming, composite keys, unknown names | `tests/DotNetForge.IntegrationTests/DatabaseServiceContractTests.cs` | PostgreSQL (CMS database) |
| Each provider as an additional database: insert, `ListCollectionsAsync`, find; a custom endpoint running its SQL there (not MongoDB); the Data Management export of it. Plus endpoints refusing unknown databases and MongoDB | `tests/DotNetForge.IntegrationTests/AdditionalDatabaseProviderTests.cs` | SQLite and PostgreSQL always; SQL Server, MySQL, MongoDB when their `DNF_TEST_*` server is set (`[AdditionalDatabaseFact]`) |
| Wrong credentials → `DatabaseAuthenticationException` (as an additional database) | `tests/DotNetForge.IntegrationTests/DatabaseCredentialTests.cs` | PostgreSQL, SQL Server, MySQL - each when its `DNF_TEST_*` variable is set (`[DatabaseServerFact]`) |
| The whole CMS (setup, sign-in, pages, media, API, read-only deployment) | all integration tests | PostgreSQL (`DNF_TEST_POSTGRES`, [testing](../guides/testing.md#databases)) |

CI ([testing](../guides/testing.md#ci)):
- PostgreSQL: the whole integration suite in `postgres-and-s3`;
- SQL Server, MySQL and MongoDB: the `additional-databases` matrix runs
  `--filter "FullyQualifiedName~AdditionalDatabase|FullyQualifiedName~DatabaseCredential"` next to a PostgreSQL
  service.
