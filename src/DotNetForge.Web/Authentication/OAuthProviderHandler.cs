using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace DotNetForge.Web.Authentication;

/// <summary>The options of one built-in provider's scheme, filled from its saved settings (<see cref="OAuthProviderRegistry"/>).</summary>
public sealed class OAuthProviderOptions : OAuthOptions
{
    public OAuthProviderDefinition? Definition { get; set; }

    /// <summary>Apple: Team ID, key ID and the key (PEM) that sign the client secret.</summary>
    public string? TeamId { get; set; }

    public string? KeyId { get; set; }

    public string? PrivateKey { get; set; }
}

/// <summary>
/// ASP.NET Core's OAuth handler for the built-in providers (.docs/features/authentication.md#built-in-oauth-providers),
/// plus what they need beyond plain OAuth 2.0: extra authorization parameters, Apple's posted callback and signed
/// client secret, X's HTTP Basic client authentication, and each provider's profile reader, which adds the claims the
/// host reads (id, verified email, names).
/// </summary>
public sealed class OAuthProviderHandler : OAuthHandler<OAuthProviderOptions>
{
    public OAuthProviderHandler(IOptionsMonitor<OAuthProviderOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    private OAuthProviderDefinition Definition => Options.Definition ?? throw new InvalidOperationException($"{Scheme.Name} has no provider definition.");

    protected override string BuildChallengeUrl(AuthenticationProperties properties, string redirectUri) =>
        QueryHelpers.AddQueryString(base.BuildChallengeUrl(properties, redirectUri),
            Definition.AuthorizeParameters.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));

    /// <summary>Apple posts code and state (<c>response_mode=form_post</c>); the base handler reads them from the query.</summary>
    protected override async Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
    {
        if (Definition.FormPost && HttpMethods.IsPost(Request.Method) && Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(Context.RequestAborted);
            Request.QueryString = QueryString.Create(form.Where(f => f.Key is "code" or "state" or "error" or "error_description")
                .Select(f => new KeyValuePair<string, string?>(f.Key, f.Value.ToString())));
        }

        return await base.HandleRemoteAuthenticateAsync();
    }

    protected override async Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context)
    {
        if (!Definition.ClientSecretInHeader && !Definition.SignedClientSecret)
        {
            return await base.ExchangeCodeAsync(context);
        }

        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = context.Code,
            ["redirect_uri"] = context.RedirectUri,
            ["client_id"] = Options.ClientId,
        };
        if (context.Properties.Items.TryGetValue(OAuthConstants.CodeVerifierKey, out var verifier) && verifier is not null)
        {
            body[OAuthConstants.CodeVerifierKey] = verifier;
            context.Properties.Items.Remove(OAuthConstants.CodeVerifierKey);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Options.TokenEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (Definition.SignedClientSecret)
        {
            body["client_secret"] = AppleClientSecret(Options);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(Options.ClientId)}:{Uri.EscapeDataString(Options.ClientSecret)}")));
        }

        request.Content = new FormUrlEncodedContent(body);
        using var response = await Backchannel.SendAsync(request, Context.RequestAborted);
        var text = await response.Content.ReadAsStringAsync(Context.RequestAborted);
        return response.IsSuccessStatusCode
            ? OAuthTokenResponse.Success(JsonDocument.Parse(text))
            : OAuthTokenResponse.Failed(new AuthenticationFailureException($"{Definition.Name}'s token endpoint answered {(int)response.StatusCode}."));
    }

    protected override async Task<AuthenticationTicket> CreateTicketAsync(ClaimsIdentity identity, AuthenticationProperties properties, OAuthTokenResponse tokens)
    {
        var profile = await Definition.ReadProfile(new ProfileContext(Definition, tokens.AccessToken ?? string.Empty,
            tokens.Response?.RootElement ?? default, Backchannel, Options.UserInformationEndpoint, Context.RequestAborted));

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, profile.Id, ClaimValueTypes.String, ClaimsIssuer));
        foreach (var (type, value) in new[]
        {
            (ClaimTypes.Email, profile.Email),
            (ClaimTypes.GivenName, profile.FirstName),
            (ClaimTypes.Surname, profile.LastName),
            (ClaimTypes.Name, profile.Name),
        })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                identity.AddClaim(new Claim(type, value.Trim(), ClaimValueTypes.String, ClaimsIssuer));
            }
        }

        return new AuthenticationTicket(new ClaimsPrincipal(identity), properties, Scheme.Name);
    }

    /// <summary>
    /// Apple's client secret: an ES256 JWT from the team's key, valid 5 minutes - made for each sign-in, so the
    /// key never expires from use (Apple accepts at most 6 months).
    /// </summary>
    public static string AppleClientSecret(OAuthProviderOptions options)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(options.PrivateKey);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = options.KeyId }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = options.TeamId,
            iat = now,
            exp = now + 300,
            aud = "https://appleid.apple.com",
            sub = options.ClientId,
        }));
        var signature = key.SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256);
        return header + "." + payload + "." + Base64Url(signature);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
