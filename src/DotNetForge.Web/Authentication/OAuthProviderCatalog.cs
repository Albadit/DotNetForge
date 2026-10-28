using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DotNetForge.Shared.Constants;

namespace DotNetForge.Web.Authentication;

/// <summary>A setting of a provider on its Providers screen (.docs/pages/providers.md#edit-a-provider).</summary>
public enum ProviderField
{
    ClientId,
    ClientSecret,

    /// <summary>Apple: the Team ID of the developer account.</summary>
    TeamId,

    /// <summary>Apple: the ID of the "Sign in with Apple" key.</summary>
    KeyId,

    /// <summary>Apple: the key file's contents (PEM, <c>-----BEGIN PRIVATE KEY-----</c>); stored protected.</summary>
    PrivateKey,

    /// <summary>GitLab: the server, <c>https://gitlab.com</c> or a self-managed instance.</summary>
    ServerUrl,

    /// <summary>Microsoft: <c>common</c> (any account), <c>consumers</c>, <c>organizations</c> or a tenant id.</summary>
    Tenant,
}

/// <summary>Who signed in, as the provider reports it. <see cref="Email"/> is set only when the provider verified it.</summary>
public sealed record ExternalProfile(string Id, string? Email, string? FirstName, string? LastName, string? Name);

/// <summary>What a profile reader gets after the code exchange.</summary>
/// <param name="TokenResponse">The token endpoint's JSON (an OpenID Connect provider's <c>id_token</c> is in it).</param>
public sealed record ProfileContext(OAuthProviderDefinition Provider, string AccessToken, JsonElement TokenResponse,
    HttpClient Backchannel, string? UserInformationEndpoint, CancellationToken CancellationToken);

/// <summary>
/// One built-in OAuth provider (.docs/features/authentication.md#built-in-oauth-providers): its endpoints, the scopes
/// that return a verified email, how to read the profile, and what the Providers screen asks for. Endpoints may use
/// <c>{server}</c> (GitLab) and <c>{tenant}</c> (Microsoft), filled from the provider's settings.
/// </summary>
public sealed record OAuthProviderDefinition(
    string Key,
    string Name,
    string ConsoleUrl,
    string Help,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string? UserInformationEndpoint,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<ProviderField> Fields,
    Func<ProfileContext, Task<ExternalProfile>> ReadProfile)
{
    /// <summary>Extra query parameters of the authorization request.</summary>
    public IReadOnlyDictionary<string, string> AuthorizeParameters { get; init; } = new Dictionary<string, string>();

    /// <summary>The provider posts the callback (Apple: <c>response_mode=form_post</c>), so its cookies need SameSite=None.</summary>
    public bool FormPost { get; init; }

    /// <summary>The token endpoint wants the client id and secret as HTTP Basic authentication (X).</summary>
    public bool ClientSecretInHeader { get; init; }

    /// <summary>The client secret is a JWT signed with <see cref="ProviderField.PrivateKey"/> (Apple).</summary>
    public bool SignedClientSecret { get; init; }

    /// <summary>Use PKCE (RFC 7636); only for providers that document it (X requires it), others may refuse the extra values.</summary>
    public bool Pkce { get; init; }

    public string Scheme => OAuthProviderCatalog.SchemePrefix + Key;

    /// <summary>The callback path to register at the provider (<c>/signin/google</c>).</summary>
    public string CallbackPath => "/signin/" + Key;
}

/// <summary>
/// The built-in OAuth providers (<see cref="AuthProviderKeys.OAuth"/>): the ten most used for signing in. Each passes
/// on an email only when the provider says it is verified - the CMS trusts that email to find or create the account.
/// </summary>
public static class OAuthProviderCatalog
{
    /// <summary>Scheme names are <c>oauth-google</c> etc., so they never collide with an extension's own scheme.</summary>
    public const string SchemePrefix = "oauth-";

