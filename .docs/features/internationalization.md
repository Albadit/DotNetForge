# Internationalization

Three separate things, each with its own rules:

| Concept | What it is | Changes content? | Configured on |
| --- | --- | --- | --- |
| **Locales** | the languages the *site* offers: one default locale (the main content, unprefixed URLs, the fallback of every missing translation) and any number of further locales, published under their code (`/de/about`) while enabled | no (the list only) | [Internationalization](../pages/internationalization.md) |
| **Translations** | a content page's title, SEO texts and layout, and a localized type's text fields, in a locale other than the default | yes | [Pages](../pages/pages.md) → **Translations** tab and the [page builder](../pages/page-builder.md); [Content Manager](../pages/content-manager.md) language switcher; `PUT /api/entries/...?locale=` |
| **Admin language** | the language the admin screens are shown in - per user, else the site's choice, else English | no | [Profile](../pages/profile.md) → **Admin language**; [Internationalization](../pages/internationalization.md) → **Admin language** |

The admin language is independent of the locales: a German-speaking editor can work in a German admin on an
English-only site, and an English admin can translate content into Japanese.

## Where the code is

| Part | Location |
| --- | --- |
| Entities `Locale`, `PageTranslation`, `ContentEntryTranslation` | `src/DotNetForge.Shared/Entities/Localization.cs`; `ContentType.Localized` (`StructuredContent.cs`), `User.AdminLanguage` (`User.cs`) |
| Mapping (unique and filtered indexes), migration, seeding | `src/DotNetForge.Data/DotNetForgeDbContext.cs`, `Database/Providers/PostgreSql/Migrations/20261104113431_AddInternationalization.cs`, `DataSeeder.SeedLocalesAsync` ([database](../architecture/database.md)) |
| Locale code validation (pure) | `src/DotNetForge.Core/Validation/LocaleCodes.cs` |
| Locale rules, cache, default switch | `src/DotNetForge.Web/Services/Localization/LocaleService.cs`, `LocaleCatalog.cs` (`LocaleSet`, `LocaleInfo`, `IDefaultLocaleSwap`), `EntryLocaleSwap.cs` |
| Request locale | `src/DotNetForge.Web/Middleware/RequestLocaleMiddleware.cs`, `Services/Localization/RequestLocale.cs` |
| Translated pages (public overlay, menu, language links, Translations tab, per-locale layouts, default switch) | `src/DotNetForge.Web/Services/Localization/PageTranslationService.cs`; `LanguageLink` in `src/DotNetForge.Shared/Content/LanguageLink.cs` |
| Translated entries | rules `src/DotNetForge.Core/Content/EntryTranslations.cs` (pure); database `src/DotNetForge.Content/EntryTranslationService.cs`; hooks in `ContentEntryService` (publish, unpublish, duplicate, delete, field changes) |
| Public rendering | `HomeController.RenderPage` / `SetLocaleViewDataAsync`, `ContentRenderService` (content lists), `Views/Shared/_Layout.cshtml`, `extensions/themes/default-theme/Views/Shared/_Layout.cshtml` |
| Screens | `Areas/Admin/Controllers/InternationalizationController.cs` + `Views/Internationalization/`; `PagesController` + `Views/Pages/_PageForm.cshtml` (Translations tab); `PageBuilderController` (locale routes); `ContentController.Translate` / `SaveTranslation` + `Views/Content/Edit.cshtml`; `Views/ContentTypes/Edit.cshtml` (**Localized**); `ProfileController.Language` |
| API | `src/DotNetForge.Api/Controllers/EntriesApiController.cs` (`?locale=`), `src/DotNetForge.Content/ContentApiService.cs` |
| Admin language | `Services/Localization/AdminTranslations.cs` (`AdminTranslations`, `AdminCatalog`, `AdminText`), `AdminLanguageService.cs`, `Middleware/AdminLanguageMiddleware.cs`; catalogs `src/DotNetForge.Web/Localization/Admin/<code>.json`; `src/DotNetForge.UI/DnfText.cs` (`IDnfText`); `dnfText` in `src/DotNetForge.UI/wwwroot/dnf.js`; `src/DotNetForge.Web/wwwroot/js/builder/ui/i18n.js` |
| Tests | unit `LocaleCodesTests`, `Content/EntryTranslationsTests`, `AdminTextCatalogTests`; integration `InternationalizationTests`, `PageTranslationTests`, `EntryTranslationTests`, `AdminLanguageTests` ([testing](../guides/testing.md)) |

