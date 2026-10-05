# Installation & First-Run Setup

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

This module defines how DotNetForge CMS is configured via `.env`, how the application detects whether it has already been installed, and the first-run setup wizard that creates the first Super Admin and marks the CMS as installed.

## Purpose

DotNetForge CMS is configured through environment variables and self-installs on first run. On startup the application determines whether it has already been installed. If it has not, the system redirects every request to a one-time setup wizard that collects the first administrator's details, creates the first user, assigns that user the **Super Admin** role, marks the CMS as installed, and redirects to the admin dashboard. Once installation is complete, the setup wizard is permanently blocked.

This file owns the `.env` configuration contract and the first-time installation flow. Concerns shared with other modules are cross-linked rather than duplicated: see [User Roles & Permissions](user_roles_permissions.md) for the role model, [Security](security.md) for password hashing, secrets handling, and HTTPS, [Users](user_roles_permissions.md) for the user record, and [Database & Transfer](transfer_updates.md) for provider support and connection details.

## Main Features

- **Environment-based configuration** via a `.env` file copied from `.env.example`, never committed to Git (see [Security](security.md)).
- **Two supported database providers**: `sqlite` (default) and `postgresql`.
- **Install-detection logic** that runs on every startup and gates the entire application until setup completes.
- **One-time setup wizard** that collects the first administrator's name (optional), email, password, and password confirmation.
- **First Super Admin creation**: the first user is created and assigned the `Super Admin` role.
- **Installed-state flag** that is set atomically when setup succeeds and that disables the wizard permanently.
- **Automatic redirect** to the admin dashboard after a successful install.
- **Defensive handling** of missing or invalid configuration, database connection failures, and attempts to re-run setup.

## Configuration / `.env`

The CMS must ship a `.env.example` file. The operator copies `.env.example` to `.env` and configures it before first run. Secrets in `.env` must never be committed to Git (the `.gitignore` must exclude `.env`).

### `.env.example` contents

```env
DATABASE_PROVIDER=sqlite
DATABASE_CONNECTION_STRING=
APP_NAME=DotNetForge CMS
APP_URL=http://localhost:5000
```

### Field reference

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `DATABASE_PROVIDER` | enum (`sqlite` \| `postgresql`) | Yes | Selects the active database provider. Defaults to `sqlite`. |
| `DATABASE_CONNECTION_STRING` | string | Conditional | Connection string for the chosen provider. May be empty for `sqlite` (a sensible local default file is used); required and non-empty for `postgresql`. See [Database & Transfer](transfer_updates.md). |
| `APP_NAME` | string | Yes | Display name of the application (e.g. shown in the admin UI and emails). Defaults to `DotNetForge CMS`. |
| `APP_URL` | string (URL) | Yes | Public base URL of the application, e.g. `http://localhost:5000`. Used for absolute link and redirect generation. |

### Supported database providers

```env
DATABASE_PROVIDER=sqlite
DATABASE_PROVIDER=postgresql
```

Only `sqlite` and `postgresql` are valid values for `DATABASE_PROVIDER`. Any other value is an invalid configuration and must abort startup with a clear error (see Edge Cases). Provider-specific connection details and import/export are owned by [Database & Transfer](transfer_updates.md).

## User Flows

### Flow: First-time configuration

1. The operator copies `.env.example` to `.env`.
2. The operator sets `DATABASE_PROVIDER` to `sqlite` or `postgresql`.
3. For `postgresql`, the operator sets a non-empty `DATABASE_CONNECTION_STRING`. For `sqlite`, the connection string may be left empty to use the default local database file.
4. The operator optionally sets `APP_NAME` and `APP_URL`.
5. The operator starts the application.

### Flow: Install detection on startup

1. The application loads and validates `.env`.
2. If `.env` is missing or invalid, the application aborts startup with a descriptive error (see Edge Cases).
3. The application connects to the configured database and applies pending migrations.
4. The application reads the installed-state flag.
5. If the CMS is **already installed**, normal request handling proceeds and the setup wizard is unreachable.
6. If the CMS is **not installed**, every incoming request (except setup wizard routes and static assets) is redirected to the setup/registration page.

### Flow: Setup wizard (create first Super Admin)

1. An unauthenticated visitor reaches the application before installation and is redirected to the setup page.
2. The setup page presents the following fields:
   - **First name** - optional
   - **Last name** - optional
   - **Email** - required
   - **Password** - required
   - **Confirm password** - required
3. The visitor submits the form.
4. The system validates all inputs (see Validation Rules). On any failure, the form is redisplayed with field-level errors and no user is created.
5. On success, the system:
   1. Creates the first user with the supplied name (if any), email, and securely hashed password (hashing is defined in [Security](security.md)).
   2. Assigns the user to the **Super Admin** role (see [User Roles & Permissions](user_roles_permissions.md)).
   3. Marks the CMS as installed by setting the installed-state flag atomically, within the same transaction as user creation.
   4. Optionally signs the new Super Admin in.
   5. Redirects the user to the admin dashboard.
6. After this point, the setup wizard is permanently blocked for all subsequent requests.

### Flow: Attempt to re-run setup after install

1. A user navigates directly to a setup wizard route on an already-installed CMS.
2. The system detects the installed-state flag is set.
3. The system blocks setup and redirects to the admin dashboard (if authenticated) or the login page (if not), without exposing the wizard.

## Role & Permission Rules

The setup wizard runs before any user or role assignment exists, so it is intentionally accessible without authentication - but only while the CMS is uninstalled. Once installed, only the role model in [User Roles & Permissions](user_roles_permissions.md) applies.