    private static readonly ProviderField[] ClientCredentials = { ProviderField.ClientId, ProviderField.ClientSecret };

    public static readonly IReadOnlyList<OAuthProviderDefinition> All = new[]
    {
        new OAuthProviderDefinition("google", "Google", "https://console.cloud.google.com/apis/credentials",
            "Google Cloud console → APIs & Services → Credentials → Create credentials → OAuth client ID (Web application).",
            "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token",
            "https://openidconnect.googleapis.com/v1/userinfo", new[] { "openid", "email", "profile" }, ClientCredentials,
            ReadOpenIdUserInfoAsync)
        {
            Pkce = true,
        },
        new OAuthProviderDefinition("microsoft", "Microsoft", "https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade",
            "Microsoft Entra admin center → App registrations → New registration (Web), then Certificates & secrets → New client secret. Work and school accounts pass on an email only with the optional claim xms_edov in Token configuration.",
            "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize", "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
            null, new[] { "openid", "email", "profile" }, new[] { ProviderField.ClientId, ProviderField.ClientSecret, ProviderField.Tenant },
            ReadMicrosoftAsync)
        {
            Pkce = true,
        },
        new OAuthProviderDefinition("apple", "Apple", "https://developer.apple.com/account/resources/identifiers/list/serviceId",
            "Apple Developer → Identifiers → Services IDs (the Services ID is the client ID; enable Sign in with Apple with this return URL), then Keys → a key with Sign in with Apple. Apple only allows https return URLs.",
            "https://appleid.apple.com/auth/authorize", "https://appleid.apple.com/auth/token",
            null, new[] { "email", "name" }, new[] { ProviderField.ClientId, ProviderField.TeamId, ProviderField.KeyId, ProviderField.PrivateKey },
            ReadAppleAsync)
        {
            AuthorizeParameters = new Dictionary<string, string> { ["response_mode"] = "form_post" },
            FormPost = true,
            SignedClientSecret = true,
        },
        new OAuthProviderDefinition("facebook", "Facebook", "https://developers.facebook.com/apps",
            "Meta for Developers → Create app (Authenticate and request data from users with Facebook Login) → Facebook Login → Settings: add the redirect URI. The app ID and app secret are under App settings → Basic.",
            "https://www.facebook.com/v21.0/dialog/oauth", "https://graph.facebook.com/v21.0/oauth/access_token",
            "https://graph.facebook.com/v21.0/me?fields=id,name,first_name,last_name,email", new[] { "email", "public_profile" }, ClientCredentials,
            ReadFacebookAsync),
        new OAuthProviderDefinition("github", "GitHub", "https://github.com/settings/developers",
            "GitHub → Settings → Developer settings → OAuth Apps → New OAuth App; the callback URL is the address below.",
            "https://github.com/login/oauth/authorize", "https://github.com/login/oauth/access_token",
            "https://api.github.com/user", new[] { "read:user", "user:email" }, ClientCredentials,
            ReadGitHubAsync),
        new OAuthProviderDefinition("linkedin", "LinkedIn", "https://www.linkedin.com/developers/apps",
            "LinkedIn Developers → Create app → Products: Sign In with LinkedIn using OpenID Connect → Auth: add the redirect URL.",
            "https://www.linkedin.com/oauth/v2/authorization", "https://www.linkedin.com/oauth/v2/accessToken",
            "https://api.linkedin.com/v2/userinfo", new[] { "openid", "profile", "email" }, ClientCredentials,
            ReadOpenIdUserInfoAsync),
        new OAuthProviderDefinition("x", "X (Twitter)", "https://developer.x.com/en/portal/projects-and-apps",
            "X Developer Portal → your app → User authentication settings: OAuth 2.0, type Web App, \"Request email from users\" on, and this callback URL. Without email access X sign-in can't find an account.",
            "https://x.com/i/oauth2/authorize", "https://api.x.com/2/oauth2/token",
            "https://api.x.com/2/users/me?user.fields=confirmed_email", new[] { "users.read", "tweet.read", "users.email" }, ClientCredentials,
            ReadXAsync)
        {
            ClientSecretInHeader = true,
            Pkce = true,
        },
        new OAuthProviderDefinition("discord", "Discord", "https://discord.com/developers/applications",
            "Discord Developer Portal → New Application → OAuth2: copy the client ID, reset the secret and add the redirect.",
            "https://discord.com/oauth2/authorize", "https://discord.com/api/oauth2/token",
            "https://discord.com/api/users/@me", new[] { "identify", "email" }, ClientCredentials,
            ReadDiscordAsync),
        new OAuthProviderDefinition("gitlab", "GitLab", "https://gitlab.com/-/user_settings/applications",
            "GitLab → User settings (or Admin area) → Applications → Add new application with the scopes openid, profile and email.",
            "{server}/oauth/authorize", "{server}/oauth/token",
            "{server}/oauth/userinfo", new[] { "openid", "profile", "email" }, new[] { ProviderField.ClientId, ProviderField.ClientSecret, ProviderField.ServerUrl },
            ReadOpenIdUserInfoAsync)
        {
            Pkce = true,
        },
        new OAuthProviderDefinition("twitch", "Twitch", "https://dev.twitch.tv/console/apps",
            "Twitch Developer Console → Register Your Application (category Website Integration) with this OAuth redirect URL.",
            "https://id.twitch.tv/oauth2/authorize", "https://id.twitch.tv/oauth2/token",
            "https://id.twitch.tv/oauth2/userinfo", new[] { "openid", "user:read:email" }, ClientCredentials,
            ReadOpenIdUserInfoAsync)
        {
            // Twitch's user info has the email only when the claims are asked for.
            AuthorizeParameters = new Dictionary<string, string>
            {
                ["claims"] = """{"userinfo":{"email":null,"email_verified":null,"preferred_username":null}}""",
            },
        },
    };

