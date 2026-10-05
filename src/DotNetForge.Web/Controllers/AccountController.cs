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
    private readonly AuthService _authService;
    private readonly IAuditService _audit;
    private readonly AppEnvironment _env;

    public AccountController(AuthService authService, IAuditService audit, AppEnvironment env)
    {
        _authService = authService;
        _audit = audit;
        _env = env;
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["AppName"] = _env.AppName;
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
        ViewData["AppName"] = _env.AppName;
        ViewData["ReturnUrl"] = returnUrl;

        var tenantId = await _authService.GetDefaultTenantIdAsync(HttpContext.RequestAborted);
        var result = await _authService.ValidateAsync(email, password, tenantId, HttpContext.RequestAborted);

        switch (result.Status)
        {
            case SignInStatus.Success when result.Principal is not null:
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal);

                // SignInAsync only writes the cookie; make this request's audit entry carry the signed-in user.
                HttpContext.User = result.Principal;
                await _audit.LogAsync(AuditActions.UserLogin, "User", result.User?.Id.ToString(),
                    result.User?.Email);

                // Only redirect to local URLs; anything else (or nothing) goes to the dashboard.
                return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/admin");

            case SignInStatus.LockedOut:
                ModelState.AddModelError(string.Empty, "Account is temporarily locked. Try again later.");
                break;
            case SignInStatus.Disabled:
                ModelState.AddModelError(string.Empty, "This account is disabled.");
                break;
            default:
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                break;
        }

        await _audit.LogAsync(AuditActions.UserLoginFailed, "User", null, email, success: false);
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

    [HttpGet("denied")]
    public IActionResult Denied()
    {
        ViewData["AppName"] = _env.AppName;
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }
}
