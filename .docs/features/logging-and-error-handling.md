# Logging and error handling

## Logging

- Providers: ASP.NET Core defaults (console, debug, event source; EventLog on Windows). No file logging, no
  Serilog - nothing is written to disk, which suits the read-only deployment. In a container, read the console
  output (`docker logs`).
- Levels: `src/DotNetForge.Web/appsettings.json` → `Default: Information`, `Microsoft.AspNetCore: Warning`,
  `Microsoft.EntityFrameworkCore.Database.Command: Warning` (SQL is not logged, failed commands are).
  `src/DotNetForge.Web/appsettings.Development.json` raises `Microsoft.AspNetCore` to `Information`.
- Startup configuration errors are written to **stderr** with `Console.Error.WriteLine`, before logging exists.
- User and security actions are recorded as **audit entries**, not logs - see [audit logging](audit-logging.md).

Application log statements (all via `ILogger<T>` message templates):

| Where | Level | Message template | When |
| --- | --- | --- | --- |
| `ScheduledPublishingService` | Information | `Scheduler finalized {Publish} publish and {Unpublish} unpublish schedule(s).` | a run changed stored schedules ([scheduled publishing](scheduled-publishing.md)) |
| `ScheduledPublishingService` | Warning | `Scheduled publishing run failed; will retry.` | any exception in a run (with the exception) |
| `MediaService` | Error | `Storing upload '{FileName}' as {StorageKey} failed.` | the storage provider threw while saving an upload (with the exception) |
| `MediaService` | Warning | `Could not delete stored object {StorageKey}; it is now orphaned.` | deleting an object failed - after a failed upload or database save, or after the row was deleted |
| `MediaFilesController` | Warning | `Media file {MediaId} has no stored object at {StorageKey}.` | a `MediaFile` row exists but the provider has no object (only when the app streams the file; with presigned URLs the object store answers 404 itself) |
| `ApiTokenAuthenticationHandler` | Warning | `Could not record last use of API token {TokenId}.` | saving `LastUsedDate` threw `DbUpdateException` or was cancelled ([headless API](headless-api.md#tokens)) |

Other controllers and services do not log. When adding logs: inject `ILogger<T>`, use message templates (`{Name}`
placeholders), never log secrets (tokens, connection strings, `STORAGE_S3_SECRET_ACCESS_KEY`), and keep hot paths
(every request, the public fallback, the extension loader) quiet.

### Expected startup log lines

Seen on a healthy start; none needs action unless noted.

| Line | When | Why |
| --- | --- | --- |
| `fail: Microsoft.EntityFrameworkCore.Database.Command[20102]` "Failed executing DbCommand" against `__EFMigrationsHistory` | PostgreSQL, first start on a **fresh** database | `DatabaseInitializer` calls `MigrateAsync`; EF queries the migrations history table before it exists, then creates it and applies the migrations. Appears once per new database. Seen again on an existing database = a real problem |
| Npgsql cannot load `libgssapi_krb5` (GSSAPI/Kerberos library) | PostgreSQL in slim Linux containers | Npgsql tries GSS encryption by default and the image has no Kerberos library. Avoided by adding `GSS Encryption Mode=Disable` to `DATABASE_CONNECTION_STRING` (as in `.env.example`) unless you use Kerberos |
| `warn: Microsoft.AspNetCore.DataProtection...` "No XML encryptor configured. Key {...} may be persisted to storage in unencrypted form." | whenever Data Protection creates a key (first start on a new database, later at key rotation), any provider | the key ring is persisted to the database table `DataProtectionKeys` without an XML encryptor - whoever can read the database can read the keys ([security](security.md), [deployment](../guides/deployment.md#database)) |
| `warn: Microsoft.EntityFrameworkCore.Query[10103]` "The query uses the 'First'/'FirstOrDefault' operator without 'OrderBy' and filter operators." | install check and Dashboard (reads of `SystemState`) | single-row reads in `InstallationStore.IsInstalledAsync` and `DashboardController`; harmless |

## Error handling

| Layer | Mechanism |
| --- | --- |
| Startup | `ConfigurationException` → stderr + exit code 1 ([configuration](configuration.md#failure-behaviour)); any DB/migration exception crashes the process |
| Global (Development) | developer exception page (default when `ASPNETCORE_ENVIRONMENT=Development`) |
| Global (other) | `app.UseExceptionHandler("/error")` re-executes the request at `/error` **with its original HTTP method** → `HomeController.Error` (`[Route("/error")]`, accepts every method) → `src/DotNetForge.Web/Views/Home/Error.cshtml` ([error screen](../pages/error.md)). A failing form POST therefore renders the error page too |
| Validation | services return a message (`IPageService`, `MediaService.UploadAsync` → `(MediaFile?, string? Error)`) or `Result` (`InstallationService`); controllers add it to `ModelState` and re-render |
| Storage | see [below](#storage-failures) |
| Not found | `NotFound()` - empty body; no status-code pages middleware |
| Rate limits | `429` with an empty body from `UseRateLimiter` (`credentials` for sign-in/setup, `api` for the API - [security](security.md), [headless API](headless-api.md#rate-limiting)) |
| API | `[ApiController]` automatic 400 ProblemDetails; explicit `BadRequest(new { error })`; 401/403 from auth (403 body `{ error }`) |
| Background | `ScheduledPublishingService` catches everything per tick and logs a warning |
| Client | `admin-content.js` shows the server's `{ error }` message (or a generic one) in an alert when a reorder fails |

### Storage failures

Uploaded media goes through `IFileStorage` ([media storage](media-storage.md#upload-flow)); a storage outage never
becomes an error page:

| Situation | Behaviour |
| --- | --- |
| `IFileStorage.SaveAsync` throws (network, credentials, quota) | `MediaService` logs the Error above, tries to delete the partial object, and returns `'<name>' could not be stored right now. Try again later.`; the Media screen shows it as a form error and the other files of the batch continue |
| Database save fails after the object was stored | the object is deleted (no orphan) and the exception is rethrown → global error page |
| Delete | the row is deleted first, then the object; an object delete failure is only logged (orphan warning) and the user sees success |
| Download of a missing object | streamed (local, or a provider without presigned URLs): `404` from the app plus the warning above; presigned redirect (S3): the object store answers 404 ([download flow](media-storage.md#download-flow)) |

### Caveats

- The error view doesn't set `AppName`, so its header falls back to "DotNetForge CMS".
- Unique-index violations (`DbUpdateException`) are translated into validation messages only in `InstallationStore`.
  Duplicates are otherwise caught before saving (page slugs by `PageService`, API token names by
  `ApiTokensController`); only concurrent requests racing past those checks reach the global error.
- Exceptions are not logged by application code except where listed above; rely on the framework's
  exception-handler logging.
