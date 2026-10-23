using System.Threading.Channels;
using DotNetForge.Extensions;
using DotNetForge.Sdk;

namespace DotNetForge.Web.Services;

/// <summary>
/// Hands every saved audit entry to the enabled extensions' <see cref="IAuditEventHandler"/>s
/// (.docs/features/audit-logging.md#extension-handlers). <see cref="Publish"/> only queues the event, so a slow or
/// failing handler never delays or breaks the request that wrote the entry; handlers run one event at a time in the
/// background, each in its own DI scope with a timeout. When the queue is full new events are dropped (and logged).
/// </summary>
public sealed class AuditEventDispatcher : BackgroundService
{
    public const int QueueCapacity = 1000;
    public static readonly TimeSpan HandlerTimeout = TimeSpan.FromSeconds(30);

    private readonly Channel<AuditEvent> _queue = Channel.CreateBounded<AuditEvent>(
        new BoundedChannelOptions(QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    private readonly IReadOnlyList<AuditHandlerContribution> _handlers;
    private readonly ExtensionCatalog _catalog;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AuditEventDispatcher> _logger;

    public AuditEventDispatcher(IEnumerable<AuditHandlerContribution> handlers, ExtensionCatalog catalog,
        IServiceScopeFactory scopes, ILogger<AuditEventDispatcher> logger)
    {
        _handlers = handlers.ToList();
        _catalog = catalog;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>Queues <paramref name="auditEvent"/>; never blocks.</summary>
    public void Publish(AuditEvent auditEvent)
    {
        if (_handlers.Count > 0 && !_queue.Writer.TryWrite(auditEvent))
        {
            _logger.LogWarning("Audit event queue is full; {Action} was not passed to extension handlers.", auditEvent.Action);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var auditEvent in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                foreach (var handler in _handlers)
                {
                    await DispatchAsync(handler, auditEvent, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task DispatchAsync(AuditHandlerContribution handler, AuditEvent auditEvent, CancellationToken stoppingToken)
    {
        try
        {
            if (!await _catalog.IsActiveAsync(handler.ExtensionId, stoppingToken))
            {
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(HandlerTimeout);
            await using var scope = _scopes.CreateAsyncScope();
            var instance = (IAuditEventHandler)scope.ServiceProvider.GetRequiredService(handler.HandlerType);
            await instance.HandleAsync(auditEvent, timeout.Token);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // Handlers are extension code: report and carry on with the next one.
            _logger.LogError(ex, "Audit handler {Handler} of extension {ExtensionId} failed on {Action}.",
                handler.HandlerType.FullName, handler.ExtensionId, auditEvent.Action);
        }
    }
}
