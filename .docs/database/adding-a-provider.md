# Adding a database provider

Adding a database means three things: **create a provider, implement the interface, register it**. The router,
the service, the configuration loader and the CMS code don't change. This page walks through Oracle as an example;
the same steps apply to MariaDB, CockroachDB, CosmosDB or anything else with a .NET driver.

```mermaid
flowchart LR
    A["1. Package + context type<br/>(EF Core databases)"] --> B["2. Provider class<br/>IDatabaseProvider"]
    B --> C["3. Migrations<br/>(SQL databases)"]
    C --> D["4. Register<br/>AddDatabaseProvider&lt;T&gt;(&quot;oracle&quot;)"]
    D --> E["5. Tests on the real database"]
    E --> F["6. Docs + CI"]
```

## 1. Package and context type

Add the EF Core provider to `Directory.Packages.props`, and a `PackageReference` in
`src/DotNetForge.Data/DotNetForge.Data.csproj`:

```xml
<PackageVersion Include="Oracle.EntityFrameworkCore" Version="10.x" />
```

SQL databases get their own context type, so they get their own migration set:

```csharp
// src/DotNetForge.Data/OracleDbContext.cs
public sealed class OracleDbContext : DotNetForgeDbContext
{
    public OracleDbContext(DbContextOptions<OracleDbContext> options) : base(options) { }

    // Database-specific model adjustments go here, never into DotNetForgeDbContext
    // (example: SqlServerDbContext makes SystemState.Id non-identity).
}
```

## 2. The provider

For SQL databases, derive from `RelationalDatabaseProvider<TContext>`. It already handles:
- registering EF Core;
- migrations at startup;
- the parameterized SQL builder and the ADO.NET executor;
- connection-string descriptions;
- generic error mapping.

You supply the driver, the dialect and the database's rules:

```csharp
// src/DotNetForge.Data/Database/Relational/OracleDatabaseProvider.cs
public sealed class OracleDatabaseProvider : RelationalDatabaseProvider<OracleDbContext>
{
    public override string DisplayName => "Oracle";

    protected override DbProviderFactory Factory => OracleClientFactory.Instance;

    protected override SqlDialect Dialect { get; } = new OracleDialect();

    // Return true only when the connection string can't belong to another database.
    public override bool CanHandle(string? connectionString) => false;

    protected override void ConfigureEfCore(DbContextOptionsBuilder options, string connectionString) =>
        Configure(options, connectionString);

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseOracle(connectionString, sql => sql.MigrationsAssembly(MigrationsAssembly));

    protected override DatabaseException? TranslateDriverException(Exception exception) => exception switch
    {
        OracleException { Number: 1017 } => new DatabaseAuthenticationException("Oracle rejected the credentials.", exception),
        OracleException { Number: 1 } => new DatabaseConflictException("A record with the same unique value already exists.", exception),
        OracleException { Number: 942 } => new DatabaseNotFoundException("The table or column does not exist.", exception),
        OracleException { Number: 12170 or 12541 } => new DatabaseConnectionException("Oracle could not be reached.", exception),
        _ => null,   // the base class maps the rest (DbException → DatabaseQueryException, timeouts)
    };
}

public sealed class OracleDialect : SqlDialect
{
    public override string QuoteIdentifier(string name) => Quote(name, '"', '"');

    public override string ParameterName(int index) => $":p{index}";

    public override void AppendPaging(StringBuilder sql, int? skip, int? take, bool hasOrderBy)
    {
        if (skip is null && take is null) return;
        sql.Append(" OFFSET ").Append(skip ?? 0).Append(" ROWS");
        if (take is not null) sql.Append(" FETCH NEXT ").Append(take.Value).Append(" ROWS ONLY");
    }
}
```

A database that is **not SQL** implements `IDatabaseProvider` directly (see `MongoDbDatabaseProvider`):
- **`Normalize`**: validate the settings.
- **`AddDbContext`**: register `DotNetForgeDbContext` with its EF Core provider, or throw
  `DatabaseConfigurationException` if it can't host the CMS model.
- **`InitializeSchemaAsync`**: create its collections and indexes.
- **`CreateExecutor`**: return an `IDatabaseExecutor` that translates `DatabaseCommand`s with the native driver.
  - Use typed values, never strings.
  - Validate unmapped names with `DatabaseSchema.EnsureIdentifier`.
  - Convert values with `ValueCoercion`.
- **`TranslateException`**.

A provider that only serves **named databases** (no CMS model, e.g. a key-value store) can throw
`DatabaseConfigurationException` from `AddDbContext` and implement just the commands it supports. Unsupported
operations raise `DatabaseProviderException`.

## 3. Migrations (SQL databases)

Add a design-time factory next to the others in `src/DotNetForge.Data/DesignTimeDbContextFactory.cs`, then generate
the initial migration:

```bash
dotnet ef migrations add InitialCreate --project src/DotNetForge.Data --startup-project src/DotNetForge.Data \
  --context OracleDbContext --output-dir Migrations/Oracle
```

From then on, every schema change adds a migration for this context too
([database → Migrations](../architecture/database.md#migrations)). Check the generated SQL for:
- **index key length limits**: every indexed text column in the model has a `HasMaxLength`;
- **identity columns** that receive explicit values;
- **date and Guid types.**

## 4. Register

```csharp
// src/DotNetForge.Data/Database/DatabaseServiceCollectionExtensions.cs → AddDefaultDatabaseProviders
.AddDatabaseProvider<OracleDatabaseProvider>("oracle")
```

Or, to keep it out of the defaults, add the line in `DependencyRegistration` (or an application's own startup)
after `AddDefaultDatabaseProviders()`. `DATABASE_PROVIDER=oracle` now selects it. Nothing else changes: no switch
statement, no enum, no edits to the router, the service or the loader.

## 5. Tests

1. Add the provider to `DatabaseProviderTests`:
   - name resolution;
   - validation messages;
   - a `Describe` case proving credentials are not shown;
   - detection, if `CanHandle` can ever return true.
2. Add the dialect to `DatabaseTranslationTests.Dialects` with the exact SQL it must produce.
3. Add an unreachable-server case to `DatabaseConnectionTests`.
4. Add the server to `TestDatabaseServer` (`DNF_TEST_ORACLE`) and run **the whole integration suite** on it:

   ```bash
   DNF_TEST_ORACLE="User Id=system;Password=…;Data Source=localhost:1521/FREEPDB1" dotnet test tests/DotNetForge.IntegrationTests
   ```

   Every test must pass; the contract tests (`DatabaseServiceContractTests`) cover CRUD, interoperability with EF
   Core, keys, text matching, conflicts and transactions.
5. Add a wrong-credentials case to `DatabaseCredentialTests`.

## 6. Docs and CI

- [providers.md](providers.md): a row in the provider table, plus provider notes.
- [configuration.md](configuration.md): an example connection string, and detection if any.
- `.env.example` and `docker/compose.dev.yml` (an opt-in profile).
- `.github/workflows/ci.yml`: a `database-providers` matrix entry that starts the server and sets the test variable.
