using System.Threading.Channels;
using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services.Webhooks;

/// <summary>A change that may fire webhooks: its event key and the entity it is about.</summary>
public sealed record WebhookEvent(Guid EventId, string Key, string EntityType, string? EntityId, string? EntityName, DateTime OccurredAt);

/// <summary>
/// Delivers webhook events out of band (.docs/features/webhooks.md#delivery). <see cref="Raise"/> (called by
/// <c>AuditService</c> for every saved audit entry) only queues the event in memory, so the request that changed
/// something never waits. In the background each event becomes one pending <see cref="WebhookDelivery"/> per enabled,
/// subscribed webhook; due attempts are sent (<see cref="WebhookSender"/>), and a failed one is followed by the next
/// attempt after an exponential backoff until <see cref="MaxAttempts"/>. Pending attempts live in the database, so they
/// survive a restart and are shared by every instance (an attempt is claimed with a short lease before it is sent).
/// Loop guard: changes made by a request that carries <see cref="WebhookSender.DeliveryHeader"/> raise nothing, and a
/// webhook takes at most <see cref="MaxEventsPerMinute"/> events a minute.
/// </summary>
public sealed class WebhookDispatcher : BackgroundService
{
    public const int QueueCapacity = 1000;
    public const int MaxAttempts = 5;
    public const int MaxEventsPerMinute = 120;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private const int BatchSize = 20;

    private readonly Channel<WebhookEvent> _queue = Channel.CreateBounded<WebhookEvent>(
        new BoundedChannelOptions(QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });

    private readonly SemaphoreSlim _due = new(0, 1);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WebhookDispatcher> _logger;
    private DateTime _prunedAt = DateTime.MinValue;

