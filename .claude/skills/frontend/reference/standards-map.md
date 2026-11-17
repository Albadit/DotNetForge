# Which part of the website standards applies

Sections of `docs/website_design_and_development_standards.md` (repository root), by task. Read the listed
sections, not the whole document. Its sections 15 to 19 (workflow phases, deliverables, final verification and
report) are for full audits and release checks, not for an ordinary change.

| Task | Sections |
| --- | --- |
| New page, dashboard or feature UI | 2.3 design system, 3 responsive, 4.3 architecture, 6.2 accessibility, 11 UX and reliability |
| Visual or brand change | 2.1 to 2.3 design principles and design system, 3.1 responsive |
| Layout broken on a screen size | 3.1 requirements, 3.2 viewport list |
| Component or form | 2.3 (controls, inputs, states), 6.2 (labels, errors, focus), 11 (validation, feedback, duplicate actions) |
| Accessibility fix | 6 (WCAG 2.2 AA; no WCAG 3.0 compliance claims) |
| Public page, metadata, SEO | 7 (titles, canonical, sitemap, robots, structured data, hreflang, redirects) |
| Speed, bundle, Core Web Vitals | 8 (targets: LCP <= 2.5 s, INP <= 200 ms, CLS <= 0.1; measure on a production build) |
| Client-side security, headers, CSP | 9.1, 9.3 |
| Refactor, code quality, lint | 4.1, 5 (zero ESLint warnings, strict TypeScript, no suppressions) |
| Reusable component or content structure | 10 |
| Tests for UI | 12 (and the `testing` skill) |

Rules from the document that apply to every change: no generic template look (2), no horizontal scroll and no
overlap at 320 to 1920 px (3), zero ESLint errors and warnings and zero type errors (5.4), WCAG 2.2 AA (6),
never fabricate scores or claim untested results (8.2, 16), and the non-negotiable list in section 17.
