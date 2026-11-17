---
name: frontend
description: >-
  Plans, builds and changes web user interfaces: pages, dashboards, layouts, components, forms, tables, dialogs, design
  tokens and themes, responsive and mobile-first layouts, SEO metadata, and client-side performance (React, Next.js,
  Tailwind and similar). Use when creating or modifying anything users see in the browser, e.g. "create a new
  dashboard", "add a settings page", "make this page responsive", "fix the layout on mobile", "improve the landing
  page SEO".
---

# Frontend

Build interfaces that fit the existing product: reuse its components and tokens, work at every screen size, are
accessible by construction, and are verified in a real browser before they are called done.

## Project standards

If the repository has `docs/website_design_and_development_standards.md`, it is the quality bar for this work. Read
the sections the task touches (map in [reference/standards-map.md](reference/standards-map.md)) instead of
rediscovering best practice. The project's `CLAUDE.md` records how the codebase actually does things (UI library,
layers, i18n); where they differ, `CLAUDE.md` wins on *how*, the standards on *how good*.

## Workflow

1. **Find what exists.** Locate the nearest similar page or component and the design system (tokens, shared UI
   primitives, field components, layout shell). Extend them; do not create a parallel variant.
2. **Place the code by the project's layers** (e.g. `app/` composes, `features/<name>/` owns domain UI, shared
   primitives in `components/`). A new page needs its route, metadata, permissions and navigation entry.
3. **Build mobile-first.** No horizontal scroll at 320 px; touch targets at least 24 x 24 px; content readable at
   200 % zoom; check 375, 768, 1024, 1440 and 1920 px.
4. **Accessible by construction** (WCAG 2.2 AA): semantic elements and landmarks, one `h1` and ordered headings,
   labels on every control, keyboard operation and visible focus, accessible names that contain the visible text,
   no colour-only state. Use the UI library's components rather than hand-rolled ARIA.
5. **States:** loading, empty, error and success for every data view; errors say what to do next; no dead buttons.
6. **Strings:** user-visible text goes through the project's i18n in every supported language.
7. **Public pages:** unique title and description, canonical URL, structured headings, `noindex` for anything behind
   sign-in; server-render what search engines must read.
8. **Performance:** no heavy library on a page that does not need it (dynamic import), images sized and lazy below
   the fold, no layout shift; measure with Lighthouse on a production build when speed is the task.
9. **Verify:** lint (zero warnings), type check, the relevant tests, then open the page in the browser at a phone
   and a desktop width and read the console. Add the page to the project's route/crawl inventory if it has one.

## Boundaries

- Deep accessibility or UX audits: `usability`. Measured performance work: `performance`. Data access, server
  actions, route handlers: `backend`. Writing or restructuring tests: `testing`.
- Never hide information in the UI as a security measure: authorization belongs on the server or in the database.
