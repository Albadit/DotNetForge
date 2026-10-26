using System.Security.Claims;
using DotNetForge.Sdk;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// Sign-in through an authentication extension (.docs/features/authentication.md#external-sign-in-extensions). The
/// extension registers a remote scheme (OAuth/OIDC) that signs into the temporary <see cref="ExternalLogin.SignInScheme"/>
/// cookie; this controller starts the challenge, then on the callback matches the provider's email claim to an
/// existing CMS user, issues the normal admin cookie and audits the outcome.
/// </summary>
[Route("account/external")]
public sealed class ExternalLoginController : Controller
{
    private const string ProviderItem = "dnf.provider";

    private readonly LoginProviders _providers;
    private readonly AuthService _authService;
    private readonly IAuditService _audit;

    public ExternalLoginController(LoginProviders providers, AuthService authService, IAuditService audit)
    {
        _providers = providers;
        _authService = authService;
        _audit = audit;
    }

    /// <summary>The "Sign in with ..." button on the sign-in screen posts here.</summary>
    [HttpPost("{scheme}")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    public async Task<IActionResult> Challenge(string scheme, string? returnUrl = null)
    {
        var provider = await _providers.FindActiveAsync(scheme, HttpContext.RequestAborted);
        if (provider is null)
        {
            return NotFound();
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(Callback), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null }),
            Items = { [ProviderItem] = provider.Scheme },
        };
        return Challenge(properties, provider.Scheme);
    }

    [HttpGet("callback")]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    public async Task<IActionResult> Callback(string? returnUrl = null)
    {
        var external = await HttpContext.AuthenticateAsync(ExternalLogin.SignInScheme);
        await HttpContext.SignOutAsync(ExternalLogin.SignInScheme);

        var scheme = external.Properties?.Items.TryGetValue(ProviderItem, out var s) == true ? s : null;
        var provider = scheme is null ? null : await _providers.FindActiveAsync(scheme, HttpContext.RequestAborted);
        var email = external.Principal?.FindFirst(ClaimTypes.Email)?.Value;
        if (!external.Succeeded || provider is null || string.IsNullOrWhiteSpace(email))
        {
            return await FailedAsync("External sign-in did not complete. Try again or sign in with your password.", provider, email);
        }

        var result = await _authService.ValidateExternalAsync(email, HttpContext.RequestAborted);
        if (result.Status != SignInStatus.Success || result.Principal is null)
        {
            return await FailedAsync(result.Status switch
            {
                SignInStatus.LockedOut => "Account is temporarily locked. Try again later.",
                SignInStatus.Disabled => "This account is disabled.",
                _ => $"No account here uses the email of your {provider.DisplayName} account.",
            }, provider, email);
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal);
        HttpContext.User = result.Principal;
        await _audit.LogAsync(AuditActions.UserLogin, "User", result.User?.Id.ToString(), result.User?.Email,
            details: $"via {provider.DisplayName}");
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/admin");
    }

    private async Task<IActionResult> FailedAsync(string message, LoginProviderContribution? provider, string? email)
    {
        // Same audit shape as a failed password sign-in, plus which provider was used.
        await _audit.LogAsync(AuditActions.UserLoginFailed, "User", null, email, success: false,
            details: provider is null ? "external sign-in" : $"via {provider.DisplayName}");
        TempData["Error"] = message; // an error toast on the sign-in screen
        return RedirectToAction(nameof(AccountController.Login), "Account");
    }
}
