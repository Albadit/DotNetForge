namespace DotNetForge.Web.Services;

/// <summary>
/// Automatic backups (.docs/pages/data-management.md): every few minutes it asks <see cref="DatabaseTransferService"/> whether
/// the schedule of the Data Management screen is due (on, installed, the interval since the last backup passed) and then
/// stores one, pruning old automatic backups. Off by default; quiet when off.
/// </summary>
public sealed class AutomaticBackupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _services;
    private readonly ILogger<AutomaticBackupService> _logger;

    public AutomaticBackupService(IServiceProvider services, ILogger<AutomaticBackupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the database initialize/seed first.
        var delay = StartupDelay;
        while (await DelayAsync(delay, stoppingToken))
        {
            delay = Interval;
            try
            {
                using var scope = _services.CreateScope();
                var transfer = scope.ServiceProvider.GetRequiredService<DatabaseTransferService>();
                if (await transfer.BackupDueAsync(stoppingToken))
                {
                    // A busy gate (an export or import running) just means: try again next tick.
                    var outcome = await transfer.CreateBackupAsync(userId: null, automatic: true, stoppingToken);
                    if (outcome.Succeeded)
                    {
                        _logger.LogInformation("Automatic backup {FileName} stored.", outcome.Record?.FileName);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // The database may not exist yet (pre-install); log and retry next tick.
                _logger.LogWarning(ex, "Automatic backup check failed; will retry.");
            }
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
