using System.Net.Http.Headers;
using System.Text;
using DotNetForge.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SmsNotifications;

/// <summary>
/// Entry point of the SMS Notifications provider. It provides an <see cref="ISmsSender"/> (a log gateway for trying it
/// out, Twilio for real messages) and subscribes to the CMS audit events listed in "notifyOn". The host runs the
/// handler in the background, so a slow SMS gateway never slows down the request that caused the event.
/// </summary>
public sealed class SmsNotificationsExtension : IExtension
{
    public void ConfigureServices(IServiceCollection services, ExtensionContext context)
    {
        var settings = new SmsSettings(
            context.GetString("gateway", "log")!,
            context.GetString("to", string.Empty)!,
            context.GetString("from", string.Empty)!,
            context.GetStringList("notifyOn").ToHashSet(StringComparer.OrdinalIgnoreCase),
            context.GetEnvironmentValue("TWILIO_ACCOUNT_SID"),
            context.GetEnvironmentValue("TWILIO_AUTH_TOKEN"));
        services.AddSingleton(settings);

        if (string.Equals(settings.Gateway, "twilio", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<ISmsSender, TwilioSmsSender>(client => client.Timeout = TimeSpan.FromSeconds(10));
        }
        else
        {
            services.AddSingleton<ISmsSender, LogSmsSender>();
        }

        services.AddAuditEventHandler<SmsAuditHandler>(context);
    }
}

public sealed record SmsSettings(string Gateway, string To, string From, IReadOnlySet<string> NotifyOn,
    string? TwilioAccountSid, string? TwilioAuthToken);

/// <summary>Sends one text message. Other extensions can inject it too.</summary>
public interface ISmsSender
{
    Task SendAsync(string to, string text, CancellationToken cancellationToken);
}

/// <summary>Turns the selected audit events into SMS messages.</summary>
public sealed class SmsAuditHandler : IAuditEventHandler
{
    private readonly SmsSettings _settings;
    private readonly ISmsSender _sender;

    public SmsAuditHandler(SmsSettings settings, ISmsSender sender)
    {
        _settings = settings;
        _sender = sender;
    }

    public Task HandleAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (!_settings.NotifyOn.Contains(auditEvent.Action) || string.IsNullOrWhiteSpace(_settings.To))
        {
            return Task.CompletedTask;
        }

        return _sender.SendAsync(_settings.To, Describe(auditEvent), cancellationToken);
    }

    private static string Describe(AuditEvent e)
    {
        var what = e.Action switch
        {
            "user.login.failed" => $"Failed sign-in for {e.EntityDisplay ?? "an unknown account"}",
            "contactform.submitted" => $"New contact message from {e.EntityDisplay}",
            "plugin.enabled" => $"Extension enabled: {e.EntityDisplay}",
            "plugin.disabled" => $"Extension disabled: {e.EntityDisplay}",
            _ => $"{e.Action}: {e.EntityDisplay ?? e.EntityId}",
        };
        var text = $"[DotNetForge] {what} ({e.TimestampUtc:yyyy-MM-dd HH:mm} UTC, IP {e.IpAddress})";
        return text.Length <= 300 ? text : text[..300];
    }
}

/// <summary>Writes the message to the application log instead of sending it - for trying the extension out.</summary>
public sealed class LogSmsSender : ISmsSender
{
    private readonly ILogger<LogSmsSender> _logger;

    public LogSmsSender(ILogger<LogSmsSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string to, string text, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SMS to {To}: {Text}", to, text);
        return Task.CompletedTask;
    }
}

/// <summary>Sends through Twilio's Messages API (https://www.twilio.com/docs/messaging/api/message-resource).</summary>
public sealed class TwilioSmsSender : ISmsSender
{
    private readonly HttpClient _http;
    private readonly SmsSettings _settings;

    public TwilioSmsSender(HttpClient http, SmsSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task SendAsync(string to, string text, CancellationToken cancellationToken)
    {
        if (_settings.TwilioAccountSid is not { } sid || _settings.TwilioAuthToken is not { } token)
        {
            throw new InvalidOperationException("The twilio gateway needs TWILIO_ACCOUNT_SID and TWILIO_AUTH_TOKEN in .env.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(sid)}/Messages.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = to,
                ["From"] = _settings.From,
                ["Body"] = text,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:{token}")));

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
