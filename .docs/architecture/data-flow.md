# Data flow

How a process starts and how a request travels through the code. Each diagram uses real class and route names.
Structure and folders are in [codebase.md](codebase.md); the UI side of a request is in [pages.md](pages.md).

## Application startup

`Program.cs` runs these steps in order. Anything that throws before `app.Run()` stops the process.

```mermaid
sequenceDiagram
    participant P as Program.cs
    participant L as EnvConfigurationLoader
    participant DI as DependencyRegistration
    participant DBI as DatabaseInitializer
    participant S as DataSeeder
    participant H as Host
    P->>L: Load(ContentRootPath, IsDevelopment())
    alt ConfigurationException
        L-->>P: throw
        P->>P: write "[DotNetForge] Configuration error: ..." to stderr, return 1
    end
    L-->>P: AppEnvironment
    P->>DI: services.AddDotNetForge(env, builder.Environment)
    P->>P: builder.Build()
    P->>DBI: InitializeAsync(db, ApplicationStopping)
    DBI->>DBI: Database.MigrateAsync()
    Note over DBI: SQLite: DotNetForgeDbContext, Migrations/<br/>PostgreSQL: PostgreSqlDbContext, Migrations/PostgreSql/
    DBI->>S: SeedAsync(db)
    P->>H: build pipeline, map routes, app.Run()
    H->>H: start ScheduledPublishingService (waits 5 s)
```

Notes:

- Configuration is loaded **before** the host's own configuration system is used; `.env` values never enter
  `IConfiguration`. Outside Development the loader refuses defaults inside the deployment directory: SQLite needs an
  explicit `DATABASE_CONNECTION_STRING` with an absolute `Data Source` (in-memory allowed), and
  `STORAGE_PROVIDER=local` needs an absolute `STORAGE_LOCAL_PATH`. In Development the defaults are
  `<contentRoot>/storage/dotnetforge.db` and `<contentRoot>/storage/media`. See
  [configuration](../features/configuration.md).
