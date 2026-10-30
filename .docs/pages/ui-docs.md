# UI docs

## Purpose

The documentation of the [DotNetForge UI library](../ui/README.md) inside the admin, modelled on a component library's
docs site: guides (introduction, using the library, customization, theming, behaviours, extensions) and one page per
component with live examples - each shown as a **Preview**, its exact **Razor** and the rendered **HTML** - a styling
reference (classes, states, tokens), an **API** table generated from the tag helper, accessibility notes and guidelines.
For anyone building an admin screen or an extension; also the pages the visual regression tests photograph. Read-only.

## Route / Navigation

| Screen | Route | View | Title (h1) |
| --- | --- | --- | --- |
| Introduction | `GET /admin/docs/ui` (= `/admin/docs/ui/introduction`) | `UiDocs/Guides/Introduction.cshtml` | "Introduction" |
| A guide | `GET /admin/docs/ui/{slug}` | `UiDocs/Guides/{Slug}.cshtml` | the guide's title |
| All components | `GET /admin/docs/ui/components` | `UiDocs/Components/Index.cshtml` | "Components" |
| A component | `GET /admin/docs/ui/components/{slug}` | `UiDocs/Components/{Slug}.cshtml` | the component's name |

An unknown slug answers 404. The pages, their order, titles, descriptions and categories are listed only in
`UiDocsCatalog` (`src/DotNetForge.Web/Areas/Admin/UiDocs/UiDocsCatalog.cs`).

| Item | Value |
| --- | --- |
| Entry point | **UI docs** in the account menu (sidebar footer), shown only to Super Admins and Developers; not in the sidebar |
| Layout | its own, `_UiDocsLayout.cshtml` - no admin shell: a header (brand, search, **Back to admin**, theme menu), the docs navigation on the left (a drawer below 64rem), the page, **On this page** on the right (hidden below 80rem), previous / next page links at the bottom |

## Relevant source files

```text
src/DotNetForge.Web/Areas/Admin/Controllers/UiDocsController.cs     Index, Components, Component(slug), Guide(slug) ([Authorize(Policy = AuthorizationPolicies.ApiDocs)])
src/DotNetForge.Web/Areas/Admin/UiDocs/UiDocsCatalog.cs             the pages and categories (navigation order, previous/next)
src/DotNetForge.Web/Areas/Admin/UiDocs/UiDocsTagHelpers.cs          <ui-doc-example>, <ui-doc-code>, <ui-doc-api>
src/DotNetForge.Web/Areas/Admin/UiDocs/UiDocsSource.cs              the views' embedded source (code tabs), HTML formatting, highlighting
src/DotNetForge.Web/Areas/Admin/UiDocs/TagHelperReference.cs        the API table: reflection + DotNetForge.UI.xml doc comments
src/DotNetForge.Web/Areas/Admin/Views/UiDocs/                       _UiDocsLayout, Guides/*.cshtml, Components/*.cshtml
src/DotNetForge.Web/wwwroot/css/admin/docs.css                      the docs layout, example frames, code colours, Level 3 demo rules
src/DotNetForge.Web/wwwroot/js/admin/docs.js                        On this page, navigation search ("/"), demo toast/confirm buttons, the Copy menu (Markdown)
src/DotNetForge.Web/DotNetForge.Web.csproj                          embeds Areas/Admin/Views/UiDocs/**/*.cshtml (the code tabs' source)
src/DotNetForge.UI/DotNetForge.UI.csproj                            GenerateDocumentationFile (the API tables' descriptions)
tests/DotNetForge.UiTests/DocsTests.cs, DocsVisualTests.cs          pages, tabs, search, the Copy menu; a screenshot of every example preview
```

## How a page is written

A page is a Razor view; the layout renders the category, the h1 and the lead from the catalog. Examples are
`<ui-doc-example id title description layout>` blocks: their content renders as the preview, and the code tabs show
the same content's source - read from the view itself, which the build embeds into the assembly (a published app has
no `.cshtml` files) - and its rendered HTML, one element per line. `<ui-doc-code id lang>` is a static snippet whose
text is also its source (never rendered; `@@` for `@`). `<ui-doc-api type="typeof(...)">` builds a tag helper's
table - attribute (Razor's kebab-case name), type (enums list their values), default (read from a fresh instance) and
description (the property's XML doc summary from `DotNetForge.UI.xml`). Plain `h2`/`h3` with `id`s become the **On
this page** entries; prose and plain `<table>`s are styled by `docs.css`.

Adding a component: a catalog entry, a view in `Components/`, and doc comments on its tag helper's properties.

## Actions

Nothing is saved. Example tabs switch between Preview / Razor / HTML (`data-tab-hash="false"`: the URL does not
change); **Copy** copies a code block; demo buttons open dialogs (`data-dialog-open`), toasts (`data-docs-toast`) and
the confirmation (`data-docs-confirm`). The search box filters the navigation (Enter opens the first match, `/`
focuses it).

The **Copy** menu (top right of every page, needs JavaScript and clipboard access) puts Markdown on the clipboard, ready
to paste into an AI assistant: a one-paragraph preamble describing the library (BEM `dnf-` classes, tag helpers,
`--dnf-*` tokens, Tailwind, `dnf.js`, CSP, state as attributes), then the page - title, lead, prose, every example's
Razor source as a ` ```cshtml ` block (not the preview), reference and API tables as Markdown tables, snippets with
their language. **Copy this page** converts the page you see; **Copy all docs** fetches every page of the navigation
(same session) and joins them with `---`. Both are built in the browser from the rendered pages (`docs.js`), so they
always match the docs; a toast reports the size.

## Permissions

Super Admins and Developers (the `ApiDocs` policy, like API Docs). Other roles get the access-denied screen; the
account-menu link is hidden for them.

## Accessibility

One h1 per page, a skip link, labelled navigation landmarks (Documentation, On this page, Previous and next page),
example tabs with `role="tab"` / `tabpanel` and arrow keys; previews follow the components' own rules.
`AccessibilityTests` runs axe on several docs pages in light and dark.