| Action | Allowed actor | Notes |
| --- | --- | --- |
| Edit `.env` configuration | Operator / deployer (filesystem access) | Not an in-app permission; managed outside the CMS. |
| Run the setup wizard | Any visitor (unauthenticated) | Only while the CMS is **not** installed. |
| Create the first user / first Super Admin | The setup wizard, once, automatically | The first user is always assigned `Super Admin`. |
| Re-run / access setup after install | Nobody | Blocked for everyone, including `Super Admin`. |
| Create additional users/roles after install | `Super Admin`, `Admin` (per permissions) | Handled by [Users](user_roles_permissions.md) and [User Roles & Permissions](user_roles_permissions.md), not the wizard. |

The canonical roles, most-privileged to least, are: `Super Admin`, `Admin`, `Editor`, `Author`, `Authenticated`, `Public`.

## Validation Rules

### Configuration (`.env`)

- `.env` must exist and be parseable; otherwise startup aborts.
- `DATABASE_PROVIDER` must be exactly `sqlite` or `postgresql`. Any other value is invalid.
- `DATABASE_CONNECTION_STRING` must be non-empty when `DATABASE_PROVIDER=postgresql`. It may be empty when `DATABASE_PROVIDER=sqlite`.
- `APP_URL`, when present, must be a valid absolute URL.

### Setup wizard form

| Field | Type | Required | Constraints |
| --- | --- | --- | --- |
| First name | string | No | Optional; trimmed; reasonable max length. |
| Last name | string | No | Optional; trimmed; reasonable max length. |
| Email | string (email) | Yes | Must be a valid email address; stored as the first user's login. |
| Password | string | Yes | Must meet password strength rules; must not be weak (see below). |
| Confirm password | string | Yes | Must exactly match Password. |

Additional rules:

- **Password and Confirm password must match exactly.** A mismatch is rejected with a field-level error and no user is created.
- **Weak passwords must be rejected.** Enforce a minimum length and complexity policy as defined in [Security](security.md); reject common/breached passwords where feasible.
- The password is never stored in plaintext; it is stored only as a secure hash (see [Security](security.md)).
- All inputs are validated server-side regardless of any client-side validation.
- The wizard runs to completion exactly once; the first created user is always assigned the `Super Admin` role.

## Edge Cases

- **App already installed (setup blocked).** If the installed-state flag is set, every setup wizard route is blocked. Authenticated users are redirected to the admin dashboard; unauthenticated users to the login page. The wizard must never create a second "first" Super Admin.
- **Missing `.env`.** Startup aborts with a clear, actionable error instructing the operator to copy `.env.example` to `.env` and configure it. The application must not start in an undefined state.
- **Invalid `.env`.** A malformed file, an unknown `DATABASE_PROVIDER` value, or a `postgresql` provider with an empty connection string aborts startup with a descriptive error naming the offending key.
- **Database connection failure.** If the configured database is unreachable or migrations fail at startup, the application aborts (or shows a maintenance error) with a clear message; it must not present the setup wizard against a non-functional database. Connection-string specifics live in [Database & Transfer](transfer_updates.md).
- **Password / confirm mismatch.** The form is rejected with a field-level error; no user is created and the CMS is not marked installed.
- **Weak password.** The form is rejected with guidance on the password policy; no user is created.
- **Re-running setup.** Direct navigation to wizard routes after install is treated as the "already installed" case and blocked; the wizard cannot be used to overwrite or reset the first admin.
- **Concurrent setup submissions (race condition).** Two simultaneous setup submissions must not both create a Super Admin. User creation and setting the installed-state flag must occur atomically (single transaction, with a uniqueness guard on the installed flag and on the admin email). The first to commit wins; the loser receives the "already installed" response.
- **Interrupted setup.** If setup fails after partial work, the transaction rolls back so the CMS remains uninstalled and the wizard remains available; the installed flag is set only on full success.
- **Duplicate email.** Because there are no prior users, email uniqueness is enforced at creation; if a seeded value and a typed value collide, creation fails with a clear error and the CMS stays uninstalled.

## Acceptance Criteria

- [ ] A `.env.example` file exists containing exactly `DATABASE_PROVIDER`, `DATABASE_CONNECTION_STRING`, `APP_NAME`, and `APP_URL`.
- [ ] Copying `.env.example` to `.env` with default values starts the application using the `sqlite` provider.
- [ ] `DATABASE_PROVIDER=postgresql` with a valid `DATABASE_CONNECTION_STRING` starts the application against PostgreSQL.
- [ ] An unknown `DATABASE_PROVIDER` value aborts startup with a descriptive error.
- [ ] `DATABASE_PROVIDER=postgresql` with an empty `DATABASE_CONNECTION_STRING` aborts startup with a descriptive error.
- [ ] A missing `.env` aborts startup with guidance to copy `.env.example`.
- [ ] On first run with no installation, all non-setup requests are redirected to the setup/registration page.
- [ ] The setup page presents First name (optional), Last name (optional), Email, Password, and Confirm password fields.
- [ ] Submitting mismatched Password and Confirm password is rejected without creating a user.
- [ ] Submitting a weak password is rejected without creating a user.
- [ ] A valid submission creates the first user, assigns it the `Super Admin` role, and stores the password only as a secure hash.
- [ ] After a successful submission, the CMS is marked installed and the user is redirected to the admin dashboard.
- [ ] After installation, navigating to any setup route is blocked and redirects to the dashboard or login page.
- [ ] Two concurrent valid setup submissions result in exactly one Super Admin and one installed CMS.
- [ ] A database connection failure at startup prevents the wizard from being shown and reports a clear error.
- [ ] `.env` is excluded from Git via `.gitignore`, and secrets are never committed.
