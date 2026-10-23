# Page builder (screen)

## Purpose

The full-screen editor of one content page's layout. How layouts work, are stored, validated and rendered:
[page builder](../features/page-builder.md).

## Route / Navigation

| Item | Value |
| --- | --- |
| Route | `GET /admin/content/{id}/builder` (plus `canvas`, `preview`, `draft.css` and the JSON API under the same path) |
| Opened from | **Open page builder** on the [Content Manager](content-manager.md#page-editor)'s **Layout** tab |
| Sidebar / breadcrumbs | none - the screen replaces the admin shell; the ✕ in the top bar returns to the Content Manager (`#layout`) |

## Relevant source files

```text
src/DotNetForge.Web/Areas/Admin/Controllers/PageBuilderController.cs   screen, canvas, preview, JSON API
src/DotNetForge.Web/Components/PageLayoutViewComponent.cs             canvas mode: the mount the editor renders into
src/DotNetForge.Web/Areas/Admin/Views/PageBuilder/Index.cshtml         the document: config as data-* attributes
src/DotNetForge.Web/wwwroot/js/builder/                                editor (main.js entry; ui/ = this screen's UI)
src/DotNetForge.Web/wwwroot/builder/editor.css, editor-host.css        editor styles
```

## Page layout

```text
┌ top bar: ✕ · page name + Live / Layout not published · Desktop | Tablet | Mobile · W (canvas width) · zoom ▾ · undo redo · Saved · Preview · ⋯ · Publish
├ left: Layers | Insert                (tree of the layout / components and ready-made blocks)
├ center: canvas                       (the page with its theme at the device width in an iframe; selection, handles, grid, guides)
└ right: inspector                     Design | Content | Advanced for the selection; page status and breakpoints otherwise
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
| Advanced | Devices it shows on, audience (everyone / signed-in / anonymous), CSS classes and variables, hidden, locked. |
| Layers | Rename (double-click), hide, lock, drag to reorder or nest; right-click for the context menu. |
| Save | Automatic (status in the top bar; click it to save now, Ctrl+S). |
| Publish / unpublish / discard | **Publish** (needs `publish`); ⋯ → Unpublish, Discard unpublished changes. |
| Preview | **Preview** opens the draft in the site's page view in a new tab. |

Keyboard shortcuts: press `?` (list in `js/builder/editor/shortcuts.js`).

## Permissions

The user must be allowed to edit the page: `Collection types` / `update`, `update.own` on a page they created, or a
role with a page **edit** grant ([page permissions](../features/content-pages-and-routing.md#page-permissions)).
Publishing and unpublishing need `publish`; uploading images needs `Media` / `create`.

## Validation / error handling / loading

The server normalizes every save; a structurally invalid document is refused with a toast. A save conflict opens the
conflict dialog. Lost connection: the status shows *Offline* and saving retries with back-off. Leaving with unsaved
changes asks first.

## Related pages

[Content Manager](content-manager.md), [Media](media.md), [page builder feature](../features/page-builder.md).
