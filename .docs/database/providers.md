# Database providers

A provider teaches DotNetForge one database technology. It implements `IDatabaseProvider`
(`src/DotNetForge.Data/Database/IDatabaseProvider.cs`) and nothing outside it knows how that database works.
Architecture: [architecture](architecture.md).

## Shipped providers

| Name (`DATABASE_PROVIDER`) | Provider class | Driver / EF provider | Context type | Schema | Detected from |
| --- | --- | --- | --- | --- | --- |
| `sqlite` | `SqliteDatabaseProvider` | Microsoft.Data.Sqlite, EF Core SQLite | `DotNetForgeDbContext` | migrations | empty string, `Data Source=` / `Filename=` without server keys |
| `postgresql`, `postgres` | `PostgreSqlDatabaseProvider` | Npgsql, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 | `PostgreSqlDbContext` | migrations | `Host=` (without `Initial Catalog`) |
| `sqlserver` | `SqlServerDatabaseProvider` | Microsoft.Data.SqlClient, EF Core SQL Server 10.0.12 | `SqlServerDbContext` | migrations | `Initial Catalog`, `Trusted_Connection`, `Integrated Security` or `TrustServerCertificate` |
| `mysql` | `MySqlDatabaseProvider` | MySql.Data, MySql.EntityFrameworkCore 10.0.9 (Oracle) | `MySqlDbContext` | migrations | never: set `DATABASE_PROVIDER=mysql` |
| `mongodb` | `MongoDbDatabaseProvider` | MongoDB.Driver, MongoDB.EntityFrameworkCore 10.0.4 | `MongoDbContext` | `EnsureCreated`: collections + indexes | `mongodb://` / `mongodb+srv://` |

Each database has its own folder, `src/DotNetForge.Data/Database/Providers/<Database>/`, with **everything** that
database needs, including its migrations. No code is shared between the databases' folders:

```text
Database/Providers/
├── Sqlite/
│   ├── SqliteDatabaseProvider.cs   IDatabaseProvider: settings, EF Core, schema, error codes
│   ├── SqliteExecutor.cs           runs DatabaseCommands through ADO.NET (+ SqliteTransaction)
│   ├── SqliteSqlBuilder.cs         DatabaseCommand → parameterized SQL
│   ├── SqliteDialect.cs            quoting, paging, LIKE escaping
│   └── Migrations/                 context: DotNetForgeDbContext (the base model)
├── PostgreSql/   PostgreSqlDatabaseProvider, PostgreSqlExecutor, PostgreSqlSqlBuilder, PostgreSqlDialect,
│                 PostgreSqlDbContext, Migrations/
├── SqlServer/    SqlServerDatabaseProvider, SqlServerExecutor, SqlServerSqlBuilder, SqlServerDialect,
│                 SqlServerDbContext, Migrations/
├── MySql/        MySqlDatabaseProvider, MySqlExecutor, MySqlSqlBuilder, MySqlDialect, MySqlDbContext, Migrations/
└── MongoDb/      MongoDbDatabaseProvider, MongoDbContext, MongoExecutor (+ key generators); no migrations
```

