using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Entities;

/// <summary>
/// A second sign-in factor a user set up on Profile → Sign-in security (.docs/features/mfa.md). One row per
/// authenticator app, email, phone number or passkey. Secrets are stored protected (ASP.NET Data Protection), never
/// in plain text.
/// </summary>
public class UserMfaMethod
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public MfaMethodType Type { get; set; }

    /// <summary>What the user sees: "Authenticator app", the passkey's name, "+41 *** *** 67".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Authenticator app: the TOTP secret, protected with Data Protection.</summary>
    public string? Secret { get; set; }

    /// <summary>SMS: the phone number in E.164 form (+41791234567).</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Passkey: the credential id.</summary>
    public byte[]? CredentialId { get; set; }

    /// <summary>Passkey: the COSE public key.</summary>
    public byte[]? PublicKey { get; set; }

    /// <summary>Authenticator app: the last accepted time step (a code works once); passkey: the signature counter.</summary>
    public long Counter { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedDate { get; set; }
}

/// <summary>A one-time recovery code (.docs/features/mfa.md#recovery-codes), stored only as a SHA-256 hash.</summary>
public class UserRecoveryCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>SHA-256 (hex) of the normalized code.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>When it was used; a used code never works again.</summary>
    public DateTime? UsedDate { get; set; }
}
