using DotNetForge.Data;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// Applies page publish/unpublish schedules (content_manager.md): on a fixed interval it publishes pages
/// whose <c>ScheduledPublishDate</c> has passed and unpublishes pages whose <c>ScheduledUnpublishDate</c>
/// has passed. Runs against a scoped <see cref="DotNetForgeDbContext"/>; the schedule date is cleared once
/// applied so it fires exactly once.
/// </summary>
public sealed class ScheduledPublishingService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _services;
    private readonly ILogger<ScheduledPublishingService> _logger;

    public ScheduledPublishingService(IServiceProvider services, ILogger<ScheduledPublishingService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the database initialize/seed first.
        if (!await DelayAsync(StartupDelay, stoppingToken))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ApplySchedulesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // The database may not exist yet (pre-install); log and retry next tick.
                _logger.LogWarning(ex, "Scheduled publishing run failed; will retry.");
            }

            if (!await DelayAsync(Interval, stoppingToken))
            {
                break;
            }
        }
    }

    private async Task ApplySchedulesAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>();
        var now = DateTime.UtcNow;

        // Public liveness is derived from the schedule at render time (HomeController.Live); this job only
        // finalizes passed schedules so the stored state stays tidy: a reached publish date is cleared (the
        // page is already live), and a reached unpublish date takes the page offline for good.
        var duePublish = await db.Pages
            .Where(p => p.ScheduledPublishDate != null && p.ScheduledPublishDate <= now)
            .ToListAsync(ct);
        foreach (var page in duePublish)
        {
            page.ScheduledPublishDate = null;
            page.UpdatedDate = now;
        }

        var dueUnpublish = await db.Pages
            .Where(p => p.ScheduledUnpublishDate != null && p.ScheduledUnpublishDate <= now)
            .ToListAsync(ct);
        foreach (var page in dueUnpublish)
        {
            page.Published = false;
            page.ScheduledUnpublishDate = null;
            page.UpdatedDate = now;
        }

        if (duePublish.Count > 0 || dueUnpublish.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Scheduler finalized {Publish} publish and {Unpublish} unpublish schedule(s).",
                duePublish.Count, dueUnpublish.Count);
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
