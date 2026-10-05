# Screen document template (`.docs/pages/<screen>.md`)

Every screen document uses these sections, in this order. Keep sections that don't apply but say so in one line
("Not applicable.", "None.") - readers and agents rely on the fixed shape. Small sections may be merged into one
heading (e.g. `## Validation / error handling / loading`) when each is a single line. Use
[`.docs/pages/content-manager.md`](../../../../.docs/pages/content-manager.md) as the model for a complex screen and
[`.docs/pages/users.md`](../../../../.docs/pages/users.md) for a simple one.

~~~markdown
# <Screen name as shown in ViewData["Title"]>

## Purpose
Why it exists, who uses it (which roles), what they accomplish. Link the feature doc that owns the rules.

## Route / Navigation
Table: route(s) with HTTP methods, query/route parameters, sidebar entry (group → label), parent, child screens,
deep links, where it is linked from.

## Relevant source files
```text
path/File.cs     what it contributes
```

## Page layout
ASCII tree from top to bottom, using the real labels in **bold** where the UI shows them.

## Components
Per component: purpose, inputs (model/ViewData/ViewBag), outputs (form target), events, state, when it appears.

## Functionality
One `###` per action: trigger → validation → service/method → data → backend effect (+ audit action) → result/UI
update → errors. Sequence diagram for the most important one.

## Data used by the page
Entities, view models, configuration, claims - and where each comes from (tenant-scoped or not).

## State
URL, TempData, ModelState, DOM/JS, cookie, database - and when each changes.

## Permissions
Exact attributes (`AdminArea`, `[Authorize(Roles = ...)]`), what happens without permission. Say explicitly when
there is no finer-grained check.

## Validation
Browser vs. server rules, messages, duplicate prevention, invalid states the UI still allows.

## Error handling
Table: failure → detection → what the user sees → recovery.

## Loading behaviour
What is server-rendered, what is async, indicators (usually none).

## Empty states
Exact empty-state text.

## User interactions
Clicks, drag and drop, confirms, keyboard. Say "None" when there are none.

## Dependencies
```text
Controller
├── Service → its dependencies
└── ...
```

## Page flow
Mermaid flowchart specific to this screen (real routes and names).

## Related pages
Navigates to / receives navigation from / shares functionality with (links).

## Important implementation details
Non-obvious behaviour: derived values, hidden conditions, caching, ordering, cross-tenant reads.

## Known limitations
Only real ones, verified in code.

## Planned (not implemented)
Optional. Target behaviour not built yet: Requirements, User flows, Rules and validation, Edge cases, Acceptance
criteria (`- [x]` only when verified in code). Rules owned by a feature doc stay in that feature doc's Planned section.

## Extension points
Where a developer adds the next feature - which class/method/file, which rule to respect.
~~~

Rules:
- Names, routes, labels and messages exactly as in the code (copy them).
- Rules owned by a feature doc are linked, not repeated.
- After writing: add/update the row in `.docs/README.md` (Screens table) and `.docs/architecture/pages.md` (route map).
