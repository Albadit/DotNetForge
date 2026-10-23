namespace DotNetForge.Shared.Constants;

/// <summary>
/// Names of the ASP.NET Core rate-limiting policies registered in <c>Startup/DependencyRegistration.cs</c>
/// (.docs/features/security.md). Partitioned per client IP address.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Credential endpoints: sign-in and the setup wizard (brute-force protection).</summary>
    public const string Credentials = "credentials";

    /// <summary>The headless API.</summary>
    public const string Api = "api";

    /// <summary>Anonymous form posts on the public site, e.g. an extension's contact form (spam protection).</summary>
    public const string Forms = "forms";
}
