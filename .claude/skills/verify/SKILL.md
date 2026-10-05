---
name: verify
description: >-
  Verifies a DotNetForge CMS change before reporting it done: dotnet build, unit and integration tests, the CI
  dotnet format check, and - for UI, routing or auth changes - running the app against a throwaway SQLite database,
  completing setup and probing routes with curl. Use after any code change, when asked to check, test, run or
  smoke-test the app, or before writing release notes.
---

# Verify a change

Run from the repository root. Report real output (counts, failures), never assumed results.

## 1. Build and test

```bash
dotnet build          # the solution, DotNetForge.slnx
dotnet test           # both test projects (or: dotnet test tests/DotNetForge.Tests, tests/DotNetForge.IntegrationTests)
```

Baseline (October 2026): 0 errors, 0 warnings; 99 unit tests pass (+5 live-S3 tests skipped unless `DNF_TEST_S3_*`
is set) and 40 integration tests pass. Integration tests need no `.env` (the factory sets environment variables and
uses a temp directory) and must not run in parallel.

Production-like backends (recommended when touching persistence or storage):

```bash
docker compose -f docker/compose.dev.yml up -d
DNF_TEST_POSTGRES="Host=localhost;Port=5432;Username=postgres;Password=postgres" dotnet test tests/DotNetForge.IntegrationTests
DNF_TEST_S3_SERVICE_URL=http://localhost:8333 DNF_TEST_S3_BUCKET=dotnetforge DNF_TEST_S3_ACCESS_KEY_ID=dev \
  DNF_TEST_S3_SECRET_ACCESS_KEY=dev DNF_TEST_S3_FORCE_PATH_STYLE=true dotnet test tests/DotNetForge.Tests
```

## 2. Style (what CI runs)

```bash
for p in src/*/*.csproj tests/*/*.csproj; do dotnet format "$p" --verify-no-changes --severity error; done
```

## 3. Smoke test the running app (UI / routing / auth changes)

Use a throwaway database and storage folder so the developer's `storage/` is untouched, and a free port:

```bash
export DATABASE_CONNECTION_STRING="Data Source=<scratch>/verify.db" \
       STORAGE_LOCAL_PATH=<scratch>/media ASPNETCORE_URLS=http://localhost:5077 ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/DotNetForge.Web --no-build --no-launch-profile &   # after dotnet build; honours ASPNETCORE_URLS
curl -s http://localhost:5077/health                 # {"status":"ok",...}
```

Read-only check of the real image (what CI's `read-only-container` job does):

```bash
docker build -f docker/Dockerfile -t dnf-check .
docker run -d --name dnf-check --read-only --tmpfs /tmp -p 5078:8080 \
  -e "DATABASE_CONNECTION_STRING=Data Source=/tmp/cms.db" -e STORAGE_LOCAL_PATH=/tmp/media dnf-check
curl -s http://localhost:5078/health && docker diff dnf-check   # diff must be empty
docker rm -f dnf-check
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
exception page and sends CSP report-only). Production also requires explicit absolute `DATABASE_CONNECTION_STRING`
and `STORAGE_LOCAL_PATH`, and the auth cookie is Secure-only (use HTTPS or `X-Forwarded-Proto: https` with
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`).
Stop the process afterwards (PowerShell:
`Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | ? CommandLine -like '*DotNetForge.Web.dll*' | % { Stop-Process -Id $_.ProcessId }`)
and delete the scratch DB.

## 4. Docs

If behaviour, routes, permissions, tables or configuration changed, confirm the owning `.docs/` document was updated
(see the mapping table in the **documentation** skill). Run the link check:

```bash
python .claude/skills/verify/check_links.py
```

## Report

What was run, the counts, anything that failed or was skipped and why.