`InvariantGlobalization` is `false` in `Directory.Build.props`, so ICU culture data is available (the `aspnet:10.0`
image ships it): `LocaleCodes` validates codes and names languages with `CultureInfo`.

## Locales

| Field (`Locale`) | Type | Rules |
| --- | --- | --- |
| `Code` | string (35) | a BCP-47 tag in canonical case (`en`, `de-DE`, `zh-Hans`, `sr-Latn-RS`, `es-419`); unique (case-insensitive check, plus a unique index); can't change after adding - it is the URL prefix |
| `DisplayName` | string (100) | shown in the admin and the public language switcher; empty on add → the language's own name (`LocaleCodes.SuggestedName`: `Deutsch (Deutschland)`) |
| `IsDefault` | bool | exactly one; a filtered unique index (`IX_Locales_IsDefault`, filter `"IsDefault"`) makes a second one impossible |
| `Enabled` | bool | offered for translation and published under its prefix; the default is always enabled |
| `SortOrder` | int | new locales go last; lists show the default first, then `SortOrder`, then `Code` (there is no reordering) |

A new database - or one from before locales existed - gets **English** (`en`, "English", default, enabled) from
`DataSeeder.SeedLocalesAsync`; once any locale exists the seeder does nothing.

### Rules and validation

Every rule is enforced by `LocaleService` (the code format through `LocaleCodes`); its methods return an error
message or `null`, and each change is audited and clears `LocaleCatalog`.

| Rule | Where | Message |
| --- | --- | --- |
| Code entered | `LocaleCodes.Normalize` | "Enter a locale code, such as en, de-DE or pt-BR." |
| Code well-formed: language (2-3 letters), optional script (4 letters), optional region (2 letters or 3 digits); `_` is read as `-`; at most 35 characters | `LocaleCodes.Normalize` | "\"x\" is not a valid locale code. Use a language code, optionally with script and region: en, de-DE, zh-Hans, pt-BR." |
| Code is a language the culture data knows (`CultureInfo.GetCultureInfo(tag, predefinedOnly: true)`) | `LocaleCodes.Normalize` | "\"x\" is not a known language. Use a code such as en, de-DE or pt-BR." |
| Code unique (case-insensitive) | `AddAsync` | "The locale de-DE already exists." |
| Display name at most 100 characters; required on edit | `AddAsync`, `UpdateAsync` | "The display name can be at most 100 characters." / "Enter a display name." |
| The default stays enabled | `UpdateAsync` | "English is the default locale and stays enabled. Make another locale the default first." |
| The default (and so the last locale) can't be deleted | `DeleteAsync` | "English is the default locale. Make another locale the default before deleting it." |
| Locale gone meanwhile | every method | "That locale no longer exists." |

A code is stored canonically (`de-de` → `de-DE`, `zh-hans` → `zh-Hans`). A new locale is never the default.

### Changing the default

`LocaleService.SetDefaultAsync`, in **one transaction**: the old default is demoted (saved first, so the filtered
index never sees two), the new one promoted and enabled, then every `IDefaultLocaleSwap` moves the content:

| Swap | What moves |
| --- | --- |
| `PageTranslationService.SwapAsync` | per page with a translation into the new default: each translated field (title, meta title, meta description, layout with its draft state) becomes the page's own value, and the page's old value becomes the old default's translation; untranslated fields stay as they are |
| `EntryLocaleSwap` → `EntryTranslationService.SwapDefaultAsync` | per translated entry: the translation's values (working copy and published) become the entry's own (`ContentEntryService.ReplaceMainDataAsync` rebuilds title, search text and value index); the entry's previous values of those fields become the old default's translation |

