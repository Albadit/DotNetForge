---
name: orchestrator
description: >-
  Chooses the skills a task needs and applies them: matches the task against every available skill's description,
  loads only the relevant ones (several when the task spans areas, none when nothing fits), and re-checks when the
  task grows. Use at the start of any task to implement, fix, optimise, design, refactor, secure, test, document or
  research something in the codebase.
argument-hint: Describe the task
---

# Orchestrator

Pick the skills a task needs, load them, and do the task with them. This file names no skills on purpose: the
skill list in this session (each skill's name and description, kept current by Claude Code as skills are added,
changed or removed) is the catalogue, so a new or updated skill takes part without any change here.

## Steps

1. **Understand the task.** The outcome wanted, the parts of the system it touches (interface, server code,
   database, tests, deployment, documentation, ...) and the constraints in `CLAUDE.md`. Read code only as far as
   needed to know what is touched.
2. **Match.** Compare the task with each available skill's description: what it does and when to use it. Choose a
   skill when its "use when" fits a part of this task. Prefer the most specific skill for each part; a task that
   spans areas gets one skill per area (a new field through the database, an endpoint and a form: the server and
   the interface skills; a slow page: the performance and the interface skills).
3. **Load only those,** with the Skill tool. No skill for an area the task does not touch, no second copy of a
   skill already loaded in this conversation, and no skill at all when none fits (a question about a regular
   expression needs none).
4. **Do the task** by the loaded skills' workflows, using the built-in tools as usual. Where instructions differ,
   the order is: the user's request, `CLAUDE.md`, the project's standards documents, then the skills; between two
   skills, the one specific to the area being changed decides.
5. **Re-check as the task moves.** When it turns out to touch another area (a form change needs a migration, a bug
   is a security hole, the user adds a requirement), load the skill for that area then.
6. **Say which skills were used,** in one line of the final message, so the choice can be corrected.

## Tags

`#<skill-name>` anywhere in the message (for example `#security`) loads that skill as well, whatever the match says.