- The scoped `DotNetForgeDbContext` resolves to `PostgreSqlDbContext` when the provider is PostgreSQL, so
  `MigrateAsync` applies that provider's own migration set. `EnsureCreated` is no longer used
  ([database → providers](database.md#providers)).
- Migration and seeding run on **every** start, synchronously, before Kestrel accepts requests. Seeding is
  idempotent (each step checks for existing rows). Details: [database.md](database.md#seeding).
- The Data Protection key ring is read from the `DataProtectionKeys` table on first use; the first key is created
  (and stored) on the first request that needs one.
- A database failure here (bad PostgreSQL connection string, locked SQLite file, an old PostgreSQL database without
  migration history) throws an unhandled exception and the process exits.

## Request pipeline

```mermaid
flowchart TD
    Req([HTTP request]) --> Dev{"Environment = Development?"}
    Dev -- no --> EH["UseExceptionHandler('/error') + UseHsts()"]
    Dev -- yes --> SH
    EH --> SH["SecurityHeadersMiddleware (CSP, nosniff, X-Frame-Options, ...)"]
    SH --> SF["UseStaticFiles (wwwroot)"]
    SF -- "file found" --> Done([static file])
    SF --> R["UseRouting (select endpoint)"]
    R --> IM["InstallationMiddleware"]
    IM -- "not installed, path not /setup" --> S302([302 /setup])
    IM -- "installed, path starts /setup" --> A302([302 /admin])
    IM --> AuthN["UseAuthentication (cookie scheme, OnValidatePrincipal session check)"]
    AuthN --> RL["UseRateLimiter (policies from [EnableRateLimiting])"]
    RL -- "limit exceeded" --> T429([429])
    RL --> AuthZ["UseAuthorization (policies, roles, ApiToken scheme for /api)"]
    AuthZ -- "anonymous on protected admin endpoint" --> Login(["302 /account/login?ReturnUrl=..."])
    AuthZ -- "signed in, role missing" --> Denied([302 /account/denied])
    AuthZ --> EP["Endpoint: controller action / /health / fallback"]
```

Key properties:

- With `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (behind a TLS proxy) the host inserts the forwarded-headers
  middleware ahead of everything, so the scheme and `RemoteIpAddress` come from `X-Forwarded-*`
  ([deployment](../guides/deployment.md#behind-a-reverse-proxy)).
- **`SecurityHeadersMiddleware`** sets `Content-Security-Policy` (`Content-Security-Policy-Report-Only` in
  Development), `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`,
  `Referrer-Policy: strict-origin-when-cross-origin` and `Permissions-Policy` on every response, including static
  files and re-executed error pages. Policy: [security](../features/security.md).
- **Static files** in `src/DotNetForge.Web/wwwroot/` are served before routing, so the install gate never blocks CSS/JS.
- `InstallationMiddleware` lets through, without a DB check, any path starting with `/css`, `/js`, `/lib`, `/images`,
  `/img`, `/fonts`, `/favicon`, `/health`, **and any path with a file extension** (`Path.HasExtension`).
  Everything else is redirected to `/setup` until installed - including `/api/*` (a `302`, not a `401`).
- The installed check is cached by `InstallationStatusCache`: once true it never queries the DB again in that
  process.
- **Authentication** uses the cookie scheme by default. API controllers request the `ApiToken` scheme explicitly
  via `[Authorize(AuthenticationSchemes = "ApiToken")]`, so a cookie never authenticates an API call and a bearer
  token never authenticates an admin screen. The token handler runs during authorization, i.e. **after** the rate
  limiter, so rejected requests never reach PBKDF2.
- **Rate limiting** applies only to endpoints with `[EnableRateLimiting]`: `credentials` (POST `/account/login`,
  POST `/setup`; 10 per minute per IP) and `api` (every `ApiControllerBase` action; 300 per minute per IP). Fixed
  windows, no queue, partitioned by `Connection.RemoteIpAddress`, per instance. Rejections get an empty `429`.

## Routing

Endpoints come from four sources, mapped in `Program.cs`:

| Source | Mapping | Matches |
| --- | --- | --- |
| Attribute routes | `[Route]`/`[HttpGet("...")]` on every controller | all admin, account, setup, extension and API routes, `/`, `/error` (any method), `/media/{id:guid}/{fileName?}` |
| Conventional route `default` | `{controller=Home}/{action=Index}/{id?}` | only actions **without** attribute routes - in practice `HomeController.RenderPage` at `/Home/RenderPage` (unused) |
| Minimal API | `app.MapGet("/health", ...)` | `/health` |
| Fallback | `app.MapFallbackToController("RenderPage", "Home")` | any path not matched above **and without a file extension** (`{*path:nonfile}`) |

Because the fallback has the lowest priority, real routes such as `/admin/...`, `/setup`, `/account/...`,
`/media/...` and `/api/...` always win over a content page with the same slug. A content page with slug `admin` is
therefore unreachable on the public site.

Full screen route list: [pages.md → Route map](pages.md#route-map).

### Public URL resolution

```mermaid
flowchart TD
    U["GET /about/team"] --> FB["Fallback → HomeController.RenderPage"]
    FB --> Load["Load (Id, ParentPageId, Slug) of ALL live pages (every tenant)"]
    Load --> Walk["For each URL segment: find child of current parent<br/>exact slug (case-insensitive) first, else a '[param]' slug"]
    Walk -- "no match" --> NF([404 NotFound])
    Walk -- "matched every segment" --> Full["Load the matched page in full"]
    Full --> RV["Capture dynamic values into ViewData['RouteValues']"]
    RV --> View["View('Page', matched)"]
    Root["GET /"] --> Index["HomeController.Index"]
    Index -- "live page with slug '/' or 'home'" --> View
    Index -- "none" --> List["Home/Index: list of live pages"]
```

Rules and limitations are documented once in
[content pages and routing](../features/content-pages-and-routing.md#public-resolution).

## Navigation flow (admin)

All admin navigation is plain links in `_Sidebar.cshtml` and full-page loads. Mutations are form POSTs followed by
`RedirectToAction` (Post/Redirect/Get), except the Content Manager tree reorder, which is a `fetch` POST. See
[pages.md → Navigation](pages.md#navigation).

## Authentication flow (admin sign-in)

```mermaid
sequenceDiagram
    actor User
    participant RL as Rate limiter (credentials)
    participant AC as AccountController
    participant AS as AuthService
    participant DB as DotNetForgeDbContext
    participant H as IPasswordHasher
    participant Aud as IAuditService
    User->>RL: POST /account/login (email, password, returnUrl, antiforgery)
    alt more than 10 attempts this minute from this IP
        RL-->>User: 429
    end
    RL->>AC: Login
    AC->>AS: GetDefaultTenantIdAsync() (oldest tenant)
    AC->>AS: ValidateAsync(email, password, tenantId)
    AS->>DB: User by (TenantId, Email)
    alt not found
        AS-->>AC: InvalidCredentials
    else Disabled
        AS-->>AC: Disabled
    else LockoutEndUtc in future
        AS-->>AC: LockedOut
    else wrong password
        AS->>DB: FailedLoginCount++ (5th → LockoutEndUtc = now + 15 min, count reset)
        AS-->>AC: InvalidCredentials
    else ok
        AS->>DB: reset counters, LastLoginDate = now
        AS->>DB: role names via UserRoles ⨝ Roles
        AS-->>AC: Success + ClaimsPrincipal
    end
    alt Success
        AC->>AC: SignInAsync(cookie scheme), HttpContext.User = principal
        AC->>Aud: user.login (entry carries the signed-in user)
        AC-->>User: LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/admin")
    else failure
        AC->>Aud: user.login.failed (success = false)
        AC-->>User: re-render Login with error
    end
```

The cookie principal carries `NameIdentifier` (user id), `Name` (display name), `Email`, `dnf:tenant` and one
`Role` claim per role. Roles are read **only at sign-in**; changing a user's roles takes effect after they sign in
again. A non-local or missing `returnUrl` goes to `/admin`. `POST /setup` follows the same pattern (rate-limited,
signs the new Super Admin in, audits `cms.installed`). Full details: [authentication](../features/authentication.md).

### Session validation (every cookie request)

```mermaid
sequenceDiagram
    participant C as Cookie handler
    participant V as DependencyRegistration.ValidateSessionAsync
    participant M as IMemoryCache
    participant AS as AuthService
    C->>V: OnValidatePrincipal
    alt no parsable NameIdentifier
        V->>C: RejectPrincipal
    end
    V->>M: get dnf:session:{userId}
    alt not cached
        V->>AS: IsActiveAsync(userId) - exists and Status = Enabled
        V->>M: set result for 30 s
    end
    alt inactive
        V->>C: RejectPrincipal + SignOutAsync (request continues anonymous)
    end
```

A disabled or deleted user therefore loses access within 30 seconds; their role claims are not re-read.

## Authorization flow (admin screen)

```mermaid
flowchart LR
    Req["POST /admin/content/update/{id}"] --> Policy{"AdminArea policy:<br/>role in Super Admin, Admin, Editor, Author?"}
    Policy -- "no cookie" --> L([302 /account/login])
    Policy -- "no" --> D([302 /account/denied → 403 page])
    Policy -- yes --> Roles{"Controller [Authorize(Roles = ...)]?"}
    Roles -- "not in role" --> D
    Roles -- ok --> Perm{"Can / CanModify<br/>(IPermissionService)"}
    Perm -- "denied: Forbid()" --> D
    Perm -- ok --> Action["ContentController.Update"]
```

All `[Authorize]` attributes on a controller must pass (they are combined with AND). The `Can`/`CanModify` step exists
only in `ContentController` (area `Collection types`) and `MediaController` (area `Media`); other screens stop at
the role checks. `CanModify` grants the "any" action, or the `.own` action when `CreatedById`/`UploadedById` is the
current user. The sidebar renders every link for every admin-capable user; restricted ones lead to the denied
screen. See [authorization](../features/authorization.md).

## API request flow

```mermaid
sequenceDiagram
    actor Client
    participant IM as InstallationMiddleware
    participant RL as Rate limiter (api)
    participant TH as ApiTokenAuthenticationHandler
    participant MC as IMemoryCache
    participant DB as DotNetForgeDbContext
    participant F as RequireApiPermission filter
    participant C as Api controller
    Client->>IM: GET /api/content/pages, Authorization: Bearer dnf_ab12cd34_...
    IM->>RL: (installed)
    alt more than 300 requests this minute from this IP
        RL-->>Client: 429
    end
    RL->>TH: authorize endpoint with scheme ApiToken
    alt no header / not Bearer
        TH-->>Client: NoResult → 401
    end
    TH->>MC: get dnf:apitoken:{SHA-256 of presented token}
    alt cached
        TH->>DB: ApiToken by cached Id
    else not cached
        TH->>DB: ApiTokens where TokenPrefix = prefix
        TH->>TH: PBKDF2 verify against each candidate
        TH->>MC: set token Id for 10 min (on success)
    end
    alt unknown, revoked or expired
        TH-->>Client: Fail → 401
    end
    opt LastUsedDate missing or older than 5 min
        TH->>DB: LastUsedDate = now (failure logged, request continues)
    end
    TH->>F: principal with dnf:permission claims + dnf:tenant
    alt permission claim missing
        F-->>Client: 403 { "error": "Missing required permission '...'" }
    end
    F->>C: execute
    C->>DB: query filtered by TenantId (AsNoTracking, projected)
    C-->>Client: 200 JSON
```

The token row is re-read on every request, so revocation and expiry take effect immediately even when the PBKDF2
result is cached. `POST /api/content/pages` goes through `IPageService.ApplyAsync` (400 `{ error }` on a rule
violation) and audits `content.created` as `API token {id}`. See [headless API](../features/headless-api.md).

## Database request flow (typical admin mutation)

```mermaid
sequenceDiagram
    actor Editor
    participant CC as ContentController.Update
    participant PS as IPageService (PageService)
    participant DB as DotNetForgeDbContext
    participant Aud as IAuditService
    Editor->>CC: POST /admin/content/update/{id} (PageInput + antiforgery)
    CC->>DB: Page where Id = id and TenantId = active tenant (tracked)
    CC->>CC: CanModify(Collection types, update, update.own, CreatedById) else Forbid()
    alt Published or schedule changed and not Can(Collection types, publish)
        CC-->>Editor: 200 re-render with "You don't have permission to publish, unpublish or schedule pages."
    end
    CC->>PS: ApplyAsync(page, form, tenantId)
    PS->>DB: AsNoTracking reads for parent/cycle, slug uniqueness, dynamic sibling
    alt rule violated (incl. length limits)
        PS-->>CC: error message
        CC-->>Editor: 200 re-render Index with ModelState error (form keeps input)
    else valid
        PS-->>CC: null (page mutated in memory, not saved)
        CC->>DB: UpdatedDate = now, SaveChangesAsync
        CC->>Aud: content.updated → separate SaveChangesAsync
        CC-->>Editor: 302 /admin/content?selected={id} + TempData["Success"]
    end
```

The entity save and the audit save are two separate `SaveChangesAsync` calls (no shared transaction). The other
content mutations follow the same shape:

| Action | Permission | Service call | On rule violation | Audit |
| --- | --- | --- | --- | --- |
| `POST /admin/content/create` | `create`; parent must exist in the tenant (else 404) | none (new page `new-page-<8 hex>`) | - | `content.created` |
| `POST /admin/content/update/{id}` | `update` or `update.own`; `publish` to change Published/schedule | `ApplyAsync` | re-render with error | `content.updated` |
| `POST /admin/content/delete/{id}` | `delete` or `delete.own` | `DeleteAsync` (re-parents children; refuses if they would clash) | re-render with error | `content.deleted` |
| `POST /admin/content/reorder` (`fetch`) | `update` | `ReorderAsync` (tenant ownership, no cycles, no duplicate slugs / second dynamic segment) | `400 { error }` → `alert(error)` | `content.reordered` |
| `POST /api/content/pages` | API key `content.create` | `ApplyAsync` | `400 { error }` | `content.created` |

## Media upload flow

```mermaid
sequenceDiagram
    actor User
    participant MC as MediaController.Upload
    participant MS as MediaService
    participant FS as IFileStorage
    participant DB as DotNetForgeDbContext
    participant Aud as IAuditService
    User->>MC: POST /admin/media/upload (multipart files, isPublic, antiforgery)
    Note over MC: RequestSizeLimit = 25 MB x 10 + 1 MB<br/>bodies over 64 KB are buffered in the OS temp dir
    MC->>MC: Can(Media, create) else Forbid(), then 1 to 10 files
    loop each file
        MC->>MS: UploadAsync(file, isPublic, TenantId, CurrentUserId)
        MS->>MS: reject empty, over 25 MB, extension not in AllowedTypes
        MS->>MS: key = {tenantId:N}/{yyyy}/{MM}/{guid:N}{ext}, content type from extension
        MS->>FS: SaveAsync(key, stream, contentType)
        alt storage throws
            MS->>FS: DeleteAsync(key) (best effort)
            MS-->>MC: "'x' could not be stored right now. Try again later." (LogError)
        else stored
            MS->>DB: add MediaFile, SaveChangesAsync
            opt save throws
                MS->>FS: DeleteAsync(key), rethrow (no orphaned object)
            end
            MS->>Aud: media.uploaded
        end
    end
    alt any file failed
        MC-->>User: 200 Media screen with errors (stored files are listed)
    else all stored
        MC-->>User: 302 /admin/media + TempData["Success"]
    end
```

Delete (`POST /admin/media/delete/{id}`) needs `Can(Media, delete)` or `delete.own` on files the user uploaded;
`MediaService.DeleteAsync` removes the row first, then the object (a failed object delete is logged as an orphan
warning), then audits `media.deleted`. Rules and limits:
[media storage → upload flow](../features/media-storage.md#upload-flow).

## Media download flow

```mermaid
sequenceDiagram
    actor B as Browser
    participant MF as MediaFilesController.Download
    participant DB as DotNetForgeDbContext
    participant FS as IFileStorage
    participant OS as Object storage
    B->>MF: GET /media/{id}/{fileName?}
    MF->>DB: MediaFile by Id (TenantId, FileName, ContentType, RelativePath, IsPublic)
    alt no row, or private and caller is not an admin-capable user of the file's tenant
        MF-->>B: 404 (existence not revealed)
    end
    MF->>MF: Content-Disposition inline (image, video, audio, PDF) or attachment<br/>Cache-Control "public, max-age=300" or "private, no-store"
    MF->>FS: GetDownloadUrlAsync(key, disposition, 1 h public / 5 min private)
    alt S3 provider returns a presigned URL
        MF-->>B: 302 to url.AbsoluteUri
        B->>OS: GET presigned URL
        OS-->>B: object (response-content-disposition)
    else local provider returns null
        MF->>FS: OpenReadAsync(key)
        alt object missing
            MF-->>B: 404 (LogWarning)
        else found
            MF-->>B: 200 stream + Content-Disposition, range requests when seekable
        end
    end
```

The trailing `fileName` is cosmetic; the id selects the file. The controller has no `[Authorize]`: the cookie (if
any) is read by `UseAuthentication`, and private access is decided by role and `dnf:tenant` claim. Details:
[media storage → download flow](../features/media-storage.md#download-flow).

## Background operation

`ScheduledPublishingService` (hosted) waits 5 s, then every 15 s opens a DI scope and finalizes due schedules across
**all tenants**. It runs on every instance; the work is idempotent. It does not decide public visibility -
`HomeController.Live` already hides/shows pages by comparing the schedule to `DateTime.UtcNow` on each request. See
[scheduled publishing](../features/scheduled-publishing.md).

```mermaid
stateDiagram-v2
    [*] --> Waiting : host start
    Waiting --> Applying : 5 s startup delay, then every 15 s
    Applying --> Waiting : saved (or nothing due)
    Applying --> Waiting : exception → LogWarning, retry next tick
    Waiting --> [*] : stoppingToken cancelled
```

## Configuration loading

```mermaid
flowchart LR
    EnvVar["Process environment variable"] -- "wins when non-empty" --> Get["EnvConfigurationLoader.Get(key)"]
    File[".env in content root (DotEnvParser)"] -- "used when env var empty/missing" --> Get
    Get --> Validate{"valid? (outside Development:<br/>absolute SQLite path and STORAGE_LOCAL_PATH)"}
    Validate -- no --> Ex["ConfigurationException → exit 1"]
    Validate -- yes --> AE["AppEnvironment singleton"]
    AE --> DbCfg["DbProviderConfigurator + context type"]
    AE --> Storage["IFileStorage: LocalFileStorage or S3FileStorage"]
    AE --> Views["AppName in ViewData / layouts"]
    AppSettings["src/DotNetForge.Web/appsettings.json / .Development.json"] --> Logging["Logging levels, AllowedHosts"]
```

See [configuration](../features/configuration.md) and
[deployment → environment variables](../guides/deployment.md#environment-variables).

## Error handling flow

| Situation | Where detected | What the user gets |
| --- | --- | --- |
| Invalid `.env`, or a relative/missing path outside Development | `EnvConfigurationLoader` at startup | Process exits with code 1; message on stderr |
| Form validation failure (incl. length limits) | `PageService`, `MediaService`, `InstallationService`, controller checks (Settings, API Tokens) | Same screen re-rendered with a validation summary |
| Permission denied by `Can`/`CanModify` | `ContentController`, `MediaController` → `Forbid()` | 302 to `/account/denied` (renders with status 403) |
| Content delete would make children clash | `PageService.DeleteAsync` | Content Manager re-rendered with the error |
| Reorder rejected (cycle, clash, foreign id) | `PageService.ReorderAsync` | `400 { error }`; `admin-content.js` shows `alert(error)` and reloads |
| Entity not found in the active tenant | controller `FirstOrDefaultAsync` → `NotFound()` | Empty-body 404 |
| Public URL not resolving | `HomeController.RenderPage` | Empty-body 404 |
| Private media requested without access, or missing object | `MediaFilesController` | Empty-body 404 (missing object also logged) |
| Media storage unavailable during upload | `MediaService.UploadAsync` | Per-file message on the Media screen; error logged |
| Anonymous on admin | cookie challenge | 302 to `/account/login?ReturnUrl=...` |
| Wrong role | cookie forbid | 302 to `/account/denied` (renders with status 403) |
| Disabled/deleted user with a valid cookie | `ValidateSessionAsync` | Signed out within 30 s; next admin request → login |
| Too many requests | rate limiter (`credentials`, `api`) | Empty `429` |
| API auth failure / missing permission | `ApiTokenAuthenticationHandler` / `RequireApiPermissionAttribute` | 401 / 403 JSON |
| API rule violation | `ContentApiController` via `IPageService` | `400 { "error": "..." }` |
| Unhandled exception (Development) | developer exception page | Stack trace page |
| Unhandled exception (other environments) | `UseExceptionHandler("/error")` → `HomeController.Error` (`[Route("/error")]`, any method) | "Something went wrong" page, for GET and POST alike |
| Background job exception | `ScheduledPublishingService` catch | Log warning; retried next tick |

Details and gaps: [logging and errors](../features/logging-and-error-handling.md).

## External integrations

Out-of-process dependencies at runtime: the database (SQLite file or PostgreSQL server) and, with
`STORAGE_PROVIDER=s3`, an S3-compatible object store - the app uploads, reads and deletes objects through
`S3FileStorage`, and browsers download directly from presigned URLs. Planned integrations and their current state
are listed in [implementation-status.md](../implementation-status.md).
