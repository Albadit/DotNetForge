# Page builder (screen)

## Purpose

The full-screen editor of one content page's layout. How layouts work, are stored, validated and rendered:
[page builder](../features/page-builder.md). It is also where structured content meets a page: a **content list** shows
entries of a content type, designed once as an item template whose elements are bound to fields
([structured content → content lists](../features/structured-content.md#content-lists)).

## Route / Navigation

| Item | Value |
| --- | --- |
| Route | `GET /admin/pages/{id}/builder` (plus `canvas`, `preview`, `draft.css` and the JSON API under the same path, including `content/types`, `content/link-targets`, `content/preview`, `content/entries` - [page builder → API](../features/page-builder.md#api)). The old `/admin/content/{id}/builder` answers 301 here |
| Opened from | **Open page builder** on the [Pages](pages.md#page-editor) screen's **Layout** tab |
| Sidebar / breadcrumbs | none - the screen replaces the admin shell; the ✕ in the top bar returns to Pages (`#layout`) |

## Relevant source files

```text
src/DotNetForge.Web/Areas/Admin/Controllers/PageBuilderController.cs   screen, canvas, preview, JSON API
src/DotNetForge.Web/Components/PageLayoutViewComponent.cs             canvas mode: the mount the editor renders into
src/DotNetForge.Web/Areas/Admin/Views/PageBuilder/Index.cshtml         the document: config as data-* attributes
src/DotNetForge.Web/wwwroot/js/builder/                                editor (main.js entry; ui/ = this screen's UI)
src/DotNetForge.Web/wwwroot/js/builder/ui/data-panel.js                content lists: the Data tab and the "from content" binding rows
src/DotNetForge.Web/wwwroot/js/builder/editor/content.js               content types and the entries the canvas previews
src/DotNetForge.Web/wwwroot/js/builder/editor/presets.js               ready-made blocks, incl. the Content lists group
src/DotNetForge.Web/wwwroot/builder/editor.css, editor-host.css        editor styles
```

## Page layout

```text
┌ top bar: ✕ · page name + Live / Layout not published · Desktop | Tablet | Mobile · W (canvas width) · zoom ▾ · undo redo · Saved · Preview · ⋯ · Publish
├ left: Layers | Insert                (tree of the layout / components and ready-made blocks)
├ center: canvas                       (the page with its theme at the device width in an iframe; selection, handles, grid, guides)
└ right: inspector                     Design | Content (Data for a content list) | Advanced for the selection; page status and breakpoints otherwise
```

The editor is light or dark like the admin screens: it uses the admin's theme setting (`localStorage` `dnf-theme`, the
admin's Light / Dark / System menu), and changing it in the editor's ⋯ menu changes it for the admin too. The canvas shows the page
light or dark independently of it (⋯ menu: Page: light / dark, light by default). Both are remembered per
browser. Menus close with Esc or a click outside.

## Functionality

| Task | How |
| --- | --- |
| Start | A page without a layout opens a dialog: empty page or a ready-made start (Hero, Text and image, Three cards). |
| Add | Drag from **Insert** onto the canvas, or click an item to add it next to the selection. |
| Select | Click on the canvas (double-click goes one layer deeper), in **Layers** (Shift = range, Ctrl = add), or with Tab / Enter / Esc. |
| Move / resize | Drag on the canvas: grid cells, free x/y, or the position in a stack; handles resize (Shift keeps the ratio); arrows nudge. |
| Edit text | Double-click a heading, text or button (bold, italic, underline, strike, link, lists, subheading in the toolbar). |
| Style | **Design**: position, flow (free / vertical / horizontal / grid), size (fixed, hug, fill, percent), gaps, padding, margin, opacity, radius, typography, fill colour and image, stroke, shadow. Edits apply to the selected device; a dot marks a device override (click to reset). |
| Content | **Content**: the component's properties from its schema (text, links, image with **Browse** = media library + upload, video URL, icons, link lists, comments). |
| Show content | **Insert → Content lists**: **Card grid**, **List**, **Carousel**, **Table**, **Detail** add a content list (in its own section) and select it; its **Data** tab opens. |
| Set up a content list | **Data** tab: **Content type** (choosing one maps the item's elements to fields automatically) and a status line ("3 of 3 published entries match. Manage Blog posts"); **Entries**: Show **Many** / **One**; for one: **Which entry** - the one named in the page URL (URL part + Matches field; the default on a `[param]` page), a specific entry, or the first that matches; for many: **Filters** (field, condition, value), **Sort by** + A→Z / Z→A, **Items**, **Pages (Previous / Next links)**; **Display**: Look grid / list / carousel / table, Columns (grid, per device), table columns (field + heading); **Item link**: Detail page (pages with a `[param]` slug) and Link pattern (`/news/{slug}`); **Texts**: when nothing matches, Previous / Next labels; **Fields**: **Map fields automatically**. |
| Bind an element | Select an element inside the item (Layers, or double-click on the canvas): each text, link, image or video property shows a row **<property> · from content** with a field menu (fields that fit first; "Fixed value (not from content)" unbinds; "Text with fields…") and the template (`{name}`, `From {price} USD`). Bound text can't be edited on the canvas (a toast says where to change it). |
| Advanced | Devices it shows on, audience (everyone / signed-in / anonymous), CSS classes and variables, hidden, locked. |
| Layers | Rename (double-click), hide, lock, drag to reorder or nest; right-click for the context menu. |
| Save | Automatic (status in the top bar; click it to save now, Ctrl+S). |
| Publish / unpublish / discard | **Publish** (needs `publish`); ⋯ → Unpublish, Discard unpublished changes. |
| Preview | **Preview** opens the draft in the site's page view in a new tab. |

Keyboard shortcuts: press `?` (list in `js/builder/editor/shortcuts.js`).

The canvas previews a content list with real **published** entries (the same query visitors get, fetched again when
the list's query changes); the first copy of the item is the editable template, the others are copies of it (a click
on them selects the list). Without a content type or without published entries the bare template shows.

## Permissions

The user must be allowed to edit the page: `Collection types` / `update`, `update.own` on a page they created, or a
role with a page **edit** grant ([page permissions](../features/content-pages-and-routing.md#page-permissions)).
Publishing and unpublishing need `publish`; uploading images needs `Media` / `create`.

## Validation / error handling / loading

The server normalizes every save; a structurally invalid document is refused with a toast. A save conflict opens the
conflict dialog. Lost connection: the status shows *Offline* and saving retries with back-off. Leaving with unsaved
changes asks first.

## Related pages

[Pages](pages.md), [Content Manager](content-manager.md) (the entries content lists show), [Content types](content-types.md),
[Media](media.md), [page builder feature](../features/page-builder.md), [structured content](../features/structured-content.md).
