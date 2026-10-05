# Email

Outbound transactional email: the SMTP connection, a test send, and the library of email templates the CMS sends
to users (confirmation, password reset, invitation, lockout, admin notifications). Today only a contract and a
development sender exist; nothing in the application sends email.

## Current state

| Piece | Location | Status |
| --- | --- | --- |
| `IEmailSender.SendAsync(EmailMessage, CancellationToken)` | `src/DotNetForge.Abstractions/Messaging/IMessagingContracts.cs` | contract |
| `EmailMessage` (`To`, `Subject`, `Body`, `IsHtml` = `true`) | same file | one recipient; no sender, reply-to, CC or attachments |
| Implementation / DI registration | - | **none**. The former `FileSystemEmailSender` (wrote messages to `storage/logs/email`, never used) was removed: the deployment directory is read-only ([deployment](../guides/deployment.md#read-only-deployment-requirements)) |
| Email Configuration screen | `GET /admin/email/configuration` → `ModulesController.EmailConfiguration` | [placeholder](../pages/module-placeholders.md) |
| Email Templates screen | `GET /admin/email/templates` → `ModulesController.EmailTemplates` | placeholder |
| Sidebar | `_Sidebar.cshtml` group **Settings · Email** → Configuration, Templates | links only; any admin-capable role can open them |

Not present: SMTP keys (neither `.env.example` nor `AppEnvironment` has any), an SMTP sender, template storage or
rendering, and any flow that sends mail. Account lockout (`AuthService.MaxFailedAttempts` = 5,
`AuthService.LockoutWindow` = 15 min) sends nothing; sign-up, password reset, confirmation and invitations are not
built ([authentication](authentication.md)). There is no encryption-at-rest helper for secrets.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

- **Email → Configuration** screen: save SMTP settings, validate them, send a test email.
- **Email → Templates** screen: list, view, edit, preview (sample data, no send) and restore-to-default templates.
- SMTP credentials are secrets: they live in `.env`, never in Git; any database override is encrypted at rest; the
  password is write-only (never returned to the UI, the API, logs or audit details).
- Email settings and templates may be tenant-scoped ([multi-tenancy](multi-tenancy.md)).
- This module owns message content and delivery only; the flows that trigger the messages (confirmation, reset,
  lockout) belong to [authentication](authentication.md).

### SMTP settings

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| SMTP host | hostname / IP | yes | e.g. `smtp.example.com` |
| SMTP port | int `1-65535` | yes | `25`, `465` (implicit TLS), `587` (STARTTLS) |
| SMTP username | string | conditional | required when the server needs authentication |
| SMTP password | secret | conditional | stored encrypted, never echoed back |
| Sender email | email | yes | `From` address |
| Sender name | string | no | display name, e.g. `DotNetForge CMS` |
| SSL/TLS enabled | bool | yes | must be consistent with the port |
| Test email recipient | email | no | used only by **Send test email**; not a sending setting |

Proposed `.env` keys (database values may override where supported):

```env
SMTP_HOST=
SMTP_PORT=587
SMTP_USERNAME=
SMTP_PASSWORD=
SMTP_SENDER_EMAIL=
SMTP_SENDER_NAME=DotNetForge CMS
SMTP_SSL_ENABLED=true
```

### Templates

Template fields: **key** (stable system id, not editable), **display name**, **subject** and **body** (HTML; both
may contain variables), **is customized** (derived: differs from the shipped default; drives *Restore default*),
**required variables** (derived).

Shipped defaults:

| Template | Key | Trigger | Available variables |
| --- | --- | --- | --- |
| Email confirmation | `email-confirmation` | registration when email confirmation is on | `{{userName}}`, `{{confirmationUrl}}`, `{{appName}}`, `{{appUrl}}` |
| Password reset | `password-reset` | user requests a reset | `{{userName}}`, `{{resetUrl}}`, `{{appName}}`, `{{expiryMinutes}}` |
| Welcome email | `welcome-email` | account confirmed/activated | `{{userName}}`, `{{appName}}`, `{{appUrl}}`, `{{loginUrl}}` |
| User invitation | `user-invitation` | admin invites a user | `{{inviterName}}`, `{{inviteUrl}}`, `{{appName}}`, `{{roleName}}`, `{{expiryDate}}` |
| Account locked | `account-locked` | lockout after repeated failed sign-ins | `{{userName}}`, `{{appName}}`, `{{unlockUrl}}`, `{{lockoutMinutes}}` |
| Admin notification | `admin-notification` | system/admin-significant event | `{{appName}}`, `{{eventType}}`, `{{eventDetails}}`, `{{timestamp}}` |

`{{appName}}` / `{{appUrl}}` map to `AppEnvironment.AppName` / `AppUrl`; `{{lockoutMinutes}}` to
`AuthService.LockoutWindow`.

### Roles

Email is part of the **Settings** permission area. Default: Super Admin and Admin can view and edit the
configuration, send a test email, and view, edit, preview and restore templates; Editor, Author, Authenticated and
Public cannot. A custom role with the matching Settings permission may be allowed. Tenant Admins manage only their
tenants' email. Role model: [authorization](authorization.md).

### User flows

1. **Configure SMTP**: enter the fields → submit → validate (format, required, port/TLS) → on failure show
   field-level errors, save nothing → on success encrypt the password, save, audit `AuditActions.SettingsChanged`
   without secret values.
2. **Send test email**: enter/confirm the recipient → validate config and recipient → connect and send a fixed test
   message → success shows recipient and timestamp; failure shows the category (authentication, timeout, connection
   refused, TLS handshake) and does not mark the configuration verified.
3. **Edit template**: select → subject, body and available variables shown → edit → save → validate (non-empty,
   only available variables) → an undefined variable rejects the save naming it → on success store, set
   *is customized*, audit `SettingsChanged`.
4. **Preview**: render subject and body with built-in sample data as sanitized HTML; no email sent; undefined
   variables produce the same validation error instead of a broken render.
5. **Restore default**: only for customized templates → confirmation dialog (customizations are overwritten) →
   replace with the shipped default, clear *is customized*, audit `SettingsChanged`; cancel changes nothing.

### Rules and validation

| Rule | Constraint |
| --- | --- |
| Host | required; valid hostname or IP |
| Port | required; integer `1-65535` |
| Sender email | required; valid email (`EmailValidator` exists in `Core/Validation`) |
| Username / password | required together when the server authenticates |
| SSL/TLS vs port | warn on insecure combinations (e.g. TLS off on `465`); block hard mismatches |
| Test recipient | valid email before a test send |
| Template subject / body | required, non-empty after trimming |
| Template variables | only the template's available variables; every required variable resolvable |
| Secrets | write-only / masked in UI and API |
| Validation location | always server-side, in addition to any client checks ([security](security.md)) |

### Edge cases

- Invalid credentials: authentication-specific error; previous verified state unchanged; credential never exposed.
- Unreachable server, refused connection, timeout, TLS failure: distinct actionable errors within a bounded time; the
  request never hangs.
- Undefined variable: save and preview fail naming it; never send or render an unresolved placeholder.
- Restore on a non-customized template: no-op or disabled; no audit entry.
- Email confirmation enabled while SMTP is unconfigured or failing: surface the dependency instead of leaving new
  users stuck.
- Two admins editing one template: no silent overwrite (warning or optimistic concurrency).
- Failures are logged without the password or other secrets.

### Acceptance criteria

- [ ] SMTP settings (host, port, username, password, sender email, sender name, SSL/TLS) can be saved; the password is encrypted and never returned in plaintext.
- [ ] Invalid settings (bad host, out-of-range port, invalid sender email) are rejected with field-level errors.
- [ ] An SSL/TLS-vs-port mismatch warns or is blocked.
- [ ] A test email to a valid recipient succeeds with a confirmation.
- [ ] A test send to an unreachable server returns a distinct timeout/connection error in bounded time and does not mark the config verified.
- [ ] Invalid credentials give an authentication-specific error on test send.
- [ ] All six default templates exist and are viewable.
- [ ] A template's subject and body can be edited and saved when only available variables are used.
- [ ] Saving or previewing a template with an undefined variable is rejected, naming the variable.
- [ ] Preview renders with sample data without sending.
- [ ] Restoring a customized template needs confirmation, restores the shipped default and clears the customized flag.
- [ ] Only Super Admin, Admin and roles with the Settings permission can view or change email configuration and templates (today the placeholders admit every admin-capable role).
- [ ] Saving SMTP settings, editing a template and restoring a template each write a `settings.changed` audit entry without secrets.
- [ ] Email confirmation enabled with SMTP unconfigured/failing is surfaced, not silent.
- [ ] The SMTP password and other secrets never appear in API responses, the UI after save, or logs.

## Where to change things

- **SMTP sender**: a new `SmtpEmailSender` in `src/DotNetForge.Infrastructure/Messaging/` implementing `IEmailSender`, BCL
  first (architecture rule 12, [codebase](../architecture/codebase.md#architectural-rules-for-changes)), registered in
  `Startup/DependencyRegistration.cs`. For development, log messages via `ILogger` or use a local SMTP catcher
  (e.g. a Mailpit container) - never write mail files into the deployment directory.
- **Contract**: if a sender name, reply-to or the template key must travel with the message, extend `EmailMessage`
  in `Abstractions` - it must stay dependency-free and entity-free.
- **Configuration**: new `SMTP_*` keys follow [configuration → Where to change things](configuration.md#where-to-change-things)
  (`EnvConfigurationLoader`, `AppEnvironment`, `.env.example`, tests). Encrypt any database-stored password with ASP.NET
  Core Data Protection (shared framework, no new package).
- **Templates**: an `EmailTemplate` entity (tenant-scoped, key + subject + body) in `src/DotNetForge.Shared/Entities`
  plus a migration; shipped defaults as constants in one place, seeded idempotently by `DataSeeder` or used as the
  fallback when no row exists. Variable parsing, validation and rendering are pure logic: put them in
  `src/DotNetForge.Core` with unit tests, and HTML-encode variable values.
- **Sending from flows**: callers (password reset, lockout, invitations) resolve the template by key, render, then
  call `IEmailSender`; never build email bodies inline in controllers.
- **Screens**: replace `EmailConfiguration` / `EmailTemplates` in `ModulesController` with an admin controller
  restricted to `Roles.SuperAdmin`/`Roles.Admin` (`admin-page` skill); mutations are `POST` + antiforgery + audit
  `AuditActions.SettingsChanged`. Add the screen docs under [pages](../pages/) and update
  [module placeholders](../pages/module-placeholders.md) and [implementation status](../implementation-status.md).
- **Test send**: run with a timeout and map exceptions to the error categories above; log without secrets.
