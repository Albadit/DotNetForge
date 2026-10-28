using System.Security.Claims;
using DotNetForge.Sdk;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Authentication;
using DotNetForge.Web.Services;
using DotNetForge.Web.Services.Mfa;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// Sign-in with a provider (.docs/features/authentication.md#external-sign-in): a built-in OAuth provider from the
/// Providers screen, or one an authentication extension adds. The provider's scheme signs into the temporary
/// <see cref="ExternalLogin.SignInScheme"/> cookie; this controller starts the challenge, then on the callback finds the
/// CMS user with the provider's verified email - or creates one while sign-ups are on - asks for the second factor when
/// the account has one, issues the normal session cookie and audits the outcome.
/// </summary>
[Route("account/external")]
public sealed class ExternalLoginController : Controller
{
    private const string ProviderItem = "dnf.provider";
    private const string RememberItem = "dnf.remember";

    private readonly LoginProviders _providers;
    private readonly AuthService _authService;
    private readonly AccountService _accounts;
    private readonly IAuditService _audit;
    private readonly AppEnvironment _env;

    public ExternalLoginController(LoginProviders providers, AuthService authService, AccountService accounts, IAuditService audit, AppEnvironment env)
    {
        _providers = providers;
        _authService = authService;
        _accounts = accounts;
        _audit = audit;
        _env = env;
    }

    /// <summary>
    /// The "Sign in with ..." button on the sign-in screen posts here. The provider's address is not answered with a
    /// redirect: the site's Content-Security-Policy (<c>form-action 'self'</c>) also applies to the redirects of a form
    /// post, so a short page continues there with a meta refresh - a new navigation, not part of the post.
    /// </summary>
    [HttpPost("{scheme}")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    public async Task<IActionResult> Challenge(string scheme, string? returnUrl = null, bool rememberMe = false)
    {
        var provider = await _providers.FindActiveAsync(scheme, HttpContext.RequestAborted);
        if (provider is null)
        {
            return NotFound();
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(Callback), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null }),
            Items = { [ProviderItem] = provider.Scheme, [RememberItem] = rememberMe ? "true" : "false" },
        };
        await HttpContext.ChallengeAsync(provider.Scheme, properties);
        if (Response.StatusCode is StatusCodes.Status302Found && Response.Headers.Location.ToString() is { Length: > 0 } location)
        {
            Response.StatusCode = StatusCodes.Status200OK;
            Response.Headers.Remove("Location");
            ViewData["AppName"] = _env.AppName;
            ViewData["Provider"] = provider.DisplayName;
            return View("Continue", location);
        }

        return new EmptyResult();
    }

    [HttpGet("callback")]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    public async Task<IActionResult> Callback(string? returnUrl = null)
    {
        var external = await HttpContext.AuthenticateAsync(ExternalLogin.SignInScheme);
        await HttpContext.SignOutAsync(ExternalLogin.SignInScheme);

        var scheme = external.Properties?.Items.TryGetValue(ProviderItem, out var s) == true ? s : null;
        var rememberMe = external.Properties?.Items.TryGetValue(RememberItem, out var r) == true && r == "true";
        var provider = scheme is null ? null : await _providers.FindActiveAsync(scheme, HttpContext.RequestAborted);
        var principal = external.Principal;
        var email = principal?.FindFirst(ClaimTypes.Email)?.Value;
        if (!external.Succeeded || provider is null)
        {
            return await FailedAsync("Signing in with the provider did not complete. Try again or sign in with your password.", provider, email);
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return await FailedAsync($"Your {provider.DisplayName} account has no verified email address, so it can't be matched to an account here.", provider, null);
        }

        var result = await _authService.ValidateExternalAsync(email, HttpContext.RequestAborted);
        if (result.Status == SignInStatus.InvalidCredentials)
        {
            // No account with this email: sign-up through the provider, while sign-ups are on.
            var registered = await _accounts.RegisterExternalAsync(email, principal!.FindFirst(ClaimTypes.GivenName)?.Value,
                principal.FindFirst(ClaimTypes.Surname)?.Value, provider.DisplayName, HttpContext.RequestAborted);
            if (registered.User is null)
            {
                return await FailedAsync(registered.Error == "Registration is turned off."
                    ? $"No account here uses the email of your {provider.DisplayName} account, and new accounts can't be created."
                    : registered.Error!, provider, email);
            }

            result = await _authService.ValidateExternalAsync(email, HttpContext.RequestAborted);
        }

        var local = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        switch (result.Status)
        {
            case SignInStatus.SecondFactorRequired when result.User is not null:
                await PendingSignIn.StartAsync(HttpContext, result.User.Id, rememberMe, local, provider.DisplayName);
                return Redirect("/login/mfa");
            case SignInStatus.Success when result.Principal is not null:
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal, AccountController.SessionProperties(rememberMe));
                HttpContext.User = result.Principal;
                await _audit.LogAsync(AuditActions.UserLogin, "User", result.User?.Id.ToString(), result.User?.Email,
                    details: $"via {provider.DisplayName}");
                return LocalRedirect(local ?? AccountController.Home(result.Principal));
            default:
                return await FailedAsync(result.Status switch
                {
                    SignInStatus.LockedOut => "Account is temporarily locked. Try again later.",
                    SignInStatus.Disabled => "This account is disabled.",
                    _ => $"No account here uses the email of your {provider.DisplayName} account.",
                }, provider, email);
        }
    }

    /// <summary>A built-in provider's sign-in failed or was cancelled at the provider (<c>OnRemoteFailure</c>).</summary>
    [HttpGet("failed")]
    public async Task<IActionResult> Failed(string? provider)
    {
        var name = OAuthProviderCatalog.Find(provider)?.Name;
        await _audit.LogAsync(AuditActions.UserLoginFailed, "User", null, null, success: false,
            details: name is null ? "external sign-in" : $"via {name}");
        TempData["Error"] = $"Signing in with {name ?? "the provider"} was cancelled or did not complete. Try again or sign in with your password.";
        return Redirect("/login");
    }

    private async Task<IActionResult> FailedAsync(string message, LoginOption? provider, string? email)
    {
        // Same audit shape as a failed password sign-in, plus which provider was used.
        await _audit.LogAsync(AuditActions.UserLoginFailed, "User", null, email, success: false,
            details: provider is null ? "external sign-in" : $"via {provider.DisplayName}");
        TempData["Error"] = message; // an error toast on the sign-in screen
        return RedirectToAction(nameof(AccountController.Login), "Account");
    }
}
