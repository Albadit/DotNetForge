using DotNetForge.Abstractions.Messaging;
using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// The <see cref="IEmailSender"/> the app uses: sends through the transport (the keyed <see cref="TransportKey"/>
/// service, <c>SmtpEmailSender</c>) and writes an <see cref="EmailLogEntry"/> for every attempt - the Email log of
/// Email → Configuration. The entry is written in its own scope, so a caller's unsaved changes are never saved with it,
/// and a failure to log never hides the send's own result.
/// </summary>
public sealed partial class LoggingEmailSender : IEmailSender
{
    public const string TransportKey = "email-transport";

    /// <summary>How long log entries are kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private readonly IEmailSender _transport;
    private readonly IServiceScopeFactory _scopes;
    private readonly AppEnvironment _env;
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender([FromKeyedServices(TransportKey)] IEmailSender transport, IServiceScopeFactory scopes,
        AppEnvironment env, ILogger<LoggingEmailSender> logger)
    {
        _transport = transport;
        _scopes = scopes;
        _env = env;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await _transport.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await WriteAsync(message, EmailConfigurationService.DescribeFailure(ex, _env.Email));
            throw;
        }

        await WriteAsync(message, error: null);
    }

    private async Task WriteAsync(EmailMessage message, string? error)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>();
            db.Add(new EmailLogEntry
            {
                Recipient = Truncate(message.To, 320),
                Subject = Truncate(message.Subject, 500),
                Template = message.Template is null ? null : Truncate(message.Template, 100),
                Success = error is null,
                Error = error is null ? null : Truncate(error, 300),
            });
            await db.SaveChangesAsync(CancellationToken.None);
            var cutoff = DateTime.UtcNow - Retention;
            await db.Set<EmailLogEntry>().Where(e => e.SentAt < cutoff).ExecuteDeleteAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogWriteFailed(ex.GetType().Name);
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    [LoggerMessage(Level = LogLevel.Warning, Message = "The email log entry could not be written ({ErrorType}).")]
    private partial void LogWriteFailed(string errorType);
}
