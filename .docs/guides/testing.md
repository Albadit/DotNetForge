# Testing

## Test projects

| Project | References | Runs against | Count (Oct 2026) |
| --- | --- | --- | --- |
| `tests/DotNetForge.Tests` | `src/` libraries (not the host, not `Api`) | pure code, fakes, temp folders, closed ports; optionally a live S3 bucket | 829 tests: 824 run, 5 live-S3 tests skipped unless `DNF_TEST_S3_*` is set |
| `tests/DotNetForge.UiTests` | `src/DotNetForge.Web/DotNetForge.Web.csproj`, Microsoft.Playwright, Deque.AxeCore.Playwright; links the integration tests' `DotNetForgeWebFactory` | the real host on Kestrel (a fresh PostgreSQL database) driven by headless Chromium ([UI tests](#ui-tests)) | screenshots, accessibility, behaviour and extension tests of the [UI library](../ui/README.md) |
| `tests/DotNetForge.IntegrationTests` | `src/DotNetForge.Web/DotNetForge.Web.csproj`; build-only references to the fixture extension `tests/fixtures/extensions/test-kit` and the `markdown-kit`, `contact-form`, `seo-toolkit` samples ([extension fixtures](#extension-fixtures)) | the real host via `WebApplicationFactory<Program>`, a fresh PostgreSQL database per factory ([databases](#databases)) and a temp media folder | 179 tests: 174 run, 5 skipped (tests that need a `DNF_TEST_*` server variable) |

```bash
dotnet test tests/DotNetForge.Tests            # Passed: 824, Skipped: 5
dotnet test tests/DotNetForge.IntegrationTests # Passed: 174, Skipped: 5
node tests/js/page-builder-fixtures.mjs        # 84/84 page builder fixtures match
```

`node tests/js/content-list-fixtures.mjs` rewrites the seven content-list fixtures (`content-list-*.json`) from the
browser modules; run it only after changing one of its cases, review the diff, then run the two commands above.

The unit tests need no external services, no network and no `.env`. The integration tests need a PostgreSQL server
(`docker compose -f docker/databases/postgres.yml up -d`, or `DNF_TEST_POSTGRES`, [databases](#databases)); the
factory sets every other environment variable itself, and nothing is written inside the repository.

## Unit tests (`DotNetForge.Tests`)

| File | Classes (tests) | Covers |
| --- | --- | --- |
| `Content/ContentSchemaTests.cs` | `ContentSchemaTests` (32) | structured content rules in `DotNetForge.Core/Content`: field definitions checked and normalized, invalid definitions refused with a reason (12 cases), nesting at most three levels, form values converted and checked with the field path (11 cases), publishing needs required fields and minimum rows (a draft does not), values follow renamed fields, index values, `ItemData` formatting and flattening, bound props, every template valid ([structured content](../features/structured-content.md#field-types)) |
| `Endpoints/EndpointRulesTests.cs` | `EndpointRulesTests` (33) | custom endpoint rules in `DotNetForge.Core/Endpoints`: paths normalized or refused (8 cases), an operation's sub-path may be empty and joins the endpoint's path (`Combine`; a repeated `{name}` or more than 10 segments refused), request paths matched with route values, route parameters tied to the path and required, bad parameter definitions explained, `@name` placeholders found (not `@@globals` or e-mail addresses), values converted per type or refused, binding from route, query and body with defaults ([custom endpoints](../features/custom-endpoints.md)) |
| `DatabaseProviderTests.cs` | `DatabaseProviderTests` | provider names and aliases, detection from the connection string (and refusing ambiguous ones) and per-provider validation (SQLite paths, PostgreSQL URLs, MongoDB database name) - resolved as additional database `extra`; only PostgreSQL hosts the CMS (`Only_postgresql_hosts_the_cms`), the CMS connection string required also in Development (`The_cms_database_needs_a_connection_string_also_in_development`); descriptions without credentials, several databases, registering and replacing providers |
| `DatabaseTranslationTests.cs` | `DatabaseTranslationTests` | exact parameterized SQL from each SQL database's own builder, values never in SQL text (checked on all four builders), LIKE escaping, aggregates, model-checked names, identifier validation, value coercion; MongoDB filters with typed values (UUID, enum, date), values never operators, escaped regexes, composite keys under `_id` |
| `DatabaseServiceTests.cs` | `DatabaseServiceTests` (mock provider) | routing to named databases, result shape, FindOne limit and default timeout, timeout vs caller cancellation, error translation with context and inner exception, bugs not translated, `Try*` results, invalid commands rejected before the provider, transactions bound to one database, streaming, connection tests, logs without values or secrets |
| `DatabaseConnectionTests.cs` | `DatabaseConnectionTests` | real PostgreSQL, SQL Server, MySQL and MongoDB drivers against a closed port → `DatabaseConnectionException` without credentials |
| `Transfer/TransferRulesTests.cs` | `TransferRulesTests` (6) | retired columns of older exports are ignored; `TransferRules`: the same schema passes, an older file may lack a table or a nullable column; a foreign format or version is refused first; a different data version, unknown tables/columns and changed types are named; a missing required column and a damaged manifest entry are refused; file names |
| `Email/EmailTemplateRendererTests.cs` | `EmailTemplateRendererTests` (11) | `EmailTemplateCatalog` defaults are valid and render with their samples; unknown, missing required and incomplete placeholders are rejected; subject/body required, subject one line; values HTML-encoded in the body, plain in the subject; a missing value throws; `EmailLayout` frames the body with the encoded site name |
| `SmtpProbeTests.cs` | `SmtpProbeTests` (4) | `SmtpProbe` against a scripted local SMTP server: every step passes and nothing is sent; a refused sign-in fails that step without the password; TLS without STARTTLS on the server; a closed port fails Connect |
| `EnvConfigurationTests.cs` | `EnvConfigurationTests`, `DotEnvParserTests` (1) | `EnvConfigurationLoader`: database settings read as written (providers validate them), a `DATABASE_PROVIDER` value stops startup, `DOTNETFORGE_ENV_FILE` replaces `.env`, named `DATABASES_*` databases and invalid keys, Development storage defaults under the content root (`storage/media`), S3 settings with R2-style defaults (`STORAGE_S3_REGION=auto`), S3 required outside Development, any S3 key selecting S3, invalid S3 configuration (missing bucket, missing region, malformed service URL, `STORAGE_S3_FORCE_PATH_STYLE`); SMTP off until configured, defaults (port 587, TLS on) and `SMTP_TLS=false`, partial or invalid SMTP configuration (host without sender, credentials only, bad sender, bad port) fails startup; SMS off until all three `TWILIO_*` values are set (two of them start fine, SMS stays off); `DotEnvParser` |
| `FileStorageTests.cs` | `LocalFileStorageTests` (8), `StorageKeyTests` (13), `S3FileStorageTests` (8, 5 of them live) | the shared `IFileStorage` contract (`FileStorageContract`: save/read, overwrite, missing → `null`, idempotent delete, invalid keys) for the local provider; local-only: files stay inside the root, no temp files left, a failed upload leaves no partial object, no download URLs; `StorageKey.IsValid`; S3 offline: presigned URLs signed, short-lived and carrying `response-content-disposition`, presigning follows a plain `http://` endpoint, invalid keys rejected before any request; S3 live (`[S3Fact]`): the contract plus a download through a presigned URL |
| `ExtensionLoaderTests.cs` | `ExtensionLoaderTests` (3) | `ExtensionLoader`: discovers every manifest below the root, cache invalidated when a manifest is added (file watcher), missing root → no extensions |
| `ExtensionRuntimeTests.cs` | `ExtensionRuntimeTests` (5) | `ExtensionRuntime` on temp folders: views-only extensions load no code, a missing assembly is reported with the build hint, a non-assembly file is reported, missing/failed/circular dependencies are reported, `ExtensionContext` typed settings and `GetEnvironmentValue` |
| `InstallationServiceTests.cs` | `InstallationServiceTests` (5) | `InstallationService` with a `FakeInstallationStore`: success, mismatch, weak password, invalid email, already installed |
| `ManifestValidationTests.cs` | `ManifestValidationTests` (31) | `ManifestValidator`: required fields, permissions, type, semver, all nine types, `assembly` must be a `.dll` inside the extension folder (11 cases: `..`, rooted, backslash, hidden segment, non-`.dll`, empty rejected) |
| `PageBuilder/*.cs` | `GoldenFixtureTests`, `DocumentValidatorTests`, `ValuesTests`, `ContentComponentTests`, `ComponentRegistryTests`, `SecurityRegressionTests`, `SegmentsTests` (with all fixture cases) | the page builder's C# side, ported with the reference project's tests: every golden fixture in `tests/fixtures/page-builder/render` (84, 7 of them content lists) normalizes and renders byte for byte; validator, value, URL and component rules ([page builder → Testing](../features/page-builder.md#testing)) |
| `PermissionTests.cs` | `PermissionTests` (13) | `PermissionService` / `PermissionMatrix` defaults and union semantics; checks from stored grants (custom role, edited built-in role, Super Admin, matrix fallback) with a fixed `IRoleGrantStore` |
| `SecurityPrimitiveTests.cs` | `PasswordHasherTests` (3), `ApiTokenFactoryTests` (2), `WebhookSignerTests` (3) | `Pbkdf2PasswordHasher`, `ApiTokenFactory`, `HmacWebhookSigner` |
| `ValidationTests.cs` | `PasswordPolicyTests` (7), `EmailValidatorTests` (6) | `PasswordPolicy`, `EmailValidator` (the `SlugHelper` tests were removed with the class; slug rules are tested through `PageService`) |

`EnvConfigurationTests` saves, clears and restores the configuration environment variables around each test and
loads from a temp directory.

Not covered by unit tests: `AuthService` (lockout), controllers. `PageService` and `MediaService` are covered by
integration tests.

### Live S3 tests

The five `[S3Fact]` tests in `S3FileStorageTests` are skipped unless these environment variables are set:

| Variable | Required | Example (local SeaweedFS) |
| --- | :-: | --- |
| `DNF_TEST_S3_SERVICE_URL` | ✔ | `http://localhost:8333` |
| `DNF_TEST_S3_BUCKET` | ✔ | `dnf-test` |
| `DNF_TEST_S3_ACCESS_KEY_ID` | ✔ | `dev` |
| `DNF_TEST_S3_SECRET_ACCESS_KEY` | ✔ | `dev` |
| `DNF_TEST_S3_REGION` | | default `auto` |
| `DNF_TEST_S3_FORCE_PATH_STYLE` | | `true` for SeaweedFS/MinIO (default `false`) |

A local S3-compatible server - either `docker compose -f docker/s3/seaweedfs.yml up -d` (SeaweedFS on port 8333, bucket
`dotnetforge` created by the `seaweedfs-bucket` service; use `DNF_TEST_S3_BUCKET=dotnetforge`) or a standalone container:

```bash
docker run -d --name s3 -p 8333:8333 chrislusf/seaweedfs server -s3 -dir=/data
until curl -sf -X PUT http://localhost:8333/dnf-test; do sleep 1; done   # creates the bucket
DNF_TEST_S3_SERVICE_URL=http://localhost:8333 DNF_TEST_S3_BUCKET=dnf-test \
DNF_TEST_S3_ACCESS_KEY_ID=dev DNF_TEST_S3_SECRET_ACCESS_KEY=dev DNF_TEST_S3_FORCE_PATH_STYLE=true \
  dotnet test tests/DotNetForge.Tests                                   # the 5 live tests now run too
```

The SeaweedFS S3 gateway runs without authentication (any key works) - development only. Any real S3-compatible
bucket (R2, AWS S3) works the same way; the tests write and delete objects under `tests/`.

## Integration tests (`DotNetForge.IntegrationTests`)

### `DotNetForgeWebFactory`

Each factory instance creates a temp work directory `<temp>/dnf-it-<guid>/` and sets, **as process environment
variables** before the host builds:

| Variable | Value |
| --- | --- |
| `DATABASE_CONNECTION_STRING` | a fresh PostgreSQL database `dnf_it_<guid>` on the `DNF_TEST_POSTGRES` server (`TestDatabaseServer.CmsDatabase`), dropped on dispose |
| `STORAGE_S3_*` | placeholder values (startup requires S3 outside Development); the host's own storage - the **keyed** `IFileStorage` `"core"` behind the extension storage router - is replaced through `ConfigureTestServices` with a `LocalFileStorage` in `<workdir>/media` (exposed as `factory.StoragePath`), so no S3 server is needed and storage-connector extensions still route correctly |
| `APP_URL` | `http://localhost` (the base of the links in recorded emails) |
| `SMTP_HOST`, `SMTP_FROM` | `smtp.test.invalid`, `cms@example.com`: email counts as configured (so **Forgot password?** and email confirmation are available); `IEmailSender` is replaced through `ConfigureTestServices` by a recorder - the messages are in `factory.SentEmails`, nothing is sent |
| `EXTENSIONS_PATH` | the constructor's `extensionsPath` argument, or empty (= the repository's `extensions/`) |

So each factory gets a fresh, migrated, seeded database and its own media folder; the work directory is deleted on
dispose (best effort). Because of these process-wide variables, parallelization is disabled (`AssemblyInfo.cs`).
The constructor takes the environment name (default `Development`) and an optional `extensionsPath`;
`new DotNetForgeWebFactory("Production")` runs the production code paths (CSP enforced, `Secure` cookie,
absolute-path configuration rules), `new DotNetForgeWebFactory(extensionsPath: ...)` hosts another extensions folder
(the fixtures). Extension code is loaded when the host builds, so each factory sees the extensions as they were then.

| Helper | Does |
| --- | --- |
| `InstallAsync()` | installs programmatically (`admin@example.com` / `Sup3rSecret`) through `IInstallationStore` and marks `InstallationStatusCache` |
| `CreateUserAsync(email, password, role)` | adds an enabled user with one built-in role, returns its id |
| `WithDbAsync(db => ...)` | runs code against a fresh `DotNetForgeDbContext` scope (arrange or assert on rows) |
| `CreateNoRedirectClient()` | cookie-keeping client that does not follow redirects, base address `https://localhost` (the auth cookie is `Secure` outside Development) |
| `SignInAsync(email?, password?)` | a `CreateNoRedirectClient()` signed in through the real login form (throws unless the POST returns `302`) |
| `PostFormAsync(client, formPage, action, fields)` | GETs `formPage` for the antiforgery token, then POSTs the form fields plus the token to `action` |
| `GetAntiforgeryTokenAsync(client, formPage)` | extracts `__RequestVerificationToken` (e.g. for a JSON `fetch`-style request with `X-CSRF-TOKEN`) |

### Extension fixtures

`tests/fixtures/extensions/` is a small extensions folder for `ExtensionTests`; its `Directory.Build.props` imports
`extensions/Directory.Build.props`, so fixtures build exactly like real extensions:

| Folder | Role |
| --- | --- |
| `test-kit` (`tests.plugin.kit`) | the one fixture with code (`TestKit.csproj` → `bin/TestKit.dll`): minimal-API endpoints (`/test-kit/ping`, `/events`, `/storage`), an MVC controller, an in-memory storage connector, a recording audit handler, a fake remote sign-in scheme `testkit` ("Test Kit") |
| `test-admin`, `test-widget`, `test-theme`, `test-module` | views-only admin tab, dashboard widget, theme (`Page.cshtml` + resources), page module |
| `test-lib`, `test-dependent` | dependency rules (enable/disable order) |
| `broken` (`tests.plugin.broken`) | a valid manifest whose `bin/Missing.dll` does not exist: "not running", Enable refused |

`DotNetForge.IntegrationTests.csproj` references `TestKit.csproj` and the three samples `SampleExtensionTests` loads
with `ReferenceOutputAssembly="false"`: they are built before the tests (so their `bin/` exists) but never linked -
the host loads them like any extension. When you add a fixture or sample with code that a test loads, add the same
kind of reference.

### Databases

The integration tests always run the CMS on PostgreSQL (`TestDatabaseServer`). `DNF_TEST_POSTGRES` holds a server
connection string **without** a database; the default is
`Host=localhost;Port=5432;Username=postgres;Password=postgres;GSS Encryption Mode=Disable` - the server of
`docker/databases/postgres.yml` (another host port: `DNF_POSTGRES_PORT=5433` when starting it, and `Port=5433` in
`DNF_TEST_POSTGRES`). Every factory creates its own database `dnf_it_<guid>` through the migrations and drops it on
dispose; `PageServiceTests` (no host) migrate their own `dnf_pages_<guid>` database the same way.

```bash
docker compose -f docker/databases/postgres.yml up -d
dotnet test tests/DotNetForge.IntegrationTests
```

The other variables are optional servers for the **additional-database** tests only:

| Variable | Example (the `docker/databases/` servers) | Runs |
| --- | --- | --- |
| `DNF_TEST_SQLSERVER` | `Server=localhost,1433;User Id=sa;Password=DotNetForge!2026;TrustServerCertificate=true` | `AdditionalDatabaseProviderTests.SqlServer`, the SQL Server credential test |
| `DNF_TEST_MYSQL` | `Server=localhost;Port=3306;Uid=root;Pwd=dotnetforge` | `AdditionalDatabaseProviderTests.MySql`, the MySQL credential test |
| `DNF_TEST_MONGODB` | `mongodb://localhost:27017/?replicaSet=rs0&directConnection=true` (a replica set) | `AdditionalDatabaseProviderTests.MongoDb` |

```bash
docker compose -f docker/databases/mysql.yml up -d
DNF_TEST_MYSQL="Server=localhost;Port=3306;Uid=root;Pwd=dotnetforge" dotnet test tests/DotNetForge.IntegrationTests --filter "FullyQualifiedName~AdditionalDatabase|FullyQualifiedName~DatabaseCredential"
```

`AdditionalDatabaseProviderTests` runs SQLite and PostgreSQL always and the others with `[AdditionalDatabaseFact]`
when their variable is set (`TestDatabaseServer.AdditionalDatabase`). `DatabaseCredentialTests` run for PostgreSQL,
SQL Server and MySQL when their variable is set (`[DatabaseServerFact]`).

### Tests

| File | Test | Asserts |
| --- | --- | --- |
| `CmsIntegrationTests.cs` | `Health_endpoint_is_ok_even_before_install` | `/health` → 200 |
| | `Before_install_all_requests_redirect_to_setup` | `/` → 302 `/setup` |
| | `Setup_page_is_served_before_install` | `/setup` → 200 containing "Email" |
| | `After_install_setup_is_blocked` | `/setup` → 302 `/admin` |
| | `After_install_admin_requires_authentication` | `/admin` → 302 containing `/login` |
| | `Api_requires_a_token` | `/api/content/pages` → 401 |
| `MediaTests.cs` | `Public_upload_is_stored_outside_the_app_and_downloadable_anonymously` | upload lands under `StoragePath`, anonymous download works |
| | `A_file_can_be_made_private_and_public_again_by_users_who_may_update_it` | an Author can't change the admin's file (denied); the admin makes it private (anonymous download 404, toast) and public again (200); two `media.updated` entries |
| | `Private_file_is_hidden_from_anonymous_users_but_served_to_admins` | anonymous → 404, signed-in admin → file |
| | `Unknown_media_id_is_not_found` | 404 |
| | `Active_or_unknown_file_types_are_rejected` (4 cases: `.html`, `.svg`, `.js`, no extension) | form error "file type that is not allowed", no row, no stored object |
| | `Delete_removes_the_row_and_the_stored_object` | object removed, link 404, `media.deleted` audited |
| | `Storage_outage_gives_a_clear_error_and_no_orphaned_row` | a failing `IFileStorage` → clear message, no `MediaFile` row |
| | `Author_cannot_delete_media_uploaded_by_someone_else` | Author's delete of another user's file is refused |
| | `Folders_can_be_selected_to_move_and_delete_with_the_files` ([Media → folders](../pages/media.md#folders)) | the screen renders a `folderIds` checkbox per folder; one `move-selected` moves a folder and a file into "Archive" while "Archive" (selected as its own target) stays at the top level; `delete-selected` of an empty and a non-empty folder deletes the empty one: "Deleted 1 item." and "only empty folders can be deleted" |
| | `The_preview_serves_text_and_pdf_from_this_site_and_lists_zip_archives` ([Media → preview](../pages/media.md#preview)) | the screen has `#media-preview` and `data-preview` per file; `/admin/media/{id}/inline` serves a private CSV as `text/plain`, `inline`, content intact, and with `?download=true` as `text/csv` attachment; `/zip` lists a .zip (`total` 2, `docs/readme.txt` of 5 bytes); an anonymous request is redirected to sign-in |
| `AccountTests.cs` ([self-service accounts](../pages/account-self-service.md), [Advanced Settings](../pages/advanced-settings.md)) | `The_old_sign_in_address_redirects_to_login` | `GET /account/login?returnUrl=...` → 301 to `/login` with the query; anonymous `/admin` → `/login?ReturnUrl=` |
| | `Sign_ups_are_off_by_default` | `/register` GET and POST → 404, no **Create an account** link, no user created |
| | `With_sign_ups_on_a_visitor_registers_with_the_default_role_once_per_email` | after enabling sign-ups on Advanced Settings: the link shows, registration → 302 `/`, signed in (public header has **Sign out**, no Admin link), user confirmed with role `Authenticated`, `user.registered` audited; the same email in other casing → "already exists", still one user |
| | `Forgot_password_answers_neutrally_and_the_reset_link_works_once` | unknown and known emails get the same "Check your email"; one recorded email to the admin; the link's page opens, the new password is set and works; reusing the link → "This link doesn't work" |
| | `With_email_confirmation_on_a_new_account_signs_in_only_after_confirming` | registration → "Check your email" + one email; sign-in → "Confirm your email first"; the link → 302 `/login`; then sign-in works; the existing Super Admin is unaffected |
| | `Advanced_settings_validate_their_input_and_need_the_settings_permission` | an invalid URL is rejected; an Admin can't make sign-ups land in `Admin` but can save with `Authenticated`; an Editor → `/account/denied` |
| `MediaFolderTests.cs` ([Media → folders](../pages/media.md#folders)) | `Folders_nest_files_go_into_them_and_paths_follow_rename_and_move` | nested folders, duplicate names refused (case-insensitive), upload into a folder (only listed there; breadcrumbs), the same file again in that folder refused but allowed in another, rename and move rewrite child paths, no move into an own folder, moving a file, non-empty delete refused, empty delete, audit entries, unknown folder 404 |
| | `Invalid_folder_names_are_refused` (3 cases) | empty, `a/b`, `..` → nothing created |
| | `Authors_create_folders_and_move_their_own_files_but_cannot_rename_folders` | an Author creates a folder and moves an own file; renaming → `/account/denied` |
| `MediaOptimizationTests.cs` ([Media Library settings](../pages/media-settings.md), [optimization](../features/media-storage.md#optimization)) | `Images_are_scaled_down_re_encoded_and_optionally_stored_as_webp` | a 4000 × 3000 JPEG is stored at 2560 × 1920, smaller, `Optimized` with `OriginalSizeBytes`; the same upload again is a duplicate; a fake PNG is stored as uploaded; with WebP on a PNG becomes `logo.webp` (`image/webp`, `.webp` key); with optimization off stored unchanged; the library shows the "Optimized:" tooltip |
| | `Upload_limits_are_per_kind_and_validated` | the screen shows Upload limits and "No ffmpeg"; out-of-range values are refused with their messages and nothing is saved; with images at 1 MB a 1.5 MB PNG is refused ("… the limit for images.") while a 30 MB MP4 passes; the Upload dialog carries `data-max-image` and the per-kind text; `media.maxImageMb` stored, `settings.changed` "Media Library settings" audited |
| | `Videos_are_not_queued_without_ffmpeg` | compression on but no `FFMPEG_PATH` → an MP4 is `None`; a `Pending` row is reset to `None` by `VideoOptimizationService.RunNextAsync`; `Ffmpeg.Arguments` (input, CRF, short-side scale, output last) |
| | `Media_export_has_the_files_in_their_folders_and_a_manifest` | the media export .zip has `Photos/`, the empty `Empty/`, `Photos/beach.jpg`, `notes.txt` (content intact) and `media.json` (path, folder, url); a `MediaExport` record (2 files) and `transfer.exported` "Media: 2 files"; "Media export" in the history; the page has the Media button |
| | `Database_and_media_export_has_the_database_export_unchanged_and_the_files_under_media` | `/admin/data/export-all` gives `dotnetforge-full-*.zip` with the database export (.zip, readable) at the root and `media/`, `media/Photos/beach.jpg`, `media/notes.txt` and `media.json` in `media/` (`database` = the export's name, path `media/Photos/beach.jpg`, folder `/Photos`); an `Export` and a `MediaExport` record, the latter noted "With <export>" |
| `EmailConfigurationTests.cs` ([Email Configuration](../pages/email-configuration.md)) | `The_settings_from_env_are_shown_read_only` | the `.env` host and port shown, no form fields, a POST to the screen is not answered |
| | `Test_sends_go_out_and_every_send_is_logged` | a plain test and the password-reset template (`[Test]`, sample URL) are sent; two `EmailLog` rows (template key, recipient) shown in the log; an invalid recipient sends nothing |
| | `A_failed_send_is_logged_with_the_reason_but_not_the_servers_text` | a failing transport (535) → a failed log entry "refused the sign-in (535)" without the server's text |
| | `The_connection_test_shows_the_failing_step` | an unresolvable host → "The connection test failed", the Connect step's reason, audited |
| | `In_development_a_local_mailpit_gets_an_inbox_link` | `SMTP_HOST=localhost`, `SMTP_PORT=1026` → "Open Mailpit inbox" to `http://localhost:8026` |
| | `Without_smtp_in_env_email_is_off` | no `SMTP_*` → "Email is off", `/forgot-password` 404 |
| | `Only_the_settings_permission_opens_the_screen` | an Editor → `/account/denied` |
| | `Send_failures_are_described_without_the_servers_text` | `DescribeFailure`: timeout, connection, 535 sign-in - the server's text is not shown |
| `EmailTemplatesTests.cs` ([Email Templates](../pages/email-templates.md)) | `A_customized_template_is_sent_and_restoring_brings_back_the_default` | the list shows every template of the catalog (five); a customized password-reset template is saved (audited) and the reset email uses it with the values filled in; restore deletes the row |
| | `Unknown_or_missing_variables_and_stale_saves_are_refused` | unknown `{{firstName}}` and a missing `{{resetUrl}}` are named; a save based on an outdated version is refused and the first save stays |
| | `The_preview_renders_sample_data_in_a_sandbox` | the preview has an enforced CSP with `sandbox` and `default-src 'none'`, renders sample data, renders unsaved text or lists errors, stores nothing; the editor page loads the self-hosted Monaco loader with the variables as data and allows inline styles while the list page doesn't; the Monaco editor script is served from `src/DotNetForge.Web/wwwroot/lib/monaco/vs/editor/editor.main.js` |
| | `Registering_sends_a_welcome_email_and_a_lockout_sends_account_locked` | registration → one welcome email; 5 wrong passwords → an "is locked" email with the minutes and the forgot-password link |
| `DataManagementTests.cs` ([Data Management](../pages/data-management.md)) | `Only_a_super_admin_reaches_the_screen` | an Admin → `/account/denied` (also the export POST); a Super Admin sees export and import |
| | `An_export_imports_back_after_a_safety_backup_and_replaces_all_data` | the export is a .zip with a manifest and a User table but no Data Protection keys; changes made afterwards are gone after the import; a safety backup is stored; records and audit written; signed out to `/login`, the imported Super Admin signs in |
| | `A_foreign_or_damaged_file_is_refused_and_changes_nothing` | not a zip, a table edited after export (checksum), import not confirmed → errors, data unchanged, failed records, no safety backup |
| | `A_backup_is_stored_downloaded_restored_and_deleted` | Back up now → a stored .zip; download (POST) returns it; restore brings back the backed-up value; delete removes the file and keeps the record |
| | `The_schedule_is_saved_and_automatic_backups_are_pruned_to_the_keep_count` | schedule saved and validated; due → backup → not due; keep 1 leaves one stored automatic backup |
| | `Import_and_restore_are_not_available_outside_development` | Production: "Not available in production", export works, import and restore → 404 |
| | `Only_the_settings_permission_opens_templates` | an Editor → `/account/denied` (also the preview); an unknown key → 404 |
| `SecurityTests.cs` | `Responses_carry_security_headers` | `nosniff`, `SAMEORIGIN`, CSP `script-src 'self'` (Production) |
| | `An_exception_during_a_post_renders_the_error_page` | regression: a failing form POST renders the error page (500), not an empty 500 (Production, throwing `IPageService`) |
| | `Login_ignores_an_external_return_url` | `returnUrl=https://evil.example/` → 302 `/admin` |
| | `Successful_login_is_audited_with_the_user` | `user.login` entry has a `UserId` |
| | `Disabling_a_user_ends_their_session` | after disabling (session cache cleared), the next admin request → 302 `/login` |
| | `Repeated_sign_in_attempts_are_rate_limited` | 12 login POSTs: first 200, last 429 |
| | `Editors_can_no_longer_change_settings` | Editor `GET /admin/settings` → 302 `/account/denied` |
| | `Authors_cannot_delete_or_publish_pages_they_did_not_create` | Author: delete of a seeded page → denied; own page: publishing → form error, saving with Disabled → 302 |
| | `Reorder_rejects_a_cycle` | `POST /admin/pages/reorder` making a page its child's child → 400 containing "cycle" |
| | `Duplicate_api_token_name_is_a_validation_error_not_a_crash` | second token with the same name → 200 "already exists" |
| | `Api_page_creation_applies_the_content_rules_and_is_audited` | API create slugifies (`our-pricing`), duplicate root slug → 400, `content.created` with `UserId = null` and `API token ...` |
| `PageBuilderTests.cs` | `A_layout_is_drafted_published_and_rendered_on_the_site` | draft save (normalized), preview, stale save → 409 (force overwrites), invalid document → 400, publish → the public page renders the layout with `/page-css/{id}.css` (`text/css`, `immutable`), no `<style>`; unpublish keeps the draft; two `content.updated` layout entries |
| | `Only_users_who_may_edit_the_page_open_the_builder_and_publishing_needs_publish` | an Author is denied a seeded page's builder; a page edit grant for Author opens it and allows saving; publish → 403 |
| | `Page_permissions_seo_and_theme_are_saved_from_the_page_editor` | the page form saves a view grant (Authenticated), `NoIndex`, social image, redirects to `#permissions`; anonymous → sign-in redirect; signed-in page has `noindex` and `og:image`; `permission.changed`; an uninstalled theme is refused |
| | `Single_users_can_be_allowed_or_blocked_on_a_page` | blocking yourself is refused; an Editor blocked from view and edit gets 404 on the page and is denied the builder and delete; an Author allowed to edit opens the builder; visitors still see the page; clearing a user's rules removes them |
| | `Users_edit_their_own_profile_and_password` | Profile details saved and shown; wrong current password refused; new password signs in |
| `PublicSiteTests.cs` ([public content page](../pages/public-page.md), [themes](../features/themes.md#theme-inheritance)) | `The_built_in_header_lists_the_menu_pages_and_shows_admin_only_to_admin_users` | without a theme, `/about` has `nav.dnf-site__nav` with the menu pages (`/news`; `/about` with `aria-current="page"`), not the unpublished Contact, and no `/admin` link for a visitor; signed in as the admin the `/admin` link is there |
| | `A_page_hands_its_theme_down_to_child_pages_without_their_own` | the page form saves About with a theme and **Use this theme for child pages too**; `ThemeResolver.PageThemeIdAsync` gives its child Team that theme, while a child with its own theme keeps it; Team's editor shows "Parent's theme" |
| | `Theme_inheritance_stops_at_the_next_page_with_a_theme_of_its_own` | `ThemeResolver.InheritedFrom` on an in-memory tree (no host): a grandchild inherits from the root through a page without a theme; a middle page with its own theme ends the walk (site theme) unless it hands its theme down too; no parent → none |
| `StructuredContentTests.cs` | `A_template_creates_the_type_and_the_types_it_links_to_and_renamed_fields_keep_their_values` | the Blog posts template also creates Categories and Authors; an empty slug is made from the title; renaming a field keeps its values (same field id); a type another type links to can't be deleted |
| | `Entries_are_drafted_validated_published_listed_and_protected_from_stale_saves` | publishing needs the required fields (a draft does not); invalid and duplicate unique values refused; list status filter, search and sort on the server; an older `baseVersion` is refused and offered as an overwrite; duplicate makes an unpublished copy with unique values made unique; bulk delete removes entries and index rows |
| | `Pages_show_content_lists_and_detail_pages_and_follow_entry_edits` | a content list shows published, filtered, sorted entries with field formatting; a `[slug]` detail page shows the entry its URL names (title after it), unknown or unpublished → 404; editing an entry changes the pages; pagination with `?pg-{node}=2` |
| | `The_page_builder_lists_content_types_and_previews_entries` | the builder's `content/*` endpoints; a layout with a content list is saved, published and rendered |
| | `The_api_reads_and_writes_entries_within_its_permissions` | `entries.read` reads only (POST and `status=draft` → 403); create validated (400) and published; published-only reads, relationships expanded, filters and slugs (draft slug → 404, unknown operator → 400); update with an older version → 409; delete without the key → 403 |
| | `Roles_decide_who_defines_types_and_who_edits_and_publishes_entries` | only admins define content types; Authors write drafts, can't publish and edit only their own entries; Editors publish anyone's |
| | `The_content_screens_render_cleanly_and_old_content_manager_links_lead_to_pages` | Content Manager, type builder, entry list, new and edit forms pass `AdminUiTests.AssertWellFormed`; a single type redirects to its entry; Pages forms post to `/admin/pages/...`; `/admin/content?selected=` and `/admin/content/{id}/builder` → 301 to `/admin/pages` |
| `CustomEndpointTests.cs` | `One_endpoint_answers_several_methods_with_typed_parameters_and_its_own_token` | an "Items" endpoint with four operations (GET, GET `{id}`, POST, DELETE `{id}`) and one token, plus a separate "Count" endpoint, created on the screen; the list shows the path and an operation badge (`DELETE /{id}`); each endpoint has its own distinct `dnfe_` token: no token, an API token (even with every key in `PermissionKeys.All`) or another endpoint's token → 401; rows, first row (404 when none), one value; query defaults; a value that looks like SQL is a 400 for an `int` and plain text in a body; POST and DELETE change rows and are audited; another method or path 404 |
| | `Public_endpoints_need_no_token_and_disabled_ones_answer_404` | a public endpoint answers without a token and ignores a token that is sent; a failing public endpoint hides the database message, a caller with the endpoint's token sees it; a disabled endpoint 404 |
| | `The_editor_validates_runs_tests_rotates_tokens_and_is_for_super_admins_and_developers` | form errors (name, "GET: write the SQL it runs", missing route parameter); "Add at least one operation"; two operations of one endpoint with the same method and path shape refused ("the endpoint already has GET /{id}"), an operation another endpoint already answers refused, and a second endpoint on the same base path refused ("already uses /api/custom/…"); the test run of one operation (`operation`, `testValues`, `rollback`) answers like the API and rolls back unless told not to; SQL errors shown to the admin; regenerating a token (`POST /admin/api-endpoints/{id}/token`) makes the old one 401 and the new one 200; Editors are refused and see no link, a Developer opens the editor; `endpoint.tested` and `endpoint.token.regenerated` audited |
| `ApiDocsTests.cs` ([API docs](../features/api-docs.md)) | `Only_super_admins_and_developers_open_the_docs` | anonymous → `/login` and an Editor → `/account/denied` on `/admin/api-docs`, `/admin/api-docs/reference/` and the OpenAPI JSON; no API Docs link for the Editor; a Developer's sidebar has API Docs, API Tokens and API Endpoints but no Content Manager, Pages, Content types, Media, Users, Settings or Plugins; the screen frames the reference; the Developer opens `/admin/api-tokens/create` (200) and is denied `/admin/users` |
| | `The_document_lists_the_api_and_the_custom_endpoints_with_their_tokens` | the OpenAPI document has `/api/entries/{type}` and `/api/content/pages`, no `/admin` or `{**path}` paths; the `ApiToken` scheme, required by `/api/entries/{type}` whose description names `entries.read`; a token custom endpoint has its own `Endpoint …` scheme, the same on its `GET` and its `DELETE /{id}` operation, which is under the tag "Items"; a public one no security; the endpoint's `dnfe_` token is in Scalar's HTML, whose CSP carries the page's nonce (`script-src 'self' 'nonce-…'`) while `/admin/api-docs` keeps `script-src 'self';` |
| `WebhookTests.cs` ([webhooks](../features/webhooks.md), [Webhooks screen](../pages/webhooks.md)) - a fake receiver replaces the handler of the `"webhooks"` `HttpClient` | `Webhooks_are_validated_and_their_secret_is_shown_once` | `http://`, a malformed URL, no event, a header without a name and `Content-Type` as a header are refused with their messages; a valid create redirects to `/admin/webhooks/{id}/secret`, which shows a 64-character secret once (then redirects); a case-different duplicate name is refused; the stored secret differs from the shown one (protected); `webhook.created` |
| | `A_test_delivery_is_signed_with_the_secret_and_carries_the_custom_headers` | **Send test delivery** → toast "Test delivered: 200"; the receiver gets the custom `Authorization` header, `X-DotNetForge-Event: webhook.test`, a signature that matches HMAC-SHA256 of the body with the secret, `User-Agent` `DotNetForge-Webhooks…`; one `Success` attempt, `LastDelivery` set, the event in the delivery log |
| | `Media_changes_fire_subscribed_webhooks_and_failures_are_retried_with_backoff` | an upload fires only the `media.create` subscriber (not a `media.delete` one) with a valid signature and `data.fileName`; a `500` answer adds attempt 2 due in about 30 s with the same body, which succeeds when made due (`WebhookDispatcher.RunDueAsync`) with the same event id and `X-DotNetForge-Delivery`; disabling cancels the pending attempt ("Cancelled: the webhook was disabled.") and writes `webhook.disabled` |
| | `Publishing_an_entry_fires_create_and_publish_with_the_entry_data` | saving a new Categories entry with `intent=publish` sends `entry.create` and `entry.publish`; the publish payload has `entity.type` `entry`, `data.contentType` `categories`, `data.published` and the published values |
| | `Changes_made_by_a_webhook_delivery_fire_nothing_and_editors_cant_open_the_screen` | an upload whose request carries `X-DotNetForge-Delivery` creates no attempt and sends nothing (loop guard); an Editor is sent to `/account/denied` and has no Webhooks link |
| `UserAndRoleManagementTests.cs` | `An_admin_creates_a_user_who_can_then_sign_in` | `POST /admin/users/create` → redirect to the edit screen; the new Editor signs in and opens Pages; hashed password, `user.created`; the list links to the form; a case-different duplicate email is refused |
| | `A_custom_role_grants_exactly_its_permissions_and_edits_apply_at_once` | a "Reviewer" role with `Collection types` / `read`: Pages 200 and its link in the sidebar, no Content Manager or Media link (no `Entries` / `Media` grant), Settings denied and absent from the sidebar; adding `Settings` / `read` opens Settings for the signed-in reviewer; `permission.changed` |
| | `A_role_in_use_cannot_be_deleted_and_built_in_roles_never` | Duplicate copies every grant (`Editor (copy)`); delete refused while a user holds the copy, works after the user is deleted (grants removed too); a built-in role is never deleted |
| | `Super_admins_and_your_own_account_are_protected` | the Super Admin can't disable or delete themselves; an Admin can't disable/delete the Super Admin, can't give the Super Admin role, can't change the grants of the Admin role but can save its description |
| | `The_sidebar_lists_roles_once` | exactly one `href="/admin/roles"` in the admin shell |
| `AdminUiTests.cs` ([design system](../architecture/design-system.md#accessibility-wcag-22-aa)) | `Every_screen_renders_without_inline_script_or_style` | every admin screen except API Endpoints and API Docs (incl. all placeholders, `/admin/api-tokens/create`, the Users and Roles create forms, every role's editor, a user's edit form, Pages with a selected page, the Content Manager, Content types and New content type), `/login`, `/` and `/about`: 200, exactly one `<h1>`, `<html lang="en">`, the skip link, no `style=`, `<style>`, inline `<script>` or `on…=` handler |
| | `The_sidebar_shows_only_screens_the_role_may_open` | Editor: Content Manager and Media links present, Plugins/Users/Settings/API Tokens/Content types absent and still → `/account/denied` by URL; admin on `/admin/api-tokens/create`: **API Tokens** has `aria-current="page"`, breadcrumb ends with "Create API token", the **API & Webhooks** group is open and active (Global Settings and Email are not) |
| | `Bulk_delete_removes_the_selected_files_the_user_may_delete` | `POST /admin/media/delete-selected`: an Author deletes none of the admin's files; the admin deletes 2 of 3, toast "Deleted 2 items.", two `media.deleted` entries |
| | `The_dashboard_shows_recent_activity_and_offers_only_permitted_actions` | admin's dashboard shows "Signed in" (recent activity) and **New API token**; an Editor's does not offer **New API token** |
| | `Every_icon_the_ui_uses_exists_in_the_sprite` | every icon named in host and extension views, `AdminNavigation.Tree`, `AuditPresentation` and `MediaPresentation` is a `<symbol>` in `src/DotNetForge.UI/wwwroot/icons.svg` |
| | `Timestamps_read_relative_to_now` (6 cases), `Old_timestamps_show_the_date` | `TimestampTagHelper.Relative`: "just now", "5 min ago", "3 h ago", "yesterday", "4 days ago", "in 5 min"; older than a week → "3 Oct 2026" |
| `ReadOnlyDeploymentTests.cs` | `A_full_session_writes_nothing_into_the_checkout` | snapshot (path, length, last write) of the checkout (content root and repository root) - minus `bin`, `obj`, `.git`, `.vs`, `TestResults`, `node_modules` - is identical before and after a Production session: setup POST, sign-in, enabling the sample admin extension, admin screens, public pages, the extension's tab, upload, download, delete |
| `ExtensionTests.cs` (fixtures, `extensionsPath` = `tests/fixtures/extensions`) | `Plugins_lists_every_extension_disabled_and_explains_what_cannot_run` | fixtures listed, none enabled; the missing DLL explained; enabling it refused ("can't be enabled") |
| | `Extension_endpoints_and_controllers_answer_only_while_enabled` | minimal-API endpoint (settings reach the code) and MVC controller: 404 while disabled, served while enabled, 404 again after disable; audit `plugin.enabled` then `plugin.disabled`; `InstalledExtensions` row `Disabled` |
| | `Enabling_needs_enabled_dependencies_and_disabling_needs_no_enabled_dependents` | "needs ... - enable it first" and "depends on ... - disable it first" refusals |
| | `Widgets_render_on_the_dashboard_and_admin_tabs_in_the_admin_area` | widget card on `/admin`, sidebar tab, `/admin/ext/{id}/raw` only while enabled; a widget's resources under `/admin/ext/...` but not `/ext/...` |
| | `Themes_render_public_pages_and_modules_appear_on_their_pages` | theme view on `/about` and `/news`, module only on its `pages`, public resource served, traversal refused (404), built-in view hosts modules after disabling the theme |
| | `A_storage_connector_takes_over_media_while_enabled` | uploads go to the connector while enabled, to the core storage after disabling |
| | `Audit_events_reach_enabled_extension_handlers` | a failed sign-in reaches the enabled handler in the background (polled up to 10 s) |
| | `Authentication_extensions_sign_in_existing_users_by_email` | "Sign in with Test Kit" only while enabled; the button's post answers the "Continuing to …" page and its meta refresh leads to the provider (`ExternalSignInAsync`); unknown email with sign-ups off → back to sign-in with "No account here uses the email", not signed in; the admin's email → `/admin`, audit `user.login` with details `via Test Kit` |
| `SignInSecurityTests.cs` ([authentication → providers](../features/authentication.md#providers), [MFA](../features/mfa.md)) | `Totp_matches_the_rfc_6238_vectors_and_accepts_a_code_once` | `Totp` against the RFC 6238 SHA-1 vectors; spaces ignored; a used step is refused; Base32 round trip (no host) |
| | `Phone_numbers_need_a_country_code` (4 cases) | `MfaService.NormalizePhone`: `+41 79 …` and `0041…` → `+41791234567`; a national number or too few digits → refused |
| | `Apples_client_secret_is_an_es256_jwt_signed_with_the_team_key` | `OAuthProviderHandler.AppleClientSecret`: header `ES256` + key id, `iss` team, `sub` client, `aud` Apple, signature verifies with the key |
| | `Provider_secrets_are_stored_protected_and_an_unreadable_one_reads_as_missing` | `ProviderSettings.Write` never contains the secret; read back with the same key ring; with another key ring the secret is missing, `unreadable` is set and the provider is not complete |
| | `Google_sign_in_matches_verified_emails_signs_up_new_people_and_refuses_unverified_ones` | against a fake Google (token and user-info endpoints): **Not set up** and the callback URL on Providers, turning on without credentials refused; saved → button on `/login`; the admin's email in upper case signs in (PKCE `code_verifier` and the client secret sent); a new email with sign-ups off refused ("new accounts can't be created"); with sign-ups on an `Authenticated`, confirmed account without a password → `/`; an unverified email refused; turned off → no button, callback 404, secret kept ("A secret is saved.") |
| | `Apple_needs_its_key_and_ids_and_the_page_never_shows_secrets` | wrong Team ID and a non-`.p8` key refused; a valid P-256 key saved, never shown again ("Saved - leave empty to keep it"); "Sign in with Apple" on `/login` |
| | `Authenticator_app_and_recovery_codes_guard_the_sign_in` | an Editor sets up the app (wrong code refused, recovery codes shown once); the password alone leads to `/login/mfa` ("Code from your authenticator app"); the current code signs in (`user.login` "with an authenticator app"); a recovery code (lower case, spaces for the dash) works once ("used already"); audit details `Authenticator app: Authenticator app`, `with an authenticator app`, `with a recovery code` |
| | `Wrong_codes_lock_the_account_like_wrong_passwords` | five wrong codes → `/login`, `LockoutEndUtc` set, `/login/mfa` redirects (the pending sign-in is gone) |
| | `Email_codes_are_set_up_and_used_with_the_sign_in_code_template` | **Email codes** on: the setup code is emailed to the admin (code in subject and body) and adds the method; at sign-in **Send code** emails a new code that signs in |
| | `Sms_codes_go_through_the_sms_sender` | with a recording `ISmsSender` and Twilio settings: a national number refused ("country code"); the texted setup code adds the number; the texted sign-in code signs in |
| | `Required_mfa_keeps_admin_users_on_sign_in_security_until_they_have_a_method_and_admins_can_reset_it` | requiring with every method off refused; with the requirement an Editor without a method is sent to Sign-in security ("Two-step verification is required"); an admin's **Reset two-step verification** removes the methods; the password alone signs in and the admin is required to set one up again |
| | `Passkey_registration_starts_for_app_url_and_refuses_a_forged_credential` | the registration options name `APP_URL`'s host as relying party; a forged credential → "couldn't be verified", nothing stored |
| `ExtensionTests.cs` → `SampleExtensionTests` (repository `extensions/`) | `Contact_form_module_renders_with_its_library_and_stores_messages` | Markdown Kit + Contact Form: Markdown intro rendered on `/contact`, the post redirects back, the message is a JSON object under `ext/contact-form/` in media storage, `contactform.submitted` audited |
| | `Seo_toolkit_serves_a_sitemap_of_live_pages_while_enabled` | `/sitemap.xml` 404 before enabling; then lists live pages (`/about/team`) but not unpublished ones; `/robots.txt` names the sitemap |
| | `Production_refuses_database_paths_inside_the_deployment_directory` (2 cases) | the host's database registration with a production host and a SQLite additional database: relative `Data Source`, missing `DATABASES_REPORTS_CONNECTION_STRING` → `DatabaseConfigurationException` (missing S3 storage is covered by `EnvConfigurationTests`) |
| | `The_cms_database_is_postgresql_and_other_providers_are_only_additional` (4 cases) | a CMS provider `sqlite`, `sqlserver`, `mysql`, `mongodb` → "the CMS database is PostgreSQL" |
| | `Outside_development_the_cms_database_needs_a_connection_string` | an empty CMS connection string with a production host → `DatabaseConfigurationException` naming `DATABASE_CONNECTION_STRING` |
| `DatabaseServiceContractTests.cs` | `Crud_round_trip`, `Ef_core_and_the_service_read_each_others_data`, `Database_generated_keys_are_created_on_insert`, `Text_filters_match_literally`, `Aggregates_group_and_count`, `Unique_violations_are_conflicts`, `Transactions_commit_or_roll_back`, `Large_results_can_be_streamed`, `Seeded_cms_data_is_queryable_by_entity_name`, `Composite_keys_are_read_and_filtered_like_other_fields`, `Unknown_names_and_connection_status_are_reported` | `IDatabaseService` on the CMS database (PostgreSQL): the provider contract ([providers](../database/providers.md#testing)) |
| | `AdditionalDatabaseTests.Commands_run_against_a_named_database_without_a_model` | a `DATABASES_REPORTS_*` SQLite database: unmapped names, `SELECT *`, aggregates, key-less `DeleteOne` refused, identifier validation |
| `DatabaseCredentialTests.cs` | `PostgreSql_…`, `SqlServer_…`, `MySql_reports_wrong_credentials` | wrong password on an additional database → `DatabaseAuthenticationException` (skipped unless the server variable is set) |
| `AdditionalDatabaseProviderTests.cs` | `Sqlite`, `PostgreSql`, `SqlServer`, `MySql`, `MongoDb` (the last three `[AdditionalDatabaseFact]`) | the provider as additional database `extra` next to the PostgreSQL CMS database: `InsertMany`, `ListCollectionsAsync`, `Find`; a custom endpoint whose SQL runs there (offered as "extra (<provider>)", stored `Database = "extra"`, answers with typed `@kind`) - not MongoDB; the Data Management export of it (`dotnetforge-database-export` manifest, the events table as JSON, a record with note "Database extra") |
| | `Endpoints_refuse_unknown_databases_and_databases_without_sql` | a MongoDB additional database is not offered in the editor; saving an endpoint on it ("which has no SQL") or on an unknown database ("There is no database 'missing'") is refused and nothing is stored |
| `PageServiceTests.cs` | `Slugifies_and_applies_valid_input`, `Rejects_duplicate_slug_under_the_same_parent_including_root`, `Rejects_a_parent_that_creates_a_cycle`, `Rejects_a_second_dynamic_segment_under_one_parent`, `Rejects_overlong_fields_before_saving`, `Reorder_rejects_moves_that_duplicate_a_slug`, `Reorder_rejects_pages_that_do_not_exist`, `Delete_reparents_children_or_refuses_when_slugs_would_clash` | `PageService` rules against a fresh PostgreSQL database (`dnf_pages_<guid>`, migrated, no host) |

Not covered: the content of the Audit Logs and API Tokens lists (every admin screen is rendered by
`AdminUiTests` for structure and CSP only; Plugins also by `ExtensionTests`), `/setup`, `/account/denied` and
`/error` markup, client-side behaviour in `dnf.js`, `pages.js`, `entries.js`, `content-types.js`, `endpoints.js`, Scalar and the builder's editor UI (no browser tests), the weather, S3, Discord and SMS samples (they
need network or credentials), Settings saves, API endpoints other than page creation and `/api/entries`, public dynamic
routes other than content-list detail pages, account lockout, the upload size limit.

### Writing a signed-in integration test

```csharp
using var factory = new DotNetForgeWebFactory();
await factory.InstallAsync();
await factory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
var editor = await factory.SignInAsync("editor@example.com", "Edit0rPass1");

var response = await DotNetForgeWebFactory.PostFormAsync(editor, "/admin/pages", "/admin/pages/create",
    new Dictionary<string, string>());

Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
await factory.WithDbAsync(async db => Assert.True(await db.Pages.AnyAsync(p => p.Title == "Untitled page")));
```

Never write into the repository from a test: use `factory.StoragePath`, the factory's temp directory or
`Path.GetTempPath()` - `ReadOnlyDeploymentTests` would catch host code that does.

## UI tests

`tests/DotNetForge.UiTests`: browser tests of the [DotNetForge UI library](../ui/README.md). `UiHost` (one per test
collection) starts the app with `DotNetForgeWebFactory.UseKestrel(0)` - a real port, because the browser needs a
socket - installs it, launches headless Chromium and signs in once through `/login`; every test opens a page with that
session in a fixed viewport (1280x900, or 390 wide for phones), colour scheme, `en-US`, UTC and reduced motion. A page
opened with `touch: true` is a touch device (`hover: none`, `pointer: coarse`, tap events). `MobileHost` is a second host
with its own database and rows, so the mobile tests' data never reaches the other tests; the assembly runs one host at a
time.

| File | Covers |
| --- | --- |
| `DocsVisualTests.cs` | the live preview of every example on every page of the [UI docs](../pages/ui-docs.md) (`[data-doc-preview]`, from `UiDocsCatalog`) in light and dark, and the table example stacked on a phone, compared with `Baselines/<os>/*.png` |
| `DocsTests.cs` | every docs page renders (h1, examples, API section), the example tabs show the Razor and HTML without changing the URL, On this page and the navigation search, the Copy menu (the clipboard holds the page / every page as Markdown), the account-menu link |
| `AccessibilityTests.cs` | axe-core (WCAG 2.0-2.2 A/AA tags) on the showcase, the main admin screens and `/login`, light and dark: no `serious` or `critical` violation |
| `BehaviourTests.cs` | dnf.js in the browser: dropdown placement and arrow keys, the confirmation dialog and toasts, tabs, a collection (search, count, selection, pager), the theme switch and subtree themes, the three customization levels, no console error or failed request on the main screens |
| `ExtensionUiTests.cs` | the bundled Audit Dashboard admin extension in its frame: dnf.css applies, tokens follow the theme, the root is `.dnf-scope[data-dnf-extension]` |
| `MobileTests.cs` | the admin by touch on a small phone (320x568), a phone (390x844) and a tablet in both orientations (820x1180, 1180x820). `Every_screen_works` crawls from the sidebar through two examples of each route shape and opens each screen's tabs, accordions, menus and dialogs: no sideways scrolling, no control off screen, covered or under 24px (plain text links are exempt), no text on text or on a control, no cut-off content, and every control the desktop has. Task tests do the rest with taps: the drawer, table sorting, filtering and paging, creating and deleting a user, bulk selection, reordering entries, pages and content-type fields, moving a page and media through the move dialog, media upload and selection, the public screens and the page builder, and (`MobileTests.Documents.cs`) Word, Excel and PowerPoint files in the media preview, also on the desktop. `MobileTests.Stability.cs` holds `dnf.js` back as a slow network would: no crawled screen may move more than 0.03 while loading, a saved view, page size and the toolbars are in place before the script arrives, and switching views keeps the first item in sight on the screen. `The_audit_notices_what_it_looks_for` plants faults, so a clean run means something. A failing screen's screenshot is in `tests/DotNetForge.UiTests/TestResults/mobile/` (git-ignored). Monaco and the UI docs are not audited; dragging on the page builder's canvas is not tested |

One-time setup per machine (Chromium, about 170 MB, into Playwright's cache - not the repository):

```bash
dotnet build tests/DotNetForge.UiTests
pwsh tests/DotNetForge.UiTests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test tests/DotNetForge.UiTests          # needs PostgreSQL like the integration tests
```

Screenshots (`Snapshots.cs`): a baseline per operating system (fonts render differently) in
`tests/DotNetForge.UiTests/Baselines/<os>/`, which is git-ignored - every machine keeps its own. A missing baseline is
recorded and the check passes, so the first run on a machine (and every CI run, which starts without baselines) only
records; later runs compare against them. `DNF_UI_UPDATE_SNAPSHOTS=1` re-records all of them after an intended visual
change (look at the new images first). A screenshot matches when at most 0.002 % of its pixels - and never fewer than 12 - differ
by more than 24 in a colour channel; on a mismatch the actual image and a red diff are written to
`tests/DotNetForge.UiTests/TestResults/ui-snapshots/` (git-ignored). Spinners and transitions are frozen
(`ScreenshotAnimations.Disabled`); the docs examples use fixed or relative-to-now dates, so the pixels do not change by day.

`tests/DotNetForge.IntegrationTests/SessionTests.cs` covers the [sessions](../features/authentication.md#sessions): role
changes on an open session, sign-out with a copied cookie, expiry, disabling, password changes and the cookie's flags;
`StaticAssetTests.cs` the caching and compression.

## JavaScript

ESLint checks every script of the CMS, the UI library and the extensions, apart from the bundled Monaco
(`eslint.config.mjs`): `npm ci` once, then `npm run lint`, or `npm run lint:fix`. It is development tooling: Node.js is
optional and nothing from npm reaches the app. The page builder has its own [fixtures](../features/page-builder.md).

## What to test when you change something

| Change | Add |
| --- | --- |
| Pure rule (validator, policy, matrix, configuration) | unit test in `DotNetForge.Tests` |
| `PageService` rule | test in `PageServiceTests` (a fresh PostgreSQL database) |
| Structured content rule (field type, option, value check) | `ContentSchemaTests`; service, screen or API behaviour in `StructuredContentTests`; a content-list rendering change also as a case in `tests/js/content-list-fixtures.mjs` |
| Storage provider | run `FileStorageContract` against it (unit test class like `LocalFileStorageTests`; live tests behind an `[S3Fact]`-style attribute) |
| New screen / endpoint / auth rule | integration test with `CreateNoRedirectClient` / `SignInAsync` asserting status and redirect |
| New API endpoint | 401 without token, 403 without the key, 200 with it |
| Anything that touches files at runtime | keep `ReadOnlyDeploymentTests` green; extend its session if the new path isn't exercised |
| Extension host, a slot, the SDK | `ExtensionTests` with a fixture under `tests/fixtures/extensions/` (code goes in `test-kit`); loading rules in `ExtensionRuntimeTests`; manifest rules in `ManifestValidationTests` |
| A sample extension | a `SampleExtensionTests` case if it runs offline; add its `.csproj` as a build-only reference |
| Schema change | run the integration suite (PostgreSQL migrations); `dotnet ef migrations has-pending-model-changes --context PostgreSqlDbContext` reports none |
| Additional-database behaviour (`IDatabaseService`, custom endpoints, export) | `AdditionalDatabaseProviderTests`, on every server you can start (`DNF_TEST_SQLSERVER`, `_MYSQL`, `_MONGODB`) |
| New database provider | [adding a provider → Tests](../database/adding-a-provider.md#4-tests) |
| UI library component, token or behaviour (`src/DotNetForge.UI`) | the component's page in the [UI docs](../pages/ui-docs.md) (examples, styling reference, doc comments for its API table); a tag helper case in `UI/DnfTagHelperTests`; a `BehaviourTests` case for new dnf.js behaviour; re-record the affected baselines on purpose |
| Markup of a screen | `UI/UiSourceTests` stays green (no Forge class, every `dnf-` class defined, no `!important`) |
| Bug fix | a regression test that fails without the fix |

## CI

`.github/workflows/ci.yml` on push/PR to `main`/`master` (and manual dispatch):

- **build-and-test** on `ubuntu-latest`, `windows-latest`, `macos-latest`: `dotnet tool restore`,
  `dotnet build DotNetForge.slnx --configuration Release`, the **unit** tests in Release (live S3 tests skipped). The
  integration tests need PostgreSQL, so they run in the Linux jobs below.
- **ui-tests** (`UI tests (Chromium)`) on Ubuntu: a `postgres:17-alpine` service, `playwright.ps1 install --with-deps
  chromium`, then `dotnet test tests/DotNetForge.UiTests`; the screenshots it records are uploaded as an artifact (it has
  no baselines to compare with, so the visual tests only record there - accessibility and behaviour tests run in full).
- **format** on Ubuntu: `dotnet format <project> --verify-no-changes --severity error` for the host, every `src/*`
  and every `tests/*` project.
- **lint-js** (`JavaScript lint`) on Ubuntu: `npm ci`, `npm run lint` ([JavaScript](#javascript)).
- **postgres-and-s3** (`Integration tests (PostgreSQL) + S3 storage`) on Ubuntu: a `postgres:17-alpine` service and
  a SeaweedFS container (`chrislusf/seaweedfs server -s3`, bucket `dnf-test`); runs the unit tests **including** the
  live S3 tests (`DNF_TEST_S3_*` set) and the whole integration suite on PostgreSQL (`DNF_TEST_POSTGRES` set).
- **additional-databases** (`Additional database (<provider>)`): a matrix that starts SQL Server 2022, MySQL 8.4 or
  MongoDB 8.0 (single-node replica set) with `docker run`, next to a `postgres:17-alpine` service for the CMS database,
  and runs `dotnet test tests/DotNetForge.IntegrationTests --filter "FullyQualifiedName~AdditionalDatabase|FullyQualifiedName~DatabaseCredential"`
  with only that entry's `DNF_TEST_SQLSERVER`, `DNF_TEST_MYSQL` or `DNF_TEST_MONGODB` set. The MongoDB entry runs on
  `ubuntu-24.04`: MongoDB 8.x exits at startup on Linux kernels 6.19 and newer
  ([SERVER-121912](https://jira.mongodb.org/browse/SERVER-121912)), which the Ubuntu 26.04 `ubuntu-latest` image has.
- **read-only-container** (`Read-only container`) on Ubuntu: `docker compose -f docker/compose.app.yml up -d --build`
  (the production image, read-only root filesystem with a `/tmp` tmpfs, PostgreSQL, RustFS and Mailpit); waits for
  `/health`, requests `/setup`, and fails if `touch /app/probe` succeeds inside the app container
  ([deployment](deployment.md#docker)).

`.github/dependabot.yml` opens weekly NuGet (grouped `Microsoft.*`/`Npgsql.*`), npm and GitHub Actions updates.

### Line endings

`.gitattributes` keeps text files LF in the repository and on every machine, as `.editorconfig` asks, so
`dotnet format <project> --verify-no-changes` gives the same answer on Windows as in CI.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

**Unit test coverage** (`DotNetForge.Tests`):

| Area | Must verify | Today |
| --- | --- | --- |
| Core services | business logic of each core service in isolation | `InstallationService`, `EnvConfigurationLoader`, `ExtensionLoader`, `ExtensionRuntime`, `LocalFileStorage` / `S3FileStorage` |
| Permissions | RBAC checks for every permission area; least privilege | a few role/area pairs in `PermissionTests` |
| Content manager | page CRUD, tree resolution, page types, scheduling, draft/published state | `PageService` rules (slug, cycle, dynamic segment, lengths, reorder, delete) in `PageServiceTests` - integration project, a fresh PostgreSQL database; no scheduling or liveness tests |
| Media validation | file-type allowlist, max upload size, blocking unsafe uploads | integration only: allowlist and active types rejected (`MediaTests`); per-kind size limits (`MediaOptimizationTests`) ([media storage](../features/media-storage.md#upload-limits)) |
| API token generation | secure creation, hashing before storage, expiration, permission scoping | ✔ creation + hash verification (`ApiTokenFactoryTests`); expiration and scoping untested |
| Manifest validation | required fields; rejection of invalid, unsafe or incompatible manifests | ✔ required fields, type, semver, permissions, `assembly` path; "unsafe" and "incompatible" do not exist as checks |
| Import/export | provider-aware export and import; import-file validation | `DataManagementTests` (on the configured test database), `TransferRulesTests` ([Data Management](../pages/data-management.md)) |

**Integration test coverage** (`DotNetForge.IntegrationTests`):

| Flow | Must verify | Today |
| --- | --- | --- |
| Setup | not-installed detection, setup screen, first user created with `Super Admin`, CMS marked installed | gate and screen ✔; `ReadOnlyDeploymentTests` POSTs `/setup` (302) and signs in, but the `Super Admin` role and installed flag are not asserted |
| Authentication | login, failed login, lockout, provider-based sign-in | login ✔ (`SignInAsync`, audit, external `returnUrl`), rate limit ✔, disabled-user session end ✔, provider-based sign-in ✔ (extension fixture); failed login only via the rate-limit test and the external unknown-email case; lockout ✘ |
| API endpoints | token-authenticated requests, permission enforcement | `401` without a token; one token-authenticated `POST /api/content/pages` with `content.create`; no 403 tests |

- Unit test files mirror the namespaces of the code under test (a folder per service or validator); today all files
  sit flat in the project root.
- Integration tests may also run against a disposable PostgreSQL instance (✔ `DNF_TEST_POSTGRES`, CI job
  `postgres-and-s3`).
- Code coverage collectable with `dotnet test --collect:"XPlat Code Coverage"` (needs `coverlet.collector`, not
  referenced today).
- **Frontend linting:** an ESLint configuration wherever JavaScript/TypeScript exists (`src/DotNetForge.Web/wwwroot/js`, admin extension
  and theme assets), runnable as `npm run lint` locally and in CI, failing the build on errors; JS/TS formatting
  enforced alongside it.

### Rules and validation

- Unit tests are fast and isolated: database and network replaced by fakes, stubs or mocks.
- Integration tests boot the real host and use a disposable database; never a pre-seeded or shared environment.
- One test framework and assertion style across both projects (✔ xUnit only).
- Tests run on Windows, macOS and Linux, in CI on every push and pull request (✔ `ci.yml`).
- ✔ The whole suite runs with a bare `dotnet test` from the root (`DotNetForge.slnx`); one project runs by path
  (`dotnet test tests/DotNetForge.Tests`).
- Naming: `PascalCase` for types, methods and public members, `camelCase` for locals and parameters, interfaces
  prefixed `I`; descriptive names, no cryptic abbreviations. `.editorconfig` has no `dotnet_naming_rule` entries to
  enforce this.
- Dependencies: central, pinned versions (✔ `Directory.Packages.props`); prefer the framework before adding a
  package.
- Extensions validate their manifests and permissions exactly like the core.

### Acceptance criteria

Unit test coverage:

- [ ] Unit tests exist for core services (only `InstallationService`, configuration loading, `ExtensionLoader`,
  `ExtensionRuntime` and the storage providers).
- [ ] Unit tests exist for permissions across all permission areas.
- [ ] Unit tests exist for the content manager (`PageService` rules ✔ in `PageServiceTests`, but in the integration
  project against a fresh PostgreSQL database; no CRUD or scheduling tests).
- [ ] Unit tests exist for media validation (file type and size, unsafe-upload blocking) - file types are covered by
  the integration test `MediaTests.Active_or_unknown_file_types_are_rejected`, size limits by
  `MediaOptimizationTests.Upload_limits_are_per_kind_and_validated`; no unit tests.
- [x] Unit tests exist for API token generation (secure generation and hashed storage) (`ApiTokenFactoryTests`).
- [ ] Unit tests exist for manifest validation, asserting the eight required fields **and** rejecting
  invalid/unsafe/incompatible manifests (required fields ✔ in `ManifestValidationTests`; no unsafe/incompatible checks).
- [x] Unit tests exist for webhook signing (`WebhookSignerTests` in `SecurityPrimitiveTests.cs`).
- [ ] Unit tests exist for import/export.

Integration test coverage:

- [ ] Integration tests cover the first-time setup flow (first admin created with `Super Admin`, CMS marked installed);
  `ReadOnlyDeploymentTests` completes setup through `POST /setup` but asserts neither the role nor the flag.
- [ ] Integration tests cover authentication (login, failed login, lockout) - login ✔ (`SecurityTests`,
  `SignInAsync`); no explicit failed-login or lockout test.
- [ ] Integration tests cover API endpoints (token auth, permission enforcement) - token auth ✔
  (`SecurityTests.Api_page_creation_applies_the_content_rules_and_is_audited`,
  `CmsIntegrationTests.Api_requires_a_token`); no permission-denied test.

Structure, execution and quality:

- [x] `tests/` contains the `DotNetForge.Tests` and `DotNetForge.IntegrationTests` projects.
- [x] At least one example unit test for manifest validation and one for a permission check (`ManifestValidationTests`,
  `PermissionTests`).
- [ ] `dotnet test` runs the full suite successfully on Windows, macOS and Linux (CI runs both projects on all three
  ✔; there is no single root `dotnet test`).
- [x] Integration tests use a disposable database and never touch development or production data
  (`DotNetForgeWebFactory`: a per-factory PostgreSQL database dropped on dispose, temp media
  folder).
- [ ] An ESLint configuration is present and runnable wherever JavaScript or TypeScript is used.
- [x] Automated formatting rules exist and are verified in CI (`.editorconfig`, `ci.yml` format job with
  `dotnet format --verify-no-changes`).
- [ ] Naming conventions are documented and consistently applied (documented in the
  [development guide](development.md#conventions) only partly; not enforced).
- [x] Dependencies are managed deliberately with no unnecessary packages (`Directory.Packages.props`, Dependabot).
- [x] The structure is clean and generated files and secrets are excluded via `.gitignore`.
