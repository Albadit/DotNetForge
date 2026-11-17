---
name: testing
description: >-
  Writes, organises and runs automated tests and quality gates: unit, component, integration, API, end-to-end
  (Playwright), accessibility (axe), visual regression, database and RLS tests, performance budgets (Lighthouse),
  fixtures, flaky-test fixes, coverage thresholds and CI test steps. Use when asked to add or improve tests,
  "implement frontend and backend testing", set up test infrastructure, fix a failing or flaky test, or verify a
  change end to end.
---

# Testing

A test is worth having when it fails for the defect it names and passes otherwise, every time, on any machine.

## Project standards

If present, read the testing sections of `docs/website_design_and_development_standards.md` (section 12, plus 18
for full verification) and `docs/backend_design_and_development_standards.md` (sections 6 and 11). The repository's
test READMEs (for example `frontend/test/README.md`, `tools/db/tests/README.md`) say where each kind of test lives, which
command runs it and what it needs; put new tests there instead of inventing a layout.

## Workflow

1. **Pick the cheapest level that proves the behaviour:** pure logic as a unit test; a server action or route with
   its dependencies faked as an integration test; a policy or trigger in SQL under the real database roles; a user
   journey, layout or accessibility in the browser. Do not repeat the same check at every level.
2. **Make it fail first** when fixing a bug: the test reproduces the defect against the old code.
3. **Deterministic:** no arbitrary sleeps (wait for a condition), no order dependence between files, its own data
   created and cleaned up, fixed clocks and seeds where time or randomness matter. Retries hide defects.
4. **Assert outcomes, not implementation:** rendered text, HTTP status, database rows, the e-mail received, the
   downloaded file. Stable selectors: roles, labels, test ids, never CSS paths.
5. **Browser checks fail on what a user would hit:** console errors and warnings, failed requests, error screens,
   serious axe violations, horizontal scrolling. Widen an ignore list only for a cause proven to be outside the app,
   with the reason next to it.
6. **Never weaken a test to make it pass:** no deleted assertions, skipped tests, loosened thresholds or broad
   mocks. Fix the code, or record why the expectation was wrong.
7. **Run it the way CI does,** then the neighbouring suites, and report the exact command and the passed, failed and
   skipped counts. A test that was not run is reported as not run.

## Boundaries

- Deciding which behaviour is wrong or untested (equivalence classes, boundaries, state machines): `functionality`.
- Load and soak tests as a performance investigation: `performance`. CI pipeline design beyond test steps:
  `operations`.