So after the switch the new default's text is the main content (unprefixed URLs, the API without `?locale=`), and the
previous default's text is reachable under its prefix. Content not translated into the new default keeps showing its
current text. A concurrent switch fails on the index (`DbUpdateException`), is rolled back and answers "The default
locale was changed at the same time by someone else. Reload and try again." - there is always exactly one default.

### Disabling and deleting

- **Disable** keeps every translation but hides it: the locale's URLs answer 404, it disappears from the language
  switcher, the Translations tab, the Content Manager switcher, the page builder route (404) and the API (`?locale=`
  → 400). Enabling it brings everything back.
- **Delete** (not the default) removes the locale's `PageTranslations` and `ContentEntryTranslations` in the same
  transaction; that content then shows the default locale's text.

### Caching

Every request needs the locales (path prefixes, default, switcher): `LocaleCatalog` (singleton) caches them as a
`LocaleSet` for **30 seconds** and is cleared by `LocaleService` on every change - in this instance only, so other
instances follow within 30 s.

## Request locale

`RequestLocaleMiddleware` runs after `UseStaticFiles` (files have no locale) and **before** `UseRouting`, so routing
sees the path without the prefix:

| Request path | Locale | Effect |
| --- | --- | --- |
| first segment is an enabled locale other than the default (case-insensitive): `/de/about`, `/de` | that locale | the prefix is taken off (`/about`, `/`), so every route answers as usual |
| first segment is the default locale's code: `/en/about` | - | `GET`/`HEAD`: **301** to `/about` (query kept) - one URL per page and language; other methods continue unchanged |
| anything else (also a disabled locale's code) | the default | - |
| a locale prefix before a path that has no localized form: `/admin`, `/api`, `/account`, `/login`, `/register`, `/forgot-password`, `/reset-password`, `/confirm-email`, `/setup`, `/health`, `/media` (and below) | the default | the path stays as it is, so `/de/admin` or `/de/login` answer **404** - the admin, the API, sign-in and setup have one URL only |

The result is the `RequestLocale` request feature (`RequestLocale.Of(HttpContext)`): `Locale` (content locale),
`Locales` (the `LocaleSet`), `UiLanguage` (for `<html lang>`), `IsTranslation`, `Prefix` and `Url(path)` /
`UrlIn(locale, path)`, which put the prefix back on links (`/about` → `/de/about`, `/` → `/de`).

Only the UI-text culture (`CultureInfo.CurrentUICulture`) follows the locale. `CurrentCulture` - number and date
formatting - stays culture-neutral, so form values, JSON and generated CSS read the same in every language.

`<html lang>`: `_Layout` and `_AuthLayout` write `RequestLocale.UiLanguage` (the content locale on the public site);
`_AdminLayout` and the page builder write the admin language (`AdminText.Language`, with `dir`); the UI docs layout
always writes `lang="en" dir="ltr"`; the sample theme writes `CurrentUICulture.Name`.

**Slugs and locale codes never share a URL.** Adding a locale whose code equals the slug of a top-level page is refused
(`LocaleService.AddAsync`: "A top-level page uses the slug 'da', which this locale's URLs would hide. Change that
page's slug first."), and a top-level page can't take an existing locale's code as its slug (`PageService.ApplyAsync`:
"The slug 'de' is a locale code (Internationalization); choose another one.").

## Public site

`HomeController.RenderPage` resolves the page from the unprefixed path - **the same slugs in every locale** - with the
usual [liveness and permission rules](content-pages-and-routing.md#public-resolution); a page that is not live is not
live in any locale. For a translation it then overlays the page's texts and layout
([content pages](#content-pages)). Every public layout gets these `ViewData` keys from
`HomeController.SetLocaleViewDataAsync` (also for the page builder's canvas and preview):

| `ViewData` key | Value |
| --- | --- |
| `Menu` | top-level live pages with "display in menu", **titles translated** and paths under the locale's prefix (`PageTranslationService.LocalizeAsync`) |
| `CurrentPath` | this page's path in the request's locale (`/de/about`), to mark the active menu link |
| `HomePath` | the site root in the locale (`/` or `/de`) |
| `Languages` | `IReadOnlyList<LanguageLink>` (`Code`, `Name`, `Url`, `Current`): this page in every enabled locale; **empty with only one enabled locale** |

The built-in `_Layout` and the sample default theme render one `<link rel="alternate" hreflang="{code}" href="{absolute
URL}">` per entry of `Languages` (the current locale included) and, with more than one, a language switcher: a
`<select data-language-switch>` (one `<option value="{Url}" lang="{code}">` per language, the current one selected) that
`wwwroot/js/site/language-switch.js` follows on change - no inline script (CSP). The select shows only with JavaScript
(`dnf-js-only` in `_Layout`, `hidden` until the script runs in the theme); without it a `<noscript>` list of links (each
with `hreflang`, `lang`, `aria-current` on the current one) switches instead. The brand link goes to `HomePath`. A theme
gets the same keys and can load the same script (served at /js/site/language-switch.js) ([themes](themes.md#theme-views)).

## Content pages

A page's own fields are the default locale's. A `PageTranslation` (one per page and locale, unique
(`PageId`, `LocaleId`)) holds another locale's:

| Field | Falls back to | Notes |
| --- | --- | --- |
| `Title` (300) | the page's title | also the menu title in that locale |
| `MetaTitle` (300) | the translated title, when one is set; else the page's meta title | |
| `MetaDescription` (1000) | the page's meta description | |
| `LayoutJson` | the page's published layout | the published page builder layout in this locale |
| `DraftLayoutJson`, `DraftVersion`, `DraftUpdatedDate`, `DraftUpdatedById`, `LayoutPublishedDate` | - | the builder's draft bookkeeping, as on `Page` |

**Stylesheet.** A translated layout has its own stylesheet, like the main layout
([page builder → rendering](page-builder.md#rendering)): `PageLayoutViewComponent` links the published one under the
locale's prefix, `/de/page-css/{id}.css?v={hash}` - `PageCssController` reads the translation's layout when the request
is in that locale and falls back to the page's - and in the builder's preview and canvas the translation's draft,
`/admin/pages/{id}/translations/{locale}/builder/draft.css`.

Everything else is shared by every locale: slug and URL, publishing switch and schedule, keywords, no-index, social
image, theme, permissions, menu flag. On a translation `CanonicalUrl` is left out (it names the default URL).
`PageTranslationService.TranslateAsync` applies the overlay to an untracked copy of the page; empty values fall back.

| Action | What happens to translations |
| --- | --- |
| **Translations** tab of the page form ([Pages](../pages/pages.md)) - shown when at least one enabled locale besides the default exists | per locale **Title**, **Meta title**, **Meta description** (placeholders show the fallback), saved with the page's **Save** by `PageTranslationService.ApplyTextsAsync`: lengths checked ("Deutsch: the title and meta title can be at most 300 characters, the meta description 1000."), a row created only when a value is set, unchanged rows skipped, one `content.translated` audit entry per changed locale; input for a locale disabled or deleted meanwhile is ignored |
| **Edit {locale} layout** → `/admin/pages/{id}/translations/{locale}/builder` | the [page builder](page-builder.md) on the translation: the first open starts from the page's own layout (draft, else published) as an unsaved draft; save, publish, unpublish and discard work on the translation's columns; canvas and preview render in the locale (menu, links, content lists, `<html lang>`). Only an enabled locale other than the default → else 404. Publishing and unpublishing are audited as `content.translated` (details `Layout published - Deutsch (de)` / `Layout unpublished - …`, `PageBuilderService.AuditLayoutAsync`) |
| Duplicate a page | the copy gets the translations (texts and layouts; a new draft version) |
| Delete a page | `PageService.DeleteAsync` removes its translations |
| Change the default locale | [swapped](#changing-the-default) |

## Entries

A content type with **Localized** on (`ContentType.Localized`, a switch on the
[content type editor](../pages/content-types.md)) keeps some field values per locale. Which fields
(`EntryTranslations.Fields`): the **top-level** fields of type **text**, **textarea** (long text) and **rich text**
that are **not unique** - an SKU or code is the same in every language, and uniqueness is checked on the entry's own
values. Every other field (numbers, media, relationships, selects, slugs, groups and repeaters...) is shared by all
locales. Turning **Localized** off hides the translations; they stay and come back when it is on again.

A `ContentEntryTranslation` (one per entry and locale) holds a working copy (`DataJson`) and published values
(`PublishedDataJson`), like the entry itself; only non-empty translatable values are kept (`EntryTranslations.Keep`),
and a missing or empty value falls back to the entry's own (`EntryTranslations.Merge`). The title of an entry in a
locale is recomputed from the merged values (`EntryData.Title`).

| Action | Effect on translations |
| --- | --- |
| Save a translation (`EntryTranslationService.SaveAsync`) | validated like the entry (lengths, rich text; required fields are not enforced), saved as the working copy; with publish - or for a type without drafts - published too; `entry.translated` audit entry |
| Publish the entry (`ContentEntryService`) | every translation's working copy becomes its published values |
| Unpublish the entry | every translation's published values are cleared |
| Duplicate the entry | the copy gets the translations' working copies (not published) |
| Delete one, several or all entries of a type (`DeleteAsync`, `DeleteManyAsync`, `DeleteAllAsync`) | their translations are deleted |
| Change the type's fields (`MigrateAsync`) | translations follow renamed fields and lose removed or no longer translatable ones |
| Change the default locale | [swapped](#changing-the-default) |

**Content Manager.** For a localized type with at least one enabled locale besides the default, the entry form shows a
language switcher: the main values (named after the default locale), then each locale - a "·" after a name marks "Not
translated yet". A locale opens the translation form at `GET`/`POST /admin/content/{type}/{id}/translations/{locale}`
(`ContentController.Translate` / `SaveTranslation`): only the translatable fields, with the shared fields' values not
shown ([Content Manager](../pages/content-manager.md)).

**Public site.** On a translated request `ContentRenderService` shows, for localized types, the **published**
translation over the published values - in content lists, in linked entries, and as the title of a detail page - and
puts the locale's prefix on item links that start with `/`.

### API

`/api/entries` reads and writes translations with `?locale={code}` ([headless API](headless-api.md#structured-content)):

| Request | With `?locale=de` |
| --- | --- |
| `GET /api/entries/{type}`, `GET /api/entries/{type}/{idOrSlug}` | a localized type's entries with the locale's values over their own: published translations, or the working copies with `status=draft`; linked entries too; `title` recomputed when a translation exists. Every entry gets `"locale": "de"` (also for a type that is not localized, and for the default locale) |
| `PUT /api/entries/{type}/{id}` | saves the **translation** (body `{ "data": { ... }, "publish": false }`; `version` is not checked): 400 `'{type}' is not localized: turn on Localized on its content type to translate its entries.` for other types; `publish: true` or a type without drafts needs `entries.publish` (else 403); invalid values → 400 `{ error: "The translation is not valid.", errors }`; answers the working copy in the locale (`depth=0`). With the default locale's code: the normal update |

An unknown or disabled code answers **400** "Unknown or disabled locale 'xx'. Enabled locales other than the default:
de, fr." Search (`q`), filters, sort and the slug lookup always use the entry's own (default-locale) values.
`POST` (create), publish, unpublish and delete take no locale; `/api/content/pages` has no translations.

## Admin language

Which language the admin screens are shown in is resolved per `/admin` request - except the UI docs under
`/admin/docs/ui`, which stay English - by `AdminLanguageMiddleware` (after `UseAuthentication`, so the user is known;
before `UseAuthorization`), through `AdminLanguageService.ResolveAsync`:

1. the user's own choice - **Profile → Admin language** (`POST /admin/profile/language`, `User.AdminLanguage`; empty =
   "Site default");
2. else the site's admin language - **Internationalization → Admin language**
   (`POST /admin/internationalization/admin-language`, setting `i18n.adminLanguage` - an `i18n.*` key is managed
   there only: [Environment Variables](../pages/environment-variables.md) lists it but refuses to change it);
3. else English.

Only offered languages count: a stored code whose catalog is gone falls through to the next step. Both values are
cached for 30 seconds (`IMemoryCache`) and cleared on change in this instance. The middleware sets the scoped
`AdminText` to the language, `RequestLocale.UiLanguage` and `CurrentUICulture`. Other requests keep the content locale.
Changes are audited (`settings.adminlanguage.changed`); the confirmation toast already shows in the new language.

### Catalogs

**English is the text in the code**; every other language is one JSON catalog,
`src/DotNetForge.Web/Localization/Admin/<code>.json`, embedded in the web assembly (resource name
`AdminText/<code>.json`, see `DotNetForge.Web.csproj`) and loaded once by `AdminTranslations` (singleton). **A language
is offered exactly when its catalog ships**; the list shows English first, then the others by their own name
(`LocaleCodes.SuggestedName`).

Twenty catalogs ship, each complete (every text of the three sections):

| Code | Language | Code | Language | Code | Language | Code | Language |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `ar` | Arabic (right to left) | `es` | Spanish | `ja` | Japanese | `ru` | Russian |
| `bn` | Bengali | `fr` | French | `ko` | Korean | `ta` | Tamil |
| `de` | German | `hi` | Hindi | `mr` | Marathi | `te` | Telugu |
| `id` | Indonesian | `it` | Italian | `nl` | Dutch | `tr` | Turkish |
| `pt` | Portuguese | `ur` | Urdu (right to left) | `vi` | Vietnamese | `zh-Hans` | Chinese (Simplified) |

Keys are the English text; `{0}`, `{1}` mark values:

```json
{
  "strings": { "Save": "Speichern", "{0} entries": "{0} Einträge" },
  "script":  { "Copied.": "Kopiert." },
  "builder": { "Publish": "Veröffentlichen" }
}
```

| Section | Used by | Reaches the browser |
| --- | --- | --- |
| `strings` | Razor views and controllers (server side) | no |
| `script` | `dnf.js` and the admin scripts in `wwwroot/js/admin/` | as JSON in `<meta name="dnf-text">`, written by `_AdminLayout` on every admin page (`AdminText.ScriptJson`; omitted in English) |
| `builder` | the page builder editor (`wwwroot/js/builder/`) | in its own `<meta name="dnf-text">`, written by `PageBuilder/Index.cshtml` (`AdminText.BuilderJson`) |

A text a catalog lacks shows in English. Right-to-left languages (`ar`, `fa`, `he`, `ur`) set `dir="rtl"` on the admin
layout and the page builder.

### Writing translatable text

| Where | How |
| --- | --- |
| Admin views | `@T["Save"]`, `@T["{0} entries", count]` - `T` is the request's `AdminText`, injected by `Areas/Admin/Views/_ViewImports.cshtml` |
| Admin controllers (toasts, form errors) | `T["Saved '{0}'.", page.Title]` - `AdminControllerBase.T` |
| A text translated later through a variable (labels in a list, a note chosen in code) | mark the literal with `AdminText.Key("Not on the site: this page is disabled.")`, then show it with `T[value]`; the sidebar labels in `AdminNavigation.Tree` are found the same way |
| `dnf.js` and admin scripts | `dnfText("Copied.")`, `dnfText("{0} selected", n)` (`window.dnfText`) |
| Page builder editor | `import { t } from "./ui/i18n.js"` (path relative to the module): `t("Saved")`, `t("{0} selected", n)` |
| Texts the UI components write themselves ("Loading", "Never", "5 min ago", "Actions"...) | the tag helper takes `var t = DnfText.For(ViewContext)` and calls `t("Never")`, which asks the request's `IDnfText`; the CMS registers `AdminText` as `IDnfText`, so they follow the admin language. An app without one keeps English |

The key must be a **string literal** (`T[$"..."]` and verbatim keys are refused by the catalog test), and values are
formatted culture-neutrally (`CultureInfo.InvariantCulture`).

**Add a string:** wrap it as above, then add it to the right section (`strings` / `script` / `builder`) of **every**
catalog. `AdminTextCatalogTests` (`tests/DotNetForge.Tests`) collects every key from the code - `T["..."]`, `_t["..."]`
and `AdminText.Key("...")` in the web project's `.cs` and `.cshtml` files (the UI docs excepted), the sidebar tree,
the components' `t("...")` in `src/DotNetForge.UI`, `dnfText("...")` / `t("...")` in `dnf.js` and
`wwwroot/js/admin/`, and `t("...")` in `wwwroot/js/builder/` - and fails, listing each problem, when a catalog misses
a key, a translation has different `{n}` placeholders, or a catalog keeps a key the code no longer uses.

**Add a language:** add `src/DotNetForge.Web/Localization/Admin/<code>.json` (a valid locale code; the file name is
the language code shown in the pickers) with all three sections complete - the test lists what is missing - and
rebuild. It is offered on Profile and Internationalization at once; nothing else registers it. A right-to-left
language also needs its code in `AdminTranslations.RightToLeftLanguages` (today `ar`, `fa`, `he`, `ur`).

**Translating.** A translator works on one catalog file per language. Every key is required - `AdminTextCatalogTests`
fails on a missing key, a changed `{n}` placeholder or a key the code no longer uses - so a new text in the code
blocks the build's tests until every catalog has it. The hardest part is sentence fragments: where markup splits a
sentence (a link or `<code>` inside it, "… first." after a link), each fragment is its own key and has to read well
around the untranslated part, in that language's word order. `AdminLanguageTests.Every_admin_screen_in_german_has_no_english_left`
renders every admin screen in German and fails on visible text that is still the English of a translated key (text
shown through a variable that was never marked). The shipped catalogs are machine-translated: a review by a native
speaker is recommended before relying on a language.

### What stays English

The admin language covers the admin screens, the sidebar, toasts, the component texts and the page builder. These
stay English in every language: messages produced by services (validation and rule messages, e.g. from
`LocaleService`, `PageService`, `ContentEntryService`), the details of audit entries (their action labels are
translated), emails (their
text comes from [email templates](email.md)), the public site's own UI text (built-in layout, sign-in and setup
screens), and the [UI docs](../pages/ui-docs.md) (`/admin/docs/ui`: skipped by `AdminLanguageMiddleware` and the
catalog test; their layout writes `<html lang="en" dir="ltr">` and no `dnf-text` meta, so components and scripts there
stay English too).

## Permissions

| Action | Needs |
| --- | --- |
| Open Internationalization | `Settings` / `read` (default: Super Admin, Admin) |
| Add, edit, enable/disable, set default, delete locales; site admin language | `Settings` / `update` |
| Own admin language | any admin user (Profile) |
| Translate a page's texts and layout | whatever edits the page ([page permissions](content-pages-and-routing.md#page-permissions)); publishing a translated layout needs `Collection types` / `publish` |
| Translate an entry | `Entries` / `update` (or `update.own` on own entries); publishing the translation needs `publish` |
| API | `entries.read`; `PUT ?locale=` needs `entries.update` (+ `entries.publish` to publish) |

## Audit

| Action | Key | Written by |
| --- | --- | --- |
| `LocaleCreated` | `locale.created` | `LocaleService.AddAsync` (details `Enabled` / `Disabled`) |
| `LocaleUpdated` | `locale.updated` | `LocaleService.UpdateAsync`, only when something changed (details e.g. `name "Deutsch" → "German", disabled`) |
| `LocaleDefaultChanged` | `locale.default.changed` | `LocaleService.SetDefaultAsync` (details `Was English (en)`) |
| `LocaleDeleted` | `locale.deleted` | `LocaleService.DeleteAsync` (details `Removed n page and m entry translation(s)`) |
| `ContentTranslated` | `content.translated` | `PageTranslationService.ApplyTextsAsync` (details `Deutsch (de): title and SEO texts`); `PageBuilderService.AuditLayoutAsync` when a translated layout is published or unpublished (details `Layout published - Deutsch (de)` / `Layout unpublished - Deutsch (de)`) |
| `EntryTranslated` | `entry.translated` | `EntryTranslationService.SaveAsync` (entity `Entry`; details `Deutsch (de), published` / `, draft`) |
| `AdminLanguageChanged` | `settings.adminlanguage.changed` | `AdminLanguageService` - the site's (entity `Setting`) or a user's own (entity `User`, details `Site default` when reset) |

The Audit Logs screen shows `locale.*` entries with the `globe` icon. Translations raise no [webhook](webhooks.md)
events. Full list: [audit logging](audit-logging.md).

## Data Management

`Locales`, `PageTranslations` and `ContentEntryTranslations` are exported and imported with everything else
([Data Management](../pages/data-management.md)); `Users.AdminLanguage` and the `i18n.adminLanguage` setting travel
with their tables.

## Known limitations

- **One URL per page**: translated pages use the page's slug under the prefix (`/de/about`); there are no per-locale
  slugs.
- Only top-level text, long text and rich text fields are translatable; text inside groups and repeaters, select
  labels and media alt texts are shared.
- Search, filters, sort and detail-page lookups (`/products/[slug]`) use the default-locale values. A detail page
  whose route field is a translatable text field gets item links with the translated value, which its lookup doesn't
  find - bind detail pages to a slug field (never translated).
- Extensions read live pages through `IPublishedPages` with default-locale paths only (e.g. a sitemap lists no
  translations).
- The twenty admin catalogs are machine-translated and not yet reviewed by native speakers ([translating](#catalogs)).
- The [What stays English](#what-stays-english) list.

## Where to change things

- **A locale rule**: `LocaleService` (and `LocaleCodes` for the code format); keep the default switch in one
  transaction and clear `LocaleCatalog` after every change.
- **More translated content** (another entity): a translation table keyed by (owner id, `LocaleId`), an
  `IDefaultLocaleSwap` registered in `DependencyRegistration`, deletion in `LocaleService.DeleteAsync`, and the overlay
  in the render path - never per-locale columns.
- **More translatable entry fields**: `EntryTranslations.TranslatableTypes` / `Fields` (pure code, unit-tested);
  rendering and the API merge through `EntryTranslations.Merge`, so they follow.
- **Locale-aware links in a theme**: use `ViewData["Languages"]`, `CurrentPath`, `HomePath` and the `Menu` paths -
  never build `/` or slug URLs by hand.
- **A new admin screen**: wrap every text (`@T[...]`, `T[...]`, `dnfText`), then add the keys to every catalog.

## Planned (not implemented)

Target behaviour not built yet. Nothing in this section exists in the code.

### Requirements

- **Reviewed catalogs**: a native speaker's review of each machine-translated catalog.
- **Content History**: translations recorded as versions of their page or entry (Content History is a
  [placeholder](../pages/module-placeholders.md)).
- **Per-locale slugs**: a translated URL segment per page (`/de/ueber-uns`), with the language links following it.
- **Webhook events** for entry translations.
- The remaining English texts of [What stays English](#what-stays-english) in the admin language where they reach
  the admin (service messages), and the public site's UI text in the content locale.

### Acceptance criteria

- [x] Locales have code, display name, is-default and enabled; codes are validated as BCP-47 and unique.
- [x] Exactly one default locale exists at all times; concurrent default changes never produce two defaults.
- [x] Changing the default asks for confirmation, is atomic and enables the new default.
- [x] The default locale can be neither disabled nor deleted; deleting another locale deletes its translations after a confirmation.
- [x] A missing translation falls back to the default locale's value.
- [x] Only users with `Settings` / `update` manage locales; translating follows the page and entry permissions.
- [x] Locale, default-locale, translation and admin-language changes are audited.
- [x] English plus twenty admin languages ship with complete catalogs (`AdminTextCatalogTests`).
- [ ] Every catalog is reviewed by a native speaker.
- [ ] Translations are recorded in Content History.
- [ ] Translated pages can have their own slugs.
