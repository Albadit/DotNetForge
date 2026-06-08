# Email Configuration & Templates

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

This module defines how DotNetForge CMS connects to an outbound SMTP server, validates and tests that connection, and manages the library of transactional email templates the system sends to users. It owns both the **Email > Configuration** and **Email > Templates** admin pages.

## Purpose

DotNetForge CMS must send transactional email (confirmations, password resets, invitations, security alerts) reliably and securely. This module provides:

- A single place to configure outbound SMTP credentials and sender identity.
- The ability to verify that configuration by sending a test email before relying on it in production.
- A managed set of email templates that can be viewed, edited, previewed with sample data, and restored to their shipped defaults.

SMTP credentials are secrets and must follow the same rules as all other secrets in the CMS: they live in `.env` and must never be committed to Git. Sensitive runtime overrides stored in the database must be encrypted at rest. See [Security](security.md) for the full secrets and hardening model.

## Data Model / Fields

### SMTP Settings (Email Configuration page)

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| SMTP host | string (hostname/IP) | Yes | The outbound mail server, e.g. `smtp.example.com`. |
| SMTP port | integer (1–65535) | Yes | The server port. Common values: `25`, `465` (implicit TLS), `587` (STARTTLS). |
| SMTP username | string | Conditional | Credential for authenticated SMTP. Required when the server requires authentication. |
| SMTP password | string (secret) | Conditional | Credential for authenticated SMTP. Stored encrypted; never returned in plaintext to the UI or API. |
| Sender email | string (email) | Yes | The `From` address applied to outgoing messages. Must be a valid email address. |
| Sender name | string | No | The friendly display name shown with the sender email, e.g. `DotNetForge CMS`. |
| SSL/TLS enabled | boolean | Yes | Whether the connection uses SSL/TLS (implicit TLS or STARTTLS). Must be consistent with the chosen port. |
| Test email recipient | string (email) | No | The destination address used by the **Send test email** action. Not persisted as a sending setting; used only to validate delivery. |

Recommended `.env` keys backing these settings (defaults may be overridden in the admin UI where the implementation supports a database fallback):

```env
SMTP_HOST=
SMTP_PORT=587
SMTP_USERNAME=
SMTP_PASSWORD=
SMTP_SENDER_EMAIL=
SMTP_SENDER_NAME=DotNetForge CMS
SMTP_SSL_ENABLED=true
```

> The `SMTP_PASSWORD` and any other credentials are secrets. They must be excluded from Git via `.gitignore` and must never appear in logs, audit-log details, or API responses.

### Email Template (Email Templates page)

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| Template key | string (system) | Yes | Stable identifier for the template (e.g. `email-confirmation`). Not user-editable. |
| Display name | string | Yes | Human-readable name shown in the template list. |
| Subject | string | Yes | The email subject line. May contain template variables. |
| Body | string (HTML/Razor) | Yes | The email body. May contain template variables. |
| Is customized | boolean (derived) | Yes | `true` when the current content differs from the shipped default. Drives the **Restore default** affordance. |
| Required variables | list (derived) | Yes | The set of variables that must be referenceable for this template to function (see below). |

### Default Templates and Their Variables

