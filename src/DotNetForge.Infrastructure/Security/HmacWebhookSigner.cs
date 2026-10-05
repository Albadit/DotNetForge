using System.Security.Cryptography;
using System.Text;
using DotNetForge.Abstractions.Security;

namespace DotNetForge.Infrastructure.Security;

/// <summary>
/// Signs webhook payloads with HMAC-SHA256 over the raw request body (.docs/features/webhooks.md). The receiver
/// recomputes the HMAC with the shared secret and compares. Output is lowercase hex.
/// </summary>
public sealed class HmacWebhookSigner : IWebhookSigner
{
    public string Sign(string secret, string payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentNullException.ThrowIfNull(payload);

        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexStringLower(hash);
    }

    public string GenerateSecret() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}
