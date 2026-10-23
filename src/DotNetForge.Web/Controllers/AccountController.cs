using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DotNetForge.Web.Controllers;

/// <summary>Email/password authentication for the admin area (.docs/features/authentication.md,
/// .docs/features/security.md).</summary>
[Route("account")]
public sealed class AccountController : Controller
{
    /// <summary>How long "Remember me" keeps a user signed in (sliding, like the 8-hour session without it).</summary>
    public static readonly TimeSpan RememberMeLifetime = TimeSpan.FromDays(30);

    private readonly AuthService _authService;
    private readonly IAuditService _audit;
    private readonly AppEnvironment _env;
    private readonly LoginProviders _loginProviders;

    public AccountController(AuthService authService, IAuditService audit, AppEnvironment env, LoginProviders loginProviders)
    {
        _authService = authService;
        _audit = audit;
        _env = env;
        _loginProviders = loginProviders;
    }

    [HttpGet("login")]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        ViewData["AppName"] = _env.AppName;
        ViewData["ReturnUrl"] = returnUrl;
        await AddLoginProvidersAsync();
        return View();
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    public async Task<IActionResult> Login(string email, string password, bool rememberMe = false, string? returnUrl = null)
    {
        ViewData["AppName"] = _env.AppName;
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["Email"] = email;
        ViewData["RememberMe"] = rememberMe;

        var tenantId = await _authService.GetDefaultTenantIdAsync(HttpContext.RequestAborted);
        var result = await _authService.ValidateAsync(email, password, tenantId, HttpContext.RequestAborted);

        switch (result.Status)
        {
            case SignInStatus.Success when result.Principal is not null:
                // Remember me: a persistent cookie that outlives the browser session, valid for RememberMeLifetime
                // (renewed while in use). Without it the cookie ends with the browser session or after 8 hours.
                var properties = rememberMe
                    ? new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.Add(RememberMeLifetime) }
                    : new AuthenticationProperties();
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal, properties);

                // SignInAsync only writes the cookie; make this request's audit entry carry the signed-in user.
                HttpContext.User = result.Principal;
                await _audit.LogAsync(AuditActions.UserLogin, "User", result.User?.Id.ToString(),
                    result.User?.Email);

                // Only redirect to local URLs; anything else (or nothing) goes to the dashboard.
                return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/admin");

            // Shown as an error toast (_Feedback reads TempData in this same request).
            case SignInStatus.LockedOut:
                TempData["Error"] = "Account is temporarily locked. Try again later.";
                break;
            case SignInStatus.Disabled:
                TempData["Error"] = "This account is disabled.";
                break;
            default:
                TempData["Error"] = "Invalid email or password.";
                break;
        }

        await _audit.LogAsync(AuditActions.UserLoginFailed, "User", null, email, success: false);
        await AddLoginProvidersAsync();
        return View();
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync(AuditActions.UserLogout);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/account/login");
    }

    /// <summary>The "Sign in with ..." buttons: providers of enabled authentication extensions.</summary>
    private async Task AddLoginProvidersAsync() =>
        ViewData["LoginProviders"] = await _loginProviders.GetActiveAsync(HttpContext.RequestAborted);

    [HttpGet("denied")]
    public IActionResult Denied()
    {
        ViewData["AppName"] = _env.AppName;
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }
}