The code shared by all databases is provider-neutral and sits one level up in `Database/`: the `IDatabaseProvider`
contract, the router, the service, command validation, `DatabaseSchema` (model names → native names) and
`ValueCoercion`. MongoDB has no migrations ([MongoDB](mongodb.md#schema-without-migrations)).

Each SQL provider has its own context type, so EF Core keeps a separate migration set with that database's column
types. The host registers the context as `DotNetForgeDbContext`, so application code never sees the subclass.

## Isolation and updates

A change for one database must not touch the others. That holds at every level where a database can change:

| What changes | Where it is handled | Effect on the other databases |
| --- | --- | --- |
| A driver or EF Core provider update (e.g. a new `MySql.EntityFrameworkCore`) | the version in `Directory.Packages.props` changes only when someone updates it (MongoDB is pinned exactly); adapt that database's folder | none: each provider has its own package |
| The database's behaviour, error codes or SQL syntax | that database's provider, dialect, SQL builder or executor in `Database/Providers/<Database>/` | none: the four SQL databases each have their own copy of this code |
| The CMS model (a new table or column) | one migration per SQL database (`Database/Providers/<Database>/Migrations/`), generated from the same model; MongoDB picks it up at startup | each database gets its own migration with its own column types |
| A database-specific model detail (e.g. SQL Server's identity rule) | that database's context type (`SqlServerDbContext`) | none: the base model is unchanged |

The price of full separation is duplication: the SQL builder, executor and connection-string helpers exist once per
SQL database. **A fix to behaviour all SQL databases share** (for example how a filter becomes SQL) **must be made in
each of the four folders.** `DatabaseTranslationTests` runs the shared expectations against all four builders, so a copy
that was missed fails there.

The guard is the test suite: CI runs the full integration suite on each of the five databases
([contract and testing](#contract-and-testing)), so an update that breaks one database fails that database's job
before release.

## The contract

| Member | Purpose |
| --- | --- |
| `DisplayName` | Name in results, logs and errors (`PostgreSQL`) |
| `CanHandle(connectionString)` | Detection fallback; return `false` when unsure (several matches are an error) |
| `Normalize(settings, host)` | Validate and apply defaults; throw `DatabaseConfigurationException` with an actionable message |
| `Describe(settings)` | `PostgreSQL db.internal:5432/cms`; never credentials |
| `AddDbContext(services, settings)` | Register `DotNetForgeDbContext` for the main database |
| `InitializeSchemaAsync(db, ct)` | Migrations, or collections and indexes |
| `CreateExecutor(settings, model, loggerFactory)` | The object that runs `DatabaseCommand`s; called once per configured database |
| `TranslateException(exception)` | Driver exception → `DatabaseException`, or `null` for non-database errors |

`IDatabaseExecutor` has the operations themselves:
- `QueryAsync` for Find, FindOne and Aggregate (streams records);
- `ExecuteAsync` for Count, Insert, Update and Delete;
- `BeginTransactionAsync`;
- `TestConnectionAsync`.

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
  registration, because the main database's `DbContext` has to be configured before the container is built.

## Provider notes

**SQLite**
- Empty connection string means `storage/dotnetforge.db` at the repository root, in Development only. A named
  SQLite database gets `storage/<name>.db`.
- Outside Development the `Data Source` must be an absolute path (the deployment directory is read-only).
  In-memory databases are allowed.
- `LIKE` is case-insensitive for ASCII by default.

**PostgreSQL**
- Key=value connection strings only; `postgres://` URLs are rejected with the correct form in the message.
- Add `GSS Encryption Mode=Disable` in slim containers ([deployment](../guides/deployment.md#database)).

**SQL Server**
- `Microsoft.Data.SqlClient` refuses to run in .NET's invariant-globalization mode, so `Directory.Build.props` sets
  `InvariantGlobalization=false`. The `aspnet:10.0` runtime image ships ICU.
- `SqlServerDbContext` marks `SystemState.Id` as not generated: SQL Server would make the int key an `IDENTITY`
  column, which rejects the fixed value 1.
- No retrying execution strategy: it can't be combined with the explicit transactions the CMS uses (installation).
- `LIKE` follows the column collation (case-insensitive by default); `[` is escaped as a wildcard.

**MySQL**
- Uses Oracle's `MySql.EntityFrameworkCore`, because Pomelo has no EF Core 10 release (latest 9.0.0, October 2026).
  Targets MySQL 8.
- Never auto-detected: `Server=` strings are shared with SQL Server.
- `LIKE` follows the collation (`utf8mb4_0900_ai_ci`: case- and accent-insensitive).

**MongoDB**
- Needs a replica set or sharded cluster (Atlas always is one).
- No migrations: the schema is created by `EnsureCreated`.
- See [MongoDB](mongodb.md).

## Contract and testing

The same behavior is verified on every provider:

| Test | Where | Database |
| --- | --- | --- |
| Provider resolution, aliases, detection, validation, descriptions without credentials, multiple databases, registering and replacing providers | `tests/DotNetForge.Tests/DatabaseProviderTests.cs` | none |
| SQL from each of the four SQL builders, parameters, LIKE escaping, identifier validation, MongoDB filters with typed values and escaped regexes | `tests/DotNetForge.Tests/DatabaseTranslationTests.cs` | none |
| Routing, result shape, timeouts vs cancellation, error translation, `Try*`, validation, transactions, streaming, logs without secrets (mock provider) | `tests/DotNetForge.Tests/DatabaseServiceTests.cs` | none |
| Unreachable servers → `DatabaseConnectionException`, per real driver | `tests/DotNetForge.Tests/DatabaseConnectionTests.cs` | none (closed port) |
| CRUD, EF Core ↔ service interoperability, generated keys, literal text matching, aggregates, unique conflicts, transactions, streaming, composite keys, unknown names | `tests/DotNetForge.IntegrationTests/DatabaseServiceContractTests.cs` | the integration database |
| Wrong credentials → `DatabaseAuthenticationException` | `tests/DotNetForge.IntegrationTests/DatabaseCredentialTests.cs` | PostgreSQL, SQL Server, MySQL servers |
| The whole CMS (setup, sign-in, pages, media, API, read-only deployment) | all integration tests | the integration database |

The integration suite runs on SQLite by default. It runs on a server when `DNF_TEST_POSTGRES`,
`DNF_TEST_SQLSERVER`, `DNF_TEST_MYSQL` or `DNF_TEST_MONGODB` is set ([testing](../guides/testing.md#databases)).

CI runs it on all five databases:
- SQLite in `build-and-test`;
- PostgreSQL in `postgres-and-s3`;
- SQL Server, MySQL and MongoDB in `database-providers`.

Verified locally on 2026-10-06 (after the per-database split): 50/50 integration tests on each of SQLite, PostgreSQL 17,
SQL Server 2022, MySQL 8.4 and MongoDB 8.0 (single-node replica set).
