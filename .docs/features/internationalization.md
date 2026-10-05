# Internationalization

Locales for the admin UI and the public site: which locales a tenant has, which one is the default (the fallback
for missing translations), and how content is translated. Today the CMS is English-only and has no locale model.

## Current state

| Piece | Location | Status |
| --- | --- | --- |
| `Tenant.DefaultLocale` (string, max 20, default `"en"`) | `src/DotNetForge.Shared/Entities/Tenant.cs`, `DotNetForgeDbContext` | seeded `"en"` by `DataSeeder`; **read nowhere** |
| `InvariantGlobalization` = `true` | `Directory.Build.props` (every project) | the process runs with the invariant culture only; creating a specific culture such as `de-DE` is not supported in this mode |
| Internationalization screen | `GET /admin/internationalization` → `ModulesController.Internationalization` | [placeholder](../pages/module-placeholders.md), sidebar **Settings · Global Settings** |

Not present: locale entities or settings, request-culture middleware (`UseRequestLocalization`), `IStringLocalizer`
or resource files, per-user admin language, localized fields on `Page`, and any locale parameter on the
[headless API](headless-api.md). Every UI string is hard-coded English and every layout (`_Layout`,
`_AuthLayout`, `_AdminLayout`, `Views/Home/Page.cshtml`) writes `<html lang="en">`.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

Configured on **Settings → Global Settings → Internationalization**, per tenant ([multi-tenancy](multi-tenancy.md)):

| Setting | Meaning |
| --- | --- |
| Admin language | language of the admin UI; defaults to the default locale; per-user override where supported |
| Website/page language | language shown to public visitors: the default locale unless a more specific enabled locale is selected/requested |
| Default locale | the single fallback locale; exactly one at all times |
| Additional locales | any number of further locales that can be enabled for translation |

**Locale** fields:

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| Locale code | string (BCP-47) | yes | e.g. `en`, `en-US`, `de-DE`, `pt-BR`, `zh-Hans`; unique per tenant |
| Locale display name | string | yes | e.g. "English (United States)" |
| Is default locale | bool | yes | exactly one `true` per tenant |
| Enabled | bool | yes | the default locale is always enabled |

**Content localization**: localizable fields (e.g. content page `Title`, `MetaTitle`, `MetaDescription`, body
modules) may hold one value per enabled locale; a missing value falls back to the default locale at render time.
Translations are recorded in Content History (a [placeholder](../pages/module-placeholders.md) today).

### Roles

Locale management is part of the **Settings** permission area (role model: [authorization](authorization.md)):

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | :-: | :-: | :-: | :-: | :-: | :-: |
| Add / edit / delete locales, set default, enable/disable, configure admin/website language | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Translate content | ✔ | ✔ | ✔ | own content only | ✘ | ✘ |
| View localized public content | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |

A global Super Admin manages locales of every tenant; a tenant-scoped Admin only those of assigned tenants.

### User flows

1. **Add locale**: *Add locale* → code + display name + enabled → validate (BCP-47, unique) → create with
   *is default* = `false`.
2. **Set default**: select a locale → *Set as default* → confirmation dialog (untranslated content will fall back
   to the new default) → in one transaction set it `true`, the previous default `false`, and enable it if it was
   disabled → audit entry.
3. **Enable/disable**: toggle *Enabled*; disabling the default is blocked; disabling a locale in active use warns
   first; disabled locales disappear from admin/public selection but keep their translations.
4. **Translate content**: in the Content Manager (or via the API) pick an enabled locale → enter localized values
   → saved for that locale; untranslated fields fall back to the default at render time.

### Rules and validation

- Locale code: valid BCP-47 tag; unique within the tenant; malformed or duplicate codes rejected with no partial
  record.
