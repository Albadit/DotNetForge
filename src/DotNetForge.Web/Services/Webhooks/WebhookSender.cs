using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using DotNetForge.Abstractions.Security;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;

namespace DotNetForge.Web.Services.Webhooks;

/// <summary>
/// Sends one delivery attempt (.docs/features/webhooks.md#delivery): <c>POST</c> of the stored JSON body with the
/// webhook's custom headers, then the CMS's own headers (which win over a custom header of the same name) - the event,
/// the event id and the HMAC-SHA256 signature over the exact body bytes. A connection error, a timeout
/// (<see cref="Timeout"/>) or any non-2xx answer - redirects are not followed - is a failure. Fills the attempt's log
/// fields; never throws for the receiver's problems.
/// </summary>
public sealed class WebhookSender
{
    public const string ClientName = "webhooks";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public const string EventHeader = "X-DotNetForge-Event";
    public const string DeliveryHeader = "X-DotNetForge-Delivery";
    private const int MaxResponseChars = 2000;

    private readonly IHttpClientFactory _clients;
    private readonly IWebhookSigner _signer;

    public WebhookSender(IHttpClientFactory clients, IWebhookSigner signer)
    {
        _clients = clients;
        _signer = signer;
    }

    /// <param name="secret">The secret in clear; null when it can't be read here - the attempt then fails without sending.</param>
    public async Task SendAsync(Webhook webhook, string? secret, WebhookDelivery delivery, CancellationToken ct)
    {
        delivery.RequestTimestamp = DateTime.UtcNow;
        delivery.TargetUrl = webhook.Url;
        delivery.NextAttemptDate = null;
        if (secret is null)
        {
            Fail(delivery, "Not sent: the signing secret can't be read on this site (it came with an import). Rotate the secret.");
            return;
        }

        var body = delivery.RequestBody ?? "{}";
        using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
        {
            Content = new StringContent(body, new UTF8Encoding(false), "application/json"),
        };
        foreach (var header in WebhookService.ReadHeaders(webhook.HeadersJson))
        {
            if (!string.IsNullOrWhiteSpace(header.Key))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value ?? string.Empty);
            }
        }

        // The CMS's headers last, replacing any custom header of the same name.
        foreach (var (name, value) in new[]
        {
            (EventHeader, delivery.Event),
            (DeliveryHeader, delivery.EventId.ToString()),
            (WebhookEvents.SignatureHeader, _signer.Sign(secret, body)),
        })
        {
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }

        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DotNetForge-Webhooks", "1.0"));

        var watch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            using var response = await _clients.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            delivery.ResponseStatusCode = (int)response.StatusCode;
            delivery.ResponseBody = await ReadStartAsync(response, timeout.Token);
            delivery.ResponseTimeMs = watch.ElapsedMilliseconds;
            if (response.IsSuccessStatusCode)
            {
                delivery.Outcome = WebhookOutcome.Success;
                delivery.Detail = null;
            }
            else
            {
                Fail(delivery, $"The receiver answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            delivery.ResponseTimeMs = watch.ElapsedMilliseconds;
            Fail(delivery, $"No answer within {Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            delivery.ResponseTimeMs = watch.ElapsedMilliseconds;
            Fail(delivery, "Could not connect: " + ex.Message);
        }
    }

    private static void Fail(WebhookDelivery delivery, string detail)
    {
        delivery.Outcome = WebhookOutcome.Failure;
        delivery.Detail = detail.Length > 4000 ? detail[..4000] : detail;
    }

    private static async Task<string?> ReadStartAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new char[MaxResponseChars];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
        return read == 0 ? null : new string(buffer, 0, read);
    }
}
