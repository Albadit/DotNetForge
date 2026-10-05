namespace DotNetForge.Abstractions.Security;

/// <summary>Abstracts the current UTC time so token/lockout logic is testable.</summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

/// <summary>Secure, salted, adaptive password hashing (.docs/features/security.md).</summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password. The result embeds the salt and parameters.</summary>
    string Hash(string password);

    /// <summary>Verifies a plaintext password against a stored hash in constant time.</summary>
    bool Verify(string password, string hash);
}

/// <summary>The one-time output of minting an API token.</summary>
/// <param name="Plaintext">The full secret, shown to the user exactly once.</param>
/// <param name="Hash">The salted hash that is persisted.</param>
/// <param name="Prefix">A short, non-secret prefix stored in clear for list identification.</param>
public readonly record struct GeneratedApiToken(string Plaintext, string Hash, string Prefix);

/// <summary>Generates and verifies API token secrets (.docs/features/headless-api.md,
/// .docs/features/security.md).</summary>
public interface IApiTokenFactory
{
    /// <summary>Mints a new token: a one-time plaintext, its salted hash, and a non-secret prefix.</summary>
    GeneratedApiToken Generate();

    /// <summary>Verifies a presented plaintext token against a stored salted hash, in constant time.</summary>
    bool Verify(string plaintext, string hash);

    /// <summary>The non-secret prefix portion of a presented token, used to look up candidates.</summary>
    string ExtractPrefix(string plaintext);
}

/// <summary>Computes the HMAC signature sent with every webhook delivery (.docs/features/webhooks.md).</summary>
public interface IWebhookSigner
{
    /// <summary>Returns the lowercase hex HMAC-SHA256 of <paramref name="payload"/> under <paramref
    /// name="secret"/>.</summary>
    string Sign(string secret, string payload);

    /// <summary>Generates a new random signing secret.</summary>
    string GenerateSecret();
}
