---
name: verify
description: >-
  Verifies a DotNetForge CMS change before reporting it done: dotnet build, unit and integration tests, the CI
  dotnet format check, and - for UI, routing or auth changes - running the app against a throwaway PostgreSQL
  database, completing setup and probing routes with curl. Use after any code change, when asked to check, test, run or
  smoke-test the app, or before writing release notes.
---

# Verify a change

Run from the repository root. Report real output (counts, failures), never assumed results.

## 1. Build and test

```bash
docker compose -f docker/databases/postgres.yml up -d   # the integration tests need PostgreSQL (localhost:5432)
dotnet build          # the solution, DotNetForge.slnx
dotnet test           # both test projects (or: dotnet test tests/DotNetForge.Tests, tests/DotNetForge.IntegrationTests)
```

Baseline (October 2026): 0 errors, 0 warnings; 758 unit tests pass (+5 live-S3 tests skipped unless `DNF_TEST_S3_*`
is set) and 98 integration tests pass (+3 credential tests skipped unless their database server is set).
Page builder JavaScript: `node tests/js/page-builder-fixtures.mjs` → 84/84 fixtures match (run it after changing
anything in `wwwroot/js/builder/core`, `renderer` or `components`, or the schema). Integration tests need no `.env` (the factory sets environment variables, creates
a fresh PostgreSQL database per factory on the `DNF_TEST_POSTGRES` server - default `localhost:5432`,
`postgres`/`postgres` - and uses a temp directory) and must not run in parallel.

Other backends (recommended when touching additional databases or storage):

```bash
docker compose -f docker/databases/mysql.yml -f docker/s3/seaweedfs.yml up -d
DNF_TEST_MYSQL="Server=localhost;Port=3306;Uid=root;Pwd=dotnetforge" dotnet test tests/DotNetForge.IntegrationTests \
  --filter "FullyQualifiedName~AdditionalDatabase|FullyQualifiedName~DatabaseCredential"   # likewise DNF_TEST_SQLSERVER, DNF_TEST_MONGODB
DNF_TEST_S3_SERVICE_URL=http://localhost:8333 DNF_TEST_S3_BUCKET=dotnetforge DNF_TEST_S3_ACCESS_KEY_ID=dev \
  DNF_TEST_S3_SECRET_ACCESS_KEY=dev DNF_TEST_S3_FORCE_PATH_STYLE=true dotnet test tests/DotNetForge.Tests
```

## 2. Style (what CI runs)

```bash
for p in src/*/*.csproj tests/*/*.csproj; do dotnet format "$p" --verify-no-changes --severity error; done
npm ci && npm run lint   # JavaScript
```

## 3. Smoke test the running app (UI / routing / auth changes)

Use a throwaway PostgreSQL database (the migrations create it on start) and a free port. Without `STORAGE_S3_*`,
uploads land in the repository's git-ignored `storage/media` - delete what you upload:

```bash
export DATABASE_CONNECTION_STRING="Host=localhost;Port=5432;Database=dnf_verify;Username=postgres;Password=postgres;GSS Encryption Mode=Disable" \
       ASPNETCORE_URLS=http://localhost:5077 ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/DotNetForge.Web --no-build --no-launch-profile &   # after dotnet build; honours ASPNETCORE_URLS
curl -s http://localhost:5077/health                 # {"status":"ok",...}
```

Read-only check of the real image (what CI's `read-only-container` job does):

```bash
docker compose -f docker/compose.app.yml up -d --build     # Production, read-only, PostgreSQL + RustFS + Mailpit
curl -s http://localhost:8080/health && curl -s -o /dev/null -w '%{http_code}\n' http://localhost:8080/setup
docker compose -f docker/compose.app.yml exec -T app sh -c 'touch /app/probe'   # must fail: /app is read-only
docker compose -f docker/compose.app.yml down -v
```

On Git Bash for Windows prefix `docker run` with `MSYS_NO_PATHCONV=1` when arguments contain `/paths`.

Complete setup with antiforgery (cookie jar + hidden field):

```bash
J=<scratch>/jar.txt; B=http://localhost:5077
T=$(curl -s -c $J -b $J $B/setup | grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' | sed 's/.*value="//;s/"$//')
curl -s -b $J -c $J -o /dev/null -w '%{http_code} %{redirect_url}\n' -X POST $B/setup \
  --data-urlencode "__RequestVerificationToken=$T" -d "Email=admin@example.com&Password=Sup3rSecret&ConfirmPassword=Sup3rSecret"
# expect: 302 .../admin ; the jar now holds the dnf.auth cookie
curl -s -b $J -o /dev/null -w '%{http_code}\n' $B/admin/content
```

Use `ASPNETCORE_ENVIRONMENT=Production` when checking error handling or the enforced CSP (Development shows the
exception page and sends CSP report-only). Production also requires an explicit `DATABASE_CONNECTION_STRING`
and `STORAGE_S3_*` settings (the compose S3 server works), and the auth cookie is Secure-only (use HTTPS or `X-Forwarded-Proto: https` with
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`).
Stop the process afterwards (PowerShell:
`Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | ? CommandLine -like '*DotNetForge.Web.dll*' | % { Stop-Process -Id $_.ProcessId }`)
and drop the scratch database
(`docker compose -f docker/databases/postgres.yml exec postgres dropdb -U postgres dnf_verify`).

## 4. Docs

If behaviour, routes, permissions, tables or configuration changed, confirm the owning `.docs/` document was updated
(see the mapping table in the **documentation** skill). Run the link check:

```bash
python .claude/skills/verify/check_links.py
```

## Report

What was run, the counts, anything that failed or was skipped and why.
