# Scheduled publishing

Content pages can carry a UTC publish and unpublish time. Visibility is decided per request by
`HomeController.Live` ([liveness](content-pages-and-routing.md#liveness)); the background job only finalizes the
stored state after a schedule has passed. The fields are edited on the
[Content Manager](../pages/content-manager.md#save-page-settings) (**Publishing** section of the form).

## `ScheduledPublishingService`

`src/DotNetForge.Web/Services/ScheduledPublishingService.cs`, registered with `AddHostedService`.

| Aspect | Behaviour |
| --- | --- |
| Start | waits `StartupDelay` = 5 s (`Program.cs` already runs `DatabaseInitializer` before the host starts hosted services; the delay is a safety margin) |
| Interval | `Interval` = 15 s between runs |
| Scope | creates a DI scope per run, resolves `DotNetForgeDbContext` |
| Tenants | all tenants (no filter) |
| Instances | runs in **every** app instance - no lock or leader election. Safe because a run is idempotent: it only clears passed dates and sets `Published = false`, so two instances at worst write the same values twice (and both log) |
| Due publish | pages with `ScheduledPublishDate <= UtcNow` → `ScheduledPublishDate = null`, `UpdatedDate = now` (Published unchanged) |
| Due unpublish | pages with `ScheduledUnpublishDate <= UtcNow` → `Published = false`, `ScheduledUnpublishDate = null`, `UpdatedDate = now` |
| Save | one `SaveChangesAsync` when anything was due; logs `Scheduler finalized {Publish} publish and {Unpublish} unpublish schedule(s).` (Information) |
| Errors | any exception except cancellation → `LogWarning("Scheduled publishing run failed; will retry.")`; next tick retries |
| Shutdown | cancellation of `stoppingToken` ends the loop cleanly |

## Behaviour to be aware of

- A due publish date is cleared even when the page is **not** `Published`: a draft with a past publish date simply
  loses the date; it does not go live.
- Unpublishing is permanent: after the job runs, the author must re-check **Published**.
- Public visibility switches at the exact scheduled second (per-request check), while stored fields change up to
  ~15 s later. Lists in the admin (tree status dots) reflect stored fields, so a page can show "on" for a few seconds
  after its unpublish time.
- No audit entries and no webhooks are produced (`entry.publish`/`entry.unpublish` exist only as constants).
- The job runs before installation too; it simply finds nothing due.
- Setting or changing a schedule needs the `publish` permission
  ([content permissions](content-pages-and-routing.md#permissions)); the job itself runs without a user.

## Where to change things

- Interval or delay: the two `static readonly TimeSpan` fields.
- What "due" means or side effects (audit, webhooks): `ApplySchedulesAsync`. Keep visibility logic in
  `HomeController.Live`, not here. Any new side effect must stay idempotent or get a cross-instance lock, because
  every instance runs the job.
