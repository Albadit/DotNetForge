using System.Security.Cryptography;
using DotNetForge.Abstractions.Security;

namespace DotNetForge.Infrastructure.Security;

/// <summary>
/// Generates cryptographically strong API tokens of the form <c>dnf_{prefix}_{secret}</c>. The full
/// value is returned once at creation; only a salted PBKDF2 hash is persisted, and the non-secret
/// <c>dnf_{prefix}</c> portion is stored in clear to look up candidates at verification time
/// (api_tokens.md, security.md).
/// </summary>
public sealed class ApiTokenFactory : IApiTokenFactory
{
    private const string TokenPrefix = "dnf";
    private const int PrefixBytes = 4;     // 8 hex chars
    private const int SecretBytes = 32;    // 256-bit secret
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 60_000;

    public GeneratedApiToken Generate()
    {
        var prefix = $"{TokenPrefix}_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(PrefixBytes))}";
        var secret = Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));
        var plaintext = $"{prefix}_{secret}";
        return new GeneratedApiToken(plaintext, ComputeHash(plaintext), prefix);
    }

    public bool Verify(string plaintext, string hash)
    {
        if (string.IsNullOrEmpty(plaintext) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        var parts = hash.Split('$');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(plaintext, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public string ExtractPrefix(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        // Prefix is the first two underscore-separated segments: dnf_xxxxxxxx
        var segments = plaintext.Split('_');
        return segments.Length >= 2 ? $"{segments[0]}_{segments[1]}" : plaintext;
    }

    private static string ComputeHash(string plaintext)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(plaintext, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return string.Join('$', Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
