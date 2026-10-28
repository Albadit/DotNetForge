namespace DotNetForge.Shared.Constants;

/// <summary>
/// The sign-in providers of Users &amp; Permissions Plugin → Providers (.docs/features/authentication.md#providers):
/// the built-in Email provider and the built-in OAuth providers, by key. <c>DataSeeder</c> keeps one
/// <c>AuthProvider</c> row per key; <c>OAuthProviderCatalog</c> (DotNetForge.Web) holds how each one signs in.
/// </summary>
public static class AuthProviderKeys
{
    public const string Email = "email";

    /// <summary>The built-in OAuth providers in the order the screen lists them (key, display name).</summary>
    public static readonly IReadOnlyList<(string Key, string Name)> OAuth = new[]
    {
        ("google", "Google"),
        ("microsoft", "Microsoft"),
        ("apple", "Apple"),
        ("facebook", "Facebook"),
        ("github", "GitHub"),
        ("linkedin", "LinkedIn"),
        ("x", "X (Twitter)"),
        ("discord", "Discord"),
        ("gitlab", "GitLab"),
        ("twitch", "Twitch"),
    };
}
