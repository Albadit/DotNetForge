# Email

Outbound transactional email: the SMTP connection, a test send, and the library of email templates the CMS sends
to users (confirmation, password reset, invitation, lockout, admin notifications). Today an SMTP sender configured on
`SMTP_*` in `.env` (shown read-only on **Email → Configuration**) sends five templates - email confirmation, password reset, welcome,
account locked and the two-factor sign-in code - which administrators can customize on **Email → Templates**
([self-service account flows](authentication.md#self-service-accounts)).

## Current state

| Piece | Location | Status |
| --- | --- | --- |
| `IEmailSender.SendAsync(EmailMessage, CancellationToken)` | `src/DotNetForge.Abstractions/Messaging/IMessagingContracts.cs` | contract |
| `EmailMessage` (`To`, `Subject`, `Body`, `IsHtml` = `true`, `Template`) | same file | one recipient; no sender, reply-to, CC or attachments; `Template` = the template key, for the email log |
| `SmtpEmailSender` | `src/DotNetForge.Infrastructure/Messaging/SmtpEmailSender.cs`, registered as the keyed transport (`LoggingEmailSender.TransportKey`) in `DependencyRegistration` | BCL `System.Net.Mail.SmtpClient` with the settings in effect, read per send (`IEmailSettingsSource`); 30 s timeout (`TimeoutException`); STARTTLS when TLS is on (implicit TLS on port 465 is **not** supported - use 587); credentials only when `SMTP_USERNAME` is set; `From` = `SMTP_FROM` with `SMTP_FROM_NAME` or `APP_NAME`. Throws when SMTP is not configured - callers check `EmailSettings.IsConfigured` first |
| `LoggingEmailSender` | `src/DotNetForge.Web/Services/LoggingEmailSender.cs`, the scoped `IEmailSender` | sends through the transport and writes an `EmailLog` entry per attempt (time, recipient, subject, template, sent/failed + reason; own DI scope; 30-day retention) - the email log of [Email → Configuration](../pages/email-configuration.md) |
| Settings in effect | `EmailConfigurationService` (`IEmailSettingsSource`) | `SMTP_*` from `.env` → `AppEnvironment.Email` only ([configuration](configuration.md#email-smtp); a partial configuration fails startup) |
| Messages sent | `AccountService` (and `AuthService` on lockout, `MfaService` for sign-in codes) | the five [templates](#templates), rendered by `EmailTemplateService.RenderAsync` |
| Email Configuration screen | `/admin/email/configuration` → `EmailConfigurationController` | ✔ the `.env` settings read-only, connection test (`SmtpProbe`), test sends (plain or a template), email log, Mailpit link in Development - [Email → Configuration](../pages/email-configuration.md) |
| Email Templates screen | `/admin/email/templates` → `EmailTemplatesController` | ✔ edit, preview (sandboxed), restore default - [Email → Templates](../pages/email-templates.md) |
| Sidebar | `AdminNavigation.Tree` group **Settings → Email** → Configuration, Templates (both need Settings read) | |

Without SMTP (nothing saved on the screen and `SMTP_HOST` / `SMTP_FROM` empty) nothing is sent: the sign-in screen has no **Forgot password?**
link, `/forgot-password` answers `404` and email confirmation cannot be turned on
([Advanced Settings](../pages/advanced-settings.md)). A failed send never fails the request: `AccountService` logs
the exception type only (SMTP errors can echo addresses) and the form answers as usual.

Not present: a plain-text part, per-language templates, and the invitation and admin-notification messages (no
flow sends them).

## Templates

Every email the CMS sends is rendered from a template ([Email → Templates](../pages/email-templates.md)). The catalog
is code (`src/DotNetForge.Core/Email/EmailTemplateCatalog.cs`); a customized subject/body is stored in the
`EmailTemplates` table only while it differs from the default, so restoring the default deletes the row.

| Template | Key | Sent when (by) | Variables (**required**) |
| --- | --- | --- | --- |
| Email confirmation | `email-confirmation` | registration with email confirmation on, and **resend** (`AccountService`) | `{{userName}}`, **`{{confirmationUrl}}`**, `{{expiryDays}}`, `{{appName}}`, `{{appUrl}}` |
| Password reset | `password-reset` | **Forgot password** for an enabled account (`AccountService`) | `{{userName}}`, **`{{resetUrl}}`**, `{{expiryMinutes}}`, `{{appName}}`, `{{appUrl}}` |
| Welcome email | `welcome-email` | an account is ready: registration without confirmation, the first confirmation, or an account created by [sign-up through a provider](authentication.md#sign-up-through-a-provider) (`AccountService`) | `{{userName}}`, `{{loginUrl}}`, `{{appName}}`, `{{appUrl}}` |
| Account locked | `account-locked` | 5 wrong passwords start the 15-minute lockout (`AuthService` → `AccountService.SendAccountLockedAsync`) | `{{userName}}`, `{{unlockUrl}}` (the forgot-password page - a reset also unlocks), `{{lockoutMinutes}}`, `{{appName}}`, `{{appUrl}}` |
| Sign-in code | `sign-in-code` | [two-factor authentication](mfa.md#email-codes) by email: a sign-in step sends a code, or a user turns **Email codes** on (`MfaService` → `AccountService.SendSignInCodeAsync`) | `{{userName}}`, **`{{code}}`**, `{{expiryMinutes}}` (10), `{{appName}}`, `{{appUrl}}` |

- `{{userName}}` = `User.DisplayName`, `{{appName}}` / `{{appUrl}}` = `APP_NAME` / `APP_URL`; links are built from
  `APP_URL`, never from the request.
- Rules (`EmailTemplateRenderer`, unit-tested): only the template's variables, written `{{name}}`; required variables
  must be in the body; no incomplete `{{`/`}}`; subject one line ≤ 500, body ≤ 20 000. Values are HTML-encoded in the
  body, plain in the subject. A template is saved, previewed or sent only when valid - an unresolved placeholder never
  reaches a recipient.
- Every email is framed by `EmailLayout` (`src/DotNetForge.Core/Email/EmailLayout.cs`): the site name with the
  forge-orange mark, the body in a white card with the orange accent bar, a footer with the site URL - the brand
  colours (`--dnf-*` token values) as inline styles in tables (what email clients render; no SVG logo). The frame isn't editable; the default
  bodies use its helpers for the heading, the orange button and the plain-link fallback.
- Nothing is sent without SMTP; welcome and account-locked emails are skipped then, and email codes count as off
  ([MFA](mfa.md#email-codes)). A failed send is logged with the
  exception type only.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

- ✔ **Email → Configuration** screen: the `.env` SMTP settings (read-only), a connection test, test sends and the email log ([Email → Configuration](../pages/email-configuration.md)).
- ✔ **Email → Templates** screen: list, view, edit, preview (sample data, no send) and restore-to-default templates
  ([Email → Templates](../pages/email-templates.md); five templates - see below).
- SMTP credentials are secrets: they live in `.env` only, never in Git or the database; the password is never shown
  (Email → Configuration says only whether it is set) and never written to logs, the email log or audit details.
- This module owns message content and delivery only; the flows that trigger the messages (confirmation, reset,
  lockout) belong to [authentication](authentication.md).

### SMTP settings

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| SMTP host | hostname / IP | yes | e.g. `smtp.example.com` |
| SMTP port | int `1-65535` | yes | `25`, `465` (implicit TLS), `587` (STARTTLS) |
| SMTP username | string | conditional | required when the server needs authentication |
| SMTP password | secret | conditional | in `.env` only, never shown (only "set" / "not set") |
| Sender email | email | yes | `From` address |
| Sender name | string | no | display name, e.g. `DotNetForge CMS` |
| SSL/TLS enabled | bool | yes | must be consistent with the port |
| Test email recipient | email | no | used only by **Send test email**; not a sending setting |

✔ The `.env` keys exist with these names: `SMTP_HOST`, `SMTP_PORT` (default 587), `SMTP_USERNAME`,
`SMTP_PASSWORD`, `SMTP_FROM`, `SMTP_FROM_NAME`, `SMTP_TLS` (default `true`)
([configuration](configuration.md#email-smtp)); they are the only SMTP configuration (no database override). Still planned: implicit TLS (port 465).

### Templates (specification)

✔ Built for four of the six templates, plus **Sign-in code** for [two-factor authentication](mfa.md#email-codes)
([Templates](#templates)); the variable lists grew where the emails needed it
(`{{expiryDays}}`, `{{appUrl}}`). Still planned: **User invitation** and **Admin notification** (no invitation flow and
no notification events exist).

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
Public cannot. A custom role with the matching Settings permission may be allowed. Role model:
[authorization](authorization.md).

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
  users stuck (✔ unconfigured: the switch is disabled with a warning and the setting counts as off; ✘ a failing
  server is only logged).
- Two admins editing one template: no silent overwrite (warning or optimistic concurrency).
- Failures are logged without the password or other secrets.

### Acceptance criteria

- [x] SMTP settings (host, port, username, password, sender email, sender name, SSL/TLS) come from `.env` and are shown read-only; the password is never returned in plaintext.
- [x] Invalid settings are rejected at startup (`EnvConfigurationLoader`: a partial or invalid `SMTP_*` configuration stops the app); the connection test shows which step fails.
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

- **SMTP sender**: `SmtpEmailSender` in `src/DotNetForge.Infrastructure/Messaging/` (BCL only, architecture rule 11,
  [codebase](../architecture/codebase.md#architectural-rules-for-changes)), registered in
  `src/DotNetForge.Web/Startup/DependencyRegistration.cs`. For development use a local SMTP catcher (e.g. a Mailpit
  container) - never write mail files into the deployment directory. Integration tests replace `IEmailSender` with a
  recorder (`DotNetForgeWebFactory.SentEmails`).
- **Contract**: if a sender name, reply-to or the template key must travel with the message, extend `EmailMessage`
  in `Abstractions` - it must stay dependency-free and entity-free.
- **Configuration**: the `SMTP_*` keys (`EnvConfigurationLoader.LoadEmail`, `EmailSettings`) follow [configuration → Where to change things](configuration.md#where-to-change-things)
  (`EnvConfigurationLoader`, `AppEnvironment`, `.env.example`, tests). Encrypt any database-stored password with ASP.NET
  Core Data Protection (shared framework, no new package).
- **Templates**: catalog and rules in `src/DotNetForge.Core/Email/` (unit tests in `tests/DotNetForge.Tests/Email/`),
  storage and audit in `EmailTemplateService`, the `EmailTemplate` entity (customized rows only).
- **Sending from flows**: callers render a template by key (`EmailTemplateService.RenderAsync`) and call
  `IEmailSender` (see `AccountService.SendAsync`); never build email bodies inline. A new template = an entry in
  `EmailTemplateCatalog` (variables, required, defaults, sample values) + the sending flow; the screen lists it
  automatically.
- **Screens**: `EmailConfigurationController` (read-only settings, tools) and `EmailTemplatesController` (Settings
  permission; POSTs with antiforgery + audit `AuditActions.SettingsChanged`); docs [Email Configuration](../pages/email-configuration.md),
  [Email Templates](../pages/email-templates.md).
- **Test send**: run with a timeout and map exceptions to the error categories above; log without secrets.
