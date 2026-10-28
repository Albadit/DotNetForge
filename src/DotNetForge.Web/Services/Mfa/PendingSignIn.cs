using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace DotNetForge.Web.Services.Mfa;

/// <summary>
/// A sign-in waiting for its second factor (.docs/features/mfa.md#sign-in): the password (or a provider) was right, the
/// user has a second factor, so instead of the session cookie they get the short-lived <c>dnf.mfa</c> cookie (10
/// minutes, encrypted like the session cookie) and the code page. It carries who signs in, Remember me, where to go
/// next, which provider was used, and the token of a code that was sent or a passkey ceremony.
/// </summary>
public sealed record PendingSignIn(Guid UserId, bool RememberMe, string? ReturnUrl, string? Via, Guid? CodeMethodId, string? CodeToken, string? PasskeyToken)
{
    public const string Scheme = "dnf.mfa";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private const string RememberClaim = "dnf:remember";
    private const string ReturnClaim = "dnf:return";
    private const string ViaClaim = "dnf:via";
    private const string CodeMethodClaim = "dnf:code-method";
    private const string CodeClaim = "dnf:code";
    private const string PasskeyClaim = "dnf:passkey";

    public static Task StartAsync(HttpContext context, Guid userId, bool rememberMe, string? returnUrl, string? via) =>
        new PendingSignIn(userId, rememberMe, returnUrl, via, null, null, null).SaveAsync(context);

    public static async Task<PendingSignIn?> ReadAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(Scheme);
        var principal = result.Principal;
        if (!result.Succeeded || principal is null || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return null;
        }

        string? Value(string type) => principal.FindFirstValue(type) is { Length: > 0 } v ? v : null;
        return new PendingSignIn(userId, Value(RememberClaim) == "true", Value(ReturnClaim), Value(ViaClaim),
            Guid.TryParse(Value(CodeMethodClaim), out var method) ? method : null, Value(CodeClaim), Value(PasskeyClaim));
    }

    /// <summary>Writes (or rewrites, after a code was sent) the cookie; its 10 minutes count from the first sign-in step.</summary>
    public async Task SaveAsync(HttpContext context)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, UserId.ToString()), new(RememberClaim, RememberMe ? "true" : "false") };
        foreach (var (type, value) in new[] { (ReturnClaim, ReturnUrl), (ViaClaim, Via), (CodeMethodClaim, CodeMethodId?.ToString()), (CodeClaim, CodeToken), (PasskeyClaim, PasskeyToken) })
        {
            if (!string.IsNullOrEmpty(value))
            {
                claims.Add(new Claim(type, value));
            }
        }

        var existing = await context.AuthenticateAsync(Scheme);
        var expires = existing.Properties?.ExpiresUtc ?? DateTimeOffset.UtcNow.Add(Lifetime);
        await context.SignInAsync(Scheme, new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)),
            new AuthenticationProperties { ExpiresUtc = expires, IsPersistent = false, AllowRefresh = false });
    }

    public static Task EndAsync(HttpContext context) => context.SignOutAsync(Scheme);
}