- Display name: required, non-empty.
- Exactly one default per tenant at all times - never zero, never two.
- The default locale is enabled; making a disabled locale the default enables it.
- At least one locale must exist; the last one cannot be removed.
- Changing the default requires explicit confirmation and is atomic (promote and demote together).
- Locale and default-locale changes are written to the audit log ([audit logging](audit-logging.md)).

### Edge cases

- Deleting the default locale: blocked; reassign the default first, then delete.
- Deleting a locale with content: warn and confirm; its translations are removed and content falls back to the
  default; default-locale values are never touched.
- Missing translation: fall back to the default locale value; if that is empty too, render empty (no error).
- Disabling a locale in use (published content, admin/website language, a tenant's default): warn; stored
  translations are kept so re-enabling restores them.
- Concurrent "set default" requests: serialized or transactional so exactly one default remains.
- Tenant default: disabling or deleting a locale that is a tenant's default is blocked until that tenant's default
  is reassigned.

### Acceptance criteria

- [ ] Admin language, website/page language, default locale and additional locales are configurable on Settings → Global Settings → Internationalization (today a placeholder).
- [ ] Each locale has code, display name, is-default and enabled.
- [ ] Adding a locale validates the code as BCP-47 and rejects malformed codes.
- [ ] Locale codes are unique per installation and per tenant.
- [ ] Exactly one default locale exists at all times; zero or several can never be persisted.
- [ ] Changing the default shows a confirmation dialog and applies only after confirmation.
- [ ] Promoting the new default and demoting the old one are atomic.
- [ ] Making a disabled locale the default enables it.
- [ ] Deleting the current default is blocked until another enabled locale is the default.
- [ ] Deleting a locale with localized content warns, requires confirmation, and content falls back to the default.
- [ ] A missing translation falls back to the default locale at render time.
- [ ] Disabling a locale in active use warns; the default locale cannot be disabled.
- [ ] Only Super Admin and Admin (Settings permission) manage locales; Editors/Authors translate per their content permissions.
- [ ] Each tenant has its own default locale and the one-default rule is enforced per tenant (only the `Tenant.DefaultLocale` column exists; nothing reads or validates it).
- [ ] Concurrent default changes never produce two defaults.
- [ ] Locale and default-locale changes are audited.

## Where to change things

- **Globalization mode**: set `InvariantGlobalization` to `false` in `Directory.Build.props` before relying on
  culture data (formatting, `CultureInfo` for BCP-47 validation); on Linux this needs ICU in the runtime image.
  Update [dependencies](../architecture/dependencies.md) and the [development guide](../guides/development.md).
- **Locale entity**: a tenant-scoped `Locale` (code, display name, is-default, enabled) in
  `src/DotNetForge.Shared/Entities`, configured in `DotNetForgeDbContext` with a unique (`TenantId`, `Code`) index,
  plus a migration (`database-change` skill). Decide whether `Tenant.DefaultLocale` stays as the source of truth or is
  derived from the default `Locale` row - keep one source only.
- **Rules** (one default, default enabled, last locale, atomic switch): one `Services/LocaleService` running the
  switch in a single `SaveChangesAsync`/transaction; pure code validation in `src/DotNetForge.Core/Validation`.
- **Request culture**: register localization in `Startup/DependencyRegistration.cs` and add
  `UseRequestLocalization` in `Program.cs` with a provider that reads the tenant's enabled locales; set `<html lang>`
  from the resolved culture in every layout.
- **Translated content**: a translation table keyed by (`PageId`, locale) rather than per-locale columns on `Page`;
  field mapping and fallback stay in `PageService` and the public render path (`HomeController`), not in views.
  The API needs a locale parameter ([headless API](headless-api.md#conventions-for-new-endpoints)).
- **Screen**: replace `ModulesController.Internationalization` with an admin controller restricted to
  `Roles.SuperAdmin`/`Roles.Admin` (`admin-page` skill); mutations `POST` + antiforgery + audit (add an
  `AuditActions` constant). Document the screen under [pages](../pages/) and update
  [implementation status](../implementation-status.md).
