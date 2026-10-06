# Database security

What the database layer does so that commands are safe by default, and what callers must still do.

## Injection

**SQL injection: values never become SQL.**
- `SqlCommandBuilder` writes statements from three things only:
  - fixed keywords;
  - identifiers quoted by the dialect (`"x"`, `[x]`, `` `x` ``);
  - parameter names (`@p0`).
- Every value is a parameter: filter values, inserted and updated values, and LIKE patterns.
- A value such as `x'; DROP TABLE "Users"; --` is compared as text. `DatabaseTranslationTests` asserts that the SQL
  text never contains values.

**MongoDB operator injection: values never become operators.**
- Filters are built with the driver's typed builders from typed BSON values. A value such as `{ "$ne": null }` is the
  string `{ "$ne": null }`.
- Commands never accept JSON or BSON from callers.

**Identifiers are validated.**
- For the main database, collection and field names must exist in the CMS model (`DatabaseNotFoundException`
  otherwise).
- For databases without a model, names must match `^[A-Za-z_][A-Za-z0-9_]{0,127}$`. That rules out SQL syntax,
  quotes, MongoDB operators (`$where`) and dotted paths.

**Text search is literal.**
- LIKE wildcards are escaped with `!`: `%`, `_`, and `[` on SQL Server.
- MongoDB patterns are regex-escaped, so `.*` matches the two characters.
- This rules out wildcard injection and expensive regular expressions supplied by users.

**Destructive writes need intent.**
- `UpdateMany`/`DeleteMany` (and the `*One` forms) without a filter are rejected.
- "Everything" has to be written as `DatabaseFilter.And()`.

## Using the service safely

`IDatabaseService` is a **privileged, server-side API**. It can read and write every table of every configured
database, including `Users.PasswordHash`, `ApiTokens` and the Data Protection keys. Therefore:
- **Never build commands from request input without an allowlist.**
  - If users choose fields, sorting or filters (a report builder, an API), map their choices to a fixed set of
    collections and fields, and apply tenant filters (`TenantId`) yourself.
  - The service validates names; it does not authorize them.
- **Keep tenant isolation.** The CMS has no global query filters; filter by `TenantId` explicitly, as everywhere else
  ([codebase rules](../architecture/codebase.md#architectural-rules-for-changes)).
- **Use read-only database accounts** for named databases that are only queried.

## Connections and secrets

- **Connection strings are secrets.** They come from the environment or `.env` (git-ignored), never from code or
  the repository ([configuration](../features/configuration.md#secrets)).
- **Nothing logs or shows them.**
  - `DatabaseSettings.ToString()` prints only the name and provider.
  - `IDatabaseProvider.Describe` returns host, port and database name only.
  - Tests check that descriptions and errors never contain passwords.
- **Pools are the drivers' own.**
  - SQL providers open short-lived connections from the ADO.NET pool, so connections are never kept or shared across
    requests.
  - MongoDB uses one client per connection string.
- **TLS is configured in the connection string:**
  - `Ssl Mode=Require` (PostgreSQL);
  - `Encrypt=true` (SQL Server);
  - `SslMode=Required` (MySQL);
  - `tls=true` or `mongodb+srv://` (MongoDB).
  - Use it whenever the database is not on the same private network.

## Logging

- **What is logged:** database name, provider, operation, collection, duration, record count and the error type.
- **What is never logged:**
  - connection strings, passwords and tokens;
  - filter values, inserted or updated values;
  - **driver messages.** A unique-violation message can echo the duplicate value, so translated exceptions carry
    DotNetForge's own message and keep the driver exception as `InnerException` for debugging.
- `DatabaseServiceTests.Logs_describe_the_command_but_never_values_or_secrets` checks this.

## Timeouts and cancellation

- Every command has a timeout: 30 s by default, or `DatabaseCommand.Timeout`.
- The service cancels at the deadline. Providers also set the server-side limit (`CommandTimeout`, MongoDB
  `MaxTime`), so the server stops working too.
- A runaway query becomes a `DatabaseTimeoutException`, not a hung request.
- Caller cancellation (the request ended) stays an `OperationCanceledException`, so it's not counted as a database
  failure.