    public WebhookDispatcher(IServiceScopeFactory scopes, ILogger<WebhookDispatcher> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>The wait before attempt <paramref name="attempt"/> + 1: 30 s, 2 min, 8 min, 32 min.</summary>
    public static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(30 * Math.Pow(4, attempt - 1));

    /// <summary>Queues the webhook event a successful audit action stands for, if any; never blocks.</summary>
    public void Raise(string auditAction, string? entityId, string? entityName, bool success, HttpContext? request)
    {
        if (!success || WebhookPayload.EventFor(auditAction) is not { } key)
        {
            return;
        }

        if (request?.Request.Headers.ContainsKey(WebhookSender.DeliveryHeader) == true)
        {
            // A receiver calling back with the delivery header: its changes don't fire webhooks again (loop guard).
            _logger.LogInformation("Webhook event {Event} not raised: the change came from a webhook delivery.", key);
            return;
        }

        var entityType = key.StartsWith("entry.", StringComparison.Ordinal) ? "entry" : "media";
        if (!_queue.Writer.TryWrite(new WebhookEvent(Guid.NewGuid(), key, entityType, entityId, entityName, DateTime.UtcNow)))
        {
            _logger.LogWarning("The webhook queue is full; {Event} for {EntityId} was dropped.", key, entityId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(QueueLoopAsync(stoppingToken), DeliveryLoopAsync(stoppingToken));
    }

    /// <summary>Turns <paramref name="e"/> into one pending attempt per enabled webhook subscribed to it.</summary>
    public async Task EnqueueAsync(WebhookEvent e, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>();
        var webhooks = (await db.Webhooks.AsNoTracking().Where(w => w.Enabled).ToListAsync(ct))
            .Where(w => w.Events.Contains(e.Key)).ToList();
        if (webhooks.Count == 0)
        {
            return;
        }

        var appUrl = scope.ServiceProvider.GetRequiredService<AppEnvironment>().AppUrl;
        var body = WebhookPayload.Build(e.EventId, e.Key, e.OccurredAt, e.EntityType, e.EntityId, e.EntityName,
            await WebhookPayload.DataAsync(db, e.Key, e.EntityId, appUrl, ct));
        var minuteAgo = DateTime.UtcNow.AddMinutes(-1);
        foreach (var webhook in webhooks)
        {
            var recent = await db.WebhookDeliveries.CountAsync(d => d.WebhookId == webhook.Id && d.AttemptNumber == 1 && d.RequestTimestamp > minuteAgo, ct);
            var limited = recent >= MaxEventsPerMinute;
            db.WebhookDeliveries.Add(new WebhookDelivery
            {
                WebhookId = webhook.Id,
                EventId = e.EventId,
                Event = e.Key,
                TargetUrl = webhook.Url,
                RequestBody = body,
                RequestTimestamp = DateTime.UtcNow,
                Outcome = limited ? WebhookOutcome.Failure : WebhookOutcome.Pending,
                NextAttemptDate = limited ? null : DateTime.UtcNow,
                Detail = limited ? $"Skipped: more than {MaxEventsPerMinute} events in a minute (loop guard)." : null,
            });
        }

        await db.SaveChangesAsync(ct);
        Wake();
    }

    /// <summary>Sends every due attempt (a batch at a time); returns how many were sent. The tests call it directly.</summary>
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var sent = 0;
        while (!ct.IsCancellationRequested)
        {
            await using var scope = _scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<DotNetForgeDbContext>();
            var now = DateTime.UtcNow;
            var due = await db.WebhookDeliveries.AsNoTracking()
                .Where(d => d.Outcome == WebhookOutcome.Pending && d.NextAttemptDate <= now)
                .OrderBy(d => d.NextAttemptDate)
                .Select(d => new { d.Id, d.NextAttemptDate })
                .Take(BatchSize)
                .ToListAsync(ct);
            if (due.Count == 0)
            {
                return sent;
            }

            var sender = services.GetRequiredService<WebhookSender>();
            var protector = WebhookService.SecretProtector(services.GetRequiredService<IDataProtectionProvider>());
            foreach (var item in due)
            {
                // Claim it: another instance may have taken it meanwhile. The lease makes it due again if this one dies.
                var leaseUntil = DateTime.UtcNow.Add(Lease);
                var claimed = await db.WebhookDeliveries
                    .Where(d => d.Id == item.Id && d.Outcome == WebhookOutcome.Pending && d.NextAttemptDate == item.NextAttemptDate)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.NextAttemptDate, leaseUntil), ct);
                if (claimed == 0)
                {
                    continue;
                }

                var delivery = await db.WebhookDeliveries.FirstAsync(d => d.Id == item.Id, ct);
                var webhook = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == delivery.WebhookId, ct);
                if (webhook is null || !webhook.Enabled)
                {
                    delivery.Outcome = WebhookOutcome.Failure;
                    delivery.NextAttemptDate = null;
                    delivery.RequestTimestamp = DateTime.UtcNow;
                    delivery.Detail = "Cancelled: the webhook was disabled.";
                }
                else
                {
                    try
                    {
                        await sender.SendAsync(webhook, WebhookService.UnprotectSecret(protector, webhook.Secret), delivery, ct);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        // Anything unexpected is a failed attempt like any other - retried, and never retried forever.
                        _logger.LogError(ex, "Sending webhook delivery {DeliveryId} failed.", delivery.Id);
                        delivery.Outcome = WebhookOutcome.Failure;
                        delivery.NextAttemptDate = null;
                        delivery.RequestTimestamp = DateTime.UtcNow;
                        delivery.Detail = $"Not sent: {ex.GetType().Name}.";
                    }

                    webhook.LastDelivery = delivery.RequestTimestamp;
                    sent++;
                    if (delivery.Outcome == WebhookOutcome.Failure && delivery.AttemptNumber < MaxAttempts)
                    {
                        var next = DateTime.UtcNow.Add(Backoff(delivery.AttemptNumber));
                        delivery.Detail += $" Retrying at {next:HH:mm:ss} UTC.";
                        db.WebhookDeliveries.Add(new WebhookDelivery
                        {
                            WebhookId = webhook.Id,
                            EventId = delivery.EventId,
                            Event = delivery.Event,
                            TargetUrl = webhook.Url,
                            RequestBody = delivery.RequestBody,
                            AttemptNumber = delivery.AttemptNumber + 1,
                            RequestTimestamp = next,
                            NextAttemptDate = next,
                        });
                    }
                    else if (delivery.Outcome == WebhookOutcome.Failure)
                    {
                        delivery.Detail += $" Gave up after {MaxAttempts} attempts.";
                    }
                }

                await db.SaveChangesAsync(ct);
            }
        }

        return sent;
    }

    private void Wake()
    {
        if (_due.CurrentCount == 0)
        {
            try
            {
                _due.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already signalled.
            }
        }
    }

    private async Task QueueLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var e in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await EnqueueAsync(e, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Queuing webhook event {Event} for {EntityId} failed.", e.Key, e.EntityId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task DeliveryLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueAsync(stoppingToken);
                if (DateTime.UtcNow - _prunedAt > TimeSpan.FromHours(1))
                {
                    await PruneAsync(stoppingToken);
                }

                await _due.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The database may be briefly unavailable: try again on the next round.
                _logger.LogError(ex, "Sending webhook deliveries failed.");
                await Task.Delay(PollInterval, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    /// <summary>The delivery log keeps <see cref="Retention"/>; older finished attempts are deleted.</summary>
    private async Task PruneAsync(CancellationToken ct)
    {
        _prunedAt = DateTime.UtcNow;
        await using var scope = _scopes.CreateAsyncScope();
        var cutoff = DateTime.UtcNow.Subtract(Retention);
        await scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>().WebhookDeliveries
            .Where(d => d.Outcome != WebhookOutcome.Pending && d.RequestTimestamp < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