The CMS must ship the following default templates. Each lists the variables that must be available when rendering. Editors may reference these variables; referencing any variable not in the available set is invalid (see [Validation Rules](#validation-rules)).

| Template | Template key | Trigger | Required / available variables |
| --- | --- | --- | --- |
| Email confirmation | `email-confirmation` | New user registration when email confirmation is enabled | `{{userName}}`, `{{confirmationUrl}}`, `{{appName}}`, `{{appUrl}}` |
| Password reset | `password-reset` | User requests a password reset | `{{userName}}`, `{{resetUrl}}`, `{{appName}}`, `{{expiryMinutes}}` |
| Welcome email | `welcome-email` | After a user successfully confirms/activates their account | `{{userName}}`, `{{appName}}`, `{{appUrl}}`, `{{loginUrl}}` |
| User invitation | `user-invitation` | An admin invites a user to the admin panel | `{{inviterName}}`, `{{inviteUrl}}`, `{{appName}}`, `{{roleName}}`, `{{expiryDate}}` |
| Account locked | `account-locked` | Account is locked after repeated failed login attempts | `{{userName}}`, `{{appName}}`, `{{unlockUrl}}`, `{{lockoutMinutes}}` |
| Admin notification | `admin-notification` | A system/admin-significant event occurs | `{{appName}}`, `{{eventType}}`, `{{eventDetails}}`, `{{timestamp}}` |

Account lockout and email-confirmation behavior are defined in [Authentication & Providers](authentication.md) and the security model in [Security](security.md); this module owns only the message content and delivery.

## Main Features

### Email Configuration

- **Save email settings** - Persist the SMTP host, port, username, password, sender email, sender name, and SSL/TLS flag. The password is stored encrypted and never echoed back.
- **Send test email** - Send a message to the configured **Test email recipient** using the currently entered settings, confirming end-to-end delivery.
- **Validate configuration** - Check the settings for correctness (format, required fields, port/TLS consistency) before saving and before attempting a test send.

### Email Templates

- **View templates** - List all templates with their display name and customized state, and view the subject/body of any template.
- **Edit templates** - Modify the subject and body of a template, using the template's available variables.
- **Restore default templates** - Revert a customized template to its shipped default content (requires confirmation).
- **Preview templates** - Render the subject and body with sample data so the editor can see the final output before saving, without sending an email.

## User Flows

### Flow: Configure SMTP

1. A user with the required role opens **Settings > Email > Configuration**.
2. The user enters SMTP host, port, username, password, sender email, sender name, and toggles SSL/TLS enabled.
3. The user submits the form.
4. The system runs **Validate configuration** (format, required fields, port/TLS sanity).
5. If validation fails, the system shows field-level errors and does not save.
6. If validation passes, the system encrypts the password, saves the settings, and records a `Settings changed` entry in the [Audit Logs](audit_logs.md) (secret values excluded from the log details).

### Flow: Send a Test Email

1. From **Email > Configuration**, the user enters or confirms the **Test email recipient**.
2. The user clicks **Send test email**.
3. The system validates the current configuration and the recipient address.
4. The system attempts to connect to the SMTP server and send a predefined test message.
5. On success, the system shows a success confirmation including the recipient and timestamp.
6. On failure (auth error, timeout, connection refused), the system shows a clear error explaining the failure category and does not mark the configuration as verified.

### Flow: Edit a Template

1. The user opens **Settings > Email > Templates** and selects a template.
2. The system displays the current subject and body and lists the template's available variables.
3. The user edits the subject and/or body, optionally inserting available variables.
4. The user saves.
5. The system validates that every referenced variable is in the template's available set and that the subject and body are non-empty.
6. If a referenced variable is undefined for that template, the system rejects the save with an error identifying the offending variable.
7. On success, the system stores the new content, sets **Is customized** to `true`, and records a `Settings changed` audit entry.

### Flow: Preview a Template with Sample Data

1. While viewing or editing a template, the user clicks **Preview**.
2. The system renders the subject and body using built-in sample data for that template's variables.
3. The system displays the rendered output (a sanitized HTML preview) without sending any email.
4. If the (unsaved) content references an undefined variable, the preview surfaces the same validation error rather than rendering a broken value.

### Flow: Restore a Template to Default

1. The user opens a customized template (**Is customized** = `true`).
2. The user clicks **Restore default**.
3. The system shows a confirmation dialog warning that current customizations will be overwritten.
4. If the user confirms, the system replaces the subject and body with the shipped default, sets **Is customized** to `false`, and records a `Settings changed` audit entry.
5. If the user cancels, no change is made.

## Role & Permission Rules

Email configuration and templates fall under the **Settings** permission area. See [User Roles & Permissions](user_roles_permissions.md) for the full RBAC model.

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | --- | --- | --- | --- | --- | --- |
| View Email Configuration | Yes | Yes | No | No | No | No |
| Edit / save SMTP settings | Yes | Yes | No | No | No | No |
| Send test email | Yes | Yes | No | No | No | No |
| View email templates | Yes | Yes | No | No | No | No |
| Edit email templates | Yes | Yes | No | No | No | No |
| Preview email templates | Yes | Yes | No | No | No | No |
| Restore default templates | Yes | Yes | No | No | No | No |

- By default only **Super Admin** and **Admin** manage email, because these pages expose credentials and system-wide messaging.
- Access is governed by granular **Settings** permissions; a custom role granted the relevant Settings permission may be allowed these actions.
- In multi-tenant deployments, email settings and templates may be scoped per tenant. Tenant Admins manage email only for their assigned tenants; a global Super Admin manages all tenants. Tenant resolution happens before the request is scoped. See [Multi-Tenancy](multi_tenancy.md).

## Validation Rules

- **SMTP host** is required and must be a syntactically valid hostname or IP address.
- **SMTP port** is required and must be an integer in the range `1–65535`.
- **Sender email** is required and must be a valid email address.
- **SMTP username / password** are required together when the server requires authentication; if a username is provided, a password is required (and vice versa).
- **SSL/TLS sanity** - The SSL/TLS flag must be consistent with the port. The system must warn or block obvious mismatches (e.g. SSL/TLS disabled on port `465`, or implicit-TLS settings on a plaintext port). A clear warning must be shown when an insecure combination is selected.
- **Test email recipient** must be a valid email address before a test send is permitted.
- **Template subject** and **template body** are required and must be non-empty after trimming.
- **Required template variables present** - Each template must remain renderable: any variable the template depends on must be resolvable, and the editor must not reference a variable outside the template's available set (an undefined variable is a validation error).
- Secret values (SMTP password) must never be returned to the client; the API and UI must treat them as write-only/masked.
- All inputs must pass server-side validation in addition to any client-side checks, per the input-validation requirements in [Security](security.md).

## Edge Cases

- **Invalid SMTP credentials** - A test send that fails authentication must report an authentication error specifically, leave the previous verified state unchanged, and must not lock out or expose the credential value.
- **Send failure / timeout** - If the SMTP server is unreachable, refuses the connection, or the operation times out, the system must surface a distinct, actionable error (connection refused vs. timeout vs. TLS handshake failure) and must not hang the request indefinitely.
- **Template references an undefined variable** - Saving or previewing content that references a variable not available to that template must fail with an error naming the variable. The system must not send or render an email containing an unresolved placeholder.
- **Restoring a customized template** - Restoring overwrites customizations and is irreversible from the UI; it must require explicit confirmation before proceeding.
- **Restore on an already-default template** - If the template is not customized, **Restore default** is a no-op (or disabled); no audit noise is generated.
- **SSL/TLS vs. port mismatch** - Selecting an insecure or contradictory combination must trigger a sanity warning; a hard mismatch (TLS expectations the port cannot satisfy) should be blocked.
- **Email confirmation tie-in** - When email confirmation is enabled in user settings but SMTP is unconfigured or failing, new registrations cannot receive their confirmation email. The system must surface this dependency and avoid silently leaving users stuck. The confirmation flow, links, and redirect behavior are owned by [Authentication & Providers](authentication.md).
- **Concurrent edits** - If two admins edit the same template simultaneously, the system must prevent silent overwrites (last-write-wins with a warning, or optimistic concurrency rejecting the stale save).
- **Secrets in logs** - Failures must be logged without leaking the SMTP password or other secret material into logs or audit-log details.

## Acceptance Criteria

- [ ] An authorized user can save SMTP settings (host, port, username, password, sender email, sender name, SSL/TLS) and the password is stored encrypted and never returned in plaintext.
- [ ] Saving invalid SMTP settings (bad host, out-of-range port, invalid sender email) is rejected with field-level errors.
- [ ] An SSL/TLS-vs-port mismatch produces a clear warning or is blocked.
- [ ] An authorized user can send a test email to a valid **Test email recipient** and receive a success confirmation on delivery.
- [ ] A test send to an unreachable server returns a distinct timeout/connection error within a bounded time and does not mark the config as verified.
- [ ] Invalid SMTP credentials produce an authentication-specific error on test send.
- [ ] All six default templates (Email confirmation, Password reset, Welcome email, User invitation, Account locked, Admin notification) exist and are viewable.
- [ ] An authorized user can edit a template's subject and body and save successfully when only available variables are referenced.
- [ ] Saving or previewing a template that references an undefined variable is rejected with an error naming that variable.
- [ ] Preview renders the subject and body with sample data without sending an email.
- [ ] Restoring a customized template requires confirmation and reverts the content to the shipped default, clearing the customized flag.
- [ ] Only Super Admin, Admin, and roles with the appropriate Settings permission can view or modify email configuration and templates.
- [ ] Saving SMTP settings, editing a template, and restoring a template each produce a `Settings changed` audit-log entry with no secret values in the details.
- [ ] When email confirmation is enabled but SMTP is unconfigured/failing, the dependency is surfaced rather than failing silently.
- [ ] SMTP password and other secrets never appear in API responses, the UI after save, or logs.