    public static OAuthProviderDefinition? Find(string? key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    public static OAuthProviderDefinition? FindByScheme(string? scheme) =>
        scheme is not null && scheme.StartsWith(SchemePrefix, StringComparison.Ordinal) ? Find(scheme[SchemePrefix.Length..]) : null;

    // ---- Profile readers --------------------------------------------------------------------------------------------

    /// <summary>OpenID Connect user info (Google, LinkedIn, GitLab, Twitch): <c>email</c> counts only with <c>email_verified</c>.</summary>
    private static async Task<ExternalProfile> ReadOpenIdUserInfoAsync(ProfileContext context)
    {
        using var json = await GetJsonAsync(context, context.UserInformationEndpoint!);
        var root = json.RootElement;
        return new ExternalProfile(
            String(root, "sub") ?? throw new InvalidOperationException("The user info has no subject."),
            IsTrue(root, "email_verified") ? String(root, "email") : null,
            String(root, "given_name"), String(root, "family_name"),
            String(root, "name") ?? String(root, "preferred_username"));
    }

    /// <summary>
    /// Microsoft: from the id token. Personal accounts' emails are verified by Microsoft; a work or school account's
    /// email only counts with the <c>xms_edov</c> ("email domain owner verified") optional claim, because organizations
    /// can set any email on an account.
    /// </summary>
    private static Task<ExternalProfile> ReadMicrosoftAsync(ProfileContext context)
    {
        var token = IdTokenClaims(context.TokenResponse);
        const string PersonalAccountsTenant = "9188040d-6c67-4c5b-b112-36a304b66dad";
        var verified = String(token, "tid") == PersonalAccountsTenant || IsTrue(token, "xms_edov");
        return Task.FromResult(new ExternalProfile(
            String(token, "oid") ?? String(token, "sub") ?? throw new InvalidOperationException("The id token has no subject."),
            verified ? String(token, "email") : null,
            String(token, "given_name"), String(token, "family_name"), String(token, "name")));
    }

    /// <summary>Apple: from the id token (Apple's private relay addresses are verified too).</summary>
    private static Task<ExternalProfile> ReadAppleAsync(ProfileContext context)
    {
        var token = IdTokenClaims(context.TokenResponse);
        return Task.FromResult(new ExternalProfile(
            String(token, "sub") ?? throw new InvalidOperationException("The id token has no subject."),
            IsTrue(token, "email_verified") ? String(token, "email") : null, null, null, null));
    }

    /// <summary>Facebook returns an email only once the person confirmed it.</summary>
    private static async Task<ExternalProfile> ReadFacebookAsync(ProfileContext context)
    {
        using var json = await GetJsonAsync(context, context.UserInformationEndpoint!);
        var root = json.RootElement;
        return new ExternalProfile(String(root, "id")!, String(root, "email"), String(root, "first_name"), String(root, "last_name"), String(root, "name"));
    }

    /// <summary>GitHub: the primary email of <c>/user/emails</c>, only when verified.</summary>
    private static async Task<ExternalProfile> ReadGitHubAsync(ProfileContext context)
    {
        using var user = await GetJsonAsync(context, context.UserInformationEndpoint!);
        using var emails = await GetJsonAsync(context, "https://api.github.com/user/emails");
        var primary = emails.RootElement.ValueKind == JsonValueKind.Array
            ? emails.RootElement.EnumerateArray().FirstOrDefault(e => IsTrue(e, "primary") && IsTrue(e, "verified"))
            : default;
        var root = user.RootElement;
        return new ExternalProfile(Number(root, "id")!, primary.ValueKind == JsonValueKind.Object ? String(primary, "email") : null,
            null, null, String(root, "name") ?? String(root, "login"));
    }

    /// <summary>X: <c>confirmed_email</c> needs the users.email scope and "Request email from users" on the app.</summary>
    private static async Task<ExternalProfile> ReadXAsync(ProfileContext context)
    {
        using var json = await GetJsonAsync(context, context.UserInformationEndpoint!);
        var data = json.RootElement.GetProperty("data");
        return new ExternalProfile(String(data, "id")!, String(data, "confirmed_email"), null, null, String(data, "name"));
    }

    /// <summary>Discord: the email only when <c>verified</c> is true.</summary>
    private static async Task<ExternalProfile> ReadDiscordAsync(ProfileContext context)
    {
        using var json = await GetJsonAsync(context, context.UserInformationEndpoint!);
        var root = json.RootElement;
        return new ExternalProfile(String(root, "id")!, IsTrue(root, "verified") ? String(root, "email") : null,
            null, null, String(root, "global_name") ?? String(root, "username"));
    }

    // ---- Helpers ----------------------------------------------------------------------------------------------------

    private static async Task<JsonDocument> GetJsonAsync(ProfileContext context, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DotNetForge", "1.0")); // GitHub refuses requests without one
        using var response = await context.Backchannel.SendAsync(request, context.CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{context.Provider.Name} answered {(int)response.StatusCode} for the user's profile.");
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.CancellationToken));
    }

    /// <summary>
    /// The claims of the <c>id_token</c> in the token response. Its signature is not checked: the token comes straight
    /// from the provider's token endpoint over TLS, in exchange for the client's own code and secret (OpenID Connect
    /// Core 3.1.3.7 allows TLS server validation in place of the signature for this flow).
    /// </summary>
    public static JsonElement IdTokenClaims(JsonElement tokenResponse)
    {
        var idToken = String(tokenResponse, "id_token") ?? throw new InvalidOperationException("The token response has no id_token.");
        var parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidOperationException("The id_token is not a JWT.");
        }

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return json.RootElement.Clone();
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text ? text : null;

    private static string? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.Number ? value.GetRawText() : String(element, name) : null;

    /// <summary>true, or "true" (Apple sends booleans as strings).</summary>
    private static bool IsTrue(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && value.GetString() == "true"));
}
