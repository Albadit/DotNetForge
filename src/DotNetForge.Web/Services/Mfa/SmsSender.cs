using System.Net.Http.Headers;
using System.Text;
using DotNetForge.Shared.Configuration;

namespace DotNetForge.Web.Services.Mfa;

/// <summary>Sends a text message (.docs/features/mfa.md#sms).</summary>
public interface ISmsSender
{
    bool IsConfigured { get; }

    /// <summary>Sends <paramref name="text"/> to <paramref name="phoneNumber"/> (E.164); throws when the service refuses.</summary>
    Task SendAsync(string phoneNumber, string text, CancellationToken ct = default);
}

/// <summary>
/// Twilio's Messages API over plain HTTP (no SDK), with <c>TWILIO_ACCOUNT_SID</c>, <c>TWILIO_AUTH_TOKEN</c> and
/// <c>TWILIO_FROM</c> (a number, or a Messaging Service SID starting with MG) from <c>.env</c>.
/// </summary>
public sealed class TwilioSmsSender : ISmsSender
{
    private readonly HttpClient _http;
    private readonly SmsSettings _settings;

    public TwilioSmsSender(HttpClient http, AppEnvironment env)
    {
        _http = http;
        _settings = env.Sms;
    }

    public bool IsConfigured => _settings.IsConfigured;

    public async Task SendAsync(string phoneNumber, string text, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("SMS is not configured (TWILIO_* in .env).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid!)}/Messages.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));
        var from = _settings.From!;
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = phoneNumber,
            [from.StartsWith("MG", StringComparison.Ordinal) ? "MessagingServiceSid" : "From"] = from,
            ["Body"] = text,
        });
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // The status only: Twilio's error body repeats the phone number.
            throw new HttpRequestException($"Twilio answered {(int)response.StatusCode}.");
        }
    }
}
