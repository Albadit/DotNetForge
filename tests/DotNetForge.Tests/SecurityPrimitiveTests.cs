using DotNetForge.Infrastructure.Security;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>Password hashing must be salted, one-way, and verify correctly (security.md).</summary>
public sealed class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_is_not_plaintext_and_is_salted()
    {
        var a = _hasher.Hash("Sup3rSecret");
        var b = _hasher.Hash("Sup3rSecret");

        Assert.DoesNotContain("Sup3rSecret", a);
        Assert.NotEqual(a, b); // distinct salts
    }

    [Fact]
    public void Verify_accepts_correct_and_rejects_wrong()
    {
        var hash = _hasher.Hash("Sup3rSecret");

        Assert.True(_hasher.Verify("Sup3rSecret", hash));
        Assert.False(_hasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Verify_rejects_malformed_hash() =>
        Assert.False(_hasher.Verify("whatever", "not-a-valid-hash"));
}

/// <summary>API tokens are generated with a prefix and verified against a salted hash (api_tokens.md).</summary>
public sealed class ApiTokenFactoryTests
{
    private readonly ApiTokenFactory _factory = new();

    [Fact]
    public void Generated_token_verifies_against_its_hash()
    {
        var token = _factory.Generate();

        Assert.StartsWith("dnf_", token.Plaintext);
        Assert.StartsWith("dnf_", token.Prefix);
        Assert.DoesNotContain(token.Plaintext, token.Hash); // hash is not the plaintext
        Assert.True(_factory.Verify(token.Plaintext, token.Hash));
        Assert.False(_factory.Verify(token.Plaintext + "x", token.Hash));
    }

    [Fact]
    public void Extracted_prefix_matches_generated_prefix()
    {
        var token = _factory.Generate();
        Assert.Equal(token.Prefix, _factory.ExtractPrefix(token.Plaintext));
    }
}

/// <summary>Webhook payloads are signed deterministically with HMAC-SHA256 (webhooks.md).</summary>
public sealed class WebhookSignerTests
{
    private readonly HmacWebhookSigner _signer = new();

    [Fact]
    public void Signature_is_deterministic_for_same_secret_and_body()
    {
        const string secret = "shared-secret";
        const string body = "{\"event\":\"entry.create\"}";

        Assert.Equal(_signer.Sign(secret, body), _signer.Sign(secret, body));
    }

    [Fact]
    public void Different_secret_yields_different_signature()
    {
        const string body = "{\"event\":\"entry.create\"}";
        Assert.NotEqual(_signer.Sign("secret-a", body), _signer.Sign("secret-b", body));
    }

    [Fact]
    public void Generated_secrets_are_unique() =>
        Assert.NotEqual(_signer.GenerateSecret(), _signer.GenerateSecret());
}
